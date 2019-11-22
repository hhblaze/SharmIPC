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
        /// <summary>
        /// Both peers must have the same version implementation
        /// </summary>
        public enum eProtocolVersion
        {
            V1 = 0, //Must be 0 for compatibility
            V2 = 2
        }

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
            public ManualResetEvent mre = null;
            public byte[] res = null;
            public Action<Tuple<bool, byte[]>> callBack = null;
            public bool IsRespOk = false;

            public AsyncManualResetEvent amre = null;

            public DateTime created = DateTime.UtcNow;
            public int TimeoutsMs = 30000;

            public void Init_MRE()
            {
                mre = new ManualResetEvent(false);
            }

            /// <summary>
            /// Works faster with timer than WaitOneAsync
            /// </summary>
            public void Init_AMRE()
            {
                amre = new AsyncManualResetEvent();
            }

            public void Set_MRE()
            {
              
                if (mre != null)
                {
                    mre.Set();
                }
                else if (amre != null)
                {
                    amre.Set();
                }

            }

            public bool WaitOne_MRE(int timeouts)
            {
                //if (Interlocked.Read(ref IsDisposed) == 1 || mre == null)  //No sense
                //    return false;
                return mre.WaitOne(timeouts);
            }

            /// <summary>
            /// Works slower than amre (AsyncManualResetEvent) with the timer
            /// </summary>
            /// <param name="timeouts"></param>
            /// <returns></returns>
            async public Task WaitOneAsync(int timeouts)
            {

                //await resp.amre.WaitAsync();
                await WaitHandleAsyncFactory.FromWaitHandle(mre, TimeSpan.FromMilliseconds(timeouts));
                //await mre.AsTask(TimeSpan.FromMilliseconds(timeouts));
            }

            //async public Task<bool> WaitOneAsync()
            //{
            //    //if (Interlocked.Read(ref IsDisposed) == 1 || amre == null)
            //    //    return false;

            //    return await amre.WaitAsync();

            //}


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
        /// Communication protocol, must be the same for both peers. Can be setup via constructor
        /// </summary>
        public eProtocolVersion ProtocolVersion
        {
            get { return this.sm.ProtocolVersion; }
        }

        /// <summary>
        /// Default is false. Descibed in https://github.com/hhblaze/SharmIPC/issues/6
        /// <para>Gives ability to parse packages in the same receiving thread before processing them in another thread</para>
        /// <para>Programmer is responsible for the returning control back ASAP from RemoteCallHandler via Task.Run(()=>process(msg))</para>
        /// </summary>
        bool ExternalProcessing = false;

        /// <summary>
        /// SharmIpc constructor
        /// </summary>
        /// <param name="uniqueHandlerName">Must be unique in OS scope (can be PID [ID of the process] + other identifications)</param>
        /// <param name="remoteCallHandler">Response routine for the remote partner requests</param>
        /// <param name="bufferCapacity">bigger buffer sends larger datablocks faster. Default value is 50000</param>
        /// <param name="maxQueueSizeInBytes">If remote partner is temporary not available, messages are accumulated in the sending buffer. This value sets the upper threshold of the buffer in bytes.</param>
        /// <param name="ExternalExceptionHandler">External exception handler can be supplied, will be returned Description from SharmIPC, like class.method name and handeled exception</param>
        /// <param name="protocolVersion">Version of communication protocol. Must be the same for both communicating peers</param>
        /// <param name="externalProcessing">Gives ability to parse packages in the same receiving thread before processing them in another thread</param>
        public SharmIpc(string uniqueHandlerName, Func<byte[], Tuple<bool, byte[]>> remoteCallHandler, long bufferCapacity = 50000, int maxQueueSizeInBytes = 20000000, 
            Action<string, System.Exception> ExternalExceptionHandler = null, eProtocolVersion protocolVersion = eProtocolVersion.V1, bool externalProcessing = false)
            :this(uniqueHandlerName,bufferCapacity,maxQueueSizeInBytes,ExternalExceptionHandler, protocolVersion, externalProcessing)
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
        /// <param name="protocolVersion">Version of communication protocol. Must be the same for both communicating peers</param>
        /// <param name="externalProcessing">Gives ability to parse packages in the same receiving thread before processing them in another thread</param>
        public SharmIpc(string uniqueHandlerName, Action<ulong, byte[]> remoteCallHandler, long bufferCapacity = 50000, int maxQueueSizeInBytes = 20000000, 
            Action<string, System.Exception> ExternalExceptionHandler = null, eProtocolVersion protocolVersion = eProtocolVersion.V1, bool externalProcessing = false)
            : this(uniqueHandlerName, bufferCapacity, maxQueueSizeInBytes, ExternalExceptionHandler, protocolVersion, externalProcessing)
        {          
            this.AsyncRemoteCallHandler = remoteCallHandler ?? throw new Exception("tiesky.com.SharmIpc: remoteCallHandler can't be null"); ;
           
        }


        SharmIpc(string uniqueHandlerName, long bufferCapacity = 50000, int maxQueueSizeInBytes = 20000000, Action<string, System.Exception> ExternalExceptionHandler = null,
            eProtocolVersion protocolVersion = eProtocolVersion.V1, bool externalProcessing = false)
        {
            this.Statistic.ipc = this;
            this.ExternalProcessing = externalProcessing;

            tmr = new Timer(new TimerCallback((state) =>
            {
                DateTime now = DateTime.UtcNow;
              
                //This timer is necessary for Calls based on Callbacks, calls based on WaitHandler have their own timeout,
                //That's why for non-callback calls, timeout will be infinite
                List<ulong> toRemove = new List<ulong>();

                //foreach (var el in df.Where(r => now.Subtract(r.Value.created).TotalMilliseconds >= r.Value.TimeoutsMs))
                foreach (var el in df.Where(r => now.Subtract(r.Value.created).TotalMilliseconds >= r.Value.TimeoutsMs).ToList())
                {
                    if(el.Value.callBack != null)
                        toRemove.Add(el.Key);
                    else 
                        el.Value.Set_MRE();
                }

                ResponseCrate rc = null;
                foreach(var el in toRemove)
                {
                    if(df.TryRemove(el, out rc))
                        rc.callBack(new Tuple<bool, byte[]>(false, null));  //timeout
                }

            }), null, 10000, 10000);


            this.ExternalExceptionHandler = ExternalExceptionHandler;
            sm = new SharedMemory(uniqueHandlerName, this, bufferCapacity, maxQueueSizeInBytes, protocolVersion);

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

                    if (this.ExternalProcessing)
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
                    }
                    else
                    {
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
                    }

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
                resp.TimeoutsMs = timeoutMs; //IS NECESSARY FOR THE CALLBACK TYPE OF RETURN

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

            resp.TimeoutsMs = Int32.MaxValue; //using timeout of the wait handle (not the timer)


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
        /// <param name="timeoutMs">Default 30 sec</param>
        /// <returns></returns>
        public async Task<Tuple<bool, byte[]>> RemoteRequestAsync(byte[] args, int timeoutMs = 30000)
        {
            if (Interlocked.Read(ref Disposed) == 1)
                return new Tuple<bool, byte[]>(false, null);

            ulong msgId = sm.GetMessageId();
            var resp = new ResponseCrate();
            resp.TimeoutsMs = timeoutMs; //enable for amre
            //resp.TimeoutsMs = Int32.MaxValue; //using timeout of the wait handle (not the timer), enable for mre

            //resp.Init_MRE();
            resp.Init_AMRE();
            

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
                        
            //await resp.mre.AsTask(TimeSpan.FromMilliseconds(timeoutMs));        //enable for mre
            await resp.amre.WaitAsync();    //enable for amre


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

        /// <summary>
        /// Returns current usage statistic
        /// </summary>
        /// <returns></returns>
        public string UsageReport()
        {
            return this.Statistic.Report();
        }

    }
}
