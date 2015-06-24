using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.IO.MemoryMappedFiles;
using System.Threading;
using NetMQ;

namespace mmf2client
{
    public partial class Form1 : Form
    {
        //https://gist.github.com/AngraMainyu/1374435

        public Form1()
        {
            InitializeComponent();
        }
        
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor accessor = null;
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor techAccessor = null;
        System.IO.MemoryMappedFiles.MemoryMappedFile mmf = null;
        System.IO.UnmanagedMemoryStream mmfStream = null;

        byte[] mmfBuffer = new byte[50000];

        EventWaitHandle ewh_ReadyToRead = null;
        EventWaitHandle ewh_ReadyToWrite = null;
        EventWaitHandle ewh_Handshake = null;

        Mutex mt = null;

        tiesky.com.SharmIpc.Commander sm = null;
        //tiesky.com.SharmNet.SharedMemory sm = new tiesky.com.SharmNet.SharedMemory("Mitch");

        //int cntarrived = 0;
        //int len = 0;
        //void DataArrived(ulong msgId, byte[] bt)
        //{
        //    cntarrived++;
        //    len += bt.Length;
        //}

        Tuple<bool, byte[]> RemoteCall(byte[] data)
        {
            //Console.WriteLine("Received: {0} bytes", (data == null ? 0 : data.Length));
            return new Tuple<bool, byte[]>(true, new byte[] { 9, 4, 12, 17 });
        }



        private void button1_Click(object sender, EventArgs e)
        {

            if (sm == null)
                sm = new tiesky.com.SharmIpc.Commander("Mitch", this.RemoteCall);

            return;
            //Writer Pattern With SystemSignals
            long capacity = 10;
            if (mmf == null)
            {               
                mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("testaccess", capacity, MemoryMappedFileAccess.ReadWrite);
                accessor = mmf.CreateViewAccessor();
            }

            if (ewh_ReadyToRead == null)
            {
                bool master = false;
                mt = new Mutex(true, "xxx");

                try
                {
                    if (mt.WaitOne(500))
                    {
                        //Master
                        Console.WriteLine("Master");
                    }
                    else
                    {
                        //Slave
                        Console.WriteLine("Slave");
                    }     
                   
                }
                catch (System.Threading.AbandonedMutexException)
                {
                    //Master
                    Console.WriteLine("Master");
                }

                //mt.ReleaseMutex();
                //mt.Close();
                //mt.Dispose();
                //mt = null;
                
                
                ewh_ReadyToRead = new EventWaitHandle(false, EventResetMode.ManualReset, "myPID_ReadyToRead");
                ewh_ReadyToWrite = new EventWaitHandle(true, EventResetMode.ManualReset, "myPID_ReadyToWrite");
            }

         

            Task.Run(() =>
            {
                //while (true)
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                for (int i = 0; i < 100000; i++)
                {

                    if (ewh_ReadyToWrite.WaitOne(2 * 1000))
                    {
                        ewh_ReadyToWrite.Reset();
                        //if still program must work then go on
                        //Writing into MMF
                        accessor.Write(3, i);

                        //Setting signal ready to read
                        ewh_ReadyToRead.Set();
                    }
                    else
                        Console.WriteLine("Timeout");
                }
                sw.Stop();
                Console.WriteLine(sw.ElapsedMilliseconds);

            });


            return;




            
            //context = NetMQContext.Create();
            //Task.Run(() =>
            //{
            //    //using (var context = NetMQContext.Create())                
            //    using (var server = context.CreateResponseSocket())
            //    {
            //        server.Bind("tcp://*:5555");

            //        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            //        sw.Start();
            //        for (int i = 0; i < 100000; i++)
            //        {
            //            var message = server.ReceiveString();
            //            server.Send("W");
            //        }
            //        sw.Stop();
            //        Console.WriteLine(sw.ElapsedMilliseconds);
            //    }
            //});



            //return;

            //Task.Run(() =>
            //{
            //    //while (true)
            //    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            //    sw.Start();
            //    //for (int i = 0; i < 1000000; i++)
            //    for (; ; )
            //    {
            //        Thread.SpinWait(1000);
            //        //Thread.Sleep(1);
            //        //ewh_ReadyToWrite.WaitOne();
            //        //ewh_ReadyToWrite.Reset();
            //        ////if still program must work then go on
            //        ////Writing into MMF
            //        ////Setting signal ready to read
            //        //ewh_ReadyToRead.Set();
            //    }
            //    sw.Stop();
            //    Console.WriteLine(sw.ElapsedMilliseconds);

            //});

            //return;


            ////WriterPattern with MMF  and Event

            //if (mmf == null)
            //{
            //    mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("testaccess", 100 * 1024, MemoryMappedFileAccess.ReadWrite);
            //    accessor = mmf.CreateViewAccessor(0, 100 * 1024);
            //}
            //if (ewh_One == null)
            //{
            //    ewh_One = new EventWaitHandle(false, EventResetMode.ManualReset, "ewh_One");
            //}

            //Task.Run(() =>
            //{


            //    accessor.Write(0, 1);


            //    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            //    sw.Start();
            //    for (int i = 0; i < 100000; i++)
            //    {
            //        while (true)
            //        {
            //            //Intensively waiting
            //            if (accessor.ReadByte(0) == 1)
            //            {
            //                accessor.Write(0, 0);   //we must write together
            //                break;
            //            }
            //        }

            //        //We can write
            //        //accessor.Write(3, 15);

            //        //Letting to read
            //        ewh_One.Set();

            //    }
            //    sw.Stop();
            //    Console.WriteLine(sw.ElapsedMilliseconds);


            //});


            //return;





            ////WriterPattern with MMF only

            //if (mmf == null)
            //{
            //    mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("testaccess", 100 * 1024, MemoryMappedFileAccess.ReadWrite);
            //    accessor = mmf.CreateViewAccessor(0, 100 * 1024);
            //}

            //Task.Run(() =>
            //{


            //    accessor.Write(0, 1);

            //    SpinWait spnw = new SpinWait();

            //    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            //    sw.Start();
            //    for (int i = 0; i < 100000; i++)
            //    {
            //        while (true)
            //        {
            //            if (accessor.ReadByte(0) == 1)
            //                break;

            //            //spnw.SpinOnce();
            //            //System.Threading.Thread.Sleep(1);
            //        }

            //        //We can write
            //        //accessor.Write(3, 15);

            //        // byte fdfd = accessor.ReadByte(3);
            //        accessor.Write(0, 0);
            //        //byte fdfd1 = accessor.ReadByte(3);
            //    }
            //    sw.Stop();
            //    Console.WriteLine(sw.ElapsedMilliseconds);


            //});


            //return;


            //Write pattern, not working test, working is underneath

            //if (mmf == null)
            //{
            //    mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("testaccess", 100 * 1024, MemoryMappedFileAccess.ReadWrite);
            //    accessor = mmf.CreateViewAccessor(0, 100 * 1024);
            //}

            //if (ewh_One == null)
            //{
            //    ewh_One = new EventWaitHandle(false, EventResetMode.ManualReset, "ewh_One");
            //}

            //Task.Run(() =>
            //{
            //    //while (true)
            //    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            //    sw.Start();
            //    for (int i = 0; i < 100000; i++)
            //    {
            //        ewh_One.WaitOne(2 * 1000);
            //        accessor.Write(3, i);
            //        ewh_One.Set();
            //        ewh_One.Reset();

            //        //if (ewh_One.WaitOne(2 * 1000))
            //        //{   
            //        //    accessor.Write(3, i);
            //        //    ewh_One.Set();
            //        //    ewh_One.Reset();
            //        //}
            //        //else
            //        //    Console.WriteLine("Timeout");
            //    }
            //    sw.Stop();
            //    Console.WriteLine(sw.ElapsedMilliseconds);

            //});


            //return;





            //Writer Pattern With SystemSignals
            if (ewh_ReadyToRead == null)
            {
                ewh_ReadyToRead = new EventWaitHandle(false, EventResetMode.ManualReset, "myPID_ReadyToRead");
                ewh_ReadyToWrite = new EventWaitHandle(true, EventResetMode.ManualReset, "myPID_ReadyToWrite");
            }

            if (mmf == null)
            {
                mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("testaccess", 100 * 1024, MemoryMappedFileAccess.ReadWrite);
                accessor = mmf.CreateViewAccessor(0, 100 * 1024);
            }

            Task.Run(() =>
            {
                //while (true)
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                    for (int i = 0; i < 100000;i++)                    
                    {

                        if (ewh_ReadyToWrite.WaitOne(2 * 1000))
                        {
                            ewh_ReadyToWrite.Reset();
                            //if still program must work then go on
                            //Writing into MMF
                            accessor.Write(3, i);
                            //Setting signal ready to read
                            ewh_ReadyToRead.Set();
                        }
                        else
                            Console.WriteLine("Timeout");
                    }
                    sw.Stop();
                    Console.WriteLine(sw.ElapsedMilliseconds);

            });


            return;

            if (mmf == null)
            {
                mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("testaccess", 1000 * 1024, MemoryMappedFileAccess.ReadWrite);
                accessor = mmf.CreateViewAccessor(0, 1000 * 1024);
                                

                //mmfStream = mmf.CreateViewStream();

                //IAsyncResult ar = mmfStream.BeginRead(mmfBuffer, 0, mmfBuffer.Length, cb, null);
                //ar.AsyncWaitHandle.WaitOne();

                //mmfStream.BeginRead(mmfBuffer, 0, mmfBuffer.Length, ar =>                 
                //{ 
                //    //ar.AsyncState is nullable here
                //    int q = mmfStream.EndRead(ar);

                //}, 
                //null);


                //EventWaitHandle ewh = new EventWaitHandle(false, EventResetMode.ManualReset, "myPIDsyncroEvent");
                //ewh.WaitOne();

                //while (true)
                //{
                //    byte br = accessor.ReadByte(5);
                //    //Console.WriteLine(br);
                //}

                //System.Threading.Thread.CurrentThread.Abort();

                //Task.Run(() =>
                //{
                //    while (true)
                //    {
                //        byte br = accessor.ReadByte(5);
                //        //Console.WriteLine(br);
                //        System.Threading.Thread.Sleep(1);
                //    }
                //});
            }
        }

        void cb(IAsyncResult ar)
        {
            int q = mmfStream.EndRead(ar);

            if (mmfBuffer[0] > 0)
            {

            }

            mmfStream.BeginRead(mmfBuffer, 0, mmfBuffer.Length, cb, null);
            ar.AsyncWaitHandle.WaitOne();
        }

        public struct CoOrds
        {
            public int x, y;           

            public CoOrds(int p1, int p2)
            {
                x = p1;
                y = p2;
            }
        }

        /// <summary>
        /// Write
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button2_Click(object sender, EventArgs e)
        {

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            for (int i = 0; i < 10000; i++)
            {
                // var res = sm.RpcCall(new byte[512], (par) => { });
                //var res = sm.RpcCall(null);
                //var res = sm.RpcCall(new byte[512]);
                sm.Call(new byte[512]);
                //sm.CallAndWaitAnswer("test", new byte[100000], 10 * 1000);
            }
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);




        //    //var res = sm.RpcCall(null);
        //    var res = sm.RpcCall(new byte[17]);
        //    //var res = sm.Call(new byte[324]);
        //    return;

        //    using (var context = NetMQContext.Create())
        //    using (var server = context.CreateResponseSocket())
        //    {
        //        server.Bind("tcp://*:5555");

        //        while (true)
        //        {
        //            //client.Send("H");                    
        //            server.Receive();
        //            server.Send(new byte[8]);
        //        }

        //    }

          
        //    return;
        //    //sm.CallAndWaitAnswer("tree",new byte[12000],10*1000);
        //    //sm.SendMessage(new byte[] { 17 });
        //    //sm.TestSendMessage();
        //    return;

        //////////    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        //////////    sw.Start();
        //////////    for (int i = 1; i < 1000000; i++)
        //////////    {
        //////////        sm.SendMessage(new byte[] { (byte)i });
        //////////    }
        //////////    sw.Stop();
        //////////    Console.WriteLine(sw.ElapsedMilliseconds);
        //////////    return;

        //    //byte b = 47;
        //    //accessor.Write(5, b);

        //    CoOrds coo = new CoOrds(12, 45);
        //   // accessor.Write<CoOrds>(17, ref coo);

        //   // accessor.WriteArray<byte>(50, new byte[] { 1, 2, 3, 4, 5, 6, 8 }, 0, 7);

        //    byte[] bt=new byte[]{34,54,23,123,4};

        //    mmfStream.Write(bt, 0, bt.Count());


        }

        /// <summary>
        /// Init
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button3_Click(object sender, EventArgs e)
        {
            //CoOrds coo;
            //accessor.Read<CoOrds>(17, out coo);
            //Console.WriteLine("client2___" + coo.x);

            //var rb = accessor.ReadByte(5);
            //Console.WriteLine("client2___" + rb);

            byte[] bt=new byte[50];
            var rb = accessor.ReadArray<byte>(50, bt, 0, 50);

            //var stream = mmf.CreateViewStream();
            //stream.BeginRead(
        }


    }
}
