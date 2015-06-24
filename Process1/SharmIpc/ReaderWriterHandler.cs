using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace tiesky.com.SharmIpc
{
    internal class ReaderWriterHandler:IDisposable
    {
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor Writer_accessor = null;
        System.IO.MemoryMappedFiles.MemoryMappedFile Writer_mmf = null;

        EventWaitHandle ewh_Writer_ReadyToRead = null;
        EventWaitHandle ewh_Writer_ReadyToWrite = null;

        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor Reader_accessor = null;
        System.IO.MemoryMappedFiles.MemoryMappedFile Reader_mmf = null;

        EventWaitHandle ewh_Reader_ReadyToRead = null;
        EventWaitHandle ewh_Reader_ReadyToWrite = null;

        SharedMemory sm = null;
        object lock_q = new object();
        Queue<byte[]> q = new Queue<byte[]>();
        byte[] toSend = null;
        bool inSend = false;
        int bufferLenS = 0;

        bool disposed = false;

        /// <summary>
        /// MsgId of the sender and payload
        /// </summary>
        Action<eMsgType, ulong, byte[]> DataArrived = null;
        

        public void Dispose()
        {
            disposed = true;
            //mreSmthToSend.Set();

            try
            {
                if (ewh_Writer_ReadyToRead != null)
                {
                    //ewh_ReadyToRead.Set();
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
                    //ewh_ReadyToRead.Set();
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
                    //ewh_ReadyToRead.Set();
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
                    //ewh_ReadyToRead.Set();
                    ewh_Reader_ReadyToWrite.Close();
                    ewh_Reader_ReadyToWrite.Dispose();
                    ewh_Reader_ReadyToWrite = null;
                }
            }
            catch
            {}

            try
            {
                if (Writer_accessor != null)
                {
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


        public ReaderWriterHandler(SharedMemory sm, Action<eMsgType, ulong, byte[]> DataArrived)
        {
            this.sm = sm;
            this.DataArrived = DataArrived;
            this.bufferLenS = Convert.ToInt32(sm.bufferCapacity) - protocolLen;

            this.InitWriter();
            this.InitReader();

            //SendProcedure1();
        }


        void InitWriter()
        {
            string prefix = sm.instanceType == tiesky.com.SharmIpc.eInstanceType.Master ? "1" : "2";

            if (ewh_Writer_ReadyToRead == null)
            {
                ewh_Writer_ReadyToRead = new EventWaitHandle(false, EventResetMode.ManualReset, sm.uniqueHandlerName + prefix + "_SharmNet_ReadyToRead");
                ewh_Writer_ReadyToWrite = new EventWaitHandle(true, EventResetMode.ManualReset, sm.uniqueHandlerName + prefix + "_SharmNet_ReadyToWrite");
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
                Writer_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity, MemoryMappedFileAccess.ReadWrite);
                Writer_accessor = Writer_mmf.CreateViewAccessor(0, sm.bufferCapacity);
            }
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
         * for MsgType Response 
         * payload
         */

        int totalBytesInQUeue = 0;

        public ulong GetMessageId()
        {
            lock (lock_q)
            {
                return ++msgId_Sending;
            }
        }

        /// <summary>
        /// Returns internal msgId
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        public bool SendMessage(eMsgType msgType, ulong msgId, byte[] msg, ulong responseMsgId=0)
        {
            //lock (lock_q)
            //{
            //    q.Enqueue(msg);
            //}

            //ulong retMsgId = 0;            

            if (totalBytesInQUeue > sm.maxQueueSizeInBytes)
                return false;

            lock (lock_q)
            {

                //Splitting message
                int i = 0;
                int left = msg == null ? 0 : msg.Length;

                byte[] pMsg = null;
                
                ushort totalChunks = msg == null ? (ushort)1 : Convert.ToUInt16(Math.Ceiling((double)msg.Length / (double)bufferLenS));
                ushort currentChunk = 1;

                //msgId_Sending++;
                //retMsgId = msgId_Sending;
                //byte[] xbt = null;

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
                        if(msg != null)
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
                        Buffer.BlockCopy(BitConverter.GetBytes(left), 0, pMsg, 9, 4);  //payload len
                        Buffer.BlockCopy(BitConverter.GetBytes(currentChunk), 0, pMsg, 13, 2);  //current chunk
                        Buffer.BlockCopy(BitConverter.GetBytes(totalChunks), 0, pMsg, 15, 2);  //total chunks
                        Buffer.BlockCopy(BitConverter.GetBytes(responseMsgId), 0, pMsg, 17, 8);  //total chunks

                        //Writing payload
                        if (msg != null)
                            Buffer.BlockCopy(msg, i, pMsg, protocolLen, left);

                        q.Enqueue(pMsg);
                        break;
                    }

                    currentChunk++;
                }

                ////For SendProcedure1
                //mreSmthToSend.Set();
            }//eo lock

            
            StartSendProcedure();

            return true;
        }

        //ManualResetEventSlim mreSmthToSend = new ManualResetEventSlim(false);
        //ManualResetEvent mreSmthToSend = new ManualResetEvent(false);

        /*SendProcedure1
        void SendProcedure1()
        {
            Task.Run(() =>
                {
                    while (true)
                    {
                        if (toSend == null)
                        {
                            while (true)
                            {
                                mreSmthToSend.WaitOne();//.Wait();

                                if (disposed)
                                    return;

                                lock (lock_q)
                                {
                                    if (q.Count() > 0)
                                    {
                                        toSend = q.Dequeue();
                                        break;
                                    }
                                    else
                                        mreSmthToSend.Reset();
                                }
                            }
                        }



                        if (ewh_Writer_ReadyToWrite.WaitOne(2 * 1000))
                        {
                            ewh_Writer_ReadyToWrite.Reset();
                            //Writing into MMF      

                            //Writer_accessor.WriteArray<byte>(0, toSend, 0, toSend.Length);
                            this.WriteBytes(0, toSend);

                            //Setting signal ready to read
                            ewh_Writer_ReadyToRead.Set();

                            lock (lock_q)
                            {
                                toSend = null;
                                if (q.Count() != 0)
                                    toSend = q.Dequeue();
                                else
                                    mreSmthToSend.Reset();                                
                            }
                        }
                        else
                        {
                            Console.WriteLine(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff") + "> Timeout of sending we must repeat operation");
                        }


                    }//eo while 
                });
        }
        */
        
        void StartSendProcedure()
        {
            lock (lock_q)
            {
                if (inSend)
                    return;

                inSend = true;
            }

            Task.Run(() =>
            {
                lock (lock_q)
                {
                    if (toSend == null)
                    {
                        if (q.Count() > 0)
                        {
                            toSend = q.Dequeue();
                            totalBytesInQUeue -= toSend.Length;
                        }
                        else
                        {
                            inSend = false;
                            return;
                        }
                    }
                }

                //here we got smth toSend
                while(true)
                {
                    if (ewh_Writer_ReadyToWrite.WaitOne(2 * 1000))
                    {
                        ewh_Writer_ReadyToWrite.Reset();
                        //Writing into MMF      
                                                
                        //Writer_accessor.WriteArray<byte>(0, toSend, 0, toSend.Length);
                        this.WriteBytes(0, toSend);

                        //Setting signal ready to read
                        ewh_Writer_ReadyToRead.Set();

                        lock (lock_q)
                        {
                            toSend = null;
                            if (q.Count() == 0)
                            {
                                //Console.WriteLine(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff") + "> Out of thread");
                                inSend = false;
                                return;
                            }
                            toSend = q.Dequeue();
                            totalBytesInQUeue -= toSend.Length;
                        }
                    }
                    else
                    {
                        Console.WriteLine(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff") + "> Timeout of sending we must repeat operation");
                    }
                }

            });

        }//eom
        



        unsafe void WriteBytes(int offset, byte[] data)
        {
            byte* ptr = (byte*)0;
            Writer_accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            Marshal.Copy(data, 0, IntPtr.Add(new IntPtr(ptr), offset), data.Length);
            Writer_accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }

        unsafe byte[] ReadBytes(int offset, int num)
        {
            byte[] arr = new byte[num];
            byte* ptr = (byte*)0;
            Reader_accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            Marshal.Copy(IntPtr.Add(new IntPtr(ptr), offset), arr, 0, num);
            Reader_accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            return arr;
        }

        /*
        public void TestSendMessage()
        {
            Task.Run(() =>
            {
                byte[] tbt = new byte[512];

                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                for (int i = 0; i < 100000; i++)
                {

                    if (ewh_Writer_ReadyToWrite.WaitOne(2 * 1000))
                    {
                        ewh_Writer_ReadyToWrite.Reset();
                        //if still program must work then go on
                        //Writing into MMF
                        //accessor.Write(0, i);   

                        //To check
                        //https://msdn.microsoft.com/en-us/library/system.io.memorymappedfiles.memorymappedviewaccessor.safememorymappedviewhandle(v=vs.100).aspx


                        //Writer_accessor.WriteArray<byte>(0, tbt, 0, tbt.Length);
                        WriteBytes(0, tbt);

                        //Setting signal ready to read
                        ewh_Writer_ReadyToRead.Set();
                    }
                    else
                        Console.WriteLine(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff") + "> Timeout");
                }
                sw.Stop();
                Console.WriteLine(sw.ElapsedMilliseconds);

            });
        }
        */




        ulong MsgId_Received = 0;
        ushort currentChunk = 0;
        byte[] chunksCollected = null;

        /// <summary>
        /// 
        /// </summary>
        void InitReader()
        {
            string prefix = sm.instanceType == tiesky.com.SharmIpc.eInstanceType.Slave ? "1" : "2";

            if (ewh_Reader_ReadyToRead == null)
            {
                
                ewh_Reader_ReadyToRead = new EventWaitHandle(false, EventResetMode.ManualReset, sm.uniqueHandlerName + prefix + "_SharmNet_ReadyToRead");
                ewh_Reader_ReadyToWrite = new EventWaitHandle(true, EventResetMode.ManualReset, sm.uniqueHandlerName + prefix + "_SharmNet_ReadyToWrite");
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
                Reader_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity, MemoryMappedFileAccess.ReadWrite);
                Reader_accessor = Reader_mmf.CreateViewAccessor(0, sm.bufferCapacity);
            }

            Task.Run(() =>
            {
                byte[] hdr = null;
                byte[] ret=null;
                ushort iCurChunk = 0;
                ushort iTotChunk = 0;
                ulong iMsgId = 0;
                int iPayLoadLen = 0;
                ulong iResponseMsgId = 0;

                eMsgType msgType = eMsgType.RpcRequest;

                while (true)
                {
                    ewh_Reader_ReadyToRead.WaitOne();
                    ewh_Reader_ReadyToRead.Reset();
                    //Reading data from MMF

                    //Reading header
                    hdr = ReadBytes(0, protocolLen);
                    msgType = (eMsgType)hdr[0];

                    //Parsing header
                    switch (msgType)
                    {
                        case eMsgType.ErrorInRpc:

                            iPayLoadLen = BitConverter.ToInt32(hdr, 9); //+4
                            iResponseMsgId = BitConverter.ToUInt64(hdr, 17); //+8

                            DataArrived(msgType, iResponseMsgId, null);
                            break;
                        
                        case eMsgType.RpcResponse:
                        case eMsgType.RpcRequest:
                        case eMsgType.Request:

                            iMsgId = BitConverter.ToUInt64(hdr, 1); //+8
                            iPayLoadLen = BitConverter.ToInt32(hdr, 9); //+4
                            iCurChunk = BitConverter.ToUInt16(hdr, 13); //+2
                            iTotChunk = BitConverter.ToUInt16(hdr, 15); //+2     
                            iResponseMsgId = BitConverter.ToUInt64(hdr, 17); //+8

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
                                        this.SendMessage(eMsgType.ErrorInRpc, this.GetMessageId(), null, iMsgId);
                                        break;
                                    case eMsgType.RpcResponse:
                                        DataArrived(eMsgType.ErrorInRpc, iResponseMsgId, null);                                        
                                        break;
                                }                              
                                break; 
                            }

                            if (iTotChunk == iCurChunk)
                            {
                                if (chunksCollected == null)
                                    DataArrived(msgType, (msgType == eMsgType.RpcResponse) ? iResponseMsgId : iMsgId, iPayLoadLen == 0 ? null : ReadBytes(protocolLen, iPayLoadLen));
                                else
                                {
                                    ret = new byte[iPayLoadLen + chunksCollected.Length];
                                    Buffer.BlockCopy(chunksCollected, 0, ret, 0, chunksCollected.Length);
                                    Buffer.BlockCopy(ReadBytes(protocolLen, iPayLoadLen), 0, ret, chunksCollected.Length, iPayLoadLen);
                                    DataArrived(msgType, (msgType == eMsgType.RpcResponse) ? iResponseMsgId : iMsgId, ret);
                                }
                                chunksCollected = null;
                                currentChunk = 0;
                            }
                            else
                            {
                                if (chunksCollected == null)
                                {
                                    chunksCollected = ReadBytes(protocolLen, iPayLoadLen);
                                }
                                else
                                {

                                    byte[] tmp = new byte[chunksCollected.Length + iPayLoadLen];
                                    Buffer.BlockCopy(chunksCollected, 0, tmp, 0, chunksCollected.Length);
                                    Buffer.BlockCopy(ReadBytes(protocolLen, iPayLoadLen), 0, tmp, chunksCollected.Length, iPayLoadLen);
                                    chunksCollected = tmp;
                                }

                                currentChunk = iCurChunk;
                            }      
                            break;
                        default:
                            //Unknown protocol type
                            chunksCollected = null;
                            currentChunk = 0;                                    
                            //Wrong income, doing nothing
                            break;
                    }

                    //Setting signal 
                    ewh_Reader_ReadyToWrite.Set();
                }
            });
        }

        
                
    }
}

