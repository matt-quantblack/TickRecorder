using System;


namespace QBTickRecorder
{
    public class TickData
    {
        //Class that extracts the main data from ProtoOASpotEvent and also adds the time at which the tick was recieved in UTC
        public int SymbolId { get; set; }
        public DateTime Time { get; set; }
        public bool IsBid { get; set; }
        public ulong Value { get; set; }

        public TickData(int symbolId, DateTime time, bool isBid, ulong value)
        {
            SymbolId = symbolId;
            Time = time;
            IsBid = isBid;
            Value = value;
        }

        public string GetFilename(string symbolName)
        {
            if (IsBid)
                return "\\Tick\\" + symbolName + "\\" + Time.ToString("yyyy-MM-dd") + "_Bid.csv";
            else
                return "\\Tick\\" + symbolName + "\\" + @Time.ToString("yyyy-MM-dd") + "_Ask.csv";
        }

        public override string ToString()
        {
            return Time.ToString("HH:mm:ss.fff") + "," + Value + "\n";
        }
    }
}
