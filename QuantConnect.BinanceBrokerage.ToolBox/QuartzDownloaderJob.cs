using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using QuantConnect.Brokerages.Binance.ToolBox;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Util;
using Quartz;

namespace QuantConnect.BinanceBrokerage.ToolBox
{
    // see: https://stackoverflow.com/questions/33465868/how-to-save-data-to-jobdatamap-during-job-execution-and-access-it-after
    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    internal class QuartzDownloaderJob : IJob
    {
        private static int Counter;
        private readonly ILogger<QuartzDownloaderJob> _logger;
        private readonly QuartzDownloaderJobData _data;

        // uses static data, see: https://stackoverflow.com/questions/36517643/pass-information-between-jobs-in-quartz-net
        public QuartzDownloaderJob(ILogger<QuartzDownloaderJob> logger, QuartzDownloaderJobData data)
        {
            _logger = logger;
            _data = data;
        }

        public Task Execute(IJobExecutionContext context)
        {
            var fromDateTime = _data.FromDate;
            var toDateTime = DateTime.UtcNow;

            _logger.LogInformation($"Running QuartzDownloaderJob; from: {fromDateTime}; to: {toDateTime}; total runs: {++Counter}");

            DownloadData(_data.Downloader, _data.Tickers, _data.Resolution, fromDateTime, toDateTime);
            _data.FromDate = toDateTime.Date;

            return Task.CompletedTask;
        }
        
        private static void DownloadData(BaseDataDownloader downloader, IList<string> tickers, string resolution, DateTime fromDate, DateTime toDate)
        {
            if (resolution.IsNullOrEmpty() || tickers.IsNullOrEmpty())
            {
                Console.WriteLine("BinanceDownloader ERROR: '--tickers=' or '--resolution=' parameter is missing");
                Console.WriteLine("--tickers=eg BTCUSD");
                Console.WriteLine("--resolution=Minute/Hour/Daily/All");
                Environment.Exit(1);
            }
            try
            {
                var allResolutions = resolution.Equals("all", StringComparison.OrdinalIgnoreCase);
                var castResolution = allResolutions ? Resolution.Minute : (Resolution)Enum.Parse(typeof(Resolution), resolution);

                // Load settings from config.json
                var dataDirectory = Config.Get("data-folder", "../../../Data");

                foreach (var ticker in tickers)
                {
                    // Download the data
                    var symbol = downloader.GetSymbol(ticker);
                    var data = downloader.Get(new DataDownloaderGetParameters(symbol, castResolution, fromDate, toDate));
                    var bars = data.Cast<TradeBar>().ToList();

                    // Save the data (single resolution)
                    var writer = new LeanDataWriter(castResolution, symbol, dataDirectory);
                    writer.Write(bars);

                    if (allResolutions)
                    {
                        // Save the data (other resolutions)
                        foreach (var res in new[] { Resolution.Hour, Resolution.Daily })
                        {
                            var resData = LeanData.AggregateTradeBars(bars, symbol, res.ToTimeSpan());

                            writer = new LeanDataWriter(res, symbol, dataDirectory);
                            writer.Write(resData);
                        }
                    }
                }
            }
            catch (Exception err)
            {
                PrintMessageAndExit(1, $"ERROR: {err.Message}");
            }
        }

        private static void PrintMessageAndExit(int exitCode = 0, string message = "")
        {
            if (!string.IsNullOrEmpty(message))
            {
                Console.WriteLine("\n" + message);
            }

            Console.WriteLine("\nUse the '--help' parameter for more information");
            Console.WriteLine("Press any key to quit");
            Console.ReadLine();
            Environment.Exit(exitCode);
        }
    }
}
