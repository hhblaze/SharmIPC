using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using tiesky.com.SharmIpcInternals;

namespace tiesky.com
{
    
    /// <summary>
    /// Inter-process communication handler. IPC for .NET
    /// https://github.com/hhblaze/SharmIPC or http://sharmipc.tiesky.com
    /// </summary>
    public class SharmIpc:IDisposable
    {
        Func<byte[], Tuple<bool, byte[]>> remoteCallHandler = null;
        /// <summary>
        /// If we don't want to answer in sync way via remoteCallHandler
        /// msgId and data, msgId must be returned back with AsyncAnswerOnRemoteCall
        /// </summary>
        Action<ulong,byte[]> AsyncRemoteCallHandler = null;

        SharedMemory sm = null;
        ConcurrentDictionary<ulong, ResponseCrate> df = new ConcurrentDictionary<ulong, ResponseCrate>();

        internal Statistic Statistic = new Statistic();

        /// <summary>
        /// Removing timeout requests
        /// </summary>
        System.Threading.Timer tmr = null;

        class ResponseCrate
        {
            /// <summary>
            /// Not SLIM version must be used (it works faster for longer delay which RPCs are)
            /// </summary>
            ManualResetEvent mre = null;
            public byte[] res = null;
            public Action<Tuple<bool, byte[]>> callBack = null;
            public bool IsRespOk = false;

            public AsyncManualResetEvent amre = null;

            public DateTime created = DateTime.UtcNow;
            public int Timeouts = 30;

            public void Init_MRE()
            {
                mre = new ManualResetEvent(false);
            }

            public void Init_AMRE()
            {
                amre = new AsyncManualResetEvent();
            }

            public void Set_MRE()
            {
                //if (Interlocked.Read(ref IsDisposed) == 1)
                //    return;

                if (amre != null)
                {
                    amre.Set();
                }
                else if (mre != null)
                { 
                    mre.Set();
                }

                //if (Interlocked.Read(ref IsDisposed) == 1 || mre == null)
                //    return;
               
            }

            public bool WaitOne_MRE(int timeouts)
            {
                if (Interlocked.Read(ref IsDisposed) == 1 || mre == null)
                    return false;
                return mre.WaitOne(timeouts);
            }

            long IsDisposed = 0;
            public void Dispose_MRE()
            {
                if (System.Threading.Interlocked.CompareExchange(ref IsDisposed, 1, 0) != 0)
                    return;

                if (mre != null)
                {
                    mre.Set();
                    mre.Dispose();
                    mre = null;
                }
                else if (amre != null)
                {
                    amre.Set();
                    amre = null;
                }
            }
          
        }

      

        /// <summary>
        /// SharmIpc constructor
        /// </summary>
        /// <param name="uniqueHandlerName">Must be unique in OS scope (can be PID [ID of the process] + other identifications)</param>
        /// <param name="remoteCallHandler">Response routine for the remote partner requests</param>
        /// <param name="bufferCapacity">bigger buffer sends larger datablocks faster. Default value is 50000</param>
        /// <param name="maxQueueSizeInBytes">If remote partner is temporary not available, messages are accumulated in the sending buffer. This value sets the upper threshold of the buffer in bytes.</param>
        /// <param name="ExternalExceptionHandler">External exception handler can be supplied, will be returned Description from SharmIPC, like class.method name and handeled exception</param>
        public SharmIpc(string uniqueHandlerName, Func<byte[], Tuple<bool, byte[]>> remoteCallHandler, long bufferCapacity = 50000, int maxQueueSizeInBytes = 20000000, Action<string, System.Exception> ExternalExceptionHandler = null)
            :this(uniqueHandlerName,bufferCapacity,maxQueueSizeInBytes,ExternalExceptionHandler)
        {
            this.remoteCallHandler = remoteCallHandler ?? throw new Exception("tiesky.com.SharmIpc: remoteCallHandler can't be null");
        }

        /// <summary>
        /// SharmIpc constructor
        /// </summary>
        /// <param name="uniqueHandlerName">Must be unique in OS scope (can be PID [ID of the process] + other identifications)</param>
        /// <param name="remoteCallHandler">Callback routine for the remote partner requests. AsyncAnswerOnRemoteCall must be used for answer</param>
        /// <param name="bufferCapacity">bigger buffer sends larger datablocks faster. Default value is 50000</param>
        /// <param name="maxQueueSizeInBytes">If remote partner is temporary not available, messages are accumulated in the sending buffer. This value sets the upper threshold of the buffer in bytes.</param>
        /// <param name="ExternalExceptionHandler">External exception handler can be supplied, will be returned Description from SharmIPC, like class.method name and handeled exception</param>
        public SharmIpc(string uniqueHandlerName, Action<ulong, byte[]> remoteCallHandler, long bufferCapacity = 50000, int maxQueueSizeInBytes = 20000000, Action<string, System.Exception> ExternalExceptionHandler = null)
            : this(uniqueHandlerName, bufferCapacity, maxQueueSizeInBytes, ExternalExceptionHandler)
        {          
            this.AsyncRemoteCallHandler = remoteCallHandler ?? throw new Exception("tiesky.com.SharmIpc: remoteCallHandler can't be null"); ;
           
        }

        SharmIpc(string uniqueHandlerName, long bufferCapacity = 50000, int maxQueueSizeInBytes = 20000000, Action<string, System.Exception> ExternalExceptionHandler = null)
        {
            tmr = new Timer(new TimerCallback((state) =>
            {
                DateTime now = DateTime.UtcNow;
                foreach (var el in df.Where(r => r.Value.amre != null && now.Subtract(r.Value.created).TotalMilliseconds >= r.Value.Timeouts))
                    el.Value.Set_MRE();
            }), null, 10000, 10000);


            this.ExternalExceptionHandler = ExternalExceptionHandler;
            sm = new SharedMemory(uniqueHandlerName, this, bufferCapacity, maxQueueSizeInBytes);

        }


        Action<string, System.Exception> ExternalExceptionHandler = null;

        /// <summary>
        /// Internal exception logger
        /// </summary>
        /// <param name="description"></param>
        /// <param name="ex"></param>
        internal void LogException(string description, System.Exception ex)
        {
            ExternalExceptionHandler?.Invoke(description, ex);
        }

        /// <summary>
        /// In case if asyncRemoteCallHandler != null
        /// </summary>
        /// <param name="msgId"></param>
        /// <param name="res"></param>
        public void AsyncAnswerOnRemoteCall(ulong msgId, Tuple<bool, byte[]> res)
        {
            if (res != null && sm != null)
                sm.SendMessage(res.Item1 ? eMsgType.RpcResponse : eMsgType.ErrorInRpc, sm.GetMessageId(), res.Item2, msgId);
        }

        //async Task CallAsyncRemoteHandler(ulong msgId, byte[] bt)
        //{
        //    AsyncRemoteCallHandler(msgId, bt);
        //}

        /// <summary>
        /// Any incoming data from remote partner is accumulated here
        /// </summary>
        /// <param name="msgType"></param>
        /// <param name="msgId"></param>
        /// <param name="bt"></param>
        internal void InternalDataArrived(eMsgType msgType, ulong msgId, byte[] bt)
        {
            ResponseCrate rsp = null;

            switch (msgType)
            {
                case eMsgType.Request:

                    Task.Run(() =>
                    {
                        if (AsyncRemoteCallHandler != null)
                        {
                            //CallAsyncRemoteHandler(msgId, bt);
                            AsyncRemoteCallHandler(msgId, bt);
                            //Answer must be supplied via AsyncAnswerOnRemoteCall
                        }
                        else
                        {
                            this.remoteCallHandler(bt);
                        }
                    });

                    break;
                case eMsgType.RpcRequest:

                    Task.Run(() =>
                    {
                        if (AsyncRemoteCallHandler != null)
                        {
                            AsyncRemoteCallHandler(msgId, bt);
                            //Answer must be supplied via AsyncAnswerOnRemoteCall
                        }
                        else
                        {
                            var res = this.remoteCallHandler(bt);
                            sm.SendMessage(res.Item1 ? eMsgType.RpcResponse : eMsgType.ErrorInRpc, sm.GetMessageId(), res.Item2, msgId);
                        }
                    });

                    break;
                case eMsgType.ErrorInRpc:
                case eMsgType.RpcResponse:

                    if (df.TryGetValue(msgId, out rsp))
                    {
                        rsp.res = bt;
                        rsp.IsRespOk = msgType == eMsgType.RpcResponse;

                        if (rsp.callBack == null)
                        {

                            //rsp.mre.Set();  //Signalling, to make waiting in parallel thread to proceed
                            rsp.Set_MRE();
                        }
                        else
                        {
                            df.TryRemove(msgId, out rsp);
                            //Calling callback in parallel thread, quicly to return to ReaderWriterhandler.Reader procedure
                            Task.Run(() =>
                            {
                                rsp.callBack(new Tuple<bool, byte[]>(rsp.IsRespOk, bt));
                            });
                        }
                    }

                    break;
            }
        }

        //internal void InternalDataArrived(eMsgType msgType, ulong msgId, byte[] bt)
        //{
        //    ResponseCrate rsp = null;

        //    switch (msgType)
        //    {
        //        case eMsgType.Request:


        //            if (AsyncRemoteCallHandler != null)
        //            {
        //                RunAsync(msgId, bt);
        //            }
        //            else
        //            {
        //                RunV1(bt);

        //            }

        //            break;
        //        case eMsgType.RpcRequest:

        //            if (AsyncRemoteCallHandler != null)
        //            {
        //                RunAsync(msgId, bt);
        //                //Answer must be supplied via AsyncAnswerOnRemoteCall
        //            }
        //            else
        //            {
        //                Run(msgId, bt);
        //            }


        //            break;
        //        case eMsgType.ErrorInRpc:
        //        case eMsgType.RpcResponse:

        //            if (df.TryGetValue(msgId, out rsp))
        //            {
        //                rsp.res = bt;
        //                rsp.IsRespOk = msgType == eMsgType.RpcResponse;

        //                if (rsp.callBack == null)
        //                {

        //                    //rsp.mre.Set();  //Signalling, to make waiting in parallel thread to proceed
        //                    rsp.Set_MRE();
        //                }
        //                else
        //                {
        //                    df.TryRemove(msgId, out rsp);
        //                    //Calling callback in parallel thread, quicly to return to ReaderWriterhandler.Reader procedure
        //                    RunV2(rsp, bt);

        //                }
        //            }

        //            break;
        //    }
        //}

        //async Task RunAsync(ulong msgId, byte[] bt)
        //{
        //    AsyncRemoteCallHandler(msgId, bt);
        //}

        //async Task Run(ulong msgId, byte[] bt)
        //{
        //    var res = this.remoteCallHandler(bt);
        //    sm.SendMessage(res.Item1 ? eMsgType.RpcResponse : eMsgType.ErrorInRpc, sm.GetMessageId(), res.Item2, msgId);
        //}

        //async Task RunV1(byte[] bt)
        //{
        //    this.remoteCallHandler(bt);
        //}

        //async Task RunV2(ResponseCrate rsp, byte[] bt)
        //{
        //    rsp.callBack(new Tuple<bool, byte[]>(rsp.IsRespOk, bt));
        //}


        /// <summary>
        /// 
        /// </summary>
        /// <param name="args">payload which must be send to remote partner</param>
        /// <param name="callBack">if specified then response for the request will be returned into callBack (async). Default is sync.</param>
        /// <param name="timeoutMs">Default 30 sec</param>
        /// <returns></returns>
        public Tuple<bool, byte[]> RemoteRequest(byte[] args, Action<Tuple<bool, byte[]>> callBack = null, int timeoutMs = 30000)
        {
       
            ulong msgId = sm.GetMessageId();
            var resp = new ResponseCrate();

            if (callBack != null)
            {
                //Async return
                resp.callBack = callBack;
                df[msgId] = resp;
                if (!sm.SendMessage(eMsgType.RpcRequest, msgId, args))
                {
                    df.TryRemove(msgId, out resp);
                    callBack(new Tuple<bool, byte[]>(false, null));
                    return new Tuple<bool, byte[]>(false, null);
                }

                return new Tuple<bool, byte[]>(true, null);
            }


            //resp.mre = new ManualResetEvent(false);
            resp.Init_MRE();

            df[msgId] = resp;          

            if (!sm.SendMessage(eMsgType.RpcRequest, msgId, args))
            {
                resp.Dispose_MRE();
                //if (resp.mre != null)
                //    resp.mre.Dispose();
                //resp.mre = null;
                df.TryRemove(msgId, out resp);
                return new Tuple<bool, byte[]>(false, null);
            }
            //else if (!resp.mre.WaitOne(timeoutMs))
            else if (!resp.WaitOne_MRE(timeoutMs))
            {
                //--STAT
                this.Statistic.Timeout();

                //if (resp.mre != null)
                //    resp.mre.Dispose();
                //resp.mre = null;
                resp.Dispose_MRE();
                df.TryRemove(msgId, out resp);
                return new Tuple<bool, byte[]>(false, null);
            }

            //if (resp.mre != null)
            //    resp.mre.Dispose();
            //resp.mre = null;
            resp.Dispose_MRE();

            if (df.TryRemove(msgId, out resp))
            {
                return new Tuple<bool, byte[]>(resp.IsRespOk, resp.res);
            }

            return new Tuple<bool, byte[]>(false, null);
        }


        /// <summary>
        /// Usage var x = await RemoteRequestAsync(...);
        /// </summary>
        /// <param name="args">payload which must be send to remote partner</param>
        /// <param name="callBack">if specified then response for the request will be returned into callBack (async). Default is sync.</param>
        /// <param name="timeoutMs">Default 30 sec</param>
        /// <returns></returns>
        public async Task<Tuple<bool, byte[]>> RemoteRequestAsync(byte[] args, Action<Tuple<bool, byte[]>> callBack = null, int timeoutMs = 30000)
        {
            if (Interlocked.Read(ref Disposed) == 1)
                return new Tuple<bool, byte[]>(false, null);

            ulong msgId = sm.GetMessageId();
            var resp = new ResponseCrate();

            if (callBack != null)
            {
                //Async return
                resp.callBack = callBack;
                df[msgId] = resp;
                if (!sm.SendMessage(eMsgType.RpcRequest, msgId, args))
                {
                    df.TryRemove(msgId, out resp);
                    callBack(new Tuple<bool, byte[]>(false, null));
                    return new Tuple<bool, byte[]>(false, null);
                }

                return new Tuple<bool, byte[]>(true, null);
            }
            
            //resp.mre = new ManualResetEvent(false);
            resp.Init_AMRE();
            resp.Timeouts = timeoutMs;

            df[msgId] = resp;

            if (!sm.SendMessage(eMsgType.RpcRequest, msgId, args))
            {
                resp.Dispose_MRE();
                //if (resp.mre != null)
                //    resp.mre.Dispose();
                //resp.mre = null;
                df.TryRemove(msgId, out resp);
                return new Tuple<bool, byte[]>(false, null);
            }

            ////else if (!resp.mre.WaitOne(timeoutMs))
            //else if (!resp.WaitOne_MRE(timeoutMs))
            //{
            //    //--STAT
            //    this.Statistic.Timeout();

            //    //if (resp.mre != null)
            //    //    resp.mre.Dispose();
            //    //resp.mre = null;
            //    resp.Dispose_MRE();
            //    df.TryRemove(msgId, out resp);
            //    return new Tuple<bool, byte[]>(false, null);
            //}


            //resp.TokenSource.Cancel();
            //CancellationToken ct = new CancellationToken();
            //var t = Task.Delay(timeoutMs,ct);
            //try
            //{
            //    await Task.WhenAny(resp.amre.WaitAsync(), t).ConfigureAwait(false);                
            //    t.Dispose();
            //}
            //catch (Exception ex)
            //{
                
            //}
            
            //await Task.WhenAny(resp.amre.WaitAsync(), Task.Delay(timeoutMs)).ConfigureAwait(false);
            //await resp.amre.WaitAsync().ConfigureAwait(false);
            await resp.amre.WaitAsync();

            //tskWait.Dispose();

            //if (resp.mre != null)
            //    resp.mre.Dispose();
            //resp.mre = null;
            resp.Dispose_MRE();

            if (df.TryRemove(msgId, out resp))
            {
                return new Tuple<bool, byte[]>(resp.IsRespOk, resp.res);
            }

            return new Tuple<bool, byte[]>(false, null);
        }


        /// <summary>
        /// Just sends payload to remote partner without awaiting response from it.
        /// </summary>
        /// <param name="args">payload</param>
        /// <returns>if Message was accepted for sending</returns>
        public bool RemoteRequestWithoutResponse(byte[] args)
        {
            if (Interlocked.Read(ref Disposed) == 1)
                return false;

            ulong msgId = sm.GetMessageId();
            return sm.SendMessage(eMsgType.Request, msgId, args);
        }

        internal long Disposed = 0;

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            if (System.Threading.Interlocked.CompareExchange(ref Disposed, 1, 0) != 0)
                return;

            //this.sm.SharmIPC.LogException("dispose test",new Exception("p1 "));
            try
            {
                if (tmr != null)
                {                   
                    tmr.Dispose();
                    tmr = null;
                }
            }
            catch
            { }

            //this.sm.SharmIPC.LogException("dispose test", new Exception("p2"));
            try
            {
                ResponseCrate rc=null;
                foreach (var el in df.ToList())
                {
                    if (df.TryRemove(el.Key, out rc))
                    {
                        rc.IsRespOk = false;
                        rc.Dispose_MRE();                                               
                    }
                    
                }
            }
            catch
            {
            }

            //this.sm.SharmIPC.LogException("dispose test", new Exception("p3"));

            try
            {
                if (sm != null)
                {
                    sm.Dispose();
                    sm = null;
                }
            }
            catch
            { }
           

        }


        public string UsageReport()
        {
            return this.Statistic.Report();
        }

    }
}
