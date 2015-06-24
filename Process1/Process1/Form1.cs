using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

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
        
        /// <summary>
        /// Test of non-blocking requests (are called when pressed Write from Process2)
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        Tuple<bool,byte[]> RemoteCall(byte[] data)
        {
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
            if(sm == null)
                sm = new tiesky.com.SharmIpc("MyNewSharmIpc", this.RemoteCall);
                        

        }

        /// <summary>
        ///Test of RemoteRequest with answer (Process 2 must receive request in its RemoteCall and return back Response).
        ///Here we calcualte the speed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button2_Click(object sender, EventArgs e)
        {
            //var res1 = sm.RemoteRequest(new byte[546],
            //  (par) =>
            //  {
            //      Console.WriteLine(par.Item1);
            //  }
            //  , 10000);

            //var res1 = sm.RemoteRequest(new byte[546],null, 10000);
            //return;

            //byte[] data = new byte[1];
            byte[] data = new byte[512];
            //byte[] data = new byte[10000];
            int iter = 10000;

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            for (int i = 0; i < iter; i++)
            {
                // var res = sm.RemoteRequest(new byte[512], (par) => { });
                //var res = sm.RemoteRequest(null);
                //var res = sm.RemoteRequest(data, (par) => { },30000);
                //sm.RemoteRequestWithoutResponse(new byte[512]);      
                var res = sm.RemoteRequest(data);
            }
            sw.Stop();
            Console.WriteLine("Speed: {0} MB/s, Finished in ms {1}",
                    Math.Round((decimal)data.Length * (decimal)iter * (decimal)1000 /
                    ((decimal)sw.ElapsedMilliseconds * 1000000m)
                    , 2), sw.ElapsedMilliseconds
                    );
           // Console.WriteLine(sw.ElapsedMilliseconds);

        }


                
    }
}
