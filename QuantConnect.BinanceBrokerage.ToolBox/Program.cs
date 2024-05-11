/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Hosting;
using QuantConnect.ToolBox;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Util;
using static QuantConnect.Configuration.ApplicationParser;
using QuantConnect.Logging;
using Quartz;
using Microsoft.Extensions.DependencyInjection;
using QuantConnect.BinanceBrokerage.ToolBox;

namespace QuantConnect.Brokerages.Binance.ToolBox
{
    static class Program
    {
        static void Main(string[] args)
        {
            Log.DebuggingEnabled = Config.GetBool("debug-mode", true);

            var optionsObject = ToolboxArgumentParser.ParseArguments(args);
            if (optionsObject.Count == 0)
            {
                PrintMessageAndExit();
            }

            if (optionsObject.ContainsKey("destination-dir"))
            {
                Config.Set("data-folder", optionsObject["destination-dir"]);
            }

            if (!optionsObject.TryGetValue("app", out var targetApp))
            {
                PrintMessageAndExit(1, "ERROR: --app value is required");
            }

            var targetAppName = targetApp?.ToString() ?? throw new ArgumentNullException(nameof(targetApp));
            if (targetAppName.Contains("downloader") || targetAppName.Contains("dl"))
            {
                var fromDate = Parse.DateTimeExact(GetParameterOrExit(optionsObject, "from-date"), "yyyyMMdd-HH:mm:ss");
                var toDate = optionsObject.ContainsKey("to-date")
                    ? Parse.DateTimeExact(optionsObject["to-date"].ToString(), "yyyyMMdd-HH:mm:ss")
                    : DateTime.UtcNow;
                var resolution = optionsObject.ContainsKey("resolution") ? optionsObject["resolution"].ToString() : "";
                var tickers = ToolboxArgumentParser.GetTickers(optionsObject);

                BaseDataDownloader dataDownloader;
                if (targetAppName.Equals("binanceusdownloader") || targetAppName.Equals("mbxusdl"))
                {
                    dataDownloader = new BinanceUSDataDownloader();
                }
                else
                {
                    dataDownloader = new BinanceDataDownloader();
                }

                //DownloadData(dataDownloader, tickers, resolution, fromDate, toDate);

                RunQuartzDownloaderJob(dataDownloader, tickers, resolution, fromDate, toDate);

            }
            else if (targetAppName.Contains("updater") || targetAppName.EndsWith("spu"))
            {
                BinanceExchangeInfoDownloader exchangeInfoDownloader;
                if (targetAppName.Equals("binanceussymbolpropertiesupdater") || targetAppName.Equals("mbxusspu"))
                {
                    exchangeInfoDownloader = new BinanceExchangeInfoDownloader(Market.BinanceUS);
                }
                else
                {
                    exchangeInfoDownloader = new BinanceExchangeInfoDownloader(Market.Binance);
                }

                ExchangeInfoDownloader(exchangeInfoDownloader);
            }
            else
            {
                PrintMessageAndExit(1, "ERROR: Unrecognized --app value");
            }
        }

        public static void RunQuartzDownloaderJob(BaseDataDownloader downloader, List<string> tickers, string resolution, DateTime fromDate, DateTime toDate)
        {
            var builder = Host.CreateDefaultBuilder()
                .ConfigureServices((cxt, services) =>
                {
                    services.AddQuartz();
                    services.AddQuartzHostedService(opt =>
                    {
                        opt.WaitForJobsToComplete = true;
                    });
                    services.AddSingleton(new QuartzDownloaderJobData()
                    {
                        Downloader = downloader,
                        Resolution = resolution,
                        Tickers = tickers,
                        FromDate = fromDate,
                    });
                }).Build();

            var schedulerFactory = builder.Services.GetRequiredService<ISchedulerFactory>();
            var scheduler = schedulerFactory.GetScheduler().GetAwaiter().GetResult();
            scheduler.Start();

            var job = JobBuilder.Create<QuartzDownloaderJob>()
                .WithIdentity("myJob", "group1")
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity("myTrigger", "group1")
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithIntervalInHours(12)
                    .RepeatForever())
                .Build();

            scheduler.ScheduleJob(job, trigger);

            builder.Run();
        }

        /// <summary>
        /// Primary entry point to the program.
        /// </summary>
        public static void DownloadData(BaseDataDownloader downloader, IList<string> tickers, string resolution, DateTime fromDate, DateTime toDate)
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
                    if (data == null)
                    {
                        continue;
                    }

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

        /// <summary>
        /// Endpoint for downloading exchange info
        /// </summary>
        public static void ExchangeInfoDownloader(IExchangeInfoDownloader exchangeInfoDownloader)
        {
            new ExchangeInfoUpdater(exchangeInfoDownloader)
                .Run();
        }
    }
}