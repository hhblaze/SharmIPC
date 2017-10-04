using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.IO;

namespace tiesky.com.SharmIpcInternals
{
    internal class SWaitHadle : EventWaitHandle
    {
        //Global prefix and extran permissions
        //http://stackoverflow.com/questions/2590334/creating-a-cross-process-eventwaithandle


        public override object InitializeLifetimeService()
        {
            return null;
        }
        public SWaitHadle(bool initialState, EventResetMode mode, string name) : base(initialState, mode, name)
        {            
        }
    }

    internal class ReaderWriterHandler:IDisposable
    {
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor Writer_accessor = null;
        System.IO.MemoryMappedFiles.MemoryMappedFile Writer_mmf = null;
        unsafe byte* Writer_accessor_ptr = (byte*)0;

        //EventWaitHandle ewh_Writer_ReadyToRead = null;
        SWaitHadle ewh_Writer_ReadyToRead = null;
        SWaitHadle ewh_Writer_ReadyToWrite = null;

        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor Reader_accessor = null;
        System.IO.MemoryMappedFiles.MemoryMappedFile Reader_mmf = null;
        unsafe byte* Reader_accessor_ptr = (byte*)0;

        SWaitHadle ewh_Reader_ReadyToRead = null;
        SWaitHadle ewh_Reader_ReadyToWrite = null;

        //ManualResetEvent mre_writer_thread = new ManualResetEvent(false);
        AsyncManualResetEvent mre_writer_thread = new AsyncManualResetEvent();

        SharedMemory sm = null;
        object lock_q = new object();
        byte[] toSend = null;
        Queue<byte[]> q = new Queue<byte[]>();       
        //ConcurrentQueue
        bool inSend = false;
        int bufferLenS = 0;

        ///// <summary>
        ///// MsgId of the sender and payload
        ///// </summary>
        //Action<eMsgType, ulong, byte[]> DataArrived = null;
        

        public void Dispose()
        {

            //this.sm.SharmIPC.LogException("dispose test", new Exception("p8"));
            try
            {
                if (ewh_Writer_ReadyToRead != null)
                {
                    //ewh_Writer_ReadyToRead.Set();
                    ewh_Writer_ReadyToRead.Close();
                    ewh_Writer_ReadyToRead.Dispose();
                    ewh_Writer_ReadyToRead = null;
                }
            }
            catch
            {
            }
            //this.sm.SharmIPC.LogException("dispose test", new Exception("p9"));
            try
            {
                if (ewh_Writer_ReadyToWrite != null)
                {
                    //ewh_Writer_ReadyToWrite.Set();
                    ewh_Writer_ReadyToWrite.Close();
                    ewh_Writer_ReadyToWrite.Dispose();
                    ewh_Writer_ReadyToWrite = null;
                }
            }
            catch
            {
            }
            //this.sm.SharmIPC.LogException("dispose test", new Exception("p10"));
            try
            {
                if (ewh_Reader_ReadyToRead != null)
                {
                    //ewh_Reader_ReadyToRead.Set();
                    ewh_Reader_ReadyToRead.Close();
                    ewh_Reader_ReadyToRead.Dispose();
                    ewh_Reader_ReadyToRead = null;
                }
            }
            catch
            {
            }
            //this.sm.SharmIPC.LogException("dispose test", new Exception("p11"));
            try
            {
                if (ewh_Reader_ReadyToWrite != null)
                {
                    //ewh_Reader_ReadyToWrite.Set();
                    ewh_Reader_ReadyToWrite.Close();
                    ewh_Reader_ReadyToWrite.Dispose();
                    ewh_Reader_ReadyToWrite = null;
                }
            }
            catch
            {}
            //this.sm.SharmIPC.LogException("dispose test", new Exception("p12"));
            try
            {
                if (mre_writer_thread != null)
                {
                    mre_writer_thread.Set();
                    //mre_writer_thread.Close();
                    //mre_writer_thread.Dispose();
                    mre_writer_thread = null;
                }
            }
            catch
            { }
            //this.sm.SharmIPC.LogException("dispose test", new Exception("p13"));

            try
            {
                if (Writer_accessor != null)
                {
                    Writer_accessor.SafeMemoryMappedViewHandle.ReleasePointer();
                    Writer_accessor.Dispose();
                    Writer_accessor = null;
                }
            }
            catch
            {}
            //this.sm.SharmIPC.LogException("dispose test", new Exception("p14"));
            try
            {
                if (Reader_accessor != null)
                {
                    Reader_accessor.SafeMemoryMappedViewHandle.ReleasePointer();
                    Reader_accessor.Dispose();
                    Reader_accessor = null;
                }
            }
            catch
            { }
            //this.sm.SharmIPC.LogException("dispose test", new Exception("p15"));
            try
            {
                if (Writer_mmf != null)
                {                    
                    Writer_mmf.Dispose();
                    Writer_mmf = null;
                }
            }
            catch
            { }
            //this.sm.SharmIPC.LogException("dispose test", new Exception("p16"));
            try
            {
                if (Reader_mmf != null)
                {
                    Reader_mmf.Dispose();
                    Reader_mmf = null;
                }
            }
            catch
            { }
            //this.sm.SharmIPC.LogException("dispose test", new Exception("p17"));
        }


        public ReaderWriterHandler(SharedMemory sm)
        {
            this.sm = sm;
            //this.DataArrived = DataArrived;
            this.bufferLenS = Convert.ToInt32(sm.bufferCapacity) - protocolLen;

            this.InitWriter();
            this.InitReader();

            //SendProcedure1();
        }


        unsafe void InitWriter()
        {
            string prefix = sm.instanceType == tiesky.com.SharmIpcInternals.eInstanceType.Master ? "1" : "2";

            
            if (ewh_Writer_ReadyToRead == null)
            {
                ewh_Writer_ReadyToRead = new SWaitHadle(false, EventResetMode.ManualReset, sm.uniqueHandlerName + prefix + "_SharmNet_ReadyToRead");
                ewh_Writer_ReadyToWrite = new SWaitHadle(true, EventResetMode.ManualReset, sm.uniqueHandlerName + prefix + "_SharmNet_ReadyToWrite");
                ewh_Writer_ReadyToWrite.Set();
            }

            //if (sm.instanceType == tiesky.com.SharmIpc.eInstanceType.Master)
            //{
            //    Console.WriteLine("My writer handlers:");
            //    Console.WriteLine(sm.uniqueHandlerName + prefix + "_SharmNet_ReadyToRead");
            //    Console.WriteLine(sm.uniqueHandlerName + prefix + "_SharmNet_ReadyToWrite");
            //    Console.WriteLine("-------");
            //}


            if (Writer_mmf == null)
            {
                //Writer_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity, MemoryMappedFileAccess.ReadWrite);
                //Writer_accessor = Writer_mmf.CreateViewAccessor(0, sm.bufferCapacity);


#if FULLNET
                var security = new MemoryMappedFileSecurity();
                security.AddAccessRule(new System.Security.AccessControl.AccessRule<MemoryMappedFileRights>(
                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
                    MemoryMappedFileRights.FullControl,
                    System.Security.AccessControl.AccessControlType.Allow));

                //More access rules
                //http://stackoverflow.com/questions/18067581/cant-open-memory-mapped-file-from-log-on-screen

                //Writer_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("Global\\" + sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity,  //If started as admin
                Writer_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity,
                    MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.DelayAllocatePages, security, System.IO.HandleInheritability.Inheritable);
#else
                Writer_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity,
                   MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.DelayAllocatePages, System.IO.HandleInheritability.Inheritable);
#endif

                Writer_accessor = Writer_mmf.CreateViewAccessor(0, sm.bufferCapacity);
                Writer_accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref Writer_accessor_ptr);
            }

            //Task.Run(() =>
            //{
            //    WriterV01();
            //});
        }

        const int protocolLen = 25;
        ulong msgId_Sending = 0;

        /*Protocol
         * 1byte - MsgType. StandardMsg value is 1 eMsgType
         * Prot for MsgType 1 
         * 8bytes - msgId (ulong)
         * 4bytes - payload length (int)
         * 2bytes - currentChunk
         * 2bytes - totalChunks  //ChunksLeft (ushort) (if there is only 1 chunk, then chunks left will be 0. if there are 2 chunks: first will be 1 then will be 0)
         * 8bytes - responseMsgId
         * payload
         */


        /*Protocol V2         
        * Nbytes - protobuf int - MsgType. StandardMsg value is 1 eMsgType (normally 1 byte)        
        * Nbytes - protobuf ulong  - msgId (ulong)
        * Nbytes - protobuf int  - payload length
        * Nbytes - currentChunkNr
        * Nbytes - totalChunks  //ChunksLeft (ushort) (if there is only 1 chunk, then chunks left will be 0. if there are 2 chunks: first will be 1 then will be 0)
        * Nbytes - protobuf ulong - responseMsgId
        * payload
        * ....
        * next message
    */

        int totalBytesInQUeue = 0;

        /// <summary>
        /// To get new Id this function must be used
        /// </summary>
        /// <returns></returns>
        public ulong GetMessageId()
        {
            lock (lock_q)
            {
                return ++msgId_Sending;
            }
        }

        /// <summary>
        /// Returns false if buffer threshold is reached
        /// </summary>
        /// <param name="msgType"></param>
        /// <param name="msgId"></param>
        /// <param name="msg"></param>
        /// <param name="responseMsgId"></param>
        /// <returns></returns>
        public bool SendMessage(eMsgType msgType, ulong msgId, byte[] msg, ulong responseMsgId=0)
        {
            if (Interlocked.Read(ref this.sm.SharmIPC.Disposed) == 1)
                return false;

            if (totalBytesInQUeue > sm.maxQueueSizeInBytes)
            {
                //Cleaning queue
                //lock (lock_q)
                //{
                //    totalBytesInQUeue = 0;
                //    q.Clear();
                //}
                //Generating exception

                //--STAT
                this.sm.SharmIPC.Statistic.TotalBytesInQueueError();

                this.sm.SharmIPC.LogException(
                    "tiesky.com.SharmIpc.ReaderWriterHandler.SendMessage: max queue treshold is reached" + sm.maxQueueSizeInBytes,
                    new Exception("ReaderWriterHandler max queue treshold is reached " + sm.maxQueueSizeInBytes +
                    $"; totalBytesInQUeue: {totalBytesInQUeue}; q.Count: {q.Count}; " +
                    $"_ready2writeSignal_Last_Setup: {this.sm.SharmIPC.Statistic._ready2writeSignal_Last_Setup}" +
                    $"_ready2ReadSignal_Last_Setup: {this.sm.SharmIPC.Statistic._ready2ReadSignal_Last_Setup}"));

                //throw new Exception("tiesky.com.SharmIpc: ReaderWriterHandler max queue treshold is reached " + sm.maxQueueSizeInBytes);

                //Is handeld on upper level
                return false;
            }
            

            lock (lock_q)
            {
                
                //Splitting message
                int i = 0;
                int left = msg == null ? 0 : msg.Length;

                byte[] pMsg = null;
                
                ushort totalChunks = msg == null ? (ushort)1 : (msg.Length == 0) ? Convert.ToUInt16(1) : Convert.ToUInt16(Math.Ceiling((double)msg.Length / (double)bufferLenS));
                ushort currentChunk = 1;

                while (true)
                {
                    if (left > bufferLenS)
                    {

                        pMsg = new byte[bufferLenS + protocolLen];

                        //Writing protocol header
                        Buffer.BlockCopy(new byte[] { (byte)msgType }, 0, pMsg, 0, 1);    //MsgType (1 for standard message)
                        Buffer.BlockCopy(BitConverter.GetBytes(msgId), 0, pMsg, 1, 8);  //msgId_Sending
                        Buffer.BlockCopy(BitConverter.GetBytes(bufferLenS), 0, pMsg, 9, 4);  //payload len
                        Buffer.BlockCopy(BitConverter.GetBytes(currentChunk), 0, pMsg, 13, 2);  //current chunk
                        Buffer.BlockCopy(BitConverter.GetBytes(totalChunks), 0, pMsg, 15, 2);  //total chunks
                        Buffer.BlockCopy(BitConverter.GetBytes(responseMsgId), 0, pMsg, 17, 8);  //total chunks


                        //Writing payload
                        if(msg != null && msg.Length>0)
                            Buffer.BlockCopy(msg, i, pMsg, protocolLen, bufferLenS);

                        left -= bufferLenS;
                        i += bufferLenS;
                        q.Enqueue(pMsg);
                        totalBytesInQUeue += pMsg.Length;
                    }
                    else
                    {
                        pMsg = new byte[left + protocolLen];

                        //Writing protocol header
                        Buffer.BlockCopy(new byte[] { (byte)msgType }, 0, pMsg, 0, 1);    //MsgType (1 for standard message)
                        Buffer.BlockCopy(BitConverter.GetBytes(msgId), 0, pMsg, 1, 8);  //msgId_Sending
                        Buffer.BlockCopy(BitConverter.GetBytes((msg != null && msg.Length == 0) ? Int32.MaxValue : left), 0, pMsg, 9, 4);  //payload len
                        Buffer.BlockCopy(BitConverter.GetBytes(currentChunk), 0, pMsg, 13, 2);  //current chunk
                        Buffer.BlockCopy(BitConverter.GetBytes(totalChunks), 0, pMsg, 15, 2);  //total chunks
                        Buffer.BlockCopy(BitConverter.GetBytes(responseMsgId), 0, pMsg, 17, 8);  //total chunks

                        //Writing payload
                        if (msg != null && msg.Length > 0)
                            Buffer.BlockCopy(msg, i, pMsg, protocolLen, left);

                        q.Enqueue(pMsg);
                        totalBytesInQUeue += pMsg.Length;
                        break;
                    }

                    currentChunk++;
                }


                //mre_writer_thread.Set();

            }//eo lock

            

            WriterV01();

            //StartSendProcedure();
            //StartSendProcedure_v2();


            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="msgType"></param>
        /// <param name="msgId"></param>
        /// <param name="msg"></param>
        /// <param name="responseMsgId"></param>
        /// <returns></returns>
        public bool SendMessageV2(eMsgType msgType, ulong msgId, byte[] msg, ulong responseMsgId = 0)
        {
            if (Interlocked.Read(ref this.sm.SharmIPC.Disposed) == 1)
                return false;

            if (totalBytesInQUeue > sm.maxQueueSizeInBytes)
            {              

                //--STAT
                this.sm.SharmIPC.Statistic.TotalBytesInQueueError();

                this.sm.SharmIPC.LogException(
                      "tiesky.com.SharmIpc.ReaderWriterHandler.SendMessageV2: max queue treshold is reached" + sm.maxQueueSizeInBytes,
                      new Exception("ReaderWriterHandler max queue treshold is reached " + sm.maxQueueSizeInBytes +
                      $"; totalBytesInQUeue: {totalBytesInQUeue}; q.Count: {q.Count}; " +
                      $"_ready2writeSignal_Last_Setup: {this.sm.SharmIPC.Statistic._ready2writeSignal_Last_Setup}" +
                      $"_ready2ReadSignal_Last_Setup: {this.sm.SharmIPC.Statistic._ready2ReadSignal_Last_Setup}"));

                //throw new Exception("tiesky.com.SharmIpc: ReaderWriterHandler max queue treshold is reached " + sm.maxQueueSizeInBytes);

                //Is handeld on upper level
                return false;
            }


            lock (lock_q)
            {

                //Splitting message
                int i = 0;
                int left = msg == null ? 0 : msg.Length;
                
                byte[] tmp;
                ushort totalChunks = msg == null ? (ushort)1 : (msg.Length == 0) ? Convert.ToUInt16(1) : Convert.ToUInt16(Math.Ceiling((double)msg.Length / (double)bufferLenS));
                ushort currentChunk = 1;
                int currentChunkLen = 0;

                while (true)
                {

                    if (left > bufferLenS)
                        currentChunkLen = bufferLenS;
                    else
                        currentChunkLen = left;

                    //Writing protocol header
                    tmp = ((ulong)msgType).ToProtoBytes();
                    totalBytesInQUeue += tmp.Length;
                    q.Enqueue(tmp);//MsgType (1 for standard message)                        
                    tmp = (msgId).ToProtoBytes();
                    totalBytesInQUeue += tmp.Length;
                    q.Enqueue(tmp);//msgId_Sending  
                    totalBytesInQUeue += tmp.Length;
                    tmp = ((ulong)currentChunkLen).ToProtoBytes();
                    q.Enqueue(tmp);//payload len  
                    tmp = ((ulong)currentChunk).ToProtoBytes();
                    totalBytesInQUeue += tmp.Length;
                    q.Enqueue(tmp);//current chunk              
                    tmp = ((ulong)totalChunks).ToProtoBytes();
                    totalBytesInQUeue += tmp.Length;
                    q.Enqueue(tmp);//total chunks                
                    tmp = (responseMsgId).ToProtoBytes();
                    totalBytesInQUeue += tmp.Length;
                    q.Enqueue(tmp);//Response message id

                    //Writing payload
                    if (msg != null && msg.Length > 0)
                    {
                        tmp = new byte[currentChunkLen];
                        totalBytesInQUeue += tmp.Length;
                        Buffer.BlockCopy(msg, i, tmp, 0, currentChunkLen);
                        q.Enqueue(tmp);
                    }


                    left -= currentChunkLen;
                    i += currentChunkLen;
                    totalBytesInQUeue += tmp.Length;

                    if (left == 0)
                        break;
                    currentChunk++;
                }

            }//eo lock

            WriterV02();

            return true;
        }
        
        /// <summary>
        /// 
        /// </summary>
        void WriterV01()
        {
            lock (lock_q)
            {
                if (inSend || q.Count() < 1)
                    return;

                inSend = true;
                toSend = q.Dequeue();
                totalBytesInQUeue -= toSend.Length;

            }

            while (true)
            {

                //--STAT
                this.sm.SharmIPC.Statistic.StartToWait_ReadyToWrite_Signal();

                if (ewh_Writer_ReadyToWrite.WaitOne())  //We don't need here async awaiter
                {
                    //--STAT
                    this.sm.SharmIPC.Statistic.StopToWait_ReadyToWrite_Signal();

                    if (Interlocked.Read(ref this.sm.SharmIPC.Disposed) == 1)
                        return;

                    ewh_Writer_ReadyToWrite.Reset();
                   
                    WriteBytes(0, toSend);

                    //Setting signal ready to read
                    ewh_Writer_ReadyToRead.Set();
                    
                    lock (lock_q)
                    {
                        if (q.Count() == 0)
                        {
                            toSend = null;
                            inSend = false;
                            return;
                        }
                        toSend = q.Dequeue();
                        totalBytesInQUeue -= toSend.Length;
                    }
                }
            }//eo while
            

        }//eof


        void WriterV02()
        {  
            int offset = 0;
            lock (lock_q)
            {
                if (inSend || q.Count() < 1)
                    return;

                inSend = true;
            }

            byte[] proto = null;

            while (true)
            {

                if (ewh_Writer_ReadyToWrite.WaitOne())  //We don't need here async awaiter
                {
  
                    if (Interlocked.Read(ref this.sm.SharmIPC.Disposed) == 1)
                        return;

                    ewh_Writer_ReadyToWrite.Reset();

                    lock (lock_q)
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            while (true)
                            {
                                toSend = q.Dequeue();
                                totalBytesInQUeue -= toSend.Length;
                                ms.Write(toSend,0, toSend.Length);
                                offset += toSend.Length;

                                if (offset >= bufferLenS || q.Count() < 1)
                                    break;
                            }
                            //Sending complete size
                            proto = ((ulong)offset).ToProtoBytes();
                            WriteBytes(0, proto);
                            WriteBytes(proto.Length, ms.ToArray());
                            ms.Close();
                        }
                    }                   

                    //Setting signal ready to read
                    ewh_Writer_ReadyToRead.Set();
                    offset = 0;

                    lock (lock_q)
                    {
                        if (q.Count() == 0)
                        {
                            toSend = null;
                            inSend = false;
                            return;
                        }                       
                    }
                }
            }//eo while


        }//eof


        unsafe void WriteBytes(int offset, byte[] data)
        {
            ////--STAT
            //this.sm.SharmIPC.Statistic.Writing(data.Length);

            Marshal.Copy(data, 0, IntPtr.Add(new IntPtr(Writer_accessor_ptr), offset), data.Length);

            //https://msdn.microsoft.com/en-us/library/system.io.memorymappedfiles.memorymappedviewaccessor.safememorymappedviewhandle(v=vs.100).aspx
        }

        unsafe byte[] ReadBytes(int offset, int num)
        {
            ////--STAT
            //this.sm.SharmIPC.Statistic.Reading(num);

            byte[] arr = new byte[num];
            Marshal.Copy(IntPtr.Add(new IntPtr(Reader_accessor_ptr), offset), arr, 0, num);
            return arr;
        }
        
        //ulong MsgId_Received = 0;
        ushort currentChunk = 0;
        byte[] chunksCollected = null;

        /// <summary>
        /// 
        /// </summary>
        unsafe void InitReader()
        {
            string prefix = sm.instanceType == eInstanceType.Slave ? "1" : "2";

            if (ewh_Reader_ReadyToRead == null)
            {
                
                ewh_Reader_ReadyToRead = new SWaitHadle(false, EventResetMode.ManualReset, sm.uniqueHandlerName + prefix + "_SharmNet_ReadyToRead");
                ewh_Reader_ReadyToWrite = new SWaitHadle(true, EventResetMode.ManualReset, sm.uniqueHandlerName + prefix + "_SharmNet_ReadyToWrite");
                ewh_Reader_ReadyToWrite.Set();
            }

            //if (sm.instanceType == tiesky.com.SharmIpc.eInstanceType.Slave)
            //{
            //    Console.WriteLine("My reader handlers:");
            //    Console.WriteLine(sm.uniqueHandlerName + prefix + "_SharmNet_ReadyToRead");
            //    Console.WriteLine(sm.uniqueHandlerName + prefix + "_SharmNet_ReadyToWrite");
            //    Console.WriteLine("-------");
            //}


            if (Reader_mmf == null)
            {
                //Reader_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity, MemoryMappedFileAccess.ReadWrite);
                //Reader_accessor = Reader_mmf.CreateViewAccessor(0, sm.bufferCapacity);

#if FULLNET
                var security = new MemoryMappedFileSecurity();
                security.AddAccessRule(new System.Security.AccessControl.AccessRule<MemoryMappedFileRights>(
                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
                    MemoryMappedFileRights.FullControl,
                    System.Security.AccessControl.AccessControlType.Allow));
                //Reader_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(@"Global\MapName1", sm.bufferCapacity, 
                Reader_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity,
                    MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.DelayAllocatePages, security, System.IO.HandleInheritability.Inheritable);
#else
                Reader_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity,
                    MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.DelayAllocatePages, System.IO.HandleInheritability.Inheritable);
#endif

                Reader_accessor = Reader_mmf.CreateViewAccessor(0, sm.bufferCapacity);
                //AcquirePointer();
                Reader_accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref Reader_accessor_ptr);
            }

            Task.Run(() =>
            {
                switch (this.sm.ProtocolVersion)
                {
                    case tiesky.com.SharmIpc.eProtocolVersion.V1:
                        ReaderV01();
                        break;
                    case tiesky.com.SharmIpc.eProtocolVersion.V2:
                        ReaderV02();
                        break;

                }
               
            });

            //ReaderV01wrapper();

            //ReaderV01();
        }

        unsafe void AcquirePointer()
        {
            Reader_accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref Reader_accessor_ptr);
        }


      
        enum eProtocolPosition
        {
            Init,
            MsgType,
            MsgId,
            PayloadLen,
            CurrentChunk,
            TotalChunks,
            ResponseMsgId,
            Payload,
            GoOut
        }

        async Task ReaderV02()
        {
            byte[] hdr = null;
            byte[] payload = null;
            byte[] ret = null;
            ushort iCurChunk = 0;
            ushort iTotChunk = 0;
            ulong iMsgId = 0;
            int iPayLoadLen = 0;
            ulong iResponseMsgId = 0;

            eMsgType msgType = eMsgType.RpcRequest;

            int jPos = 0;
            //byte[] jReadBytes = null;
            bool gout = false;
            
            byte[] sizer8 = new byte[8];
            byte[] sizer4 = new byte[4];
            byte[] sizer2 = new byte[2];
            int size = 0;
            Action ClearSizer8 = () =>
            {
                sizer8[0] = 0;
                sizer8[1] = 0;
                sizer8[2] = 0;
                sizer8[3] = 0;
                sizer8[4] = 0;
                sizer8[5] = 0;
                sizer8[6] = 0;
                sizer8[7] = 0;

                size = 0;
            };
            Action ClearSizer4 = () =>
            {
                sizer4[0] = 0;
                sizer4[1] = 0;
                sizer4[2] = 0;
                sizer4[3] = 0;

                size = 0;
            };
            Action ClearSizer2 = () =>
            {
                sizer2[0] = 0;
                sizer2[1] = 0;

                size = 0;
            };
            int iHdr = 0;
            int totalFileSize = 0;
            eProtocolPosition protPos = eProtocolPosition.Init;

            try
            {
                while (true)
                {

                    await WaitHandleAsyncFactory.FromWaitHandle(ewh_Reader_ReadyToRead);

                    if (Interlocked.Read(ref this.sm.SharmIPC.Disposed) == 1)
                        return;

                    //Setting STOP for ewh_Reader_ReadyToRead.WaitOne()
                    ewh_Reader_ReadyToRead.Reset();
                   
                    //Reading multiple packages
                    iHdr = 0;                                       
                    totalFileSize = 0;                   
                    protPos = eProtocolPosition.Init;
                    
/*Protocol V2     
    * 
    * Nbytes - totalSentLenght
    * 
    * START MESSAGE CYCLE
    * 
    * Nbytes - protobuf int - MsgType. StandardMsg value is 1 eMsgType (normally 1 byte)        
    * Nbytes - protobuf ulong  - msgId (ulong)
    * Nbytes - protobuf int  - payload length
    * Nbytes - currentChunkNr
    * Nbytes - totalChunks  //ChunksLeft (ushort) (if there is only 1 chunk, then chunks left will be 0. if there are 2 chunks: first will be 1 then will be 0)
    * Nbytes - protobuf ulong - responseMsgId
    * payload
    * ....
    * next message
    * ....
    * * END MESSAGE CYCLE
 */

                    while (true)
                    {   
                        if(gout)
                        {
                            gout = false;
                            break;
                        }

                        switch(protPos)
                        {
                            case eProtocolPosition.GoOut:
                                gout = true;
                                break;
                            case eProtocolPosition.Init:
                                payload = ReadBytes(0, 100); //Reading totalFileSize and a bit of the rest of the file
                                sizer4[size] = payload[size];                               
                                if ((sizer4[size] & 0x80) > 0)
                                    size++;
                                else
                                {
                                    size++; //we will use it as length
                                    totalFileSize = Convert.ToInt32(sizer4.FromProtoBytes());                                    
                                    
                                    hdr = new byte[totalFileSize];
                                    
                                    if ((size + totalFileSize) > payload.Length)
                                    {
                                        //Part need to be tested
                                        var usedSpace = payload.Length - size;
                                        Buffer.BlockCopy(payload, size, hdr, 0, usedSpace);
                                        payload = ReadBytes(payload.Length, totalFileSize - usedSpace);
                                        Buffer.BlockCopy(payload, 0, hdr, usedSpace, payload.Length - size);
                                    }
                                    else
                                        Buffer.BlockCopy(payload, size, hdr, 0, totalFileSize);

                                    ClearSizer4();
                                    protPos = eProtocolPosition.MsgType;
                                }
                                
                                break;
                            case eProtocolPosition.MsgType:                                
                                msgType = (eMsgType)hdr[iHdr];
                                iHdr++;                                
                                protPos = eProtocolPosition.MsgId;

                                break;
                            case eProtocolPosition.MsgId:
                                sizer8[size] = hdr[iHdr];
                                if ((sizer8[size] & 0x80) > 0)
                                    size++;
                                else
                                {
                                    iMsgId = sizer8.FromProtoBytes();
                                    ClearSizer8();
                                    protPos = eProtocolPosition.PayloadLen;
                                }
                                iHdr++;
                                break;
                            case eProtocolPosition.PayloadLen:
                                sizer4[size] = hdr[iHdr];
                                if ((sizer4[size] & 0x80) > 0)
                                    size++;
                                else
                                {
                                    iPayLoadLen = Convert.ToInt32(sizer4.FromProtoBytes());
                                    ClearSizer4();
                                    protPos = eProtocolPosition.CurrentChunk;
                                }
                                iHdr++;
                                break;
                            case eProtocolPosition.CurrentChunk:
                                sizer2[size] = hdr[iHdr];
                                if ((sizer2[size] & 0x80) > 0)
                                    size++;
                                else
                                {
                                    iCurChunk = Convert.ToUInt16(sizer2.FromProtoBytes());
                                    ClearSizer2();
                                    protPos = eProtocolPosition.TotalChunks;
                                }
                                iHdr++;
                                break;
                            case eProtocolPosition.TotalChunks:
                                sizer2[size] = hdr[iHdr];
                                if ((sizer2[size] & 0x80) > 0)
                                    size++;
                                else
                                {
                                    iTotChunk = Convert.ToUInt16(sizer2.FromProtoBytes());
                                    ClearSizer2();
                                    protPos = eProtocolPosition.ResponseMsgId;
                                }
                                iHdr++;
                                break;
                            case eProtocolPosition.ResponseMsgId:
                                sizer8[size] = hdr[iHdr];
                                if ((sizer8[size] & 0x80) > 0)
                                    size++;
                                else
                                {
                                    iResponseMsgId = sizer8.FromProtoBytes();
                                    ClearSizer8();
                                    protPos = eProtocolPosition.Payload;
                                }
                                iHdr++;
                                break;
                            case eProtocolPosition.Payload:
                                //Reading payload    
                                if (iPayLoadLen == Int32.MaxValue)
                                    payload = new byte[0];
                                else if (iPayLoadLen == 0)
                                    payload = null;
                                else
                                {
                                    payload = new byte[iPayLoadLen];
                                    Buffer.BlockCopy(hdr, iHdr, payload, 0, iPayLoadLen);
                                    iHdr += iPayLoadLen;
                                }

                                if(iHdr >= hdr.Length)
                                    protPos = eProtocolPosition.GoOut;
                                else
                                    protPos = eProtocolPosition.MsgType;
                                //Processing payload
                                switch (msgType)
                                {
                                    case eMsgType.ErrorInRpc:                                      
                                        this.sm.SharmIPC.InternalDataArrived(msgType, iResponseMsgId, null);                                        
                                        break;

                                    case eMsgType.RpcResponse:
                                    case eMsgType.RpcRequest:
                                    case eMsgType.Request:

                                        jPos = 7;                                      
                                      
                                        if (iCurChunk == 1)
                                            chunksCollected = null;
                                        else if (iCurChunk != currentChunk + 1)
                                        {
                                            //Wrong income, sending special signal back, waiting for new MsgId   
                                            switch (msgType)
                                            {
                                                case eMsgType.RpcRequest:                                                   
                                                    this.SendMessage(eMsgType.ErrorInRpc, this.GetMessageId(), null, iMsgId);                                                    
                                                    break;
                                                case eMsgType.RpcResponse:                                                  
                                                    this.sm.SharmIPC.InternalDataArrived(eMsgType.ErrorInRpc, iResponseMsgId, null);                                                   
                                                    break;
                                            }
                                            //!!! Here complete get out (sending instance has restarted and started new msgId sending cycle)
                                            protPos = eProtocolPosition.GoOut;
                                            break;
                                        }

                                        if (iTotChunk == iCurChunk)
                                        {
                                            jPos = 13;
                                            if (chunksCollected == null)
                                            {                                                
                                                jPos = 27;
                                                this.sm.SharmIPC.InternalDataArrived(msgType, 
                                                    (msgType == eMsgType.RpcResponse) ? iResponseMsgId : iMsgId, payload);
                                                
                                                jPos = 15;
                                            }
                                            else
                                            {
                                                jPos = 16;
                                                ret = new byte[iPayLoadLen + chunksCollected.Length];
                                                Buffer.BlockCopy(chunksCollected, 0, ret, 0, chunksCollected.Length);
                                                Buffer.BlockCopy(payload, 0, ret, chunksCollected.Length, iPayLoadLen);
                                                this.sm.SharmIPC.InternalDataArrived(msgType, (msgType == eMsgType.RpcResponse) ? iResponseMsgId : iMsgId, ret);
                                                jPos = 17;
                                            }
                                            chunksCollected = null;
                                            currentChunk = 0;
                                        }
                                        else
                                        {
                                            jPos = 18;
                                            if (chunksCollected == null)
                                            {
                                                jPos = 19;
                                                chunksCollected = payload;
                                                jPos = 20;
                                            }
                                            else
                                            {
                                                jPos = 21;
                                                ret = new byte[chunksCollected.Length + iPayLoadLen];
                                                Buffer.BlockCopy(chunksCollected, 0, ret, 0, chunksCollected.Length);
                                                Buffer.BlockCopy(payload, 0, ret, chunksCollected.Length, iPayLoadLen);
                                                chunksCollected = ret;
                                                jPos = 22;
                                            }
                                            jPos = 23;
                                            currentChunk = iCurChunk;
                                        }
                                        break;
                                   
                                    default:
                                        //Unknown protocol type
                                        jPos = 24;
                                        chunksCollected = null;
                                        currentChunk = 0;
                                        //Wrong income, doing nothing
                                        throw new Exception("tiesky.com.SharmIpc: Reading protocol contains errors");
                                        //break;
                                }//eo switch
                                break;
                        }
                       
                        

                    }//end of package

                    

                    jPos = 25;
                    //Setting signal 
                    ewh_Reader_ReadyToWrite.Set();

                    //--STAT
                    this.sm.SharmIPC.Statistic.Stop_ReadProcedure_Signal();

                    //--STAT
                    this.sm.SharmIPC.Statistic.Start_WaitForRead_Signal();

                    jPos = 26;
                }
            }
            catch (System.Exception ex)
            {
                //constrained execution region (CER)
                //https://msdn.microsoft.com/en-us/library/system.runtime.interopservices.safehandle.dangerousaddref(v=vs.110).aspx

                this.sm.SharmIPC.LogException("SharmIps.ReaderWriterHandler.InitReader LE, jPos=" + jPos + "; ", ex);
            }


        }//eo Reader V1


        async Task ReaderV01()
        {
            byte[] hdr = null;
            byte[] ret = null;
            ushort iCurChunk = 0;
            ushort iTotChunk = 0;
            ulong iMsgId = 0;
            int iPayLoadLen = 0;
            ulong iResponseMsgId = 0;

            eMsgType msgType = eMsgType.RpcRequest;
            int jPos = 0;
            int jProtocolLen = 0;
            int jPayloadLen = 0;
            byte[] jReadBytes = null;
         
            try
            {
                while (true)
                {
                    jPos = 0;

                    //Exchange these 2 lines to make Reader event wait in async mode
                    //ewh_Reader_ReadyToRead.WaitOne();
                    await WaitHandleAsyncFactory.FromWaitHandle(ewh_Reader_ReadyToRead);//.ConfigureAwait(false);


                    //--STAT
                    this.sm.SharmIPC.Statistic.Stop_WaitForRead_Signal();

                    if (Interlocked.Read(ref this.sm.SharmIPC.Disposed) == 1)
                        return;
                    //jPos = 1;
                    //if (ewh_Reader_ReadyToRead == null) //Special Dispose case
                    //    return;
                    //jPos = 2;

                    //--STAT
                    this.sm.SharmIPC.Statistic.Start_ReadProcedure_Signal();

                    //Setting STOP for ewh_Reader_ReadyToRead.WaitOne()
                    ewh_Reader_ReadyToRead.Reset();
                   


                    //Reading data from MMF
                    jPos = 3;
                    //Reading header
                    hdr = ReadBytes(0, protocolLen);
                    jPos = 4;
                    msgType = (eMsgType)hdr[0];

                    //Parsing header
                    switch (msgType)
                    {
                        case eMsgType.ErrorInRpc:
                            jPos = 5;
                            iPayLoadLen = BitConverter.ToInt32(hdr, 9); //+4
                            iResponseMsgId = BitConverter.ToUInt64(hdr, 17); //+8

                            this.sm.SharmIPC.InternalDataArrived(msgType, iResponseMsgId, null);
                            jPos = 6;
                            break;

                        case eMsgType.RpcResponse:
                        case eMsgType.RpcRequest:
                        case eMsgType.Request:

                            jPos = 7;
                            bool zeroByte = false;
                            iMsgId = BitConverter.ToUInt64(hdr, 1); //+8
                            iPayLoadLen = BitConverter.ToInt32(hdr, 9); //+4
                            if (iPayLoadLen == Int32.MaxValue)
                            {
                                zeroByte = true;
                                iPayLoadLen = 0;
                            }
                            iCurChunk = BitConverter.ToUInt16(hdr, 13); //+2
                            iTotChunk = BitConverter.ToUInt16(hdr, 15); //+2     
                            iResponseMsgId = BitConverter.ToUInt64(hdr, 17); //+8
                            jPos = 8;
                            if (iCurChunk == 1)
                                chunksCollected = null;
                            else if (iCurChunk != currentChunk + 1)
                            {
                                //Wrong income, sending special signal back, waiting for new MsgId   
                                switch (msgType)
                                {
                                    case eMsgType.RpcRequest:
                                        jPos = 9;
                                        this.SendMessage(eMsgType.ErrorInRpc, this.GetMessageId(), null, iMsgId);
                                        jPos = 10;
                                        break;
                                    case eMsgType.RpcResponse:
                                        jPos = 11;
                                        this.sm.SharmIPC.InternalDataArrived(eMsgType.ErrorInRpc, iResponseMsgId, null);
                                        jPos = 12;
                                        break;
                                }
                                break;
                            }

                            if (iTotChunk == iCurChunk)
                            {
                                jPos = 13;
                                if (chunksCollected == null)
                                {
                                    jPos = 14;
                                    //Was
                                    //this.sm.SharmIPC.InternalDataArrived(msgType, (msgType == eMsgType.RpcResponse) ? iResponseMsgId : iMsgId, iPayLoadLen == 0 ? ((zeroByte) ? new byte[0] : null) : ReadBytes(Reader_accessor_ptr, protocolLen, iPayLoadLen));
                                    jProtocolLen = protocolLen;
                                    jPayloadLen = iPayLoadLen;
                                    jReadBytes = ReadBytes(protocolLen, iPayLoadLen);
                                    jPos = 27;
                                    this.sm.SharmIPC.InternalDataArrived(msgType, (msgType == eMsgType.RpcResponse) ? iResponseMsgId : iMsgId, iPayLoadLen == 0 ? ((zeroByte) ? new byte[0] : null) : jReadBytes);
                                    ///////////// test
                                    jPos = 15;
                                }
                                else
                                {
                                    jPos = 16;
                                    ret = new byte[iPayLoadLen + chunksCollected.Length];
                                    Buffer.BlockCopy(chunksCollected, 0, ret, 0, chunksCollected.Length);
                                    Buffer.BlockCopy(ReadBytes(protocolLen, iPayLoadLen), 0, ret, chunksCollected.Length, iPayLoadLen);
                                    this.sm.SharmIPC.InternalDataArrived(msgType, (msgType == eMsgType.RpcResponse) ? iResponseMsgId : iMsgId, ret);
                                    jPos = 17;
                                }
                                chunksCollected = null;
                                currentChunk = 0;
                            }
                            else
                            {
                                jPos = 18;
                                if (chunksCollected == null)
                                {
                                    jPos = 19;
                                    chunksCollected = ReadBytes(protocolLen, iPayLoadLen);
                                    jPos = 20;
                                }
                                else
                                {
                                    jPos = 21;
                                    byte[] tmp = new byte[chunksCollected.Length + iPayLoadLen];
                                    Buffer.BlockCopy(chunksCollected, 0, tmp, 0, chunksCollected.Length);
                                    Buffer.BlockCopy(ReadBytes(protocolLen, iPayLoadLen), 0, tmp, chunksCollected.Length, iPayLoadLen);
                                    chunksCollected = tmp;
                                    jPos = 22;
                                }
                                jPos = 23;
                                currentChunk = iCurChunk;
                            }
                            break;                     
                        default:
                            //Unknown protocol type
                            jPos = 24;
                            chunksCollected = null;
                            currentChunk = 0;
                            //Wrong income, doing nothing
                            throw new Exception("tiesky.com.SharmIpc: Reading protocol contains errors");
                            //break;
                    }//eo switch

                    jPos = 25;
                    //Setting signal 
                    ewh_Reader_ReadyToWrite.Set();

                    //--STAT
                    this.sm.SharmIPC.Statistic.Stop_ReadProcedure_Signal();

                    //--STAT
                    this.sm.SharmIPC.Statistic.Start_WaitForRead_Signal();

                    jPos = 26;
                }
            }
            catch (System.Exception ex)
            {
                //latest jPos = 27
                /*
                 *  int jProtocolLen = 0;
            int jPayloadLen = 0;
            byte[] jReadBytes = null;
                 */
                /*					
                System.ObjectDisposedException: Das SafeHandle wurde geschlossen. bei System.Runtime.InteropServices.SafeHandle.DangerousAddRef(Boolean& success) 
                bei System.StubHelpers.StubHelpers.SafeHandleAddRef(SafeHandle pHandle, Boolean& success) 
                bei Microsoft.Win32.Win32Native.SetEvent(SafeWaitHandle handle) bei System.Threading.EventWaitHandle.Set() bei tiesky.com.SharmIpcInternals.ReaderWriterHandler.b__28_0()
                */

                /*
                 System.ObjectDisposedException: Das SafeHandle wurde geschlossen. bei System.Runtime.InteropServices.SafeHandle.DangerousAddRef(Boolean& success) 
                 bei Microsoft.Win32.Win32Native.SetEvent(SafeWaitHandle handle)  
                 bei System.Threading.EventWaitHandle.Set() bei tiesky.com.SharmIpcInternals.ReaderWriterHandler.b__28_0()
                 */

                //constrained execution region (CER)
                //https://msdn.microsoft.com/en-us/library/system.runtime.interopservices.safehandle.dangerousaddref(v=vs.110).aspx

                this.sm.SharmIPC.LogException("SharmIps.ReaderWriterHandler.InitReader LE, jPos=" + jPos + "; jProtLen=" + jProtocolLen + "; jPaylLen=" + jPayloadLen + "; jReadBytesLen=" + (jReadBytes == null ? 0 : jReadBytes.Length), ex);
            }


        }//eo Reader V1


        /// <summary>
        /// 
        /// </summary>
        //async Task ReaderV01_2(byte[] uhdr)
        //{
        //    byte[] hdr = null;
        //    byte[] ret = null;
        //    ushort iCurChunk = 0;
        //    ushort iTotChunk = 0;
        //    ulong iMsgId = 0;
        //    int iPayLoadLen = 0;
        //    ulong iResponseMsgId = 0;

        //    eMsgType msgType = eMsgType.RpcRequest;
        //    int jPos = 0;
        //    int jProtocolLen = 0;
        //    int jPayloadLen = 0;
        //    byte[] jReadBytes = null;
        //    int msgOffset = 3;
        //    ushort qMsg = 0;

        //    try
        //    {
        //        while (true)
        //        {
        //            msgOffset = 3;

        //            if(uhdr == null)
        //            {
        //                //if (sm.instanceType == eInstanceType.Slave)
        //                //    Console.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.ms") + "> reader is waiting ");

                        
        //                //Exchange these 2 lines to make Reader event wait in async mode
        //                //ewh_Reader_ReadyToRead.WaitOne();
        //                await WaitHandleAsyncFactory.FromWaitHandle(ewh_Reader_ReadyToRead);//.ConfigureAwait(true);


        //                if (Interlocked.Read(ref this.sm.SharmIPC.Disposed) == 1)
        //                    return;

        //                //--STAT
        //                this.sm.SharmIPC.Statistic.Stop_WaitForRead_Signal();
                                                
        //                if (ewh_Reader_ReadyToRead == null) //Special Dispose case
        //                    return;                        

        //                //--STAT
        //                this.sm.SharmIPC.Statistic.Start_ReadProcedure_Signal();

        //                //Setting STOP for ewh_Reader_ReadyToRead.WaitOne()
        //                ewh_Reader_ReadyToRead.Reset();
                    
        //                //Reading data from MMF
                        
        //                //Reading header
        //                hdr = ReadBytes(1, 2);
        //                qMsg = BitConverter.ToUInt16(hdr, 0);                     
        //            }
        //            else
        //            {
        //                //First income from previous protocol
        //                //getting quantity of records 
        //                qMsg = BitConverter.ToUInt16(uhdr, 1); //2 bytes will tell quantity of messages                        
        //                //Cleaning uhdr, from the next iterration will work only previous loop
        //                uhdr = null;
                        
        //            }
                    
        //            for (int z=0;z< qMsg; z++)
        //            {
        //                hdr = ReadBytes(msgOffset, protocolLen);

        //                msgType = (eMsgType)hdr[0];
        //                //Parsing header
        //                switch (msgType)
        //                {
        //                    case eMsgType.ErrorInRpc:
        //                        jPos = 5;
        //                        iPayLoadLen = BitConverter.ToInt32(hdr, 9); //+4
        //                        iResponseMsgId = BitConverter.ToUInt64(hdr, 17); //+8

        //                        this.sm.SharmIPC.InternalDataArrived(msgType, iResponseMsgId, null);
        //                        jPos = 6;
        //                        break;

        //                    case eMsgType.RpcResponse:
        //                    case eMsgType.RpcRequest:
        //                    case eMsgType.Request:

        //                        jPos = 7;
        //                        bool zeroByte = false;
        //                        iMsgId = BitConverter.ToUInt64(hdr, 1); //+8
        //                        iPayLoadLen = BitConverter.ToInt32(hdr, 9); //+4
        //                        if (iPayLoadLen == Int32.MaxValue)
        //                        {
        //                            zeroByte = true;
        //                            iPayLoadLen = 0;
        //                        }
        //                        iCurChunk = BitConverter.ToUInt16(hdr, 13); //+2
        //                        iTotChunk = BitConverter.ToUInt16(hdr, 15); //+2     
        //                        iResponseMsgId = BitConverter.ToUInt64(hdr, 17); //+8
        //                        jPos = 8;
        //                        if (iCurChunk == 1)
        //                        {
        //                            chunksCollected = null;
        //                            MsgId_Received = iMsgId;
        //                        }
        //                        else if (iCurChunk != currentChunk + 1)
        //                        {
        //                            //Wrong income, sending special signal back, waiting for new MsgId   
        //                            switch (msgType)
        //                            {
        //                                case eMsgType.RpcRequest:
        //                                    jPos = 9;
        //                                    this.SendMessage(eMsgType.ErrorInRpc, this.GetMessageId(), null, iMsgId);
        //                                    jPos = 10;
        //                                    break;
        //                                case eMsgType.RpcResponse:
        //                                    jPos = 11;
        //                                    this.sm.SharmIPC.InternalDataArrived(eMsgType.ErrorInRpc, iResponseMsgId, null);
        //                                    jPos = 12;
        //                                    break;
        //                            }
        //                            break;
        //                        }

        //                        if (iTotChunk == iCurChunk)
        //                        {
        //                            jPos = 13;
        //                            if (chunksCollected == null)
        //                            {
        //                                jPos = 14;
        //                                //Was
        //                                //this.sm.SharmIPC.InternalDataArrived(msgType, (msgType == eMsgType.RpcResponse) ? iResponseMsgId : iMsgId, iPayLoadLen == 0 ? ((zeroByte) ? new byte[0] : null) : ReadBytes(Reader_accessor_ptr, protocolLen, iPayLoadLen));
        //                                jProtocolLen = protocolLen;
        //                                jPayloadLen = iPayLoadLen;
        //                                jReadBytes = ReadBytes(msgOffset + protocolLen, iPayLoadLen);
        //                                jPos = 27;
        //                                this.sm.SharmIPC.InternalDataArrived(msgType, (msgType == eMsgType.RpcResponse) ? iResponseMsgId : iMsgId, iPayLoadLen == 0 ? ((zeroByte) ? new byte[0] : null) : jReadBytes);
        //                                ///////////// test
        //                                jPos = 15;
        //                            }
        //                            else
        //                            {
        //                                jPos = 16;
        //                                ret = new byte[iPayLoadLen + chunksCollected.Length];
        //                                Buffer.BlockCopy(chunksCollected, 0, ret, 0, chunksCollected.Length);
        //                                Buffer.BlockCopy(ReadBytes(msgOffset + protocolLen, iPayLoadLen), 0, ret, chunksCollected.Length, iPayLoadLen);
        //                                this.sm.SharmIPC.InternalDataArrived(msgType, (msgType == eMsgType.RpcResponse) ? iResponseMsgId : iMsgId, ret);
        //                                jPos = 17;
        //                            }
        //                            chunksCollected = null;
        //                            currentChunk = 0;
        //                        }
        //                        else
        //                        {
                                    

        //                            jPos = 18;
        //                            if (chunksCollected == null)
        //                            {
        //                                jPos = 19;
        //                                chunksCollected = ReadBytes(msgOffset + protocolLen, iPayLoadLen);
        //                                jPos = 20;
        //                            }
        //                            else
        //                            {
        //                                jPos = 21;
        //                                byte[] tmp = new byte[chunksCollected.Length + iPayLoadLen];
        //                                Buffer.BlockCopy(chunksCollected, 0, tmp, 0, chunksCollected.Length);
        //                                Buffer.BlockCopy(ReadBytes(msgOffset + protocolLen, iPayLoadLen), 0, tmp, chunksCollected.Length, iPayLoadLen);
        //                                chunksCollected = tmp;
        //                                jPos = 22;
        //                            }
        //                            jPos = 23;
        //                            currentChunk = iCurChunk;
        //                        }
        //                        break;
        //                    default:
        //                        //Unknown protocol type
        //                        jPos = 24;
        //                        chunksCollected = null;
        //                        currentChunk = 0;
        //                        //Wrong income, doing nothing
        //                        throw new Exception("tiesky.com.SharmIpc: Reading protocol contains errors");
        //                        //break;
        //                }//eo switch

        //                msgOffset += protocolLen + iPayLoadLen;
        //            }//eo for loop of messages
                    
        //            //Setting signal 
        //            ewh_Reader_ReadyToWrite.Set();

        //            //--STAT
        //            this.sm.SharmIPC.Statistic.Stop_ReadProcedure_Signal();

        //            //--STAT
        //            this.sm.SharmIPC.Statistic.Start_WaitForRead_Signal();                    
        //        }
        //    }
        //    catch (System.Exception ex)
        //    {             
        //        //constrained execution region (CER)
        //        //https://msdn.microsoft.com/en-us/library/system.runtime.interopservices.safehandle.dangerousaddref(v=vs.110).aspx

        //        this.sm.SharmIPC.LogException("SharmIps.ReaderWriterHandler.InitReader LE, jPos=" + jPos + "; jProtLen=" + jProtocolLen + "; jPaylLen=" + jPayloadLen + "; jReadBytesLen=" + (jReadBytes == null ? 0 : jReadBytes.Length), ex);
        //    }


        //}//EO ReaderV02


    }//eo class 
}

