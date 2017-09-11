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


namespace tiesky.com
{

    public class AsyncManualResetEvent
    {
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
