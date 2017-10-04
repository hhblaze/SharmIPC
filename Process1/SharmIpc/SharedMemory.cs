using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO.MemoryMappedFiles;


namespace tiesky.com.SharmIpcInternals
{
    internal enum eInstanceType
    {
        Undefined,
        Master,
        Slave
    }

    internal enum eMsgType:byte
    {
        RpcRequest=1,        
        RpcResponse=2,
        ErrorInRpc=3,     
        Request = 4,

        //SwitchToV2=5
    }

 

    
    internal class SharedMemory:IDisposable
    {
       

        //System.IO.MemoryMappedFiles.MemoryMappedViewAccessor accessor = null;
        //System.IO.MemoryMappedFiles.MemoryMappedFile mmf = null;

        Mutex mt = null;

        //EventWaitHandle ewh_ReadyToRead = null;
        //EventWaitHandle ewh_ReadyToWrite = null;

        internal string uniqueHandlerName = "";
        internal long bufferCapacity = 50000;
        internal int maxQueueSizeInBytes = 20000000;
        internal eInstanceType instanceType = eInstanceType.Undefined;

        ReaderWriterHandler rwh = null;
        internal SharmIpc SharmIPC = null;

        internal tiesky.com.SharmIpc.eProtocolVersion ProtocolVersion = tiesky.com.SharmIpc.eProtocolVersion.V1;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="uniqueHandlerName">Can be name of APP, both syncronized processes must use the same name and it must be unique among the OS</param>
        /// <param name="SharmIPC">SharmIPC instance</param>
        /// <param name="bufferCapacity"></param>
        /// <param name="maxQueueSizeInBytes"></param>     
        /// <param name="protocolVersion"></param> 
        public SharedMemory(string uniqueHandlerName, SharmIpc SharmIPC, long bufferCapacity = 50000, int maxQueueSizeInBytes = 20000000, tiesky.com.SharmIpc.eProtocolVersion protocolVersion = tiesky.com.SharmIpc.eProtocolVersion.V1)
        {
            this.SharmIPC = SharmIPC;
            this.maxQueueSizeInBytes = maxQueueSizeInBytes;
            this.ProtocolVersion = protocolVersion;

            //if (dataArrived == null)
            //    throw new Exception("tiesky.com.SharmIpc: dataArrived callback can't be empty");

            if (String.IsNullOrEmpty(uniqueHandlerName) || uniqueHandlerName.Length > 200)
                throw new Exception("tiesky.com.SharmIpc: uniqueHandlerName can't be empty or more then 200 symbols");

            if (bufferCapacity < 256)
                bufferCapacity = 256;

            if (bufferCapacity > 1000000)    //max 1MB
                bufferCapacity = 1000000;

            this.uniqueHandlerName = uniqueHandlerName;
            this.bufferCapacity = bufferCapacity;

            try
            {
                mt = new Mutex(true, uniqueHandlerName + "SharmNet_MasterMutex");

                if (mt.WaitOne(500))
                {
                    instanceType = eInstanceType.Master;
                }
                else
                {
                    instanceType = eInstanceType.Slave;
                    if (mt != null)
                    {
                        //mt.ReleaseMutex();
                        mt.Close();
                        mt.Dispose();
                        mt = null;
                    }
                }              
            }
            catch (System.Threading.AbandonedMutexException)
            {
                instanceType = eInstanceType.Master;
            }

            Console.WriteLine("tiesky.com.SharmIpc: " + instanceType + " of " + uniqueHandlerName);

            rwh = new ReaderWriterHandler(this);          
        }

        /// <summary>
        /// Disposing
        /// </summary>
        public void Dispose()
        {
            //this.SharmIPC.LogException("dispose test", new Exception("p7"));
            try
            {
                
                if (mt != null)
                {
                    mt.ReleaseMutex();
                    mt.Close();
                    mt.Dispose();
                    mt = null;
                }
            }
            catch{
            }

            //this.SharmIPC.LogException("dispose test", new Exception("p6"));

            if (rwh != null)
            {
                rwh.Dispose();
                rwh = null;
            }

            //this.SharmIPC.LogException("dispose test", new Exception("p7"));

        }


        public ulong GetMessageId()
        {
            return this.rwh.GetMessageId();
        }

        public bool SendMessage(eMsgType msgType, ulong msgId, byte[] msg, ulong responseMsgId = 0)     
        {
            switch(ProtocolVersion)
            {
                case tiesky.com.SharmIpc.eProtocolVersion.V1:
                    return this.rwh.SendMessage(msgType, msgId, msg, responseMsgId);                    
                case tiesky.com.SharmIpc.eProtocolVersion.V2:
                    return this.rwh.SendMessageV2(msgType, msgId, msg, responseMsgId);
                    
            }

            return false;
        }


        //public void TestSendMessage()
        //{
        //    this.rwh.TestSendMessage();
        //}

     

       
    }//eoc
}
