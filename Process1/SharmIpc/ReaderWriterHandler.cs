using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

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
                
                Writer_accessor = Writer_mmf.CreateViewAccessor(0, sm.bufferCapacity);
                Writer_accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref Writer_accessor_ptr);
            }

            Task.Run(() =>
            {
                WriterV01();
            });
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
                    new Exception("ReaderWriterHandler max queue treshold is reached " + sm.maxQueueSizeInBytes));

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


                mre_writer_thread.Set();

            }//eo lock


            //StartSendProcedure();
            //StartSendProcedure_v2();
            

            return true;
        }


        //void WriterV01()
        async Task WriterV01()
        {
            try
            {
                while (true)
                {

                    //if(mre_writer_thread.WaitOne())
                    await mre_writer_thread.WaitAsync();

                    {
                        //if (Interlocked.Read(ref this.sm.SharmIPC.Disposed) == 1)
                        //    return;

                        lock (lock_q)
                        {
                            if (q.Count() < 1)
                            {
                                mre_writer_thread.Reset();
                                continue;
                            }
                            toSend = q.Dequeue();
                            totalBytesInQUeue -= toSend.Length;

                        }

                        if (ewh_Writer_ReadyToWrite.WaitOne())
                        {

                            //--STAT
                            this.sm.SharmIPC.Statistic.StopToWait_ReadyToWrite_Signal();

                            ewh_Writer_ReadyToWrite.Reset();
                            //Writing into MMF      

                            //Writer_accessor.WriteArray<byte>(0, toSend, 0, toSend.Length);
                            //this.WriteBytes(Writer_accessor_ptr, 0, toSend);
                            WriteBytes(0, toSend);

                            //Setting signal ready to read
                            ewh_Writer_ReadyToRead.Set();

                        }

                    }


                }
            }
            catch (Exception ex)
            {
                //this.sm.SharmIPC.LogException("SharmIps.ReaderWriterHandler.WriterV01", ex);
            }
            

        }


        void StartSendProcedure()
        {
            lock (lock_q)
            {
                if (inSend || q.Count() < 1)
                    return;

                inSend = true;
                toSend = q.Dequeue();
                totalBytesInQUeue -= toSend.Length;

            }

            //Task.Run(() =>
            //{
                //here we got smth toSend
                while (true)
                {

                    //--STAT
                    this.sm.SharmIPC.Statistic.StartToWait_ReadyToWrite_Signal();

                    //if (ewh_Writer_ReadyToWrite.WaitOne(2 * 1000))

                    // if (await WaitHandleAsyncFactory.FromWaitHandle(ewh_Writer_ReadyToWrite).ConfigureAwait(false))
                    if (ewh_Writer_ReadyToWrite.WaitOne())
                    {
                        //--STAT
                        this.sm.SharmIPC.Statistic.StopToWait_ReadyToWrite_Signal();

                        ewh_Writer_ReadyToWrite.Reset();
                        //Writing into MMF      

                        //Writer_accessor.WriteArray<byte>(0, toSend, 0, toSend.Length);
                        //this.WriteBytes(Writer_accessor_ptr, 0, toSend);
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

            //});

        }//eof




        byte[] btMln = new byte[1000000];

        void StartSendProcedure_v2()
        {
            lock (lock_q)
            {
                if (inSend || q.Count() < 1)
                    return;

                inSend = true;
                toSend = q.Dequeue();
                totalBytesInQUeue -= toSend.Length;

            }

            //Task.Run(() =>
            //{
                uint p = 0;
                int ptr = 0;
               
                //here we got smth toSend
                while (true)
                {
                    p = 0;
                    ptr = 0;

                    //--STAT
                    this.sm.SharmIPC.Statistic.StartToWait_ReadyToWrite_Signal();

                //if(sm.instanceType == eInstanceType.Master)
                //    Console.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.ms") + "> waiting of writing");

                //if (ewh_Writer_ReadyToWrite.WaitOne(2 * 1000))
                //if (await WaitHandleAsyncFactory.FromWaitHandle(ewh_Writer_ReadyToWrite).ConfigureAwait(false))
                if (ewh_Writer_ReadyToWrite.WaitOne())
                {
                        //--STAT
                        this.sm.SharmIPC.Statistic.StopToWait_ReadyToWrite_Signal();

                        ewh_Writer_ReadyToWrite.Reset();
                        //Writing into MMF      


                        while(true)
                        {
                            //Collecting messages 2b sent
                            if (toSend == null)
                            {
                                lock (lock_q)
                                {
                                    if (q.Count() < 1)
                                        break;

                                    toSend = q.Dequeue();
                                }
                                totalBytesInQUeue -= toSend.Length;
                                if (ptr + toSend.Length > btMln.Length)
                                    break;
                            }

                            Buffer.BlockCopy(toSend, 0, btMln, ptr, toSend.Length);
                            p++;
                            ptr += toSend.Length;
                            toSend = null;
                            if (ptr >= btMln.Length)
                                break;
                        }

                        toSend = new byte[3 + ptr];
                        Buffer.BlockCopy(btMln, 0, toSend, 3, ptr);                        
                        Buffer.BlockCopy(BitConverter.GetBytes(p), 0, toSend, 1, 2);
                        toSend[0] = 5;

                    //Writer_accessor.WriteArray<byte>(0, toSend, 0, toSend.Length);

                    //this.WriteBytes(Writer_accessor_ptr, 0, toSend);
                    this.WriteBytes(0, toSend);
                    toSend = null;

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

            //});

        }//eof

        //unsafe void WriteBytes(byte* ptr, int offset, byte[] data)
        //{
        //    //--STAT
        //    this.sm.SharmIPC.Statistic.Writing(data.Length);

        //    Marshal.Copy(data, 0, IntPtr.Add(new IntPtr(ptr), offset), data.Length);

        //    //https://msdn.microsoft.com/en-us/library/system.io.memorymappedfiles.memorymappedviewaccessor.safememorymappedviewhandle(v=vs.100).aspx
        //}

        //unsafe byte[] ReadBytes(byte* ptr, int offset, int num)
        //{
        //    //--STAT
        //    this.sm.SharmIPC.Statistic.Reading(num);

        //    byte[] arr = new byte[num];
        //    Marshal.Copy(IntPtr.Add(new IntPtr(ptr), offset), arr, 0, num);
        //    return arr;
        //}

        unsafe void WriteBytes(int offset, byte[] data)
        {
            //--STAT
            this.sm.SharmIPC.Statistic.Writing(data.Length);

            Marshal.Copy(data, 0, IntPtr.Add(new IntPtr(Writer_accessor_ptr), offset), data.Length);

            //https://msdn.microsoft.com/en-us/library/system.io.memorymappedfiles.memorymappedviewaccessor.safememorymappedviewhandle(v=vs.100).aspx
        }

        unsafe byte[] ReadBytes(int offset, int num)
        {
            //--STAT
            this.sm.SharmIPC.Statistic.Reading(num);

            byte[] arr = new byte[num];
            Marshal.Copy(IntPtr.Add(new IntPtr(Reader_accessor_ptr), offset), arr, 0, num);
            return arr;
        }
        
        ulong MsgId_Received = 0;
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

                var security = new MemoryMappedFileSecurity();
                security.AddAccessRule(new System.Security.AccessControl.AccessRule<MemoryMappedFileRights>(
                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
                    MemoryMappedFileRights.FullControl,
                    System.Security.AccessControl.AccessControlType.Allow));
                //Reader_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(@"Global\MapName1", sm.bufferCapacity, 
                Reader_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity,
                    MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.DelayAllocatePages, security, System.IO.HandleInheritability.Inheritable);


                Reader_accessor = Reader_mmf.CreateViewAccessor(0, sm.bufferCapacity);
                //AcquirePointer();
                Reader_accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref Reader_accessor_ptr);
            }

            Task.Run(() =>
            {
                ReaderV01();
            });

            //ReaderV01wrapper();

            //ReaderV01();
        }

        unsafe void AcquirePointer()
        {
            Reader_accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref Reader_accessor_ptr);
        }

        //async Task ReaderV01wrapper()
        //{
        //    Task.Run(() =>
        //    {
        //        await ReaderV01();
        //    });
        //}
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
                            {
                                chunksCollected = null;
                                MsgId_Received = iMsgId;
                            }
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
                        //Such changes can run other protocols
                        case eMsgType.SwitchToV2:
                            ReaderV02(hdr);                           
                            return;
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
        async void ReaderV02(byte[] uhdr)
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
            int msgOffset = 3;
            ushort qMsg = 0;

            try
            {
                while (true)
                {
                    msgOffset = 3;

                    if(uhdr == null)
                    {
                        //if (sm.instanceType == eInstanceType.Slave)
                        //    Console.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.ms") + "> reader is waiting ");

                        
                        //Exchange these 2 lines to make Reader event wait in async mode
                        //ewh_Reader_ReadyToRead.WaitOne();
                        await WaitHandleAsyncFactory.FromWaitHandle(ewh_Reader_ReadyToRead);//.ConfigureAwait(true);

                        //--STAT
                        this.sm.SharmIPC.Statistic.Stop_WaitForRead_Signal();
                                                
                        if (ewh_Reader_ReadyToRead == null) //Special Dispose case
                            return;                        

                        //--STAT
                        this.sm.SharmIPC.Statistic.Start_ReadProcedure_Signal();

                        //Setting STOP for ewh_Reader_ReadyToRead.WaitOne()
                        ewh_Reader_ReadyToRead.Reset();
                    
                        //Reading data from MMF
                        
                        //Reading header
                        hdr = ReadBytes(1, 2);
                        qMsg = BitConverter.ToUInt16(hdr, 0);                     
                    }
                    else
                    {
                        //First income from previous protocol
                        //getting quantity of records 
                        qMsg = BitConverter.ToUInt16(uhdr, 1); //2 bytes will tell quantity of messages                        
                        //Cleaning uhdr, from the next iterration will work only previous loop
                        uhdr = null;
                        
                    }
                    
                    for (int z=0;z< qMsg; z++)
                    {
                        hdr = ReadBytes(msgOffset, protocolLen);

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
                                {
                                    chunksCollected = null;
                                    MsgId_Received = iMsgId;
                                }
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
                                        jReadBytes = ReadBytes(msgOffset + protocolLen, iPayLoadLen);
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
                                        Buffer.BlockCopy(ReadBytes(msgOffset + protocolLen, iPayLoadLen), 0, ret, chunksCollected.Length, iPayLoadLen);
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
                                        chunksCollected = ReadBytes(msgOffset + protocolLen, iPayLoadLen);
                                        jPos = 20;
                                    }
                                    else
                                    {
                                        jPos = 21;
                                        byte[] tmp = new byte[chunksCollected.Length + iPayLoadLen];
                                        Buffer.BlockCopy(chunksCollected, 0, tmp, 0, chunksCollected.Length);
                                        Buffer.BlockCopy(ReadBytes(msgOffset + protocolLen, iPayLoadLen), 0, tmp, chunksCollected.Length, iPayLoadLen);
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

                        msgOffset += protocolLen + iPayLoadLen;
                    }//eo for loop of messages
                    
                    //Setting signal 
                    ewh_Reader_ReadyToWrite.Set();

                    //--STAT
                    this.sm.SharmIPC.Statistic.Stop_ReadProcedure_Signal();

                    //--STAT
                    this.sm.SharmIPC.Statistic.Start_WaitForRead_Signal();                    
                }
            }
            catch (System.Exception ex)
            {             
                //constrained execution region (CER)
                //https://msdn.microsoft.com/en-us/library/system.runtime.interopservices.safehandle.dangerousaddref(v=vs.110).aspx

                this.sm.SharmIPC.LogException("SharmIps.ReaderWriterHandler.InitReader LE, jPos=" + jPos + "; jProtLen=" + jProtocolLen + "; jPaylLen=" + jPayloadLen + "; jReadBytesLen=" + (jReadBytes == null ? 0 : jReadBytes.Length), ex);
            }


        }//EO ReaderV02


    }//eo class 
}

