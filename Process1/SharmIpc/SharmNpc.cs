#pragma warning disable CA1416 // Validate platform compatibility - Suppressed as PipeOptions.CurrentUserOnly aims for Linux/macOS safety where possible

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tiesky.com.SharmNpcInternals;

// Re-use helper classes from SharmIPC where applicable
// Assuming AsyncManualResetEvent, WaitHandleAsyncFactory, Statistic, eMsgType, BytesProcessing are available
// (Included relevant ones below for completeness)

namespace tiesky.com
{
   

    /// <summary>
    /// Inter-process communication handler using Named Pipes. Aims to be a drop-in replacement for SharmIPC.
    /// </summary>
    public class SharmNpc : ISharm, IDisposable
    {
        #region SharmIPC Compatibility Fields & Properties

        // --- Public Callbacks & Handlers ---
        /// <summary>
        /// Handler for synchronous RPC requests from the remote peer.
        /// Must return a Tuple indicating success and the response payload.
        /// Mutually exclusive with AsyncRemoteCallHandler.
        /// </summary>
        public Func<byte[], Tuple<bool, byte[]>> RemoteCallHandler { get; set; } = null;

        /// <summary>
        /// Handler for asynchronous requests (Request) or RPC requests (RpcRequest) from the remote peer.
        /// For RpcRequest, the response MUST be sent back using AsyncAnswerOnRemoteCall.
        /// Mutually exclusive with RemoteCallHandler.
        /// </summary>
        public Action<ulong, byte[]> AsyncRemoteCallHandler { get; set; } = null;


        /// <summary>
        /// If true, user handlers (RemoteCallHandler/AsyncRemoteCallHandler) are invoked directly
        /// on the receiving thread. The user is responsible for returning control quickly (e.g., using Task.Run).
        /// If false (default), handlers are invoked via Task.Run automatically.
        /// </summary>
        public bool ExternalProcessing { get; set; } = false;

        /// <summary>
        /// Optional handler for logging internal exceptions. Provides a description and the Exception.
        /// </summary>
        public Action<string, System.Exception> ExternalExceptionHandler { get; set; } = null;

        // --- Internal State ---
        private readonly ConcurrentDictionary<ulong, ResponseCrate> _pendingRequests = new ConcurrentDictionary<ulong, ResponseCrate>();
        private long _nextMessageId = 0;
        private readonly System.Threading.Timer _timeoutTimer = null;
        internal readonly Statistic Statistic = new Statistic();
        private long _disposed = 0;

        #endregion

        #region Named Pipes Fields

        private readonly string _pipeName;
        private readonly PipeRole _role;
        private PipeStream _pipeStream = null;
        private CancellationTokenSource _connectionCts = null; // Manages the lifetime of connection and loops
        private readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(5); // Timeout for client connection attempts
        private readonly TimeSpan _serverConnectTimeout = TimeSpan.FromSeconds(30); // Timeout for waiting client to connect
        private readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(2); // Delay before client reconnect attempts
        private volatile bool _isConnected = false;
        private volatile bool _isConnecting = false; // Prevent overlapping connection attempts
        private readonly ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();
        //private readonly AsyncManualResetEvent _sendSignal = new AsyncManualResetEvent(); // Signals the send loop
        private Task _receiveTask = null;
        private Task _sendTask = null;
        private Task _connectionTask = null; // Tracks the main connection loop (server listen/client connect)
        private readonly int _maxQueueSizeInBytes; // Copied from SharmIPC constructor
        private long _currentQueueSize = 0;
        private readonly object _connectLock = new object(); // To synchronize connection attempts
        private bool _readerIsReady = false;
        //private ConcurrentQueue<Queue<byte[]>> sendQ = new ConcurrentQueue<Queue<byte[]>>();
        private int _communicationProtocolVersion=0;

        // Pipe Options - Consider security implications
        private const PipeOptions PipeSecurityOptions = PipeOptions.Asynchronous | PipeOptions.WriteThrough /*| PipeOptions.CurrentUserOnly*/;
        // Note: CurrentUserOnly is ideal for security on Unix-like systems but might
        // require the processes to run as the same user. Test thoroughly in your environment.
        // If running as different users (e.g., root service and user app) is needed, remove CurrentUserOnly
        // and ensure appropriate permissions on the pipe file (e.g., in /tmp). On Windows, named pipes have ACLs.

        #endregion

        #region Public Events

        /// <summary>
        /// Fired when a connection to the peer is successfully established.
        /// </summary>
        public Action PeerConnected;

        /// <summary>
        /// Fired when the connection to the peer is lost or closed.
        /// </summary>
        public Action PeerDisconnected;

        public bool Verbose=false;

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether a connection to the peer is currently established.
        /// </summary>
        public bool IsConnected => _isConnected && _pipeStream?.IsConnected == true;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a NamedPipesIPC instance.
        /// </summary>
        /// <param name="uniquePipeName">A unique name for the pipe (OS-wide). Both processes must use the same name.</param>
        /// <param name="role">Whether this instance acts as the Server (listens) or Client (connects).</param>
        /// <param name="remoteCallHandler">Synchronous handler for incoming RPC requests.</param>
        /// <param name="bufferCapacity">Approximation for internal buffer sizes (less critical than MMF version). Default: 50000.</param>
        /// <param name="maxQueueSizeInBytes">Maximum total size of messages allowed in the send queue before rejecting new messages. Default: 20MB.</param>
        /// <param name="externalExceptionHandler">Handler for internal exceptions.</param>
        /// <param name="externalProcessing">If false (default), handlers are invoked via Task.Run automatically on the receiving thread</param>
        public SharmNpc(string uniquePipeName, PipeRole role, Func<byte[], Tuple<bool, byte[]>> remoteCallHandler, long bufferCapacity = 50000, int maxQueueSizeInBytes = 20000000,
            Action<string, System.Exception> externalExceptionHandler = null, bool externalProcessing = false)
            : this(uniquePipeName, role, bufferCapacity, maxQueueSizeInBytes, externalExceptionHandler, externalProcessing)
        {
            this.RemoteCallHandler = remoteCallHandler ?? throw new ArgumentNullException(nameof(remoteCallHandler), "RemoteCallHandler cannot be null for this constructor.");
        }

        /// <summary>
        /// Creates a NamedPipesIPC instance.
        /// </summary>
        /// <param name="uniquePipeName">A unique name for the pipe (OS-wide). Both processes must use the same name.</param>
        /// <param name="role">Whether this instance acts as the Server (listens) or Client (connects).</param>
        /// <param name="asyncRemoteCallHandler">Asynchronous handler for incoming requests.</param>
        /// <param name="bufferCapacity">Approximation for internal buffer sizes (less critical than MMF version). Default: 50000.</param>
        /// <param name="maxQueueSizeInBytes">Maximum total size of messages allowed in the send queue before rejecting new messages. Default: 20MB.</param>
        /// <param name="externalExceptionHandler">Handler for internal exceptions.</param>
        /// <param name="externalProcessing">If false (default), handlers are invoked via Task.Run automatically on the receiving thread</param>
        public SharmNpc(string uniquePipeName, PipeRole role, Action<ulong, byte[]> asyncRemoteCallHandler, long bufferCapacity = 50000, int maxQueueSizeInBytes = 20000000,
            Action<string, System.Exception> externalExceptionHandler = null, bool externalProcessing = false)
             : this(uniquePipeName, role, bufferCapacity, maxQueueSizeInBytes, externalExceptionHandler, externalProcessing)
        {
            this.AsyncRemoteCallHandler = asyncRemoteCallHandler ?? throw new ArgumentNullException(nameof(asyncRemoteCallHandler), "AsyncRemoteCallHandler cannot be null for this constructor.");
        }

        // Base private constructor
        private SharmNpc(string uniquePipeName, PipeRole role, long bufferCapacity, int maxQueueSizeInBytes,
            Action<string, System.Exception> externalExceptionHandler, bool externalProcessing)
        {
            if (string.IsNullOrWhiteSpace(uniquePipeName))
                throw new ArgumentException("Unique pipe name cannot be empty.", nameof(uniquePipeName));
          

            this.Statistic.ipc = this;
            this.ExternalProcessing = externalProcessing;
            this.ExternalExceptionHandler = externalExceptionHandler;
            _pipeName = GetPlatformPipeName(uniquePipeName);
            _role = role;
            _maxQueueSizeInBytes = maxQueueSizeInBytes;
            // bufferCapacity is less relevant now, but can inform internal buffer sizes if needed.

            // Initialize and start the timeout timer (same logic as SharmIPC)
            _timeoutTimer = new Timer(CheckTimeouts, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

            // Start connection process
            _connectionCts = new CancellationTokenSource();
            StartConnectionProcess(_connectionCts.Token);
        }

        #endregion

        #region Connection Management

        private static string GetPlatformPipeName(string baseName)
        {
            // Base name validation (simple example)
            if (baseName.Contains("/") || baseName.Contains("\\"))
                throw new ArgumentException("Base pipe name should not contain path separators.", nameof(baseName));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return baseName; // On Windows, "." is implicit for local pipes
            }
            else
            {
                // Use /tmp for Linux/macOS - ensure permissions allow access
                // Consider using AppContext.BaseDirectory or a user-specific temp dir?
                // Using /tmp is common but has potential permission/cleanup issues.
                var socketPath = Path.Combine("/tmp", $"{baseName}.pipe");
                // Basic check for safety, real security needs more
                if (!socketPath.StartsWith("/tmp/"))
                    throw new ArgumentException("Invalid pipe path generated.", nameof(baseName));
                return socketPath;
            }
        }


        private void StartConnectionProcess(CancellationToken token)
        {
            LogInfo($"Starting connection process as {_role}");
            _connectionTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    if (_isConnected || _isConnecting)
                    {
                        await Task.Delay(500, token); // Wait if already connected or connecting
                        continue;
                    }

                    lock (_connectLock)
                    {
                        if (_isConnected || _isConnecting) continue; // Double check after lock
                        _isConnecting = true;
                    }

                    try
                    {
                        if (_role == PipeRole.Server)
                        {
                            await StartServerAsync(token).ConfigureAwait(false);
                        }
                        else // Client
                        {
                            await StartClientAsync(token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        LogInfo("Connection process canceled.");
                        break; // Exit loop if cancellation requested
                    }
                    catch (Exception ex)
                    {
                        LogExceptionInternal($"Connection process failed", ex);
                        // Decide on retry strategy here, maybe exponential backoff?
                        await Task.Delay(_reconnectDelay, token).ConfigureAwait(false); // Wait before retrying
                    }
                    finally
                    {
                        _isConnecting = false;
                    }

                    // If server failed to start listening, maybe wait before retrying?
                    if (_role == PipeRole.Server && !_isConnected)
                    {
                        await Task.Delay(_reconnectDelay, token).ConfigureAwait(false);
                    }
                }
                LogInfo($"Connection process stopped.");
            }, token);
        }

        private async Task StartServerAsync(CancellationToken token)
        {
            LogInfo($"Server waiting for connection on '{_pipeName}'...");
            NamedPipeServerStream serverStream = null;
            try
            {
                // Consider buffer sizes? PipeTransmissionMode.Byte is good.
                serverStream = new NamedPipeServerStream(
                   _pipeName,
                   PipeDirection.InOut,
                   1, // Max connections = 1 (peer-to-peer)
                   PipeTransmissionMode.Byte,
                   PipeSecurityOptions
                // Potential security: Add PipeSecurity for Windows ACLs if needed
                );

                using var connectTimeoutCts = new CancellationTokenSource(_serverConnectTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutCts.Token);

                // Asynchronously wait for a client connection
                //await serverStream.WaitForConnectionAsync(token).ConfigureAwait(false);
                await serverStream.WaitForConnectionAsync(linkedCts.Token).ConfigureAwait(false);

                LogInfo("Server connected to client.");
                await HandleNewConnection(serverStream, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                serverStream?.Dispose();
                throw; // Re-throw cancellation
            }
            catch (Exception ex)
            {
                LogExceptionInternal($"Server failed to wait for connection on '{_pipeName}'", ex);
                serverStream?.Dispose();
                // Do not set _isConnected = false here, let HandleDisconnection do it
                HandleDisconnection(); // Ensure cleanup on failure
                                       // Let the outer loop handle retry delay
            }
        }

        private async Task StartClientAsync(CancellationToken token)
        {
            LogInfo($"Client attempting to connect to '{_pipeName}'...");
            NamedPipeClientStream clientStream = null;
            while (!token.IsCancellationRequested && !_isConnected)
            {
                try
                {
                    clientStream = new NamedPipeClientStream(
                        ".", // Local machine
                        _pipeName,
                        PipeDirection.InOut,
                        PipeSecurityOptions);

                    // Use a connection timeout
                    using var connectTimeoutCts = new CancellationTokenSource(_connectTimeout);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutCts.Token);

                    await clientStream.ConnectAsync(linkedCts.Token).ConfigureAwait(false);

                    LogInfo("Client connected to server.");
                    await HandleNewConnection(clientStream, token).ConfigureAwait(false);
                    return; // Successfully connected, exit loop
                }
                catch (OperationCanceledException)
                {
                    clientStream?.Dispose();
                    if (token.IsCancellationRequested)
                    {
                        LogInfo("Client connection attempt canceled.");
                        throw; // Re-throw main cancellation
                    }
                    else
                    {
                        LogInfo("Client connection attempt timed out.");
                        // Timeout is expected, loop will retry after delay
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is TimeoutException) // Common connection failures
                {
                    LogInfo($"Client connection failed: {ex.Message}. Retrying...");
                    clientStream?.Dispose();
                }
                catch (Exception ex)
                {
                    LogExceptionInternal($"Client connection failed unexpectedly", ex);
                    clientStream?.Dispose();
                    // Let the outer loop handle retry delay for unexpected errors too
                }

                // If still here, connection failed or timed out, wait before retry
                if (!token.IsCancellationRequested)
                {
                    await Task.Delay(_reconnectDelay, token).ConfigureAwait(false);
                }
            }
            // If loop exits due to cancellation
            if (token.IsCancellationRequested) LogInfo("Client connection process canceled.");

        }

        private async Task HandleNewConnection(PipeStream stream, CancellationToken token)
        {
            // Ensure only one active connection setup
            if (_isConnected)
            {
                LogInfo("Ignoring new connection, already connected.");
                stream.Dispose();
                return;
            }

            _pipeStream = stream;
            _isConnected = true;
            _isConnecting = false; // Connection established or failed


            // Start Receive and Send loops
            _receiveTask = Task.Run(() => ReceiveLoopAsync(token), token);
            //_sendTask = Task.Run(() => SendLoopAsync(token), token);
            _sendTask = Task.Run(() => SendLoopAsync(), token);
            _readerIsReady = true;

            await Task.Delay(200, token);

            
            // Notify listeners
            PeerConnected?.Invoke();
            LogInfo("Peer connection established and loops started.");

            // Optional: Send a handshake or version check message here?

            // Wait for either loop to finish (indicating disconnection)
            // This isn't strictly necessary if the loops handle their own exit cleanly
            // await Task.WhenAny(_receiveTask, _sendTask);
            // LogInfo("A communication loop has ended.");
            // HandleDisconnection might be called within the loops themselves
        }


        private void HandleDisconnection()
        {
            if (!_isConnected && _pipeStream == null) return; // Already handled

            lock (_connectLock) // Ensure atomicity of disconnection state change
            {
                if (!_isConnected && _pipeStream == null) return; // Double check

                LogInfo("Handling disconnection...");
                _isConnected = false;


                // Dispose stream safely
                var stream = _pipeStream;
                _pipeStream = null; // Nullify before Dispose to prevent race conditions
                try { stream?.Dispose(); } catch (Exception ex) { 
                    if(Verbose)
                        LogExceptionInternal("Error disposing pipe stream", ex); 
                }

                // Stop loops (ReceiveLoopAsync and SendLoopAsync should detect closure and exit)
                // Signal send loop to wake up and exit if it's waiting
                //_sendSignal.Set();

                // Cancel pending requests
                var requestsToCancel = _pendingRequests.Values.ToList();
                _pendingRequests.Clear();
                Statistic.UpdatePending(0);

                foreach (var crate in requestsToCancel)
                {
                    try
                    {
                        crate.IsRespOk = false;
                        crate.res = null; // Indicate failure due to disconnection
                        if (crate.callBack != null)
                        {
                            // Execute callback asynchronously
                            Task.Run(() => crate.callBack(new Tuple<bool, byte[]>(false, null)));
                        }
                        else
                        {
                            crate.Set_MRE_AMRE(); // Signal waiting threads/tasks
                        }
                        crate.Dispose_MRE_AMRE(); // Clean up crate resources
                    }
                    catch (Exception ex)
                    {
                        if(Verbose)
                            LogExceptionInternal("Error cancelling pending request during disconnect", ex);
                    }
                }

                Interlocked.Exchange(ref _currentQueueSize, 0); // Reset queue size counter
                                                                // Clear the send queue (items will be lost)
                while (_sendQueue.TryDequeue(out _)) { }


                LogInfo("Disconnection handled.");
                // Notify listeners
                PeerDisconnected?.Invoke();

                // Client should automatically try to reconnect via StartConnectionProcess loop
                // Server should automatically try to listen again via StartConnectionProcess loop
            }
        }


        #endregion

        #region Receive Loop and Processing

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            LogInfo("Receive loop started.");
            byte[] lengthBuffer = new byte[4]; // To read the 4-byte length prefix
            MemoryStream messageStream = new MemoryStream(); // To accumulate message chunks

            try
            {
                while (_isConnected && !token.IsCancellationRequested)
                {
                    // 1. Read the 4-byte length prefix
                    int totalBytesRead = 0;
                    while (totalBytesRead < 4 && _isConnected && !token.IsCancellationRequested)
                    {
                        int bytesRead = await _pipeStream.ReadAsync(lengthBuffer, totalBytesRead, 4 - totalBytesRead, token).ConfigureAwait(false);
                        if (bytesRead == 0) throw new IOException("Pipe closed while reading message length."); // Peer disconnected
                        totalBytesRead += bytesRead;
                    }
                    if (totalBytesRead < 4) break; // Loop exit condition met


                    int messageLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

                    if (messageLength < 0 || messageLength > _maxQueueSizeInBytes * 2) // Sanity check length (allow slightly larger than max queue for safety)
                    {
                        throw new InvalidDataException($"Invalid message length received: {messageLength}");
                    }
                    if (messageLength == 0)
                    {
                        LogInfo("Received zero-length message (potentially keep-alive?). Skipping.");
                        continue;
                    }


                    // 2. Read the message data
                    byte[] messageBuffer = new byte[messageLength]; // Consider ArrayPool later
                    totalBytesRead = 0;
                    while (totalBytesRead < messageLength && _isConnected && !token.IsCancellationRequested)
                    {
                        int bytesRead = await _pipeStream.ReadAsync(messageBuffer, totalBytesRead, messageLength - totalBytesRead, token).ConfigureAwait(false);
                        if (bytesRead == 0) throw new IOException("Pipe closed while reading message data."); // Peer disconnected
                        totalBytesRead += bytesRead;
                    }
                    if (totalBytesRead < messageLength) break; // Loop exit condition met

                    Statistic.MessageReceived(messageLength + 4);

                    // 3. Process the complete message (V2 Deserialization)
                    DeserializeAndProcessMessage(messageBuffer);

                } // End while connected
            }
            catch (OperationCanceledException)
            {
                LogInfo("Receive loop canceled.");
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException || ex is InvalidDataException)
            {
                if(Verbose)
                    LogExceptionInternal("Receive loop terminated due to pipe error", ex);
                // Pipe likely broken, trigger disconnection
            }
            catch (Exception ex)
            {
                if (Verbose)
                    LogExceptionInternal("Receive loop terminated unexpectedly", ex);
            }
            finally
            {
                LogInfo("Receive loop finished.");
                HandleDisconnection(); // Ensure cleanup and state change if loop exits
            }
        }

        // --- V2 Deserialization Logic (Adapted from SharmIPC.ReaderWriterHandler.ReaderV02) ---
        private void DeserializeAndProcessMessage(byte[] messageData)
        {
            MemoryStream ms = new MemoryStream(messageData);
            byte[] payload = null;
            byte[] ret = null;
            ushort iCurChunk = 0;
            ushort iTotChunk = 0;
            ulong iMsgId = 0;
            int iPayLoadLen = 0; // Use int for payload length matching V1/V2 intent
            ulong iResponseMsgId = 0;
            eMsgType msgType = eMsgType.Request; // Default

            byte[] chunksCollected = null; // Track chunks for multi-part messages
            ushort lastReceivedChunk = 0; // Track last received chunk number

            try
            {
                while (ms.Position < ms.Length) // Process all messages within the buffer
                {
                    // Reset for next message part in buffer
                    payload = null;
                    iCurChunk = 0;
                    iTotChunk = 0;
                    iMsgId = 0;
                    iPayLoadLen = 0;
                    iResponseMsgId = 0;

                    int bytesRead;
                    msgType = (eMsgType)BytesProcessing.ReadProtoUInt64(ms, out bytesRead);
                    iMsgId = BytesProcessing.ReadProtoUInt64(ms, out bytesRead);
                    iPayLoadLen = (int)BytesProcessing.ReadProtoUInt64(ms, out bytesRead); // Expecting payload length
                    iCurChunk = (ushort)BytesProcessing.ReadProtoUInt64(ms, out bytesRead);
                    iTotChunk = (ushort)BytesProcessing.ReadProtoUInt64(ms, out bytesRead);
                    iResponseMsgId = BytesProcessing.ReadProtoUInt64(ms, out bytesRead);

                    if (iPayLoadLen < 0) throw new InvalidDataException($"Invalid payload length: {iPayLoadLen}");

                    // Read Payload
                    if (iPayLoadLen > 0)
                    {
                        if (ms.Position + iPayLoadLen > ms.Length) throw new InvalidDataException("Payload length exceeds buffer bounds.");
                        payload = new byte[iPayLoadLen];
                        ms.Read(payload, 0, iPayLoadLen);
                    }
                    else if (iPayLoadLen == 0)
                    {
                        payload = Array.Empty<byte>(); // Represent empty payload consistently
                    }
                    else // iPayLoadLen < 0 is invalid
                    {
                        throw new InvalidDataException($"Invalid payload length: {iPayLoadLen}");
                    }


                    // --- Process the received chunk ---
                    if (msgType == eMsgType.ErrorInRpc)
                    {
                        InternalDataArrived(msgType, iResponseMsgId, null); // ResponseMsgId is the key here
                        lastReceivedChunk = 0; // Reset chunk tracking
                        chunksCollected = null;
                    }
                    else if (msgType == eMsgType.RpcResponse || msgType == eMsgType.RpcRequest || msgType == eMsgType.Request)
                    {
                        // Chunk Assembly Logic (Similar to SharmIPC)
                        if (iCurChunk == 1)
                        {
                            chunksCollected = null; // Start of a new message
                            lastReceivedChunk = 0;
                        }
                        else if (iCurChunk != lastReceivedChunk + 1)
                        {
                            // Out-of-order chunk detected! This indicates a problem.
                            LogExceptionInternal($"Chunk order error. Expected {lastReceivedChunk + 1}, got {iCurChunk}. MsgId: {iMsgId}, RespId: {iResponseMsgId}", new InvalidDataException("Chunk order mismatch"));
                            // Handle error: Could try to notify sender (ErrorInRpc), or just discard.
                            // Discarding is simpler for now.
                            chunksCollected = null;
                            lastReceivedChunk = 0;

                            // If it was a response we were waiting for, signal failure
                            if (msgType == eMsgType.RpcResponse && _pendingRequests.TryGetValue(iResponseMsgId, out var crate))
                            {
                                crate.IsRespOk = false;
                                crate.res = null;
                                crate.Set_MRE_AMRE();
                                // Let the original caller handle removal from dictionary
                            }
                            continue; // Skip processing this broken message part
                        }

                        lastReceivedChunk = iCurChunk; // Update last received chunk

                        if (iTotChunk == iCurChunk) // Last chunk
                        {
                            byte[] finalPayload;
                            if (chunksCollected == null) // Single chunk message
                            {
                                finalPayload = payload ?? Array.Empty<byte>();
                            }
                            else // Multi-chunk message completed
                            {
                                // Combine collected chunks with the final payload
                                finalPayload = new byte[chunksCollected.Length + (payload?.Length ?? 0)];
                                Buffer.BlockCopy(chunksCollected, 0, finalPayload, 0, chunksCollected.Length);
                                if (payload != null)
                                {
                                    Buffer.BlockCopy(payload, 0, finalPayload, chunksCollected.Length, payload.Length);
                                }
                            }

                            // Process the fully assembled message
                            InternalDataArrived(msgType, (msgType == eMsgType.RpcResponse || msgType == eMsgType.ErrorInRpc) ? iResponseMsgId : iMsgId, finalPayload);

                            // Reset for next potential message
                            chunksCollected = null;
                            lastReceivedChunk = 0;
                        }
                        else // Intermediate chunk
                        {
                            if (chunksCollected == null)
                            {
                                chunksCollected = payload ?? Array.Empty<byte>();
                            }
                            else
                            {
                                // Append payload to collected chunks
                                byte[] combined = new byte[chunksCollected.Length + (payload?.Length ?? 0)];
                                Buffer.BlockCopy(chunksCollected, 0, combined, 0, chunksCollected.Length);
                                if (payload != null)
                                {
                                    Buffer.BlockCopy(payload, 0, combined, chunksCollected.Length, payload.Length);
                                }
                                chunksCollected = combined;
                            }
                        }
                    }
                    else
                    {
                        LogInfo($"Received unknown message type: {msgType}");
                        // Discard unknown message types
                        chunksCollected = null;
                        lastReceivedChunk = 0;
                    }

                } // End while processing stream
            }
            catch (Exception ex)
            {
                LogExceptionInternal("Error deserializing or processing message", ex);
                // Potentially trigger disconnect if data seems corrupted
                HandleDisconnection();
            }
            finally
            {
                ms.Dispose();
            }
        }

        // InternalDataArrived: Almost identical to SharmIPC
        private void InternalDataArrived(eMsgType msgType, ulong msgId, byte[] data)
        {
            ResponseCrate crate = null;

            switch (msgType)
            {
                case eMsgType.Request:
                    if (AsyncRemoteCallHandler != null)
                    {
                        if (ExternalProcessing) AsyncRemoteCallHandler(msgId, data);
                        else Task.Run(() => AsyncRemoteCallHandler(msgId, data));
                    }
                    else if (RemoteCallHandler != null) // Allow Request to go to sync handler if async isn't defined? SharmIPC did this.
                    {
                        if (ExternalProcessing) RemoteCallHandler(data); // Discard result for Request type
                        else Task.Run(() => RemoteCallHandler(data));
                    }
                    break;

                case eMsgType.RpcRequest:
                    if (AsyncRemoteCallHandler != null)
                    {
                        // User MUST call AsyncAnswerOnRemoteCall for RpcRequest
                        if (ExternalProcessing) AsyncRemoteCallHandler(msgId, data);
                        else Task.Run(() => AsyncRemoteCallHandler(msgId, data));
                    }
                    else if (RemoteCallHandler != null)
                    {
                        // Run handler and send response back immediately
                        if (ExternalProcessing)
                        {
                            try
                            {
                                var result = RemoteCallHandler(data);
                                SendMessageInternal(result.Item1 ? eMsgType.RpcResponse : eMsgType.ErrorInRpc, GetNextMessageId(), result.Item2, msgId);
                            }
                            catch (Exception ex)
                            {
                                LogExceptionInternal("Exception in synchronous RemoteCallHandler (ExternalProcessing)", ex);
                                SendMessageInternal(eMsgType.ErrorInRpc, GetNextMessageId(), null, msgId); // Send error back
                            }
                        }
                        else
                        {
                            Task.Run(() =>
                            {
                                try
                                {
                                    var result = RemoteCallHandler(data);
                                    SendMessageInternal(result.Item1 ? eMsgType.RpcResponse : eMsgType.ErrorInRpc, GetNextMessageId(), result.Item2, msgId);
                                }
                                catch (Exception ex)
                                {
                                    LogExceptionInternal("Exception in synchronous RemoteCallHandler (Task.Run)", ex);
                                    SendMessageInternal(eMsgType.ErrorInRpc, GetNextMessageId(), null, msgId); // Send error back
                                }
                            });
                        }
                    }
                    break;

                case eMsgType.ErrorInRpc:
                case eMsgType.RpcResponse:
                    if (_pendingRequests.TryGetValue(msgId, out crate)) // msgId here is the *original* request ID
                    {
                        // Store result, signal completion
                        crate.res = data;
                        crate.IsRespOk = (msgType == eMsgType.RpcResponse);

                        if (crate.callBack != null)
                        {
                            // Remove *before* calling callback to prevent potential race if callback retries quickly
                            if (_pendingRequests.TryRemove(msgId, out var removedCrate))
                            {
                                Statistic.UpdatePending(_pendingRequests.Count);
                                // Run callback async
                                Task.Run(() =>
                                {
                                    try
                                    {
                                        removedCrate.callBack(new Tuple<bool, byte[]>(removedCrate.IsRespOk, removedCrate.res));
                                    }
                                    catch (Exception ex)
                                    {
                                        LogExceptionInternal("Exception in response callback", ex);
                                    }
                                    finally
                                    {
                                        removedCrate.Dispose_MRE_AMRE(); // Clean up crate after callback
                                    }
                                });
                            }
                            // If TryRemove failed, another thread (timeout?) might have removed it. Do nothing.
                        }
                        else
                        {
                            // For sync (MRE) or async (AMRE/TCS) waits, just signal.
                            // The waiting thread/task is responsible for removing from the dictionary.
                            crate.Set_MRE_AMRE();
                        }
                    }
                    else
                    {
                        LogInfo($"Received response for unknown or timed-out request ID: {msgId}");
                        // Ignore silently
                    }
                    break;
            }
        }


        #endregion

        #region Send Loop and Methods

        private ulong GetNextMessageId()
        {
            return (ulong)Interlocked.Increment(ref _nextMessageId);
        }

        // Called by public methods to queue a message for sending
        private bool SendMessageInternal(eMsgType msgType, ulong msgId, byte[] data, ulong responseMsgId = 0)
        {
            if (!IsConnected || Interlocked.Read(ref _disposed) == 1)
            {
                LogInfo($"SendMessageInternal failed: Not connected or disposed. MsgType: {msgType}, MsgId: {msgId}");
                return false;
            }

            try
            {
                // Serialize using V2 logic
                var serializedMessageParts = SerializeMessageV2(msgType, msgId, data, responseMsgId);
                int messageTotalBytes = serializedMessageParts.Sum(p => p.Length);

                // Check queue size limit
                if (Interlocked.Read(ref _currentQueueSize) + messageTotalBytes > _maxQueueSizeInBytes)
                {
                    LogExceptionInternal("Send queue max size reached", new Exception($"Queue limit {_maxQueueSizeInBytes} exceeded. Current: {Interlocked.Read(ref _currentQueueSize)}, Adding: {messageTotalBytes}"));
                    Statistic.Error(); // Count as an error?
                    return false;
                }

                // Frame the message (Length Prefix + Serialized Data)
                byte[] lengthPrefix = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, messageTotalBytes);

                byte[] framedMessage = new byte[4 + messageTotalBytes];
                Buffer.BlockCopy(lengthPrefix, 0, framedMessage, 0, 4);
                int currentPos = 4;
                foreach (var part in serializedMessageParts)
                {
                    Buffer.BlockCopy(part, 0, framedMessage, currentPos, part.Length);
                    currentPos += part.Length;
                }


                // Add to queue and signal sender task
                _sendQueue.Enqueue(framedMessage);
                Interlocked.Add(ref _currentQueueSize, framedMessage.Length);
                //_sendSignal.Set(); // Signal the SendLoopAsync that data is available

                this.SendLoopAsync();

                return true;
            }
            catch (Exception ex)
            {
                LogExceptionInternal("Error serializing or queuing message", ex);
                return false;
            }
        }

        // --- V2 Serialization Logic (Adapted from SharmIPC.ReaderWriterHandler.SendMessageV2) ---
        // Returns a list of byte arrays representing the serialized parts (header + payload chunk)
        private List<byte[]> SerializeMessageV2(eMsgType msgType, ulong msgId, byte[] msg, ulong responseMsgId = 0)
        {
            List<byte[]> messageParts = new List<byte[]>();
            int bufferLenS = 50000 - 100; // Use a reasonable chunk size, less critical than MMF fixed size. Reduce slightly for headers.

            int i = 0;
            int payloadLength = msg?.Length ?? 0;
            int left = payloadLength;

            ushort totalChunks = (ushort)(payloadLength == 0 ? 1 : Math.Ceiling((double)payloadLength / bufferLenS));
            if (totalChunks == 0) totalChunks = 1; // Ensure at least one chunk even for null payload

            ushort currentChunk = 1;
            int currentChunkLen = 0;

            do // Use do-while to handle zero-length payload correctly
            {
                if (left > bufferLenS)
                    currentChunkLen = bufferLenS;
                else
                    currentChunkLen = left;

                // Build header parts
                messageParts.Add(((ulong)msgType).ToProtoBytes());
                messageParts.Add(msgId.ToProtoBytes());
                messageParts.Add(((ulong)currentChunkLen).ToProtoBytes()); // Actual payload length for this chunk
                messageParts.Add(((ulong)currentChunk).ToProtoBytes());
                messageParts.Add(((ulong)totalChunks).ToProtoBytes());
                messageParts.Add(responseMsgId.ToProtoBytes());

                // Add payload chunk if needed
                if (currentChunkLen > 0 && msg != null)
                {
                    byte[] payloadChunk = new byte[currentChunkLen];
                    Buffer.BlockCopy(msg, i, payloadChunk, 0, currentChunkLen);
                    messageParts.Add(payloadChunk);
                }
                else if (currentChunkLen == 0 && payloadLength == 0 && currentChunk == 1)
                {
                    // This handles the case of sending a message with explicitly zero-length or null payload
                    // Header is already added above. No payload part needed.
                }


                left -= currentChunkLen;
                i += currentChunkLen;
                currentChunk++;

            } while (left > 0 || (payloadLength == 0 && currentChunk == 1)); // Ensure loop runs once for empty/null payload


            return messageParts;
        }


        int inSend = 0;

        private async Task SendLoopAsync()
        {
            while (true)
            {
                if (Interlocked.CompareExchange(ref inSend, 1, 0) != 0)
                    return;

                try
                {
                    if (!IsConnected) return;
                    if (!_readerIsReady)
                    {
                        while (!_readerIsReady && !_connectionCts.Token.IsCancellationRequested)
                        {
                            await Task.Delay(10);
                        }
                    }

                    if (_connectionCts.Token.IsCancellationRequested)
                        return;

                    while (_sendQueue.TryDequeue(out var messageToSend))
                    {
                        Interlocked.Add(ref _currentQueueSize, -messageToSend.Length); // Decrement queue size

                        await _pipeStream.WriteAsync(messageToSend, 0, messageToSend.Length, _connectionCts.Token).ConfigureAwait(false);
                        await _pipeStream.FlushAsync().ConfigureAwait(false);

                        Statistic.MessageSent(messageToSend.Length);
                    }

                   

                }
                catch (Exception ex)
                {
                    HandleDisconnection(); // Ensure cleanup if send loop exits
                    return;
                }
                finally
                {
                    Interlocked.Exchange(ref inSend, 0);
                }

                if (_sendQueue.IsEmpty)
                    return;

            }

        }

        // --- Send Loop Task ---
        //private async Task SendLoopAsync_OLD(CancellationToken token)
        //{
        //    LogInfo("Send loop started.");
        //    try
        //    {
        //        while (!token.IsCancellationRequested)
        //        {
        //            // Wait for data to be available in the queue
        //            await _sendSignal.WaitAsync(TimeSpan.FromMilliseconds(-1), token).ConfigureAwait(false); // Wait indefinitely until signaled or canceled
        //            _sendSignal.Reset(); // Reset the signal immediately after waking up

        //            if (token.IsCancellationRequested) break;


        //            while (_sendQueue.TryDequeue(out byte[] messageToSend))
        //            {
        //                Interlocked.Add(ref _currentQueueSize, -messageToSend.Length); // Decrement queue size

        //                if (!IsConnected)
        //                {
        //                    LogInfo("Send loop: Not connected, discarding message.");
        //                    continue; // Discard if pipe broke while waiting/dequeuing
        //                }

        //                try
        //                {
        //                    // Write the entire framed message
        //                    await _pipeStream.WriteAsync(messageToSend, 0, messageToSend.Length, token).ConfigureAwait(false);
        //                    await _pipeStream.FlushAsync(token).ConfigureAwait(false); // Ensure data is sent
        //                    Statistic.MessageSent(messageToSend.Length);
        //                }
        //                catch (OperationCanceledException)
        //                {
        //                    LogInfo("Send loop canceled during write.");
        //                    _sendQueue.Enqueue(messageToSend); // Re-queue if canceled? Or discard? Discarding is simpler.
        //                    Interlocked.Add(ref _currentQueueSize, messageToSend.Length); // Add back if re-queued
        //                    throw; // Propagate cancellation
        //                }
        //                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
        //                {
        //                    LogExceptionInternal("Send loop pipe error", ex);
        //                    // Re-queue message? Might lead to infinite loop if pipe stays broken. Discarding is safer.
        //                    // _sendQueue.Enqueue(messageToSend); Interlocked.Add(ref _currentQueueSize, messageToSend.Length);
        //                    throw; // Let the outer handler trigger disconnection
        //                }
        //            }
        //        }
        //    }
        //    catch (OperationCanceledException)
        //    {
        //        LogInfo("Send loop canceled.");
        //    }
        //    catch (Exception ex)
        //    {
        //        LogExceptionInternal("Send loop terminated unexpectedly", ex);
        //    }
        //    finally
        //    {
        //        LogInfo("Send loop finished.");
        //        HandleDisconnection(); // Ensure cleanup if send loop exits
        //    }
        //}

        #endregion

        #region Public API Methods (SharmIPC compatible)

        /// <summary>
        /// Current 0
        /// </summary>
        public int CommunicationProtocolVersion
        {
            get { return _communicationProtocolVersion; }
        }

        /// <summary>
        /// Sends an RPC request and waits synchronously for a response.
        /// </summary>
        /// <param name="args">Payload to send.</param>
        /// <param name="callBack">If provided, the response is returned asynchronously via this callback. The method returns immediately with (true, null).</param>
        /// <param name="timeoutMs">Timeout in milliseconds to wait for a response (only applicable if callBack is null). Default: 30000.</param>
        /// <returns>If callBack is null: Tuple(success, responseBytes). If callBack is provided: Tuple(true, null) if queued, Tuple(false, null) if error.</returns>
        public Tuple<bool, byte[]> RemoteRequest(byte[] args, Action<Tuple<bool, byte[]>> callBack = null, int timeoutMs = 30000)
        {
            if (Interlocked.Read(ref _disposed) == 1) return new Tuple<bool, byte[]>(false, null);
            if (!IsConnected) return new Tuple<bool, byte[]>(false, null);

            ulong msgId = GetNextMessageId();
            var crate = new ResponseCrate();

            if (callBack != null)
            {
                crate.callBack = callBack;
                crate.TimeoutsMs = timeoutMs; // Timer will handle timeout for callbacks
                crate.Init_CallbackMode(); // No MRE/AMRE needed

                if (!_pendingRequests.TryAdd(msgId, crate))
                {
                    LogExceptionInternal("Failed to add callback request to dictionary (duplicate msgId?)", new Exception($"MsgId collision: {msgId}"));
                    return new Tuple<bool, byte[]>(false, null); // Should be rare
                }
                Statistic.UpdatePending(_pendingRequests.Count);


                if (!SendMessageInternal(eMsgType.RpcRequest, msgId, args))
                {
                    // Failed to queue message
                    _pendingRequests.TryRemove(msgId, out _);
                    Statistic.UpdatePending(_pendingRequests.Count);
                    // Invoke callback immediately with failure
                    Task.Run(() => callBack(new Tuple<bool, byte[]>(false, null)));
                    return new Tuple<bool, byte[]>(false, null); // Indicate queuing failed
                }

                // Message queued successfully, callback will be invoked later
                return new Tuple<bool, byte[]>(true, null);
            }
            else // Synchronous wait
            {
                crate.TimeoutsMs = timeoutMs; // Store timeout, but MRE wait uses it directly
                crate.Init_MRE(); // Use ManualResetEvent for sync wait

                if (!_pendingRequests.TryAdd(msgId, crate))
                {
                    LogExceptionInternal("Failed to add sync request to dictionary (duplicate msgId?)", new Exception($"MsgId collision: {msgId}"));
                    crate.Dispose_MRE_AMRE();
                    return new Tuple<bool, byte[]>(false, null);
                }
                Statistic.UpdatePending(_pendingRequests.Count);

                Tuple<bool, byte[]> result = null;
                try
                {
                    if (!SendMessageInternal(eMsgType.RpcRequest, msgId, args))
                    {
                        // Failed to send/queue
                        result = new Tuple<bool, byte[]>(false, null);
                    }
                    else
                    {
                        // Wait for response or timeout
                        if (!crate.WaitOne_MRE(timeoutMs))
                        {
                            // Timeout
                            Statistic.Timeout();
                            result = new Tuple<bool, byte[]>(false, null); // Indicate timeout
                        }
                        else
                        {
                            // Got response (or ErrorInRpc)
                            result = new Tuple<bool, byte[]>(crate.IsRespOk, crate.res);
                        }
                    }
                }
                finally
                {
                    // Always remove from dictionary and dispose crate after sync wait completes or fails
                    if (_pendingRequests.TryRemove(msgId, out var removedCrate))
                    {
                        Statistic.UpdatePending(_pendingRequests.Count);
                        removedCrate.Dispose_MRE_AMRE();
                    }
                    else
                    {
                        // If removed by timeout checker or disconnect handler already, ensure crate is disposed
                        crate.Dispose_MRE_AMRE();
                    }
                }
                return result ?? new Tuple<bool, byte[]>(false, null); // Fallback
            }
        }

        /// <summary>
        /// Sends an RPC request and returns an awaitable Task for the response.
        /// </summary>
        /// <param name="args">Payload to send.</param>
        /// <param name="timeoutMs">Timeout in milliseconds to wait for a response. Default: 30000.</param>
        /// <returns>A Task that resolves to Tuple(success, responseBytes).</returns>
        public async Task<Tuple<bool, byte[]>> RemoteRequestAsync(byte[] args, int timeoutMs = 30000)
        {
            if (Interlocked.Read(ref _disposed) == 1) return new Tuple<bool, byte[]>(false, null);
            if (!IsConnected) return new Tuple<bool, byte[]>(false, null);

            ulong msgId = GetNextMessageId();
            var crate = new ResponseCrate();
            crate.TimeoutsMs = timeoutMs; // Use timeout with AMRE wait
            crate.Init_AMRE(); // Use AsyncManualResetEvent

            if (!_pendingRequests.TryAdd(msgId, crate))
            {
                LogExceptionInternal("Failed to add async request to dictionary (duplicate msgId?)", new Exception($"MsgId collision: {msgId}"));
                crate.Dispose_MRE_AMRE(); // Dispose the AMRE we created
                return new Tuple<bool, byte[]>(false, null);
            }
            Statistic.UpdatePending(_pendingRequests.Count);


            Tuple<bool, byte[]> result = null;
            CancellationTokenSource timeoutCts = null;
            try
            {
                if (!SendMessageInternal(eMsgType.RpcRequest, msgId, args))
                {
                    // Failed to send/queue
                    result = new Tuple<bool, byte[]>(false, null);
                }
                else
                {
                    // Wait for response or timeout using AMRE's built-in timeout wait
                    timeoutCts = new CancellationTokenSource(timeoutMs);
                    if (!await crate.WaitOne_AMRE_WithTimeout(timeoutMs, timeoutCts.Token).ConfigureAwait(false))
                    {
                        // Timeout or Canceled
                        if (!timeoutCts.IsCancellationRequested) // Check if it was timeout vs external cancellation
                        {
                            Statistic.Timeout();
                            LogInfo($"Async request {msgId} timed out after {timeoutMs} ms.");
                        }
                        else
                        {
                            LogInfo($"Async request {msgId} cancelled before timeout.");
                        }
                        result = new Tuple<bool, byte[]>(false, null);
                    }
                    else
                    {
                        // Got response signal
                        result = new Tuple<bool, byte[]>(crate.IsRespOk, crate.res);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LogInfo($"Async request {msgId} cancelled during await.");
                result = new Tuple<bool, byte[]>(false, null);
            }
            finally
            {
                timeoutCts?.Dispose();
                // Always remove from dictionary and dispose crate after async wait completes or fails
                if (_pendingRequests.TryRemove(msgId, out var removedCrate))
                {
                    Statistic.UpdatePending(_pendingRequests.Count);
                    removedCrate.Dispose_MRE_AMRE();
                }
                else
                {
                    // If removed by disconnect handler already, ensure crate is disposed
                    crate.Dispose_MRE_AMRE();
                }
            }

            return result ?? new Tuple<bool, byte[]>(false, null); // Fallback
        }


        /// <summary>
        /// Sends a request without expecting or waiting for a response. Fire-and-forget.
        /// </summary>
        /// <param name="args">Payload to send.</param>
        /// <returns>True if the message was successfully queued for sending, false otherwise.</returns>
        public bool RemoteRequestWithoutResponse(byte[] args)
        {
            if (Interlocked.Read(ref _disposed) == 1) return false;
            if (!IsConnected) return false;

            ulong msgId = GetNextMessageId(); // ID is still generated but not tracked for response
            return SendMessageInternal(eMsgType.Request, msgId, args);
        }


        /// <summary>
        /// Sends a response back for a request that was handled by AsyncRemoteCallHandler.
        /// </summary>
        /// <param name="originalMsgId">The message ID received in AsyncRemoteCallHandler.</param>
        /// <param name="response">Tuple indicating success and the response payload.</param>
        public void AsyncAnswerOnRemoteCall(ulong originalMsgId, Tuple<bool, byte[]> response)
        {
            if (Interlocked.Read(ref _disposed) == 1) return;
            if (!IsConnected) return;

            if (response != null)
            {
                SendMessageInternal(response.Item1 ? eMsgType.RpcResponse : eMsgType.ErrorInRpc, GetNextMessageId(), response.Item2, originalMsgId);
            }
            else
            {
                // Send error if response is null? Or just do nothing? Sending error is safer.
                SendMessageInternal(eMsgType.ErrorInRpc, GetNextMessageId(), null, originalMsgId);
            }
        }

        /// <summary>
        /// Returns a string with current usage statistics.
        /// </summary>
        public string UsageReport()
        {
            Statistic.UpdatePending(_pendingRequests.Count); // Ensure count is up-to-date
            return this.Statistic.Report();
        }

        #endregion

        #region Timeout Handling (for Callbacks)

        // Timer callback to check for timed-out callback-based requests
        private void CheckTimeouts(object state)
        {
            if (Interlocked.Read(ref _disposed) == 1 || _pendingRequests.IsEmpty) return;

            DateTime now = DateTime.UtcNow;
            List<ulong> timedOutIds = new List<ulong>();

            try
            {
                // Iterate safely over a snapshot of the dictionary keys
                foreach (var kvp in _pendingRequests.ToList()) // ToList creates a snapshot
                {
                    ResponseCrate crate = kvp.Value;
                    // Only check timeouts for requests using callbacks or potentially stuck AMREs
                    if (crate != null && (crate.callBack != null /*|| crate.amre != null*/ )) // Only timeout callbacks explicitly for now
                    {
                        // Check if timeout is defined (might be MaxValue for non-timeout cases)
                        if (crate.TimeoutsMs > 0 && crate.TimeoutsMs != int.MaxValue)
                        {
                            if (now.Subtract(crate.created).TotalMilliseconds >= crate.TimeoutsMs)
                            {
                                timedOutIds.Add(kvp.Key);
                            }
                        }
                    }
                }

                // Process timed-out requests
                foreach (var msgId in timedOutIds)
                {
                    if (_pendingRequests.TryRemove(msgId, out var timedOutCrate))
                    {
                        Statistic.UpdatePending(_pendingRequests.Count);
                        Statistic.Timeout();
                        LogInfo($"Request {msgId} timed out (callback).");

                        if (timedOutCrate.callBack != null)
                        {
                            // Run callback async with failure
                            Task.Run(() =>
                            {
                                try { timedOutCrate.callBack(new Tuple<bool, byte[]>(false, null)); }
                                catch (Exception ex) { LogExceptionInternal("Exception in timeout callback", ex); }
                                finally { timedOutCrate.Dispose_MRE_AMRE(); } // Dispose after callback
                            });
                        }
                        else
                        {
                            // If it wasn't a callback but somehow got here (e.g., future AMRE timeout logic), signal it and dispose
                            timedOutCrate.IsRespOk = false;
                            timedOutCrate.res = null;
                            timedOutCrate.Set_MRE_AMRE();
                            timedOutCrate.Dispose_MRE_AMRE();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Avoid crashing the timer thread
                LogExceptionInternal("Error during timeout check", ex);
            }
        }

        #endregion

        #region Disposal

        /// <summary>
        /// Disposes resources used by the NamedPipesIPC instance.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                return; // Already disposed

            LogInfo("Disposing NamedPipesIPC...");

            // 1. Stop the timeout timer
            _timeoutTimer?.Dispose();

            // 2. Signal cancellation to all running tasks
            _connectionCts?.Cancel();

            // 3. Trigger disconnection handling (closes stream, cleans pending requests)
            HandleDisconnection(); // Call this explicitly to ensure cleanup happens now

            // 4. Wait briefly for tasks to shut down (optional, depends on requirements)
            // Consider using Task.WhenAll with a timeout on _connectionTask, _receiveTask, _sendTask
            // Example: Task.WhenAll(_connectionTask ?? Task.CompletedTask, _receiveTask ?? Task.CompletedTask, _sendTask ?? Task.CompletedTask).Wait(1000);
            // Be cautious with blocking waits in Dispose.

            // 5. Dispose cancellation token source
            _connectionCts?.Dispose();

            LogInfo("NamedPipesIPC disposed.");
            // Nullify handlers to prevent accidental use after dispose
            ExternalExceptionHandler = null;
            RemoteCallHandler = null;
            AsyncRemoteCallHandler = null;
            PeerConnected = null;
            PeerDisconnected = null;

        }

        #endregion

        #region Logging Helpers

        private void LogInfo(string message)
        {
            // Simple console log, replace with your preferred logging mechanism
            // Console.WriteLine($"[NamedPipesIPC:{_role}:{_pipeName}] INFO: {message}");
            if(Verbose)
                ExternalExceptionHandler?.Invoke($"[NamedPipesIPC:{_role}] INFO: {message}", null);

        }

        private void LogExceptionInternal(string context, Exception ex)
        {
            // Console.WriteLine($"[NamedPipesIPC:{_role}:{_pipeName}] ERROR: {context} - {ex}");
            Statistic.Error(); // Increment internal error count
            ExternalExceptionHandler?.Invoke($"[NamedPipesIPC:{_role}] ERROR: {context}", ex);
        }


        #endregion

        #region ResponseCrate Helper Class (Adapted from SharmIPC)

        // Internal class to hold state for pending RPC requests
        private class ResponseCrate : IDisposable
        {
            // --- Sync Wait ---
            public ManualResetEvent mre = null;

            // --- Async Wait ---
            public AsyncManualResetEvent amre = null; // Using our own AMRE implementation
                                                      // Or alternatively: public TaskCompletionSource<bool> Tcs { get; private set; }


            // --- Callback ---
            public Action<Tuple<bool, byte[]>> callBack = null;

            // --- Result ---
            public byte[] res = null;
            public bool IsRespOk = false;

            // --- Metadata ---
            public DateTime created = DateTime.UtcNow;
            public int TimeoutsMs = 30000; // Used by timer for callbacks, or Wait methods
            private long _isDisposed = 0;


            public void Init_MRE()
            {
                mre = new ManualResetEvent(false);
            }

            public void Init_AMRE()
            {
                amre = new AsyncManualResetEvent();
                // Tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public void Init_CallbackMode()
            {
                // No event needed for callback mode
            }

            public bool WaitOne_MRE(int timeoutMs)
            {
                if (Interlocked.Read(ref _isDisposed) == 1 || mre == null) return false;
                return mre.WaitOne(timeoutMs);
            }

            public Task<bool> WaitOne_AMRE_WithTimeout(int timeoutMs, CancellationToken token)
            {
                if (Interlocked.Read(ref _isDisposed) == 1 || amre == null) return Task.FromResult(false);
                return amre.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), token); // Use AMRE's timeout wait

                // Alternative with TCS:
                // if (Tcs == null) return Task.FromResult(false);
                // using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                // var timeoutTask = Task.Delay(timeoutMs, linkedCts.Token);
                // var completed = await Task.WhenAny(Tcs.Task, timeoutTask);
                // linkedCts.Cancel(); // Cancel the one that didn't complete
                // return completed == Tcs.Task;
            }

            public void Set_MRE_AMRE()
            {
                if (Interlocked.Read(ref _isDisposed) == 1) return;
                mre?.Set();
                amre?.Set();
                // Tcs?.TrySetResult(true);
            }


            public void Dispose_MRE_AMRE()
            {
                if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0) return;

                // Set events before disposing to unblock any waiters
                try { mre?.Set(); } catch { }
                try { amre?.Set(); } catch { }
                // try { Tcs?.TrySetCanceled(); } catch { } // Cancel TCS on dispose? Or let waiters timeout? Setting might be better.

                mre?.Dispose();
                // AMRE doesn't need dispose if it's just TCS based
                mre = null;
                amre = null; // Allow GC
                             // Tcs = null;
                callBack = null; // Remove reference
                res = null; // Allow GC
            }

            public void Dispose()
            {
                Dispose_MRE_AMRE();
            }
        }

        #endregion
    }
}
#pragma warning restore CA1416 // Validate platform compatibility