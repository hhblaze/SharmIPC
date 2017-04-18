using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tiesky.com.SharmIpcInternals
{
    internal class Statistic
    {

        DateTime _ready2writeSignal_Start = DateTime.MinValue;
        long _ready2writeSignal_Last = -1;
        long _ready2writeSignal_Max = -1;
        DateTime _ready2writeSignal_Max_Setup = DateTime.MinValue;
        DateTime _ready2writeSignal_Last_Setup = DateTime.MinValue;
        const string dtf = "dd.MM.yyyy HH:mm:ss.ms";

        public void StartToWait_ReadyToWrite_Signal()
        {
            _ready2writeSignal_Start = DateTime.UtcNow;
        }

        public void StopToWait_ReadyToWrite_Signal()
        {
            _ready2writeSignal_Last = DateTime.UtcNow.Subtract(_ready2writeSignal_Start).Ticks;
            _ready2writeSignal_Last_Setup = DateTime.UtcNow;
            if (_ready2writeSignal_Max < _ready2writeSignal_Last)
            {
                _ready2writeSignal_Max = _ready2writeSignal_Last;
                _ready2writeSignal_Max_Setup = DateTime.UtcNow;
            }
        }



        public string Report()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("_ready2writeSignal_Max: " + _ready2writeSignal_Max + $" ({_ready2writeSignal_Max/TimeSpan.TicksPerMillisecond}); Setup: " + _ready2writeSignal_Max_Setup.ToString(dtf));
            sb.Append("<br>");
            sb.Append("_ready2writeSignal_Last: " + _ready2writeSignal_Last + $" ({_ready2writeSignal_Last/TimeSpan.TicksPerMillisecond}); Setup: " + _ready2writeSignal_Last_Setup.ToString(dtf));
            sb.Append("<br>");
            return sb.ToString();
        }


    }
}
