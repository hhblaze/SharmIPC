using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;



namespace MemoryMappedFile
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            button1_Click(null, null);
        }

        tiesky.com.ISharm sm = null;
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
                sm = new tiesky.com.SharmNpc("MNPC", tiesky.com.SharmNpcInternals.PipeRole.Server, this.RemoteCall, externalProcessing: false);
                //sm = new tiesky.com.SharmIpc("MyNewSharmIpc", this.RemoteCall, protocolVersion: tiesky.com.SharmIpc.eProtocolVersion.V1);
                //or to get ability to answer to remote partner in async way
                //sm = new tiesky.com.SharmIpc("Global/MyNewSharmIpc", this.AsyncRemoteCallHandler);                
            }

            //System.Threading.ThreadPool.SetMinThreads(100, 100);
        }

        #region "test"
        async Task t001_TestIntensiveParallel()
        {
            System.Diagnostics.Stopwatch sw = null;
            List<byte[]> mll = new List<byte[]>();


            //var uzuz = sm.RemoteRequest(new byte[] { 50 }, (Tuple<bool, byte[]> myres1) => 
            //{
            //    Console.WriteLine("received");
            //}, timeoutMs: 10000);


            //return;

            //var uzuz = await sm.RemoteRequestAsync(new byte[1700]);
            //return;

            //Parallel.For(0, 100, async (aii) =>
            //{
            //    for (int j = 0; j < 1000; j++)
            //    {
            //        //var tor = sm.RemoteRequest(new byte[50]);
            //        var tor = await sm.RemoteRequestAsync(new byte[50]);
            //        //Console.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.ms") + "> masterRes " +tor.Item1 + " " + tor.Item2.Length);
            //    }
            //});

            int totalSentAndReceivedbytes=0;
            sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            await Parallel.ForEachAsync(Enumerable.Range(0, 20), 
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async (aii, cancellationToken) =>
            {
                for (int j = 0; j < 2000; j++)
                {
                    var dataToSend = new byte[1];
                    var tor = await sm.RemoteRequestAsync(dataToSend);
                    Interlocked.Add(ref totalSentAndReceivedbytes, dataToSend.Length + tor.Item2.Length);
                }
            });           


            //for (int j = 0; j < 10000; j++)
            //{
            //    //var tor = sm.RemoteRequest(new byte[1]);
            //    var tor = await sm.RemoteRequestAsync(new byte[1] { 17 });
            //    //mll.Add(tor.Item2);
            //}

            sw.Stop();
            string showStr = $"Elapse: {sw.ElapsedMilliseconds}; Totalbyte: {totalSentAndReceivedbytes}";
            Console.WriteLine(showStr);
            Debug.WriteLine(showStr);
            MessageBox.Show(showStr);

            return;


            //sw = new System.Diagnostics.Stopwatch();
            //sw.Start();

            //for (int j = 0; j < 10000; j++)
            //{
            //    var tor = await sm.RemoteRequestAsync(new byte[1]);

            //}
            //sw.Stop();
            ////Console.WriteLine("ELAPS: " + sw.ElapsedMilliseconds);
            //MessageBox.Show("ELAPS: " + sw.ElapsedMilliseconds);
            //return;




            //sw = new System.Diagnostics.Stopwatch();
            //sw.Start();
            //for (int j = 0; j < 20000; j++)
            //{
            //    var tor = sm.RemoteRequest(new byte[1], null);

            //}
            //sw.Stop();
            //MessageBox.Show("ELAPS: " + sw.ElapsedMilliseconds);
            //Console.WriteLine("ELAPS: " + sw.ElapsedMilliseconds);
            //return;



            //sw = new System.Diagnostics.Stopwatch();
            //sw.Start();
            //for (int j = 0; j < 1000; j++)
            //{
            //    var tor = sm.RemoteRequest(new byte[50], (ans) => {

            //    });

            //}
            //sw.Stop();
            //MessageBox.Show("ELAPS: " + sw.ElapsedMilliseconds);
            ////Console.WriteLine("ELAPS: " + sw.ElapsedMilliseconds);
            //return;



            //System.Threading.ThreadPool.SetMinThreads(100, 100);

            var tasks = new List<Task>();
            //Action a = () =>
            //{
            //    for (int j = 0; j < 10000; j++)
            //    {                  
            //        var tor = sm.RemoteRequest(new byte[1]);

            //        //var tor = sm.RemoteRequest(new byte[50],(par) => {
            //        //});
            //        //Console.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.ms") + "> masterRes " +tor.Item1 + " " + tor.Item2.Length);
            //    }
            //};

            //var t = Task.Run(() => { sm.RemoteRequest(new byte[50]); });
            for (int i = 0; i < 5; i++)
            { 
                int index = i;
                //tasks.Add(Task.Factory.StartNew(a));

                //tasks.Add(Task.Factory.StartNew(RunMeManyAsync));
                tasks.Add(Task.Run(() =>  RunMeManyAsync() ));
                //tasks.Add(RunMeManyAsync());
            }

            sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            await Task.WhenAll(tasks.ToArray());
            sw.Stop();
            Console.WriteLine("ELAPS: " + sw.ElapsedMilliseconds);
            MessageBox.Show("ELAPS: " + sw.ElapsedMilliseconds);
        }
        #endregion


        async Task RunMeManyAsync()
        {
            for (int j = 0; j < 10000; j++)
            {
                //var tor = await sm.RemoteRequestAsync(new byte[1], (ans) =>
                //{

                //});

                var tor = await sm.RemoteRequestAsync(new byte[] { 18 });

            }
        }

        /// <summary>
        ///Test of RemoteRequest with answer (Process 2 must receive request in its RemoteCall and return back Response).
        ///Here we calcualte the speed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void button2_Click(object sender, EventArgs e)
        {
            //tiesky.com.SharmIpc.AsyncManualResetEvent mre = new tiesky.com.SharmIpc.AsyncManualResetEvent();

            //Task.Run(() =>
            //{
            //    System.Threading.Thread.Sleep(3000);
            //    mre.Set();
            //});

            //mre.Set();

            //var trtz = await Task.WhenAny(mre.WaitAsync(), Task.Delay(10000));

            //return;


            //var uzuz = await sm.RemoteRequestAsync(new byte[1700]);
            //return;

            t001_TestIntensiveParallel();
            return;

            var x = new DateTime(636282847257956630, DateTimeKind.Utc);
            var x1 = new DateTime(636282847236855000, DateTimeKind.Utc);

            var dfr41 = sm.RemoteRequest(new byte[1700]);
            return;
            byte[] here = new byte[2500];
            

            Action<int> a1 = (id) =>
            {

                //Console.WriteLine("YAHOO " + id);
                //DBreeze.Diagnostic.SpeedStatistic.StartCounter("a"+id);
                int tt = 0;
                for (int i = 0; i < 1000; i++)
                {
                   var xr = sm.RemoteRequest(new byte[1700]);
                    tt += xr.Item2.Length;
                }
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ms") + "> DONE " + tt);
                //DBreeze.Diagnostic.SpeedStatistic.PrintOut("a" + id);
                //Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ms") + "> DONE " + tt);
            };

            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ms") + "> START");
            for (int j = 0; j < 40; j++)
            {
                //((Action)(() => { a1(); })).DoAsync();
                System.Threading.Tasks.Task.Run(() =>
                {
                    // Console.WriteLine("Running " + j.ToString());
                    a1(j);
                });
                //new System.Threading.Thread(() => { a1(); }).Start();
            }

            return;

            var res222 = sm.RemoteRequest(new byte[1700]);
            Console.WriteLine("Received " + res222.Item2.Length + " bytes");
            return;

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

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            for (int i = 0; i < iter; i++)
            {
                // var res = sm.RemoteRequest(new byte[512], (par) => { });
                //var res = sm.RemoteRequest(null);
                //var res = sm.RemoteRequest(data, (par) => { },30000);
                //sm.RemoteRequestWithoutResponse(new byte[512]);
                //sm.RemoteRequestWithoutResponse(new byte[1]);
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

        async private void button3_Click(object sender, EventArgs e)
        {
            //System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            //sw.Start();
            //byte[] bt = new byte[1];
            //char[] cbt = new char[1];


            //using (MemoryStream ms = new MemoryStream())
            //{
            //    using (var inputStreamReader = new StreamReader(ms))
            //    {
            //        var jj1 = await inputStreamReader.ReadAsync(cbt, 0, 1);
            //        var jj = await inputStreamReader.ReadBlockAsync(cbt, 0, 1);
            //    }
            //    //var jj = await ms.ReadAsync(bt, 0, 1);
            //}
            //    sw.Stop();
            //Console.WriteLine("Finished in ms {0}", sw.ElapsedMilliseconds);

            //var tor = await sm.RemoteRequestAsync(new byte[] { 1 });

            //for (int j = 0; j < 1; j++)
            //{              
            //    var tor = await sm.RemoteRequestAsync(new byte[] { 1 }).ConfigureAwait(false);
            //    Console.WriteLine("mmmmm");
            //}

        }

        async private void button4_Click(object sender, EventArgs e)
        {
            for (int j = 0; j < 1; j++)
            {
                var tor = await sm.RemoteRequestAsync(new byte[] { 2 }).ConfigureAwait(false);
                Console.WriteLine("here");
            }
        }
    }
}
