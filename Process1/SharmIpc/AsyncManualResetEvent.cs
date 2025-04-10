/*
 * Credits to
 * https://github.com/StephenCleary/AsyncEx
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;


namespace tiesky.com.SharmIpcInternals
{

    public class AsyncManualResetEvent
    {
        // can be used WaitHandleAsyncFactory.cs (From WaitHandle)

        private volatile TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();
        private readonly object _mutex;

        public AsyncManualResetEvent()
        {
            _mutex = new object();
        }

        public Task<bool> WaitAsync()
        {
            lock (_mutex)
            {
                return _tcs.Task;
            }
        }


        public void Set()
        {
            lock (_mutex)
            {
                _tcs.TrySetResult(true);
            }
        }

        public void Reset()
        {
            lock (_mutex)
            {
                if (_tcs.Task.IsCompleted)
                    _tcs = new TaskCompletionSource<bool>();
            }
        }

    }


    // can be used WaitHandleAsyncFactory.cs (From WaitHandle)
    //public static class WaitHandleExtensions
    //{
    //    public static Task AsTask(this WaitHandle handle)
    //    {
    //        return AsTask(handle, Timeout.InfiniteTimeSpan);
    //    }

    //    public static Task AsTask(this WaitHandle handle, TimeSpan timeout)
    //    {
    //        var tcs = new TaskCompletionSource<object>();
    //        var registration = ThreadPool.RegisterWaitForSingleObject(handle, (state, timedOut) =>
    //        {
    //            var localTcs = (TaskCompletionSource<object>)state;
    //            if (timedOut)
    //                localTcs.TrySetResult(null);
    //            //localTcs.TrySetCanceled();
    //            else
    //                localTcs.TrySetResult(null);
    //        }, tcs, timeout, executeOnlyOnce: true);
    //        tcs.Task.ContinueWith((_, state) => ((RegisteredWaitHandle)state).Unregister(null), registration, TaskScheduler.Default);
    //        return tcs.Task;
    //    }
    //}


    //public class AsyncManualResetEvent
    //{
    //    private volatile TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();

    //    public Task WaitAsync() { return _tcs.Task; }
    //    public void Set() { _tcs.TrySetResult(true); }
    //    public void Reset()
    //    {

    //        while (true)
    //        {
    //            var tcs = _tcs;
    //            if (!tcs.Task.IsCompleted ||
    //                Interlocked.CompareExchange(ref _tcs, new TaskCompletionSource<bool>(), tcs) == tcs)
    //                return;
    //        }
    //    }

    //}
}
