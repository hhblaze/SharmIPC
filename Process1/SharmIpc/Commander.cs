using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace tiesky.com.SharmIpc
{
    public class Commander:IDisposable
    {        
        Action<ulong, byte[]> dataArrived = null;

        SharedMemory sm = null;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="uniqueHandlerName">must be unique among OS (can be PID of the process + other identification)</param>
        /// <param name="dataArrived">callback where </param>
        /// <param name="bufferCapacity">As bigger buffer as faster it sends bigger data blocks. default value is 50000</param>
        /// <param name="maxQueueSizeInBytes">If remote partner doesn't answer messages start to accumulate in memory until given treshold, then send will return error</param>
        public Commander(string uniqueHandlerName, Action<ulong, byte[]> dataArrived, long bufferCapacity = 50000, int maxQueueSizeInBytes = 20000000)
        {
           this.dataArrived = dataArrived;
           sm = new SharedMemory(uniqueHandlerName, this.InternalDataArrived, bufferCapacity, maxQueueSizeInBytes);
        }



        class ResponseCrate
        {
            public ManualResetEvent mre = null;
            public byte[] res = null;
            public Action<Tuple<bool, byte[]>> callBack = null;
            public bool IsRespOk = true;
        }

        ConcurrentDictionary<ulong, ResponseCrate> df = new ConcurrentDictionary<ulong, ResponseCrate>();
        

        void InternalDataArrived(eMsgType msgType, ulong msgId, byte[] bt)
        {   
            ResponseCrate rsp = null;

            switch (msgType)
            {
                case eMsgType.Request:
                    //Do nothing
                    break;
                case eMsgType.RpcRequest:

                    //!!!!!!!!!!!!!!!!Call SM Implementer GetSMImplementer
                    sm.SendMessage(eMsgType.RpcResponse, sm.GetMessageId(), BitConverter.GetBytes(msgId));

                    break;

                case eMsgType.ErrorInRpcAnswer:
                case eMsgType.RpcResponse:

                    //Answer to caller
                    ulong rMsgId = BitConverter.ToUInt64(bt,0);

                    if (df.TryGetValue(rMsgId, out rsp))
                    {
                        rsp.res = bt;
                        rsp.IsRespOk = msgType == eMsgType.RpcResponse;

                        if (rsp.callBack == null)
                        {
                            rsp.mre.Set();
                        }
                        else
                        {
                            df.TryRemove(rMsgId, out rsp);
                            rsp.callBack(new Tuple<bool, byte[]>(rsp.IsRespOk, bt));
                        }
                    }
             
                    break;                
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="remoteMethodName"></param>
        /// <param name="args"></param>
        /// <param name="callBack">if specified then result will be returned into callBack</param>
        /// <param name="timeoutMs">Default 30 sec</param>
        /// <returns></returns>
        public Tuple<bool, byte[]> RpcCall(byte[] args, Action<Tuple<bool, byte[]>> callBack = null, int timeoutMs = 30000)
        {
       
            ulong msgId = sm.GetMessageId();
            var resp = new ResponseCrate();

            if (callBack != null)
            {
                //Async return
                resp.callBack = callBack;
                df[msgId] = resp;
                if (!sm.SendMessage(eMsgType.RpcRequest, msgId, args))
                    callBack(new Tuple<bool, byte[]>(false, null));

                return new Tuple<bool, byte[]>(false, null);
            }

            resp.mre = new ManualResetEvent(false);

            df[msgId] = resp;          

            if (!sm.SendMessage(eMsgType.RpcRequest, msgId, args))
            {                
                resp.mre.Dispose();
                resp.mre = null;
                df.TryRemove(msgId, out resp);
                return new Tuple<bool, byte[]>(false, null);
            }
            else if (!resp.mre.WaitOne(timeoutMs))
            {
                resp.mre.Dispose();
                resp.mre = null;
                df.TryRemove(msgId, out resp);
                return new Tuple<bool, byte[]>(false, null);
            }

            resp.mre.Dispose();
            resp.mre = null;

            if (df.TryRemove(msgId, out resp))
            {
                return new Tuple<bool, byte[]>(resp.IsRespOk, resp.res);
            }

            return new Tuple<bool, byte[]>(false, null);
        }


        /// <summary>
        /// Non RPC call
        /// </summary>
        /// <param name="args"></param>
        /// <returns>if Message was accepted for sending</returns>
        public bool Call(byte[] args)
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
