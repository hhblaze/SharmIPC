using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace mmf2client
{
    public partial class Form1 : Form
    {
 
        public Form1()
        {
            InitializeComponent();

            button1_Click(null,null);
        }

        tiesky.com.SharmIpc sm = null;
   

        Tuple<bool, byte[]> RemoteCall(byte[] data)
        {
            //Console.WriteLine("Received: {0} bytes", (data == null ? 0 : data.Length));
            //return new Tuple<bool, byte[]>(true, new byte[] { 9, 4, 12, 17 });
            //return new Tuple<bool, byte[]>(true, new byte[] { 9 });
            return new Tuple<bool, byte[]>(true, new byte[512]);
        }

        void AsyncRemoteCallHandler(ulong msgId, byte[] data)
        {
            //sm.AsyncAnswerOnRemoteCall(msgId, new Tuple<bool, byte[]>(true, new byte[] { 9 }));
            //System.Threading.Thread.Sleep(50000);
            sm.AsyncAnswerOnRemoteCall(msgId, new Tuple<bool, byte[]>(true, new byte[1]));

            //if(data != null && data.Length>0)
            //{
            //    if(data[0] == 1)
            //    {
            //        System.Threading.Thread.Sleep(10000);
            //        sm.AsyncAnswerOnRemoteCall(msgId, new Tuple<bool, byte[]>(true, data));
            //    }
            //    else
            //    {
            //        sm.AsyncAnswerOnRemoteCall(msgId, new Tuple<bool, byte[]>(true, data));
            //    }
            //}


            //Task.Run(() =>
            //{
            //    //sm.AsyncAnswerOnRemoteCall(msgId, new Tuple<bool, byte[]>(true, new byte[] { 9, 4, 12, 17, 25 }));
            //    sm.AsyncAnswerOnRemoteCall(msgId, new Tuple<bool, byte[]>(true, new byte[] { 9 }));
            //}
            //);
        }


        private void button1_Click(object sender, EventArgs e)
        {
            if (sm == null)
            {
                sm = new tiesky.com.SharmIpc("Global/MyNewSharmIpc", this.AsyncRemoteCallHandler);
                //sm = new tiesky.com.SharmIpc("Global/MyNewSharmIpc", this.RemoteCall);
            }

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

        private void button3_Click(object sender, EventArgs e)
        {

        }
    }
}
