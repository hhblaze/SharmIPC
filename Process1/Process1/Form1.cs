﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.Runtime.InteropServices;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace MemoryMappedFile
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        tiesky.com.SharmIpc sm = null;
        int z = 0;
        System.Diagnostics.Stopwatch swNonBlockingCall = new System.Diagnostics.Stopwatch();


        void AsyncRemoteCallHandler(ulong msgId, byte[] data)
        {
            Task.Run(() =>
                {
                    sm.AsyncAnswerOnRemoteCall(msgId, new Tuple<bool, byte[]>(true, new byte[] { 5 }));
                });
        }
        /// <summary>
        /// Test of non-blocking requests (are called when pressed Write from Process2)
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        Tuple<bool,byte[]> RemoteCall(byte[] data)
        {
            return new Tuple<bool, byte[]>(true, new byte[10]);   


            if (z == 0)
                swNonBlockingCall.Start();
            z++;

            if (z == 10000)
            {
                swNonBlockingCall.Stop();                
                
                Console.WriteLine("Speed: {0} MB/s, finished in ms {1}",
                    Math.Round((decimal)data.Length * (decimal)z * (decimal)1000 /
                    ((decimal)swNonBlockingCall.ElapsedMilliseconds * 1000000m)
                    ,2), swNonBlockingCall.ElapsedMilliseconds
                    );
                z = 0;
                swNonBlockingCall.Reset();
            }
            //Console.WriteLine("Received: {0} bytes", (data == null ? 0 : data.Length));
            return new Tuple<bool, byte[]>(true, new byte[] { 5, 6, 7 });            
        }


        Tuple<bool, byte[]> RemoteCallStandard(byte[] data)
        {
            return new Tuple<bool, byte[]>(true, new byte[] { 5, 6, 7 });
        }

        private void button1_Click(object sender, EventArgs e)
        {
            //Initializing SharmIpc with then name     

            if (sm != null)
            {
                sm.Dispose();
                sm = null;
            }

            if (sm == null)
            {
                sm = new tiesky.com.SharmIpc("MyNewSharmIpc", true, this.RemoteCall);
                //or to get ability to answer to remote partner in async way
                //sm = new tiesky.com.SharmIpc("MyNewSharmIpc", this.AsyncRemoteCallHandler);                
            }
                        

        }

        /// <summary>
        ///Test of RemoteRequest with answer (Process 2 must receive request in its RemoteCall and return back Response).
        ///Here we calcualte the speed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button2_Click(object sender, EventArgs e)
        {
            //var dfr41 = sm.RemoteRequest(new byte[1700]);
            //return;
            //byte[] here = new byte[2500];
            

            //Action<int> a1 = (id) =>
            //{

            //    //Console.WriteLine("YAHOO " + id);
            //    //DBreeze.Diagnostic.SpeedStatistic.StartCounter("a"+id);
            //    int tt = 0;
            //    for (int i = 0; i < 1000; i++)
            //    {
            //       var xr = sm.RemoteRequest(new byte[1700]);
            //        tt += xr.Item2.Length;
            //    }
            //    Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ms") + "> DONE " + tt);
            //    //DBreeze.Diagnostic.SpeedStatistic.PrintOut("a" + id);
            //    //Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ms") + "> DONE " + tt);
            //};

            //Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ms") + "> START");
            //for (int j = 0; j < 40; j++)
            //{
            //    //((Action)(() => { a1(); })).DoAsync();
            //    System.Threading.Tasks.Task.Run(() =>
            //    {
            //        // Console.WriteLine("Running " + j.ToString());
            //        a1(j);
            //    });
            //    //new System.Threading.Thread(() => { a1(); }).Start();
            //}

            //return;

            //var res222 = sm.RemoteRequest(new byte[1700]);
            //Console.WriteLine("Received " + res222.Item2.Length + " bytes");
            //return;

            //var res1 = sm.RemoteRequest(new byte[546],
            //  (par) =>
            //  {
            //      Console.WriteLine(par.Item1);
            //  }
            //  , 10000);

            //var res1 = sm.RemoteRequest(new byte[546],null, 10000);
            //return;

            byte[] data = new byte[1];
            //byte[] data = new byte[512];
            //byte[] data = new byte[10000];
            int iter = 100000;
            //var spinWait = new SpinWait();

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            for (int i = 0; i < iter; i++)
            {
                // var res = sm.RemoteRequest(new byte[512], (par) => { });
                //var res = sm.RemoteRequest(null);
                //var res = sm.RemoteRequest(data, (par) => { },30000);
                //sm.RemoteRequestWithoutResponse(new byte[512]);
                //sm.RemoteRequestWithoutResponse(new byte[1]);

              // var res = sm.RemoteRequest(data);
                sm.RemoteRequestWithoutResponse(data);

                //spinWait.SpinOnce();
                //Thread.SpinWait(100);
            }
            sw.Stop();
            Console.WriteLine("Speed: {0} MB/s, Finished in ms {1}",
                    Math.Round((decimal)data.Length * (decimal)iter * (decimal)1000 /
                    ((decimal)sw.ElapsedMilliseconds * 1000000m)
                    , 2), sw.ElapsedMilliseconds
                    );
           // Console.WriteLine(sw.ElapsedMilliseconds);

        }


        System.IO.MemoryMappedFiles.MemoryMappedFile Reader_mmf = null;
        System.IO.MemoryMappedFiles.MemoryMappedFile Writer_mmf = null;
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor Writer_accessor = null;
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor Reader_accessor = null;
        long bufferCapacity = 50000;

        private void button3_Click(object sender, EventArgs e)
        {
            Writer_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("_SharmNet_MMF", bufferCapacity, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite);
            Writer_accessor = Writer_mmf.CreateViewAccessor(0, bufferCapacity);

            Reader_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("_SharmNet_MMF", bufferCapacity, MemoryMappedFileAccess.ReadWrite);
            Reader_accessor = Reader_mmf.CreateViewAccessor(0, bufferCapacity);

            //WriteBytes(Writer_accessor, 0, new byte[] { 3 });


            //Task.Run(() =>
            //{
            //    byte[] res = null;
            //    var spinWait = new SpinWait();

            //    while (true)
            //    {
            //        res = ReadBytes(Writer_accessor, 0, 1);
            //        Thread.SpinWait(100000);
            //        //spinWait.SpinOnce();
            //    }


            //});

            //while (true)
            //{
            //    //http://www.albahari.com/threading/part5.aspx#_SpinLock_and_SpinWait
            //    //res = ReadBytes(Writer_accessor, 0, 1);
            //    Thread.SpinWait(1000);
            //    //spinWait.SpinOnce();
            //}


            wptr = InitPtr(Writer_accessor);
            rptr = InitPtr(Reader_accessor);

            WriteBytes1(wptr, 0, new byte[] { 1, 2, 3 });
            byte[] t = ReadBytes1(rptr, 1, 1);
            WriteBytes1(wptr, 0, new byte[] { 5, 6, 7 });
            t = ReadBytes1(rptr, 1, 1);
            return;


            byte[] data = new byte[100];
            int iter = 100000;
            var spinWait = new SpinWait();

           

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            for (int i = 0; i < iter; i++)
            {
                //spinWait.SpinOnce();
                //Thread.SpinWait(1000);
                //ReadBytes(Writer_accessor, 0, 1);
                WriteBytes(Writer_accessor, 0, new byte[10000]);
            }
            sw.Stop();
            Console.WriteLine("Speed: {0} MB/s, Finished in ms {1}",
                    Math.Round((decimal)data.Length * (decimal)iter * (decimal)1000 /
                    ((decimal)sw.ElapsedMilliseconds * 1000000m)
                    , 2), sw.ElapsedMilliseconds
                    );

        }

        IntPtr wptr;
        IntPtr rptr;

        unsafe IntPtr InitPtr(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor accessor)
        {
            byte* ptr = (byte*)0;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            return new IntPtr(ptr);
        }

        unsafe void WriteBytes1(IntPtr ptr, int offset, byte[] data)
        {

            //byte* ptr = (byte*)0;
            //accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            Marshal.Copy(data, 0, IntPtr.Add(ptr, offset), data.Length);
           // accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }

        unsafe byte[] ReadBytes1(IntPtr ptr, int offset, int num)
        {
            byte[] arr = new byte[num];           
            Marshal.Copy(IntPtr.Add(ptr, offset), arr, 0, num);            
            return arr;
        }



        unsafe void WriteBytes(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor accessor,int offset, byte[] data)
        {
            byte* ptr = (byte*)0;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            Marshal.Copy(data, 0, IntPtr.Add(new IntPtr(ptr), offset), data.Length);
            accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }

        unsafe byte[] ReadBytes(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor accessor, int offset, int num)
        {
            byte[] arr = new byte[num];
            byte* ptr = (byte*)0;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            Marshal.Copy(IntPtr.Add(new IntPtr(ptr), offset), arr, 0, num);
            accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            return arr;
        }
    }
}
