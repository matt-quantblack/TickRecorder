using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QBTickRecorder
{
    public class SymbolDisplay
    {
        public string Symbol { get; set; }
        public long SymbolId { get; set; }
        public double? Bid { get; set; }
        public double? Ask { get; set; }
        public DateTime LastTick { get; set; }
        public DateTime LastWrite { get; set; }


        public string LastTickString
        {
            get
            {
                if (LastTick > new DateTime())
                    return LastTick.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
                else
                    return "";
            }
        }

        public string LastWriteString
        {
            get
            {
                if (LastWrite > new DateTime())
                    return LastWrite.ToString("yyyy-MM-dd HH:mm:ss");
                else
                    return "";
            }
        }




        public SymbolDisplay(string symbol, long symbolId)
        {            
            Symbol = symbol;
            SymbolId = symbolId;
        }
    }
}
