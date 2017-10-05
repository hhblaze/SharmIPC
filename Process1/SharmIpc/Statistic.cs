using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tiesky.com.SharmIpcInternals
{
    internal class Statistic
    {
        internal SharmIpc ipc = null;        
        DateTime _ready2writeSignal_Start = DateTime.MinValue;
        long _ready2writeSignal_Last = -1;
        long _ready2writeSignal_Max = -1;
        DateTime _ready2writeSignal_Max_Setup = DateTime.MinValue;
        public DateTime _ready2ReadSignal_Last_Setup = DateTime.MinValue;
        public DateTime _ready2writeSignal_Last_Setup = DateTime.MinValue;
        const string dtf = "dd.MM.yyyy HH:mm:ss.ms";
        ulong _ready2writeSignal_Calls = 0;
        ulong _writing = 0;
        ulong _reading = 0;
        int _writing_max = 0;
        int _reading_max = 0;
        ulong _writing_times = 1;
        ulong _reading_times = 1;
        int _error_totalBytesInQueue = 0;
        int _timeouts = 0;
        DateTime _timeouts_Last_Setup = DateTime.MinValue;

        DateTime _readProcedure_Start = DateTime.MinValue;
        long _readProcedure_Max = -1;
        DateTime _readProcedure_Max_Setup = DateTime.MinValue;

        DateTime _waitForRead_Start = DateTime.MinValue;
        long _waitForRead_Max = -1;
        DateTime _waitForRead_Max_Setup = DateTime.MinValue;

        public long TotalBytesInQueue = 0;
        

        public void Start_WaitForRead_Signal()
        {
            _waitForRead_Start = DateTime.UtcNow;
        }

        public void Stop_WaitForRead_Signal()
        {
            _ready2ReadSignal_Last_Setup = DateTime.UtcNow;

            if (_waitForRead_Start == DateTime.MinValue)
                return;

            var t = DateTime.UtcNow.Subtract(_waitForRead_Start).Ticks;
            if (_waitForRead_Max < t)
            {
                _waitForRead_Max = t;
                _waitForRead_Max_Setup = DateTime.UtcNow;
            }
        }



        public void Start_ReadProcedure_Signal()
        {
            _readProcedure_Start = DateTime.UtcNow;
        }

        public void Stop_ReadProcedure_Signal()
        {
            var t = DateTime.UtcNow.Subtract(_readProcedure_Start).Ticks;
            if (_readProcedure_Max < t)
            {
                _readProcedure_Max = t;
                _readProcedure_Max_Setup = DateTime.UtcNow;
            }
        }

        public void StartToWait_ReadyToWrite_Signal()
        {
            _ready2writeSignal_Start = DateTime.UtcNow;
            _ready2writeSignal_Calls++;
        }

        public void StopToWait_ReadyToWrite_Signal()
        {
            _ready2writeSignal_Last_Setup = DateTime.UtcNow;
            _ready2writeSignal_Last = DateTime.UtcNow.Subtract(_ready2writeSignal_Start).Ticks;
            
            if (_ready2writeSignal_Max < _ready2writeSignal_Last)
            {
                _ready2writeSignal_Max = _ready2writeSignal_Last;
                _ready2writeSignal_Max_Setup = DateTime.UtcNow;
            }
        }

        public void Writing(int quantity)
        {
            _writing_times++;
            _writing += (ulong)quantity;
            if (quantity > _writing_max)
                _writing_max = quantity;
        }


        public void Reading(int quantity)
        {
            _reading_times++;
            _reading += (ulong)quantity;
            if (quantity > _reading_max)
                _reading_max = quantity;
        }

        public void TotalBytesInQueueError()
        {
            _error_totalBytesInQueue++;
        }

        public void Timeout()
        {
            _timeouts++;
            _timeouts_Last_Setup = DateTime.UtcNow;
        }

        public string Report()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Time: " + DateTime.UtcNow.ToString(dtf) + $"; Protocol: {this.ipc.ProtocolVersion.ToString()}");
            sb.Append("<hr>");

            sb.Append("_ready2writeSignal_Calls: " + _ready2writeSignal_Calls + ";");
            sb.Append("<br>");
            sb.Append("_ready2writeSignal_Max: " + _ready2writeSignal_Max + $" ({_ready2writeSignal_Max/TimeSpan.TicksPerMillisecond}); Setup: " + _ready2writeSignal_Max_Setup.ToString(dtf));
            sb.Append("<br>");
            sb.Append("_ready2writeSignal_Last (shows when writer's await was set): " + _ready2writeSignal_Last + $" ({_ready2writeSignal_Last/TimeSpan.TicksPerMillisecond}); Setup: " + _ready2writeSignal_Last_Setup.ToString(dtf));
            sb.Append("<br>");
            sb.Append("_ready2ReadSignal_Last_Setup (shows when read's await was set): " + _ready2ReadSignal_Last_Setup.ToString(dtf));
            sb.Append("<br>");
            
            sb.Append("<hr>");
            sb.Append("_waitForRead_Max: " + _waitForRead_Max + $" ({_waitForRead_Max / TimeSpan.TicksPerMillisecond }); Setup: " + _waitForRead_Max_Setup.ToString(dtf));
            sb.Append("<br>");
            sb.Append("_readProcedure_Max: " + _readProcedure_Max + $" ({_readProcedure_Max / TimeSpan.TicksPerMillisecond }); Setup: " + _readProcedure_Max_Setup.ToString(dtf));            
            sb.Append("<br>");


         
            sb.Append("<hr>");
            sb.Append("_writing: " + _writing + " bytes; Max: " + _writing_max + $" bytes; Times: {_writing_times}; Middle: {_writing / _writing_times} bytes");
            sb.Append("<br>");
            sb.Append("_reading: " + _reading + " bytes; Max: " + _reading_max + $" bytes; Times: {_reading_times}; Middle: {_reading / _reading_times} bytes");
            sb.Append("<br>");

            sb.Append("<hr>");
            sb.Append("TotalBytesInQueue: " + this.TotalBytesInQueue + ";");
            sb.Append("<br>");
            sb.Append("_error_totalBytesInQueue: " + _error_totalBytesInQueue + ";");
            sb.Append("<br>");

            sb.Append("<hr>");
            sb.Append("_timeouts: " + _timeouts + "; Last setup: " + _timeouts_Last_Setup.ToString(dtf));
            sb.Append("<br>");

            return sb.ToString();
        }


    }
}
