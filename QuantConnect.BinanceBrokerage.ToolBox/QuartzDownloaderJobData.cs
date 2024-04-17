using System;
using System.Collections.Generic;
using QuantConnect.Brokerages.Binance.ToolBox;

namespace QuantConnect.BinanceBrokerage.ToolBox
{
    public class QuartzDownloaderJobData
    {
        public BaseDataDownloader Downloader { get; set; }
        public List<string> Tickers { get; set; }
        public string Resolution { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
    }
}
