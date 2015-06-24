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

namespace MemoryMappedFile
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor accessor = null;
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor techAccessor = null;

        System.IO.MemoryMappedFiles.MemoryMappedFile mmf = null;

        EventWaitHandle ewh_ReadyToRead = null;
        EventWaitHandle ewh_ReadyToWrite = null;

        EventWaitHandle ewh_Handshake = null;
        Mutex mt = null;

        NetMQContext context = null;
        NetMQ.Sockets.RequestSocket client = null;

        tiesky.com.SharmIpc.Commander sm = null;
        //tiesky.com.SharmNet.SharedMemory sm = new tiesky.com.SharmNet.SharedMemory("Mitch");

        //int cntarrived = 0;
        //int len = 0;
        //void DataArrived(ulong msgId, byte[] bt)
        //{
        //    cntarrived++;
        //    len += bt.Length;
        //}

        Tuple<bool,byte[]> RemoteCall(byte[] data)
        {
            Console.WriteLine("Received: {0} bytes", (data == null ? 0 : data.Length));
            return new Tuple<bool, byte[]>(true, new byte[] { 5, 6, 7 });            
        }

        private void button1_Click(object sender, EventArgs e)
        {

           
            if(sm == null)
                sm = new tiesky.com.SharmIpc.Commander("Mitch", this.RemoteCall);
                        

            return;

            //sm.InitMaster();

            //return;


            long capacity = 10;
            //Reader Pattern
            if (mmf == null)
            {
                
                mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("testaccess", capacity, MemoryMappedFileAccess.ReadWrite);
                //accessor = mmf.CreateViewAccessor(0, capacity - 1);
                //techAccessor = mmf.CreateViewAccessor(capacity - 1, 1);

                accessor = mmf.CreateViewAccessor();
                //techAccessor = mmf.CreateViewAccessor(5, 3);

                //accessor.Write(capacity-1, 1);                

                //for (int i = 0; i < capacity; i++)
                //{
                //    Console.WriteLine(i + "=" + accessor.ReadByte(i));
                //}
                                
            }

            if (ewh_ReadyToRead == null)
            {
                
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
                
                

                byte[] bt = new byte[100 * 1024];
                byte b;

                while (true)
                {
                    ewh_ReadyToRead.WaitOne();
                    ewh_ReadyToRead.Reset();
                    //Reading data from MMF
                    //accessor.ReadArray<byte>(0, bt, 0, 102400);

                    b = accessor.ReadByte(3);


                    //Setting signal 
                    ewh_ReadyToWrite.Set();
                }
            });


            return;















            //Task.Run(() =>
            //{
            //    using (var context = NetMQContext.Create())
            //    using (var client = context.CreateRequestSocket())
            //    {
            //        client.Connect("tcp://localhost:5555");

            //        while (true)
            //        {                    
            //            client.Send("H");
            //            var message = client.ReceiveString();                        
            //        }
            //    }
            //});

            //return;

            //if (mmf == null)
            //{
            //    mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("testaccess", 1000 * 1024, MemoryMappedFileAccess.ReadWrite);
            //    accessor = mmf.CreateViewAccessor(0, 1000 * 1024);
            //}

            //Task.Run(() =>
            //{
            //    while (true)
            //    {
                    
            //    }
            //});

            //return;



            ////Reading pattern with MMF and Event

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
            //    byte mb;
            //    SpinWait spnw = new SpinWait();

            //    while (true)
            //    {
            //        //Wait until accessor.ReadByte(0) == 0
            //        //read from (3,17)
            //        //set (0) to 1   

            //        ewh_One.WaitOne();
            //        ewh_One.Reset();

            //        //while (true)
            //        //{
            //        //    if (accessor.ReadByte(0) == 0)
            //        //        break;
            //        //    //spnw.SpinOnce();
            //        //    //System.Threading.Thread.Sleep(1);
            //        //}

            //        mb = accessor.ReadByte(3);
            //        accessor.Write(0, 1);

            //    }
            //});

            //return;



            ////Reading pattern with MMF only

            //if (mmf == null)
            //{
            //    mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("testaccess", 100 * 1024, MemoryMappedFileAccess.ReadWrite);
            //    accessor = mmf.CreateViewAccessor(0, 100 * 1024);
            //}

            //Task.Run(() =>
            //{
            //    byte mb;
            //    SpinWait spnw = new SpinWait();

            //    while (true)
            //    {
            //        //Wait until accessor.ReadByte(0) == 0
            //        //read from (3,17)
            //        //set (0) to 1   

            //        while (true)
            //        {
            //            if (accessor.ReadByte(0) == 0)
            //                break;
            //            //spnw.SpinOnce();
            //            //System.Threading.Thread.Sleep(1);
            //        }

            //        mb = accessor.ReadByte(3);
            //        accessor.Write(0, 1);

            //    }
            //});

            //return;



            ////Reader Pattern (not working test), working is underneath
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
            //    //byte[] bt = new byte[100 * 1024];
            //    byte b;

            //    //ewh_One.Set();  //Telling to sender to send
            //    //ewh_One.Reset();

            //    while (true)
            //    {
            //        ewh_One.WaitOne();                    
            //        b = accessor.ReadByte(3);
            //        ewh_One.Set();
            //        ewh_One.Reset();
            //    }
            //});


            //return;




           

            //Reader Pattern
            if (mmf == null)
            {
                mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("testaccess", 100 * 1024, MemoryMappedFileAccess.ReadWrite);
                accessor = mmf.CreateViewAccessor(0, 100 * 1024);
            }

            if (ewh_ReadyToRead == null)
            {
                ewh_ReadyToRead = new EventWaitHandle(false, EventResetMode.ManualReset, "myPID_ReadyToRead");
                ewh_ReadyToWrite = new EventWaitHandle(true, EventResetMode.ManualReset, "myPID_ReadyToWrite");
            }

            
            Task.Run(() =>
            {
                byte[] bt=new byte[100 * 1024];
                byte b;

                while (true)
                {
                    ewh_ReadyToRead.WaitOne();
                    ewh_ReadyToRead.Reset();
                    //Reading data from MMF
                    //accessor.ReadArray<byte>(0, bt, 0, 102400);
                    b = accessor.ReadByte(3);


                    //Setting signal 
                    ewh_ReadyToWrite.Set();
                }
            });


            return;

            //if (mmf == null)
            //{
            //    mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("testaccess", 1000 * 1024, MemoryMappedFileAccess.ReadWrite);
            //    accessor = mmf.CreateViewAccessor(0, 1000 * 1024);
            //}

           
          

        }

        /// <summary>
        /// Write
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button2_Click(object sender, EventArgs e)
        {
            var res1 = sm.RpcCall(new byte[99]);
            return;

            //using (var context = NetMQContext.Create())
            //using (var client = context.CreateRequestSocket())
            //{
            //    client.Connect("tcp://localhost:5555");

            //    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            //    sw.Start();
            //    for (int i = 1; i < 10000; i++)
            //    {
            //        //sm.CallAndWaitAnswer("test", new byte[512], 10 * 1000);
            //        //client.Send(new byte[512]);
            //        client.Send(new byte[100000]);
            //        var message = client.Receive();
            //    }
            //    sw.Stop();
            //    Console.WriteLine(sw.ElapsedMilliseconds);

            //}


            //return;


            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            for (int i = 1; i < 10000; i++)
            {
               // var res = sm.RpcCall(new byte[512], (par) => { });
                //var res = sm.RpcCall(null);
                var res = sm.RpcCall(new byte[512]);
                //sm.Call(new byte[512]);
                //sm.CallAndWaitAnswer("test", new byte[100000], 10 * 1000);
            }
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);


            return;

            byte b = 23;
            accessor.Write(5, b);
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


        private void button3_Click(object sender, EventArgs e)
        {
            //byte rb = 0;

            //System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            //sw.Start();
            //for (int i = 1; i < 10000000; i++)
            //{
            //    rb = accessor.ReadByte(5);               
            //}
            //sw.Stop();
            
            //rb = accessor.ReadByte(5);
            //Console.WriteLine("___" + rb);

            //Console.WriteLine(sw.ElapsedMilliseconds);


            CoOrds coo;


            //accessor.Read<CoOrds>(17, out coo);
            //Console.WriteLine("___" + coo.x + "_" + coo.y);


            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            for (int i = 1; i < 10000000; i++)
            {
                accessor.Read<CoOrds>(17, out coo);
            }
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);

            accessor.Read<CoOrds>(17, out coo);
            Console.WriteLine("___" + coo.x + "_" + coo.y);
        }
                
    }
}
