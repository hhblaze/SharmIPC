using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace tiesky.com.SharmIpcInternals
{
    internal class SspinWait
    {
        SpinWait spinwt = new SpinWait();

        public SspinWait()
        {           
        }
     
        int currentIter = 0;
        int waitIter = 5;  //Start waiting from 10 iterations then every new iteration doubling. 100 times , then spinwt.SpinOnce

        const long testIterations = 10000;
        static long iterationsInMs = 0; //300K

        public void Spin()
        {
            if (currentIter > iterationsInMs)
            {                
                this.spinwt.SpinOnce();
                return;
            }
            
            Thread.SpinWait(waitIter);
            currentIter += waitIter;
        }

        public void Clear()
        {           
            currentIter = 0;
        }

        /// <summary>
        /// Runs onec in the beginning
        /// </summary>
        public static void Measure()
        {
            
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            long tksInMs = 0;
            sw.Start();            
            Thread.Sleep(1);
            sw.Stop();

            tksInMs = sw.ElapsedTicks;
            sw.Reset();
            sw.Start();
            Thread.SpinWait((int)testIterations);           
            sw.Stop();
            
            //TimeSpan.TicksPerMillisecond
            iterationsInMs = (tksInMs / sw.ElapsedTicks) * testIterations;
        }

        
    }
}
