using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using tiesky.com.SharmNpcInternals;

namespace tiesky.com
{
    /*
     In the new implementation, the library takes ownership of the Streams you pass to it. 
    Once the SharmNpc finishes sending a stream over the pipe, it will automatically call .Dispose() on that stream. 
    Therefore, you do not need to wrap your outgoing streams in using blocks!
     */
    public partial class SharmNpc
    {
        // Define virtual message types extending your existing eMsgType enum.
        // We use numbers starting from 20 to avoid collisions with Request(0), RpcRequest(1), etc.
        private const eMsgType MsgType_StreamRpcStart = (eMsgType)20;
        private const eMsgType MsgType_StreamData = (eMsgType)21;
        private const eMsgType MsgType_StreamEnd = (eMsgType)22;
        private const eMsgType MsgType_StreamRpcResponseStart = (eMsgType)23;

        /// <summary>
        /// Handler for incoming RPC requests that include a Stream payload.
        /// Returns a Tuple containing: (Success Flag, Response Args, Optional Response Stream).
        /// </summary>
        public Func<ulong, byte[], Stream, Task<(bool, byte[], Stream)>> AsyncStreamCallHandler { get; set; } = null;

        private readonly ConcurrentDictionary<ulong, IpcReceiverStream> _activeIncomingStreams = new();

        #region Public Stream API

        /// <summary>
        /// Sends an RPC request with an attached stream payload.
        /// </summary>
        /// <param name="args">Header arguments/metadata for the request.</param>
        /// <param name="payloadStream">The stream of data to send over the pipe.</param>
        /// <param name="timeoutMs">Timeout to wait for the complete response.</param>
        /// <returns>A tuple of (Success, Response Data, Optional Response Stream)</returns>
        public async Task<(bool success, byte[] responseData, Stream responseStream)> RemoteRequestStreamAsync(
            byte[] args, Stream payloadStream, int timeoutMs = 30000)
        {
            if (Interlocked.Read(ref _disposed) == 1 || !IsConnected) return (false, null, null);

            ulong msgId = GetNextMessageId();
            var crate = new ResponseCrate();
            crate.TimeoutsMs = timeoutMs;
            crate.Init_AMRE();

            if (!_pendingRequests.TryAdd(msgId, crate))
            {
                crate.Dispose_MRE_AMRE();
                return (false, null, null);
            }
            Statistic.UpdatePending(_pendingRequests.Count);

            try
            {
                // 1. [streamStart] Send the header framing
                if (!SendStreamFrameInternal(MsgType_StreamRpcStart, msgId, args))
                    return (false, null, null);

                // 2. [data] Send the stream chunks in the background (respects backpressure)
                _ = Task.Run(() => ProcessOutgoingStreamAsync(msgId, payloadStream, _connectionCts.Token));

                // 3. Wait for the response
                if (!await crate.WaitOne_AMRE_WithTimeout(timeoutMs, CancellationToken.None).ConfigureAwait(false))
                {
                    Statistic.Timeout();
                    return (false, null, null);
                }

                // If the response is also a stream, it will be mapped here
                _activeIncomingStreams.TryGetValue(msgId, out var responseStreamObj);

                return (crate.IsRespOk, crate.res, responseStreamObj);
            }
            finally
            {
                if (_pendingRequests.TryRemove(msgId, out var removedCrate))
                {
                    Statistic.UpdatePending(_pendingRequests.Count);
                    removedCrate.Dispose_MRE_AMRE();
                }
            }
        }

        #endregion

        #region Internal Stream Processing (Receive)

        ///// <summary>
        ///// Bridges the synchronous receive loop to our asynchronous stream channels.
        ///// </summary>
        internal async ValueTask HandleIncomingStreamMessageAsync(eMsgType msgType, ulong trackingId, ReadOnlyMemory<byte> payloadMemory)
        {
            switch (msgType)
            {
                case MsgType_StreamRpcStart:
                case MsgType_StreamRpcResponseStart:
                    var receiverStream = new IpcReceiverStream(trackingId, 64);
                    _activeIncomingStreams[trackingId] = receiverStream;

                    byte[] args = payloadMemory.Length > 0 ? payloadMemory.ToArray() : Array.Empty<byte>();

                    if (msgType == MsgType_StreamRpcStart)
                    {
                        if (AsyncStreamCallHandler != null)
                        {
                            Task.Run(async () =>
                            {
                                try
                                {
                                    var result = await AsyncStreamCallHandler(trackingId, args, receiverStream).ConfigureAwait(false);
                                    if (result.Item3 != null)
                                    {
                                        SendStreamFrameInternal(MsgType_StreamRpcResponseStart, trackingId, result.Item2, trackingId);
                                        await ProcessOutgoingStreamAsync(trackingId, result.Item3, _connectionCts.Token).ConfigureAwait(false);
                                    }
                                    else
                                    {
                                        SendMessageInternal(result.Item1 ? eMsgType.RpcResponse : eMsgType.ErrorInRpc, GetNextMessageId(), result.Item2, trackingId);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogExceptionInternal("Exception in AsyncStreamCallHandler", ex);
                                    SendMessageInternal(eMsgType.ErrorInRpc, GetNextMessageId(), null, trackingId);
                                }
                            });
                        }
                    }
                    else if (msgType == MsgType_StreamRpcResponseStart)
                    {
                        if (_pendingRequests.TryGetValue(trackingId, out var crate))
                        {
                            crate.IsRespOk = true;
                            crate.res = args;
                            crate.Set_MRE_AMRE();
                        }
                    }
                    break;

                case MsgType_StreamData:
                    if (_activeIncomingStreams.TryGetValue(trackingId, out var stream))
                    {
                        byte[] chunk = ArrayPool<byte>.Shared.Rent(payloadMemory.Length);
                        payloadMemory.Span.CopyTo(chunk);

                        // THIS IS THE MAGIC FIX: 
                        // Await safely. If channel isn't full, ValueTask completes synchronously.
                        // If it IS full, it releases the thread back to the ThreadPool.
                        await stream.WriteChunkAsync(new StreamChunk(chunk, payloadMemory.Length)).ConfigureAwait(false);
                    }
                    break;

                case MsgType_StreamEnd:
                    if (_activeIncomingStreams.TryRemove(trackingId, out var finishingStream))
                    {
                        finishingStream.Complete();
                    }
                    break;
            }
        }



        ///// <summary>
        ///// Bridges the synchronous receive loop to our asynchronous stream channels.
        ///// </summary>
        //internal void HandleIncomingStreamMessage(eMsgType msgType, ulong trackingId, ReadOnlySpan<byte> payloadSpan)
        //{
        //    switch (msgType)
        //    {
        //        case MsgType_StreamRpcStart:
        //        case MsgType_StreamRpcResponseStart:
        //            // Create a bounded receiver stream (Memory Friendly - prevents infinite buffering)
        //            var receiverStream = new IpcReceiverStream(trackingId, 64); // Max 64 buffered chunks (~32-64MB)
        //            _activeIncomingStreams[trackingId] = receiverStream;

        //            byte[] args = payloadSpan.Length > 0 ? payloadSpan.ToArray() : Array.Empty<byte>();

        //            if (msgType == MsgType_StreamRpcStart)
        //            {
        //                if (AsyncStreamCallHandler != null)
        //                {
        //                    // Trigger the user's handler
        //                    Task.Run(async () =>
        //                    {
        //                        try
        //                        {
        //                            var result = await AsyncStreamCallHandler(trackingId, args, receiverStream).ConfigureAwait(false);

        //                            // Handle Response (can be stream or byte array)
        //                            if (result.Item3 != null)
        //                            {
        //                                SendStreamFrameInternal(MsgType_StreamRpcResponseStart, trackingId, result.Item2, trackingId);
        //                                await ProcessOutgoingStreamAsync(trackingId, result.Item3, _connectionCts.Token).ConfigureAwait(false);
        //                            }
        //                            else
        //                            {
        //                                SendMessageInternal(result.Item1 ? eMsgType.RpcResponse : eMsgType.ErrorInRpc, GetNextMessageId(), result.Item2, trackingId);
        //                            }
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            LogExceptionInternal("Exception in AsyncStreamCallHandler", ex);
        //                            SendMessageInternal(eMsgType.ErrorInRpc, GetNextMessageId(), null, trackingId);
        //                        }
        //                    });
        //                }
        //            }
        //            else if (msgType == MsgType_StreamRpcResponseStart)
        //            {
        //                // Signal the pending RemoteRequestStreamAsync that headers arrived, and the stream is available
        //                if (_pendingRequests.TryGetValue(trackingId, out var crate))
        //                {
        //                    crate.IsRespOk = true;
        //                    crate.res = args;
        //                    crate.Set_MRE_AMRE();
        //                }
        //            }
        //            break;

        //        case MsgType_StreamData:
        //            if (_activeIncomingStreams.TryGetValue(trackingId, out var stream))
        //            {
        //                // Copy payload out of the rented array so ReceiveLoop can reuse it
        //                byte[] chunk = ArrayPool<byte>.Shared.Rent(payloadSpan.Length);
        //                payloadSpan.CopyTo(chunk);

        //                // Push to channel. If channel is full, this blocks the Receive loop naturally,
        //                // which pushes backpressure directly to the OS named pipe buffer (Safe & highly efficient).
        //                stream.WriteChunk(new StreamChunk(chunk, payloadSpan.Length));
        //            }
        //            break;

        //        case MsgType_StreamEnd:
        //            if (_activeIncomingStreams.TryRemove(trackingId, out var finishingStream))
        //            {
        //                finishingStream.Complete();
        //            }
        //            break;
        //    }
        //}

        internal void AbortAllStreams()
        {
            foreach (var stream in _activeIncomingStreams.Values)
            {
                stream.Complete(new IOException("IPC connection disconnected."));
            }
            _activeIncomingStreams.Clear();
        }

        #endregion

        #region Internal Stream Processing (Send)

        /// <summary>
        /// Reads from the user's stream and feeds it into the pipe frame queue efficiently.
        /// </summary>
        private async Task ProcessOutgoingStreamAsync(ulong trackingId, Stream payloadStream, CancellationToken ct)
        {
            // Rent a reasonably sized buffer for stream transit (512KB fits well inside typical IPC queues)
            byte[] buffer = ArrayPool<byte>.Shared.Rent(512 * 1024);
            try
            {
                int bytesRead;
                while ((bytesRead = await payloadStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                {
                    // Memory safety: Apply backpressure if the send queue is getting too large!
                    while (Interlocked.Read(ref _currentQueueSize) > (_maxQueueSizeInBytes * 0.75))
                    {
                        await Task.Delay(10, ct).ConfigureAwait(false); // Yield until SendLoop drains the queue
                    }

                    if (!SendStreamFrameInternal(MsgType_StreamData, trackingId, buffer.AsSpan(0, bytesRead)))
                    {
                        LogInfo($"Failed to queue stream chunk for tracking ID {trackingId}.");
                        break;
                    }
                }

                // [streamEnd]
                SendStreamFrameInternal(MsgType_StreamEnd, trackingId, ReadOnlySpan<byte>.Empty);
            }
            catch (Exception ex)
            {
                LogExceptionInternal($"Error pushing stream data to IPC queue for ID {trackingId}", ex);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                payloadStream.Dispose();
            }
        }

        /// <summary>
        /// Optimized zero-allocation frame builder specifically for single-chunk stream packets.
        /// </summary>
        private bool SendStreamFrameInternal(eMsgType msgType, ulong msgId, ReadOnlySpan<byte> chunkData, ulong responseMsgId = 0)
        {
            if (!IsConnected || Interlocked.Read(ref _disposed) == 1) return false;

            try
            {
                int chunkLen = chunkData.Length;
                ulong payloadIndicator = (chunkLen == 0) ? int.MaxValue : (ulong)chunkLen;

                int headersSize = GetVarIntSize((ulong)msgType) + GetVarIntSize(msgId) +
                                  GetVarIntSize(payloadIndicator) + GetVarIntSize(1) +
                                  GetVarIntSize(1) + GetVarIntSize(responseMsgId);

                int totalFrameSize = headersSize + chunkLen + 4;

                byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(totalFrameSize);
                Span<byte> span = rentedBuffer.AsSpan();

                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), totalFrameSize - 4);
                int pos = 4;

                pos += WriteVarInt(span.Slice(pos), (ulong)msgType);
                pos += WriteVarInt(span.Slice(pos), msgId);
                pos += WriteVarInt(span.Slice(pos), payloadIndicator);
                pos += WriteVarInt(span.Slice(pos), 1); // curChunk
                pos += WriteVarInt(span.Slice(pos), 1); // totChunk
                pos += WriteVarInt(span.Slice(pos), responseMsgId);

                if (chunkLen > 0)
                {
                    chunkData.CopyTo(span.Slice(pos));
                }

                _sendQueue.Enqueue(new QueuedFrame(rentedBuffer, totalFrameSize));
                Interlocked.Add(ref _currentQueueSize, totalFrameSize);

                _ = SendLoopAsync();
                return true;
            }
            catch (Exception ex)
            {
                LogExceptionInternal("Error serializing stream message", ex);
                return false;
            }
        }

        #endregion

        #region Internal Types

        internal readonly struct StreamChunk
        {
            public readonly byte[] Buffer;
            public readonly int Length;

            public StreamChunk(byte[] buffer, int length)
            {
                Buffer = buffer;
                Length = length;
            }
        }

        /// <summary>
        /// A read-only stream that consumes incoming IPC chunks fed via a Channel.
        /// Manages internal ArrayPool arrays safely.
        /// </summary>
        private class IpcReceiverStream : Stream
        {
            private readonly ulong _trackingId;
            private readonly Channel<StreamChunk> _channel;
            private StreamChunk _currentChunk;
            private int _currentChunkPosition;

            public IpcReceiverStream(ulong trackingId, int maxQueuedChunks)
            {
                _trackingId = trackingId;
                // Bounded channel creates safety constraint against runaway memory allocation
                _channel = Channel.CreateBounded<StreamChunk>(new BoundedChannelOptions(maxQueuedChunks)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleWriter = true,
                    SingleReader = true
                });
            }

            //public void WriteChunk(StreamChunk chunk)
            //{
            //    // Synchronously block to apply backpressure to the pipe if buffer is full
            //    _channel.Writer.WriteAsync(chunk).AsTask().GetAwaiter().GetResult();
            //}

            public ValueTask WriteChunkAsync(StreamChunk chunk)
            {
                return _channel.Writer.WriteAsync(chunk);
            }

            public void Complete(Exception error = null)
            {
                _channel.Writer.TryComplete(error);
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (_currentChunk.Buffer == null || _currentChunkPosition >= _currentChunk.Length)
                {
                    if (_currentChunk.Buffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(_currentChunk.Buffer);
                        _currentChunk = default;
                    }

                    try
                    {
                        if (!await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                            return 0; // Stream completed

                        _channel.Reader.TryRead(out _currentChunk);
                        _currentChunkPosition = 0;
                    }
                    catch (ChannelClosedException)
                    {
                        return 0;
                    }
                }

                int available = _currentChunk.Length - _currentChunkPosition;
                int toCopy = Math.Min(count, available);

                Buffer.BlockCopy(_currentChunk.Buffer, _currentChunkPosition, buffer, offset, toCopy);
                _currentChunkPosition += toCopy;

                return toCopy;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Complete();
                    if (_currentChunk.Buffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(_currentChunk.Buffer);
                        _currentChunk = default;
                    }
                }
                base.Dispose(disposing);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        #endregion
    }
}