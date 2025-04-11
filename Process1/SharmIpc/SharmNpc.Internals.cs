using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tiesky.com;

namespace tiesky.com.SharmNpcInternals
{
    #region Helper Classes (Copied/Adapted from SharmIPC)

    // --- Enums ---
    public enum PipeRole
    {
        Server, // Creates and waits for connection
        Client  // Connects to existing server
    }

    // Matches SharmIPC internal enum
    internal enum eMsgType : byte
    {
        RpcRequest = 1,
        RpcResponse = 2,
        ErrorInRpc = 3,
        Request = 4,
    }

    // --- Statistic Class (Simplified for brevity) ---
    internal class Statistic
    {
        internal long TotalSent = 0;
        internal long TotalReceived = 0;
        internal long TotalTimeouts = 0;
        internal long TotalErrors = 0;
        internal long TotalBytesSent = 0;
        internal long TotalBytesReceived = 0;
        internal int CurrentPendingRequests = 0;
        internal SharmNpc ipc; // Reference back if needed

        internal void MessageSent(long bytes) { Interlocked.Increment(ref TotalSent); Interlocked.Add(ref TotalBytesSent, bytes); }
        internal void MessageReceived(long bytes) { Interlocked.Increment(ref TotalReceived); Interlocked.Add(ref TotalBytesReceived, bytes); }
        internal void Timeout() { Interlocked.Increment(ref TotalTimeouts); }
        internal void Error() { Interlocked.Increment(ref TotalErrors); }
        internal void UpdatePending(int count) { Interlocked.Exchange(ref CurrentPendingRequests, count); }

        public string Report()
        {
            return $"Sent: {TotalSent} ({TotalBytesSent} B), Received: {TotalReceived} ({TotalBytesReceived} B), Pending: {CurrentPendingRequests}, Timeouts: {TotalTimeouts}, Errors: {TotalErrors}, Connected: {ipc?.IsConnected}";
        }
    }

    // --- AsyncManualResetEvent (Keep as is) ---
    public class AsyncManualResetEvent
    {
        private volatile TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _mutex = new object();

        public Task WaitAsync()
        {
            lock (_mutex)
            {
                return _tcs.Task;
            }
        }

        public Task<bool> WaitAsync(TimeSpan timeout)
        {
            return WaitAsync(timeout, CancellationToken.None);
        }

        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            Task waitTask;
            lock (_mutex)
            {
                waitTask = _tcs.Task;
            }

            if (waitTask.IsCompleted) return true; // Already set

            //using var timeoutCts = new CancellationTokenSource();
            using (var timeoutCts = new CancellationTokenSource())
            {
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var completedTask = await Task.WhenAny(waitTask, Task.Delay(timeout, linkedCts.Token)).ConfigureAwait(false);

                if (completedTask == waitTask)
                {
                    linkedCts.Cancel(); // Cancel timeout delay task
                    return true; // Event was set
                }
                else
                {
                    // Timeout occurred or external cancellation
                    // We don't cancel the original TCS task, just return false for timeout
                    return false;
                }
            }
              
        }


        public void Set()
        {
            TaskCompletionSource<bool> tcsToSet = null;
            lock (_mutex)
            {
                tcsToSet = _tcs;
            }
            // Set result outside lock to avoid potential deadlocks if continuations run synchronously
            tcsToSet?.TrySetResult(true);
        }

        public void Reset()
        {
            lock (_mutex)
            {
                if (_tcs.Task.IsCompleted)
                    _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }


    // --- BytesProcessing (Keep as is) ---
    internal static class BytesProcessing
    {
        // Substring and Concat methods (optional, can use Linq or Spans)

        public static byte[] ToProtoBytes(this ulong value)
        {
            var buffer = new byte[10]; // Max 10 bytes for ulong varint
            var pos = 0;
            do
            {
                var byteVal = value & 0x7f;
                value >>= 7;

                if (value != 0)
                {
                    byteVal |= 0x80;
                }
                buffer[pos++] = (byte)byteVal;
            } while (value != 0);

            var result = new byte[pos];
            Buffer.BlockCopy(buffer, 0, result, 0, pos);
            return result;
        }

        public static ulong FromProtoBytes(this byte[] bytes, out int bytesRead)
        {
            bytesRead = 0;
            int shift = 0;
            ulong result = 0;
            foreach (byte byteValue in bytes)
            {
                bytesRead++;
                ulong tmp = byteValue & 0x7fUL;
                result |= tmp << shift;

                if (shift > 63) // Protect against invalid data causing overflow
                {
                    throw new ArgumentException("Varint decoding exceeded maximum size.", nameof(bytes));
                }

                if ((byteValue & 0x80) != 0x80)
                {
                    return result;
                }
                shift += 7;
            }
            throw new ArgumentException("Cannot decode varint from byte array, unexpected end.", nameof(bytes));
        }

        // Helper overload if you don't need bytesRead immediately
        public static ulong FromProtoBytes(this byte[] bytes)
        {
            return FromProtoBytes(bytes, out _);
        }

        public static ulong ReadProtoUInt64(Stream stream, out int bytesRead)
        {
            bytesRead = 0;
            int shift = 0;
            ulong result = 0;
            byte[] buffer = new byte[1];

            while (shift <= 63) // Max 10 bytes for ulong (7 bits payload per byte)
            {
                int bytesReadFromStream = stream.Read(buffer, 0, 1);
                if (bytesReadFromStream == 0) throw new EndOfStreamException("Unexpected end of stream while reading varint.");

                byte byteValue = buffer[0];
                bytesRead++;
                ulong tmp = byteValue & 0x7fUL;
                result |= tmp << shift;

                if ((byteValue & 0x80) != 0x80)
                {
                    return result;
                }
                shift += 7;
            }

            throw new ArgumentException("Varint decoding exceeded maximum size.", nameof(stream));
        }
    }

    #endregion Helper Classes
}
