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
        internal bool Master = true;
        
        class ResponseCrate
        {
            ///// <summary>
            ///// Not SLIM version must be used (it works faster for longer delay which RPCs are)
            ///// </summary>
            //public ManualResetEvent mre = null;
            public byte[] res = null;
            public Action<Tuple<bool, byte[]>> callBack = null;
            public bool IsRespOk = false;

            bool wait = true;

            public void Init_MRE()
            {
                //mre = new ManualResetEvent(false);
            }

            public void Set()
            {
                wait = false;
            }

            public bool WaitOne(int ms)
            {
                DateTime now = DateTime.UtcNow;
                var spinWait = new SspinWait();

                while (wait)
                {
                    if ((DateTime.UtcNow - now).TotalMilliseconds > ms)
                        return false;

                    // Thread.SpinWait(100);
                    spinWait.Spin();
                }

                return true;
            }
                      
            //int IsDisposed = 0;
            //public void Dispose_MRE()
            //{
            //    int newVal = Interlocked.CompareExchange(ref IsDisposed, 1, 0);
            //    if (newVal == 0 && mre != null)
            //    {                   
            //        mre.Set();
            //        mre.Dispose();
            //        mre = null;
            //    }
            //}
          
        }

      

        /// <summary>
        /// SharmIpc constructor
        /// </summary>
        /// <param name="uniqueHandlerName">Must be unique in OS scope (can be PID [ID of the process] + other identifications)</param>
        /// <param name="remoteCallHandler">Response routine for the remote partner requests</param>
        /// <param name="bufferCapacity">bigger buffer sends larger datablocks faster. Default value is 50000</param>
        /// <param name="maxQueueSizeInBytes">If remote partner is temporary not available, messages are accumulated in the sending buffer. This value sets the upper threshold of the buffer in bytes.</param>
        /// <param name="ExternalExceptionHandler">External exception handler can be supplied, will be returned Description from SharmIPC, like class.method name and handeled exception</param>
        public SharmIpc(string uniqueHandlerName, bool runAsMaster=true, Func<byte[], Tuple<bool, byte[]>> remoteCallHandler = null, long bufferCapacity = 50000, int maxQueueSizeInBytes = 20000000, Action<string, System.Exception> ExternalExceptionHandler = null)
        {
            if (remoteCallHandler == null)
                throw new Exception("tiesky.com.SharmIpc: remoteCallHandler can't be null");

            this.Master = runAsMaster;
            this.remoteCallHandler = remoteCallHandler;
            this.ExternalExceptionHandler = ExternalExceptionHandler;
            sm = new SharedMemory(uniqueHandlerName, this, bufferCapacity, maxQueueSizeInBytes);
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
        {
            if (remoteCallHandler == null)
                throw new Exception("tiesky.com.SharmIpc: remoteCallHandler can't be null");

            this.AsyncRemoteCallHandler = remoteCallHandler;
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
            if (res != null)
                sm.SendMessage(res.Item1 ? eMsgType.RpcResponse : eMsgType.ErrorInRpc, sm.GetMessageId(), res.Item2, msgId);
        }
        
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
                            rsp.Set();  //Signalling, to make waiting in parallel thread to proceed
                            //rsp.Set_MRE();
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
                //resp.Dispose_MRE();               
                df.TryRemove(msgId, out resp);
                return new Tuple<bool, byte[]>(false, null);
            }
            else if (!resp.WaitOne(timeoutMs))            
            {              
                //resp.Dispose_MRE();
                df.TryRemove(msgId, out resp);
                return new Tuple<bool, byte[]>(false, null);
            }
         
            //resp.Dispose_MRE();

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
            ulong msgId = sm.GetMessageId();
            return sm.SendMessage(eMsgType.Request, msgId, args);
        }


        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (sm != null)
                {
                    sm.Dispose();
                    sm = null;
                }
            }
            catch
            {}

            try
            {
                ResponseCrate rc=null;
                foreach (var el in df.ToList())
                {
                    if (df.TryRemove(el.Key, out rc))
                    {
                        rc.IsRespOk = false;
                        //rc.Dispose_MRE();                        
                        
                    }
                    
                }
            }
            catch
            {
            }
         
        }
    }
}
