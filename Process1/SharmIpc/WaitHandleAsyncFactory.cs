/*
 In this class all credits to https://github.com/StephenCleary/AsyncEx 
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tiesky.com
{
    /// <summary>
    /// Provides interop utilities for <see cref="WaitHandle"/> types.
    /// </summary>
    public static class WaitHandleAsyncFactory
    {
        /// <summary>
        /// Wraps a <see cref="WaitHandle"/> with a <see cref="Task"/>. When the <see cref="WaitHandle"/> is signalled, the returned <see cref="Task"/> is completed. If the handle is already signalled, this method acts synchronously.
        /// </summary>
        /// <param name="handle">The <see cref="WaitHandle"/> to observe.</param>
        public unsafe static Task<bool> FromWaitHandle(WaitHandle handle)
        {
            // return FromWaitHandle(handle, Timeout.InfiniteTimeSpan);
            return FromWaitHandle(handle, new TimeSpan(0,0,20));
        }

        public static Task<bool> FromWaitHandle(WaitHandle handle, TimeSpan timeout)
        {
            // Handle synchronous cases.
            var alreadySignalled = handle.WaitOne(0);
            if (alreadySignalled)
                return Task.FromResult(true);
            if (timeout == TimeSpan.Zero)
                return Task.FromResult(false);

            // Register all asynchronous cases.
            var tcs = new TaskCompletionSource<bool>();

            var threadPoolRegistration = ThreadPool.UnsafeRegisterWaitForSingleObject(handle,
                (state, timedOut) => ((TaskCompletionSource<bool>)state).TrySetResult(!timedOut),
                tcs, timeout,true);

            tcs.Task.ContinueWith(_ =>
            {
                threadPoolRegistration.Unregister(handle);
                threadPoolRegistration = null;
            }, TaskScheduler.Default);
            return tcs.Task;
        }
        
    }
}
