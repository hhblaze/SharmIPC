using System;
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

namespace mmf2client
{
    public partial class Form1 : Form
    {
 
        public Form1()
        {
            InitializeComponent();
        }

        tiesky.com.SharmIpc sm = null;
   

        Tuple<bool, byte[]> RemoteCall(byte[] data)
        {
            //Console.WriteLine("Received: {0} bytes", (data == null ? 0 : data.Length));
            //return new Tuple<bool, byte[]>(true, new byte[] { 9, 4, 12, 17 });
            //return new Tuple<bool, byte[]>(true, new byte[] { 9 });
            //Thread.Sleep(10 * 1000);
            return new Tuple<bool, byte[]>(true, new byte[1]);
        }

        void AsyncRemoteCallHandler(ulong msgId, byte[] data)
        {
            Task.Run(() =>
            {
                //sm.AsyncAnswerOnRemoteCall(msgId, new Tuple<bool, byte[]>(true, new byte[] { 9, 4, 12, 17, 25 }));
                sm.AsyncAnswerOnRemoteCall(msgId, new Tuple<bool, byte[]>(true, new byte[] { 9 }));
            }
            );
        }


        private void button1_Click(object sender, EventArgs e)
        {

            if (sm == null)
                //sm = new tiesky.com.SharmIpc("MyNewSharmIpc", this.AsyncRemoteCallHandler);
              sm = new tiesky.com.SharmIpc("MyNewSharmIpc", false, this.RemoteCall);

        }


        /// <summary>
        /// Testing Requests without response
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button2_Click(object sender, EventArgs e)
        {
            //sm.RemoteRequestWithoutResponse(null);
            var res = sm.RemoteRequest(new byte[] { 9 });
            return;

            //var res = sm.RemoteRequest(new byte[546],
            //    (par) =>
            //    {
            //        Console.WriteLine(par.Item1);
            //    }
            //    ,10000);
            //return;

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            for (int i = 0; i < 10000; i++)
            {
                //sm.RemoteRequestWithoutResponse(new byte[1]);
                sm.RemoteRequestWithoutResponse(new byte[512]);
                //sm.RemoteRequestWithoutResponse(new byte[10000]);
            }
            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);


        }


        System.IO.MemoryMappedFiles.MemoryMappedFile Reader_mmf = null;
        System.IO.MemoryMappedFiles.MemoryMappedFile Writer_mmf = null;
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor Writer_accessor = null;
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor Reader_accessor = null;
        long bufferCapacity = 50000;

        private void button3_Click(object sender, EventArgs e)
        {
            //Writer_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("_SharmNet_MMF", bufferCapacity, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite);
            //Writer_accessor = Writer_mmf.CreateViewAccessor(0, bufferCapacity);

            try
            {
                Reader_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateNew("_SharmNet_MMF", bufferCapacity, MemoryMappedFileAccess.ReadWrite);
                Reader_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateNew("_SharmNet_MMF", bufferCapacity, MemoryMappedFileAccess.ReadWrite);
            }
            catch (Exception ex)
            {
                
            }

            Reader_mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen("_SharmNet_MMF", bufferCapacity, MemoryMappedFileAccess.ReadWrite);
            Reader_accessor = Reader_mmf.CreateViewAccessor(0, bufferCapacity);

            //WriteBytes(0, new byte[] { 3 });


            Task.Run(() => {
                byte[] res = null;
                var spinWait = new SpinWait();

                while (true)
                {
                    res = ReadBytes(0, 1);
                    if(res != null && res.Length > 0 && res[0] ==3)
                    {
                        break;
                    }
                    spinWait.SpinOnce();
                }


            });
        }

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
    }
}
