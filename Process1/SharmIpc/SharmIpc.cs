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
        
        class ResponseCrate
        {
            /// <summary>
            /// Not SLIM version must be used (it works faster for longer delay which RPCs are)
            /// </summary>
            public ManualResetEvent mre = null;
            public byte[] res = null;
            public Action<Tuple<bool, byte[]>> callBack = null;
            public bool IsRespOk = true;
        }


        /// <summary>
        /// SharmIpc constructor
        /// </summary>
        /// <param name="uniqueHandlerName">Must be unique in OS scope (can be PID [ID of the process] + other identifications)</param>
        /// <param name="remoteCallHandler">Response routine for the remote partner requests</param>
        /// <param name="bufferCapacity">bigger buffer sends larger datablocks faster. Default value is 50000</param>
        /// <param name="maxQueueSizeInBytes">If remote partner is temporary not available, messages are accumulated in the sending buffer. This value sets the upper threshold of the buffer in bytes.</param>
        public SharmIpc(string uniqueHandlerName, Func<byte[], Tuple<bool, byte[]>> remoteCallHandler, long bufferCapacity = 50000, int maxQueueSizeInBytes = 20000000)
        {
            if (remoteCallHandler == null)
                throw new Exception("tiesky.com.SharmIpc: remoteCallHandler can't be null");

            this.remoteCallHandler = remoteCallHandler;
            sm = new SharedMemory(uniqueHandlerName, this.InternalDataArrived, bufferCapacity, maxQueueSizeInBytes);
        }

        /// <summary>
        /// SharmIpc constructor
        /// </summary>
        /// <param name="uniqueHandlerName">Must be unique in OS scope (can be PID [ID of the process] + other identifications)</param>
        /// <param name="remoteCallHandler">Callback routine for the remote partner requests. AsyncAnswerOnRemoteCall must be used for answer</param>
        /// <param name="bufferCapacity">bigger buffer sends larger datablocks faster. Default value is 50000</param>
        /// <param name="maxQueueSizeInBytes">If remote partner is temporary not available, messages are accumulated in the sending buffer. This value sets the upper threshold of the buffer in bytes.</param>
        public SharmIpc(string uniqueHandlerName, Action<ulong, byte[]> remoteCallHandler, long bufferCapacity = 50000, int maxQueueSizeInBytes = 20000000)
        {
            if (remoteCallHandler == null)
                throw new Exception("tiesky.com.SharmIpc: remoteCallHandler can't be null");

            this.AsyncRemoteCallHandler = remoteCallHandler;
            sm = new SharedMemory(uniqueHandlerName, this.InternalDataArrived, bufferCapacity, maxQueueSizeInBytes);
        }

        /// <summary>
        /// In case if asyncRemoteCallHandler != null
        /// </summary>
        /// <param name="msgId"></param>
        /// <param name="res"></param>
        public void AsyncAnswerOnRemoteCall(ulong msgId, Tuple<bool, byte[]> res)
        {
            sm.SendMessage(res.Item1 ? eMsgType.RpcResponse : eMsgType.ErrorInRpc, sm.GetMessageId(), res.Item2, msgId);
        }
        
        /// <summary>
        /// Any incoming data from remote partner is accumulated here
        /// </summary>
        /// <param name="msgType"></param>
        /// <param name="msgId"></param>
        /// <param name="bt"></param>
        void InternalDataArrived(eMsgType msgType, ulong msgId, byte[] bt)
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
                            rsp.mre.Set();
                        }
                        else
                        {
                            df.TryRemove(msgId, out rsp);
                            rsp.callBack(new Tuple<bool, byte[]>(rsp.IsRespOk, bt));
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

            resp.mre = new ManualResetEvent(false);

            df[msgId] = resp;          

            if (!sm.SendMessage(eMsgType.RpcRequest, msgId, args))
            {
                if (resp.mre != null)
                    resp.mre.Dispose();
                resp.mre = null;
                df.TryRemove(msgId, out resp);
                return new Tuple<bool, byte[]>(false, null);
            }
            else if (!resp.mre.WaitOne(timeoutMs))
            {
                if (resp.mre != null)
                    resp.mre.Dispose();
                resp.mre = null;
                df.TryRemove(msgId, out resp);
                return new Tuple<bool, byte[]>(false, null);
            }

            if (resp.mre != null)
                resp.mre.Dispose();
            resp.mre = null;

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
                        if (rc.mre != null)
                        {
                            rc.IsRespOk = false;
                            rc.mre.Set();
                            rc.mre.Dispose();
                            rc.mre = null;
                        }
                    }
                    
                }
            }
            catch
            {
            }
         
        }
    }
}
