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
    internal class ReaderWriterHandler:IDisposable
    {
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor Writer_accessor = null;
        System.IO.MemoryMappedFiles.MemoryMappedFile Writer_mmf = null;
        unsafe byte* Writer_accessor_ptr = (byte*)0;

        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor Reader_accessor = null;
        System.IO.MemoryMappedFiles.MemoryMappedFile Reader_mmf = null;
        unsafe byte* Reader_accessor_ptr = (byte*)0;

        SharedMemory sm = null;
        object lock_q = new object();
        Queue<byte[]> q = new Queue<byte[]>();
        byte[] toSend = null;
        bool inSend = false;
        int bufferLenS = 0;

        public void Dispose()
        {
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

            SspinWait.Measure();

            this.InitWriter();  //Must be initiated first
            this.InitReader();

        }


        unsafe void InitWriter()
        {
            string prefix = "1";
            sm.instanceType = tiesky.com.SharmIpcInternals.eInstanceType.Master;

            if (!sm.SharmIPC.Master)
            {
                sm.instanceType = eInstanceType.Slave;
                prefix = "2";
            }            

            if (Writer_mmf == null)
            {
                //Writer_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity, MemoryMappedFileAccess.ReadWrite);

                var security = new MemoryMappedFileSecurity();                
                security.AddAccessRule(new System.Security.AccessControl.AccessRule<MemoryMappedFileRights>(
                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null), 
                    MemoryMappedFileRights.FullControl, 
                    System.Security.AccessControl.AccessControlType.Allow));
                //Writer_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(@"Global\MapName1", sm.bufferCapacity, 
                Writer_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity,
                    MemoryMappedFileAccess.ReadWrite,MemoryMappedFileOptions.DelayAllocatePages, security, System.IO.HandleInheritability.Inheritable);

                //try
                //{
                //    //Trying master
                //    Writer_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity, MemoryMappedFileAccess.ReadWrite);
                //}
                //catch (Exception)
                //{
                //    prefix = "2";
                //    sm.instanceType = tiesky.com.SharmIpcInternals.eInstanceType.Slave;
                //    Writer_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity, MemoryMappedFileAccess.ReadWrite);
                //}

                //try
                //{
                //    //Trying master
                //    Writer_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateNew(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity, MemoryMappedFileAccess.ReadWrite);
                //}
                //catch (Exception)
                //{
                //    prefix = "2";
                //    sm.instanceType = tiesky.com.SharmIpcInternals.eInstanceType.Slave;
                //    Writer_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity, MemoryMappedFileAccess.ReadWrite);
                //}

                Writer_accessor = Writer_mmf.CreateViewAccessor(0, sm.bufferCapacity);
                Writer_accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref Writer_accessor_ptr);
            }
        }

        const int technicalLen = 2;
        const int protocolLen = 25; //Where 2 (communication signals: semaphore and ping)
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
        /// <param name="msg"></param>
        /// <returns></returns>
        public bool SendMessage(eMsgType msgType, ulong msgId, byte[] msg, ulong responseMsgId=0)
        {

            if (totalBytesInQUeue > sm.maxQueueSizeInBytes)
            {
                //Cleaning queue
                lock (lock_q)
                {
                    totalBytesInQUeue = 0;
                    q.Clear();
                }
                //Generating exception
                throw new Exception("tiesky.com.SharmIpc: ReaderWriterHandler max queue treshold is reached " + sm.maxQueueSizeInBytes);
                //return false;
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

                ////For SendProcedure1
                //mreSmthToSend.Set();
            }//eo lock

            
            StartSendProcedure();

            return true;
        }
              
        /// <summary>
        /// 
        /// </summary>
        unsafe void StartSendProcedure()
        {
            lock (lock_q)
            {
                if (inSend)
                    return;

                inSend = true;
            }

            //Task.Run(() =>
            //{
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

            var spinWait = new SspinWait();

            //here we got smth toSend
            while (true)
            {
                if (ReadBytes(Writer_accessor_ptr, 0, 1)[0] != 0)
                {
                    //Not read yet, waiting
                    //spinWait.SpinOnce();
                    //Thread.SpinWait(100);

                    spinWait.Spin();
                }
                else
                {
                    spinWait.Clear();
                    //we can freely write here
                    this.WriteBytes(Writer_accessor_ptr, technicalLen, toSend);
                    //Setting signal for reader                    
                    WriteBytes(Writer_accessor_ptr, 0, new byte[] { 1 });

                    lock (lock_q)
                    {
                        toSend = null;
                        if (q.Count() == 0)
                        {
                            //Console.WriteLine(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff") + "> Out of thread");
                            //    Console.WriteLine(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff") + "> Timeout of sending we must repeat operation");
                            inSend = false;
                            return;
                        }
                        toSend = q.Dequeue();
                        totalBytesInQUeue -= toSend.Length;
                    }
                }
            }

        //});

        }//eom
        



        //unsafe void WriteBytes(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor accessor, int offset, byte[] data)
        //{
        //    byte* ptr = (byte*)0;
        //    accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        //    Marshal.Copy(data, 0, IntPtr.Add(new IntPtr(ptr), offset), data.Length);
        //    accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        //}

        //unsafe byte[] ReadBytes(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor accessor, int offset, int num)
        //{
        //    byte[] arr = new byte[num];
        //    byte* ptr = (byte*)0;
        //    accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        //    Marshal.Copy(IntPtr.Add(new IntPtr(ptr), offset), arr, 0, num);
        //    accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        //    return arr;
        //}


        unsafe void WriteBytes(byte* ptr, int offset, byte[] data)
        {           
            Marshal.Copy(data, 0, IntPtr.Add(new IntPtr(ptr), offset), data.Length);         
        }

        unsafe byte[] ReadBytes(byte* ptr, int offset, int num)
        {
            byte[] arr = new byte[num];           
            Marshal.Copy(IntPtr.Add(new IntPtr(ptr), offset), arr, 0, num);            
            return arr;
        }



        ulong MsgId_Received = 0;
        ushort currentChunk = 0;
        byte[] chunksCollected = null;

        DateTime pingLastRead = DateTime.MinValue;
        byte LastPingValue = 0;
        byte CurrentPingValue = 0;
        byte ownPing = 0;
        int PingRepetitions = 0;

        /// <summary>
        /// 
        /// </summary>
        unsafe void InitReader()
        {
            string prefix = sm.instanceType == eInstanceType.Slave ? "1" : "2";
            
            if (Reader_mmf == null)
            {
                //Reader_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity, MemoryMappedFileAccess.ReadWrite);

                var security = new MemoryMappedFileSecurity();
                security.AddAccessRule(new System.Security.AccessControl.AccessRule<MemoryMappedFileRights>(
                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
                    MemoryMappedFileRights.FullControl,
                    System.Security.AccessControl.AccessControlType.Allow));
                //Reader_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(@"Global\MapName1", sm.bufferCapacity, 
                Reader_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(sm.uniqueHandlerName + prefix + "_SharmNet_MMF", sm.bufferCapacity,
                    MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.DelayAllocatePages, security, System.IO.HandleInheritability.Inheritable);


                Reader_accessor = Reader_mmf.CreateViewAccessor(0, sm.bufferCapacity);
                Reader_accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref Reader_accessor_ptr);
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

                var spinWait = new SspinWait();

                try
                {                   
                    while (true)
                    {
                        //Ping handler
                        if (DateTime.UtcNow.Subtract(pingLastRead).TotalSeconds > 2)
                        {
                            //Reading partner state ping
                            pingLastRead = DateTime.UtcNow;
                            CurrentPingValue = ReadBytes(Reader_accessor_ptr, 1, 1)[0];
                            if (CurrentPingValue == 1 || (LastPingValue == 0 && CurrentPingValue > 0))
                            {
                                //Console.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.ms") + " Partner Connected " + CurrentPingValue);
                                this.sm.SharmIPC.SetPartnerState(SharmIpc.ePartnerState.Connected);
                                PingRepetitions = 0;
                            }
                            else if (CurrentPingValue == LastPingValue)
                            {
                                PingRepetitions++;
                            }
                            else
                                PingRepetitions = 0;

                            if (PingRepetitions == 2 && LastPingValue != 0)
                            {
                                //(Console.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.ms") + " Partner Disconnected ");
                                this.sm.SharmIPC.SetPartnerState(SharmIpc.ePartnerState.Disconnected);
                                LastPingValue = 0;
                                CurrentPingValue = 0;
                                PingRepetitions = 0;
                                this.WriteBytes(Reader_accessor_ptr, 1, new byte[] { 0 });
                            }

                            LastPingValue = CurrentPingValue;
                            //Console.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.ms") + " " + LastPingValue);



                            //Writing self-ping
                            if (ownPing == 255)
                                ownPing = 2;
                            else
                                ownPing++;

                            this.WriteBytes(Writer_accessor_ptr, 1, new byte[] { ownPing });
                        }




                        //Reading semaphore
                        if (ReadBytes(Reader_accessor_ptr, 0, 1)[0] != 1)
                        {
                            //Nothing to read
                            spinWait.Spin();
                        }
                        else
                        {
                            spinWait.Clear();
                            //Reading header
                            hdr = ReadBytes(Reader_accessor_ptr, technicalLen, protocolLen);
                            msgType = (eMsgType)hdr[0];

                            //Parsing header
                            switch (msgType)
                            {
                                case eMsgType.ErrorInRpc:

                                    iPayLoadLen = BitConverter.ToInt32(hdr, 9); //+4
                                    iResponseMsgId = BitConverter.ToUInt64(hdr, 17); //+8

                                    this.sm.SharmIPC.InternalDataArrived(msgType, iResponseMsgId, null);
                                    break;

                                case eMsgType.RpcResponse:
                                case eMsgType.RpcRequest:
                                case eMsgType.Request:

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
                                                this.sm.SharmIPC.InternalDataArrived(eMsgType.ErrorInRpc, iResponseMsgId, null);
                                                break;
                                        }
                                        break;
                                    }

                                    if (iTotChunk == iCurChunk)
                                    {
                                        if (chunksCollected == null)
                                            this.sm.SharmIPC.InternalDataArrived(msgType, (msgType == eMsgType.RpcResponse) ? iResponseMsgId : iMsgId, iPayLoadLen == 0 ? ((zeroByte) ? new byte[0] : null) : ReadBytes(Reader_accessor_ptr, protocolLen + technicalLen, iPayLoadLen));
                                        else
                                        {
                                            ret = new byte[iPayLoadLen + chunksCollected.Length];
                                            Buffer.BlockCopy(chunksCollected, 0, ret, 0, chunksCollected.Length);
                                            Buffer.BlockCopy(ReadBytes(Reader_accessor_ptr, protocolLen + technicalLen, iPayLoadLen), 0, ret, chunksCollected.Length, iPayLoadLen);
                                            this.sm.SharmIPC.InternalDataArrived(msgType, (msgType == eMsgType.RpcResponse) ? iResponseMsgId : iMsgId, ret);
                                        }
                                        chunksCollected = null;
                                        currentChunk = 0;
                                    }
                                    else
                                    {
                                        if (chunksCollected == null)
                                        {
                                            chunksCollected = ReadBytes(Reader_accessor_ptr, protocolLen + technicalLen, iPayLoadLen);
                                        }
                                        else
                                        {

                                            byte[] tmp = new byte[chunksCollected.Length + iPayLoadLen];
                                            Buffer.BlockCopy(chunksCollected, 0, tmp, 0, chunksCollected.Length);
                                            Buffer.BlockCopy(ReadBytes(Reader_accessor_ptr, protocolLen + technicalLen, iPayLoadLen), 0, tmp, chunksCollected.Length, iPayLoadLen);
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
                                    throw new Exception("tiesky.com.SharmIpc: Reading protocol contains errors");
                                    //break;
                            }

                            //Setting signal back for writer
                            WriteBytes(Reader_accessor_ptr, 0, new byte[] { 0 });                            

                        }//eo read else
                        
                    }//eo while
                }
                catch(System.Exception ex)
                {
					/*					
					System.ObjectDisposedException: Das SafeHandle wurde geschlossen. bei System.Runtime.InteropServices.SafeHandle.DangerousAddRef(Boolean& success) 
                    bei System.StubHelpers.StubHelpers.SafeHandleAddRef(SafeHandle pHandle, Boolean& success) 
                    bei Microsoft.Win32.Win32Native.SetEvent(SafeWaitHandle handle) bei System.Threading.EventWaitHandle.Set() bei tiesky.com.SharmIpcInternals.ReaderWriterHandler.b__28_0()
					*/
                    this.sm.SharmIPC.LogException("SharmIps.ReaderWriterHandler.InitReader LE", ex);
                }               


            });
        }

        
                
    }
}

