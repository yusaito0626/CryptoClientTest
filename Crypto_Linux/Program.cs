using Binance.Net.Enums;
using Binance.Net.Objects.Options;
using CoinW.Net.Clients;
using Crypto_Clients;
using Crypto_Trading;
using CryptoClients.Net.Enums;
using CryptoExchange.Net;
using CryptoExchange.Net.Logging.Extensions;
using CryptoExchange.Net.SharedApis;
using Discord;
using Enums;
using LockFreeQueue;
using LockFreeStack;
using PubnubApi.EndPoint;
using System;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.SymbolStore;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
//using Terminal.Gui;
using Utils;
using static System.Runtime.InteropServices.JavaScript.JSType;
//using static Terminal.Gui.View;


namespace Crypto_Linux
{
    internal class Program
    {
        static string defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        static string logPath = Path.Combine(AppContext.BaseDirectory, "crypto.log");
        static string outputPath = AppContext.BaseDirectory;
        static string outputPath_org = AppContext.BaseDirectory;
        static List<string> APIList = new List<string>();
        static string discordTokenFile = "";
        static string masterFile = "";
        static string virtualBalanceFile = "";
        static string strategyFile = "";

        static string intradayPnLFile = "";

        static Crypto_Clients.Crypto_Clients crypto_client = Crypto_Clients.Crypto_Clients.GetInstance();
        static QuoteManager qManager = QuoteManager.GetInstance();
        static OrderManager oManager = OrderManager.GetInstance();
        static ThreadManager thManager = ThreadManager.GetInstance();
        static MessageDeliverer MsgDeliverer = MessageDeliverer.GetInstance();
        static websocketServer ws_server = websocketServer.GetInstance();

        static msgType msg_type = msgType.NOTIFICATION;
        static msgType err_msg_type = msgType.ERROR;

        static Dictionary<string, string> sendingItem = new Dictionary<string, string>();
        static JsonSerializerOptions js_option = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        static int logSize = 50000;
        static LockFreeStack<logEntry> logEntryStack;
        static LockFreeStack<fillInfo> fillInfoStack;

        static public Dictionary<string, Strategy> strategies;

        static bool enabled;

        static SISOQueue<string> logQueue;
        static SISOQueue<DataFill> filledOrderQueue;
        static SISOQueue<MarketImpact> marketImpactQueue;

        static StreamWriter logFile;
        static StreamWriter marketImpactFile;

        static private bool threadsStarted;
        static private int stopTradingCalled;
        static private bool live;
        static private bool test;
        static private bool privateConnect;
        static private bool log_public = false;
        static private bool setRealPos;
        static private bool msgLogging;
        static private DateTime? lastConnectedTime = null;
        static private double reconnectionPeriod = 4;

        static string str_endTime;
        static DateTime endTime;

        static int msg_Interval;
        static DateTime nextMsgTime;

        static DateTime? intradayPnLTime = null;

        static Dictionary<string, strategyInfo> strategyInfos = new Dictionary<string, strategyInfo>();
        static Dictionary<string, instrumentInfo> instrumentInfos = new Dictionary<string, instrumentInfo>();
        static Dictionary<string, connecitonStatus> connectionStates = new Dictionary<string, connecitonStatus>();
        static Dictionary<string , threadStatus> threadStates = new Dictionary<string, threadStatus>();
        static Dictionary<string, queueInfo> queueInfos = new Dictionary<string, queueInfo>();

        static Dictionary<string, latency> LatencyList = new Dictionary<string, latency>();

        static bool isRunning = false;

        static int mismatch_count = 0;

        static int EoDProcessCalled = 0;
        static async Task Main(string[] args)
        {

            Console.CancelKeyPress += (sender, e) =>
            {
                addLog("Terminating the app...");
                e.Cancel = true;
                EoDProcess().GetAwaiter().GetResult();
                isRunning = false;
            };

            AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) =>
            {
                addLog("SIGTERM detected");

                EoDProcess().GetAwaiter().GetResult(); 
                isRunning = false;
            };
            
            await mainProcess();
            addLog("Bye Bye");
            updateLog();
        }

        static private async Task mainProcess()
        {
            logQueue = new SISOQueue<string>();

            Console.WriteLine("Crypto Trading App Ver." + GlobalVariables.ver_major + ":" + GlobalVariables.ver_minor + ":" + GlobalVariables.ver_patch);
            threadsStarted = false;
            live = false;
            test = false;
            privateConnect = true;
            setRealPos = true;
            msgLogging = false;

            marketImpactQueue = new SISOQueue<MarketImpact>();

            filledOrderQueue = new SISOQueue<DataFill>();
            logEntryStack = new LockFreeStack<logEntry>();
            fillInfoStack = new LockFreeStack<fillInfo>();
            int i = 0;
            while (i < logSize)
            {
                logEntryStack.push(new logEntry());
                fillInfoStack.push(new fillInfo());
                ++i;
            }

            strategies = new Dictionary<string, Strategy>();

            enabled = false;

            str_endTime = "23:30:00";

            msg_Interval = 30;

            if (!readConfig())
            {
                addLog("Failed to read config.", Enums.logType.ERROR);
                updateLog();
                return;
            }
            Console.WriteLine("readConfig completed");
            ;

            nextMsgTime = DateTime.UtcNow + TimeSpan.FromMinutes(msg_Interval);


            logFile = new StreamWriter(new FileStream(logPath, FileMode.Create));

            qManager._addLog = addLog;
            oManager._addLog = addLog;
            crypto_client.setAddLog(addLog);
            thManager._addLog = addLog;
            ws_server._addLog = addLog;

            ws_server.onExitCommand = async () =>
            {
                await EoDProcess();
                isRunning = false;
            };

            qManager.initializeInstruments(masterFile);
            qManager.setQueues(crypto_client);

            oManager.setOrdLogPath(outputPath);
            oManager.setInstruments(qManager.instruments);
            oManager.filledOrderQueue = filledOrderQueue;
            oManager.MI_outputQueue = marketImpactQueue;

            //readAPIFiles(APIsPath);
            if (test && privateConnect)
            {
                getAPIsFromEnv(true);
            }
            else
            {
                getAPIsFromEnv(live);
            }

            foreach (var ins in qManager.instruments.Values)
            {
                instrumentInfo insInfo = new instrumentInfo();
                insInfo.symbol = ins.symbol;
                insInfo.market = ins.market.ToString();
                insInfo.symbol_market = ins.symbol_market;
                insInfo.baseCcy = ins.baseCcy;
                insInfo.quoteCcy = ins.quoteCcy;
                insInfo.market_impact_curve = new Dictionary<double, decimal>();
                foreach (double d in GlobalVariables.MI_period)
                {
                    insInfo.market_impact_curve[d] = 0;
                }
                instrumentInfos[ins.symbol_market] = insInfo;
            }

            setStrategies(strategyFile);
            qManager.strategies = strategies;
            oManager.strategies = strategies;

            Dictionary<string, masterInfo> masterinfos = new Dictionary<string, masterInfo>();

            foreach (var ins in qManager.instruments)
            {
                masterInfo ms = new masterInfo();
                ms.symbol = ins.Value.symbol;
                ms.market = ins.Value.market.ToString();
                ms.baseCcy = ins.Value.baseCcy;
                ms.quoteCcy = ins.Value.quoteCcy;
                ms.taker_fee = ins.Value.taker_fee;
                ms.maker_fee = ins.Value.maker_fee;
                ms.price_unit = ins.Value.price_unit;
                ms.quantity_unit = ins.Value.quantity_unit;
                masterinfos[ins.Key] = ms;
            }
            await ws_server.setMasterInfo(masterinfos);

            Dictionary<string, strategySetting> stgSettings = new Dictionary<string, strategySetting>();
            foreach (var stg in strategies)
            {
                strategySetting setting = new strategySetting();
                setting.name = stg.Value.name;
                setting.baseCcy = stg.Value.baseCcy;
                setting.quoteCcy = stg.Value.quoteCcy;
                setting.taker_market = stg.Value.taker_market.ToString();
                setting.maker_market = stg.Value.maker_market.ToString();
                setting.order_throttle = stg.Value.order_throttle;
                setting.markup = stg.Value.const_markup;
                setting.min_markup = stg.Value.min_markup;
                setting.max_skew = stg.Value.maxSkew;
                setting.skew_widening = stg.Value.skewWidening;
                setting.skew_widening_const = stg.Value.skewWidening_const;
                setting.maxMakerPosition = stg.Value.maxMakerPosition;
                setting.targetMakerPosition = stg.Value.targetMakerPosition;
                setting.ToBsize = stg.Value.ToBsize;
                setting.ToBsizeMultiplier = stg.Value.ToBsizeMultiplier;
                setting.intervalAfterFill = stg.Value.intervalAfterFill;
                setting.modThreshold = stg.Value.modThreshold;
                setting.skewThreshold = stg.Value.skewThreshold;
                setting.oneSideThreshold = stg.Value.oneSideThreshold;
                setting.decaying_time = stg.Value.markup_decay_basetime;
                setting.rv_penalty_multiplier = stg.Value.rv_penalty_multiplier;
                setting.rv_base_param = stg.Value.rv_base_param;
                setting.maxBaseMarkup = stg.Value.max_baseMarkup;
                //setting.predictFill = stg.Value.predictFill;
                setting.skew_type = stg.Value.skew_type.ToString();
                setting.skew_step = stg.Value.skew_step;
                stgSettings[stg.Key] = setting;
            }
            await ws_server.setStrategySetting(stgSettings);
            updateLog();

            if (!await MsgDeliverer.setDiscordToken(discordTokenFile))
            {
                addLog("Message configuration not found", Enums.logType.WARNING);
            }
            updateLog();
            if (!await tradePreparation(live))
            {
                Console.WriteLine("Failed to intialize the platform");
                updateLog();
                foreach (var th in thManager.threads.Values)
                {
                    th.stop();
                }
                return;
            }
            updateLog();
            marketImpactFile = new StreamWriter(new FileStream(outputPath + "/Market_Impact.csv", FileMode.Append, FileAccess.Write));

            ws_server.StartAsync(CancellationToken.None);

            
            if(live)
            {
                addLog("Latency check");

                i = 0;
                int trial = 3;
                Stopwatch sw = new Stopwatch();
                Dictionary<market, double> avgLatency = new Dictionary<market, double>();
                double latency = 0;

                while (i < trial)
                {
                    foreach (var m in qManager._markets)
                    {
                        Thread.Sleep(1000);
                        sw.Start();
                        await crypto_client.getBalance([m.Key]);
                        sw.Stop();
                        latency = sw.Elapsed.TotalNanoseconds / 1000000;
                        sw.Reset();
                        addLog(m.Key + " trial " + i.ToString() + ": " + latency.ToString("N3") + " ms");
                        if (avgLatency.ContainsKey(m.Key))
                        {
                            avgLatency[m.Key] += latency;
                        }
                        else
                        {
                            avgLatency[m.Key] = latency;
                        }
                    }
                    updateLog();
                    ++i;
                }

                foreach (var l in avgLatency)
                {
                    addLog("Average Latency of " + l.Key + ": " + (l.Value / trial).ToString("N3") + " ms");
                }
                updateLog();
                Thread.Sleep(1000);
            }

            if (!test)
            {
                startTrading();
                if (live)
                {
                    addLog("Live Trading");
                }
                else
                {
                    addLog("Simulation Mode");
                }

            }
            else
            {
                addLog("Test Mode");
                Task.Run(testFunc);
            }


            isRunning = true;

            i = 0;
            try
            {
                while (isRunning)
                {
                    sendFills();
                    outputMI();
                    await statusCheck();
                    setInstrumentInfo();
                    setStrategyInfo();
                    broadcastInfos();
                    ThreadPool.GetAvailableThreads(out int worker, out int io);
                    ThreadPool.GetMaxThreads(out int maxWorker, out int maxIo);

                    if (maxWorker - worker > 20)
                    {
                        addLog($"Worker Threads: {maxWorker - worker}/{maxWorker}");
                    }

                    ++i;
                    if (i > 30)
                    {
                        await messagePnL(false);
                        //string msg = messagePnL();
                        //Console.WriteLine(msg);
                        await timer_PeriodicMsg_Tick();
                        i = 0;
                    }
                    updateLog();

                    Thread.Sleep(1000);
                }
            }
            catch(Exception ex)
            {
                addLog("Error occured during the logging process.  Message:" + ex.Message, logType.ERROR);
                if (ex.StackTrace != null)
                {
                    addLog(ex.StackTrace, Enums.logType.WARNING);
                }
            }
            finally
            {
                if(isRunning)
                {
                    isRunning = false;
                }
            }
            
            addLog("Exit the main process...");
            Thread.Sleep(1000);
        }

        static private async Task messagePnL(bool discord = false)
        {
            decimal volumeAll = 0;
            decimal posPnLAll = 0;
            decimal tradingPLAll = 0;
            decimal feeAll = 0;
            decimal totalAll = 0;
            decimal prev_notionalAll = 0;

            decimal feedCountQuoteAll = 0;
            decimal feedCountQuoteLatent = 0;
            decimal feedCountTradeAll = 0;
            decimal feedCountTradeLatent = 0;
            decimal feedCountOrderAll = 0;
            decimal feedCountOrderLatent = 0;
            decimal feedCountFillAll = 0;
            decimal feedCountFillLatent = 0;

            string msg = "";
            bool sendingNotional = false;
            DateTime current = DateTime.UtcNow;
            intradayPnL pnl;
            List<intradayPnL> pnls = new List<intradayPnL>();
            if (intradayPnLTime == null ||  (intradayPnLTime.HasValue && current > intradayPnLTime.Value + TimeSpan.FromMinutes(30)))
            {
                if(current.Minute < 30)
                {
                    intradayPnLTime = new DateTime(current.Year,current.Month, current.Day, current.Hour, 0, 0);
                }
                else
                {
                    intradayPnLTime = new DateTime(current.Year, current.Month, current.Day, current.Hour, 30, 0);
                }
                sendingNotional = true;
            }
            foreach (var stg in strategies.Values)
            {
                if (stg.maker != null && stg.taker != null)
                {
                    msg = "";
                    stg.maker.count_Allquotes = 0;
                    stg.maker.count_Latentquotes = 0;
                    stg.maker.count_AllTrade = 0;
                    stg.maker.count_LatentTrade = 0;
                    stg.maker.count_AllOrderUpdates = 0;
                    stg.maker.count_LatentOrderUpdates = 0;
                    stg.maker.count_AllFill = 0;
                    stg.maker.count_LatentFill = 0;
                    feedCountQuoteAll += stg.maker.cum_Allquotes;
                    feedCountQuoteLatent += stg.maker.cum_Latentquotes;
                    feedCountTradeAll += stg.maker.cum_AllTrade;
                    feedCountTradeLatent += stg.maker.cum_LatentTrade;
                    feedCountOrderAll += stg.maker.cum_AllOrderUpdates;
                    feedCountOrderLatent += stg.maker.cum_LatentOrderUpdates;
                    feedCountFillAll += stg.maker.cum_AllFill;
                    feedCountFillLatent += stg.maker.cum_LatentFill;

                    stg.netExposure = stg.maker.net_pos + stg.taker.net_pos;
                    stg.notionalVolume = stg.maker.my_buy_notional + stg.maker.my_sell_notional;
                    stg.posPnL = stg.SoD_baseCcyPos * (stg.taker.mid - stg.taker.open_mid);
                    stg.tradingPnL = (stg.taker.my_sell_notional - stg.taker.my_sell_quantity * stg.taker.mid) + (stg.taker.my_buy_quantity * stg.taker.mid - stg.taker.my_buy_notional);
                    stg.tradingPnL += (stg.maker.my_sell_notional - stg.maker.my_sell_quantity * stg.taker.mid) + (stg.maker.my_buy_quantity * stg.taker.mid - stg.maker.my_buy_notional);
                    stg.totalFee = stg.taker.base_fee * stg.taker.mid + stg.taker.quote_fee + stg.maker.base_fee * stg.taker.mid + stg.maker.quote_fee;
                    stg.totalPnL = stg.posPnL + stg.tradingPnL - stg.totalFee;

                    msg += DateTime.UtcNow.ToString() + " - Strategy " + stg.name + " -    \nNotional Volume:" + stg.notionalVolume.ToString("N2") + "\nNet Exposure:" + stg.netExposure.ToString("N" + stg.maker.quantity_scale) + "    [Maker Balance:" + stg.maker.net_pos.ToString("N" + stg.maker.quantity_scale) + "]\nPosition PnL:" + stg.posPnL.ToString("N2") + "\nTrading PnL:" + stg.tradingPnL.ToString("N2") + "\nFee:" + stg.totalFee.ToString("N2") + "\nTotal:" + stg.totalPnL.ToString("N2") + "\n";

                    pnl = new intradayPnL();
                    pnl.strategy_name = stg.name;
                    pnl.OADatetime = current.ToOADate();
                    pnl.PnL = (double)stg.totalPnL;
                    pnl.notionalVolume = (double)stg.notionalVolume;
                    pnls.Add(pnl);
                    volumeAll += stg.notionalVolume;
                    posPnLAll += stg.posPnL;
                    tradingPLAll += stg.tradingPnL;
                    feeAll += stg.totalFee;
                    totalAll += stg.totalPnL;
                    msg += "markup_bid:" + stg.temp_markup_bid.ToString("N2") + "  markup_ask:" + stg.temp_markup_ask.ToString("N2") + "   Base markup:" + stg.base_markup.ToString("N2") + "   Markup decay:" + stg.markup_decay.ToString("N2") + "\n";

                    if(stg.multiLayer_strategy)
                    {
                        string ord_id;
                        DataSpotOrderUpdate ord;
                        using (var ulock = stg.updating.getlock())
                        {
                            using (var olock = oManager.order_lock.getlock())
                            {
                                string sells = "";
                                string buys = "";
                                for (int i = 0; i < stg.layers; ++i)
                                {

                                    ord_id = stg.live_sellorders[stg.layers - 1 - i];
                                    if (oManager.orders.ContainsKey(ord_id))
                                    {
                                        ord = oManager.orders[ord_id];
                                        sells += "Layer " + (stg.layers - 1 - i).ToString() + " " + ord.status.ToString() + "  " + ord.order_quantity.ToString() + "@" + ord.order_price.ToString("N" + stg.maker.price_scale) + "\n";
                                    }
                                    else
                                    {
                                        sells += "Layer " + (stg.layers - 1 - i).ToString() + " " + ord_id + " Not Found\n";
                                    }

                                    ord_id = stg.live_buyorders[i];
                                    if (oManager.orders.ContainsKey(ord_id))
                                    {
                                        ord = oManager.orders[ord_id];
                                        buys += "Layer " + (i).ToString() + " " + ord.status.ToString() + "  " + ord.order_quantity.ToString() + "@" + ord.order_price.ToString("N" + stg.maker.price_scale) + "\n";
                                    }
                                    else
                                    {
                                        buys += "Layer " + (i).ToString() + " " + ord_id + " Not Found\n";
                                    }
                                }

                                msg += "Live Orders [Sell]\n" + sells + "Live Orders [Buy]\n" + buys;
                            }
                        }
                    }
                    else
                    {

                    }
                    Console.WriteLine(msg);
                    if(discord)
                    {
                        string err = "";
                        err = await MsgDeliverer.sendMessage(msg, msg_type);
                        if(err != "")
                        {
                            addLog(err, logType.WARNING);
                        }
                    }
                }
            }

            msg = "";
            string latency_msg = "Latent messages.\n";
            latency_msg += "<QuotesUpdate> All count:" + feedCountQuoteAll.ToString("N0") + "  Latent Feed:" + feedCountQuoteLatent.ToString("N0") + "\n";
            latency_msg += "<Trades> All count:" + feedCountTradeAll.ToString("N0") + "  Latent Feed:" + feedCountTradeLatent.ToString("N0") + "\n";
            latency_msg += "<OrderUpdate> All count:" + feedCountOrderAll.ToString("N0") + "  Latent Feed:" + feedCountOrderLatent.ToString("N0") + "\n";
            latency_msg += "<Fill> All count:" + feedCountFillAll.ToString("N0") + "  Latent Feed:" + feedCountFillLatent.ToString("N0") + "\n";
            using (var olock = oManager.order_lock.getlock())
            {
                latency_msg += "Live Order Count:" + oManager.live_orders.Count.ToString() + "\n";
                foreach (var o in oManager.live_orders.Values)
                {
                    latency_msg += o.internal_order_id + " " + o.symbol_market + " " + o.position_side.ToString() + " " + o.side.ToString() + " " + o.order_quantity.ToString() + "@" + o.order_price.ToString() + "\n";
                }
            }
            
            pnl = new intradayPnL();
            pnl.strategy_name = "Total";
            pnl.OADatetime = current.ToOADate();
            pnl.PnL = (double)totalAll;
            pnl.notionalVolume = (double)volumeAll;
            pnls.Add(pnl);
            if (pnls.Count > 0)
            {
                ws_server.processIntradayPnL(pnls);
            }
            msg += DateTime.UtcNow.ToString() + " - All -    \nNotional Volume:" + volumeAll.ToString("N2") + "\nPosition PnL:" + posPnLAll.ToString("N2") + "\nTrading PnL:" + tradingPLAll.ToString("N2") + "\nFee:" + feeAll.ToString("N2") + "\nTotal:" + totalAll.ToString("N2") + "\n";

            msg = latency_msg + "\n" + msg;
            Console.WriteLine(msg);
            if(discord)
            {
                string err = "";
                err = await MsgDeliverer.sendMessage(msg, msg_type);
                if (err != "")
                {
                    addLog(err, logType.WARNING);
                }
            }
            //return msg;
        }

        static private async Task testFunc()
        {
            Console.WriteLine("test GMO coin getTicker");

            await crypto_client.getCurrentMid(market.gmocoin, "");

            await EoDProcess();
            isRunning = false;
        }


        static private async Task sendFills()
        {
            DataFill fill;
            fillInfo fInfo;
            while (filledOrderQueue.Count > 0)
            {
                fill = filledOrderQueue.Dequeue();
                if(fill != null)
                {
                    if (fillInfoStack.Count > 0)
                    {
                        fInfo = fillInfoStack.pop();
                    }
                    else
                    {
                        fInfo = new fillInfo();
                    }
                    if (qManager.instruments.ContainsKey(fill.symbol_market))
                    {
                        Instrument ins = qManager.instruments[fill.symbol_market];
                        if (fill.timestamp != null)
                        {
                            fInfo.timestamp = ((DateTime)fill.timestamp).ToString("HH:mm:ss.fff");
                        }
                        else
                        {
                            fInfo.timestamp = "";
                        }
                        fInfo.market = fill.market.ToString();
                        fInfo.symbol = fill.symbol;
                        fInfo.side = fill.side.ToString();
                        fInfo.fill_price = fill.price.ToString("N" + ins.price_scale);
                        fInfo.quantity = fill.quantity.ToString("N" + ins.quantity_scale);
                        fInfo.fee = (fill.fee_quote + fill.fee_base * fill.price).ToString();
                        fInfo.msg = fill.msg;
                        ws_server.processFill(fInfo);
                        fill.init();
                        oManager.pushbackFill(fill);
                    }
                }
                else
                {
                    break;
                }
            }
        }

        static private bool readConfig()
        {
            string fileContent;

            if (File.Exists(defaultConfigPath))
            {
                Console.WriteLine("Reading config file " + defaultConfigPath);
                fileContent = File.ReadAllText(defaultConfigPath);
            }
            else
            {
                addLog("Config file doesn't exist. path:" + defaultConfigPath, Enums.logType.ERROR);
                return false;
            }
            using JsonDocument doc = JsonDocument.Parse(fileContent);
            var root = doc.RootElement;
            JsonElement elem;
            if (root.TryGetProperty("live", out elem))
            {
                live = elem.GetBoolean();
                
            }
            else
            {
                live = false;
            }
            if (root.TryGetProperty("test", out elem))
            {
                test = elem.GetBoolean();
            }
            else
            {
                test = false;
            }
            if (root.TryGetProperty("privateConnect", out elem))
            {
                if (!live)
                {
                    privateConnect = elem.GetBoolean();
                }
            }
            else
            {
                privateConnect = true;
            }
            if (root.TryGetProperty("reconnectionPeriod", out elem))
            {
                reconnectionPeriod = elem.GetDouble();
            }
            else
            {
                reconnectionPeriod = 4;
            }
            if (root.TryGetProperty("setRealPos", out elem))
            {
                if (!live)
                {
                    setRealPos = elem.GetBoolean();
                }
            }
            else
            {
                setRealPos = true;
            }
            if (root.TryGetProperty("msgLogging", out elem))
            {
                msgLogging = elem.GetBoolean();
            }
            else
            {
                msgLogging = false;
            }
            if (root.TryGetProperty("logPublic", out elem))
            {
                log_public = elem.GetBoolean();
            }
            else
            {
                log_public = false;
            }
            if (root.TryGetProperty("endTime", out elem))
            {
                str_endTime = elem.GetString();
                TimeSpan timeOfDay = TimeSpan.ParseExact(str_endTime, "hh\\:mm\\:ss", null);

                endTime = DateTime.UtcNow.Date.Add(timeOfDay);
                if (DateTime.UtcNow >= endTime)
                {
                    endTime = endTime.AddDays(1);
                }
            }
            else
            {
                TimeSpan timeOfDay = TimeSpan.ParseExact(str_endTime, "hh\\:mm\\:ss", null);

                endTime = DateTime.UtcNow.Date.Add(timeOfDay);
                if (DateTime.UtcNow >= endTime)
                {
                    endTime = endTime.AddDays(1);
                }
            }
            if(root.TryGetProperty("APIEnvList",out elem))
            {
                foreach(var env_name in elem.EnumerateArray())
                {
                    APIList.Add(env_name.GetString());
                }
                
            }
            if (root.TryGetProperty("masterFile", out elem))
            {
                masterFile = elem.GetString();
            }
            else
            {
                addLog("Master file path is not configured.", Enums.logType.ERROR);
                return false;
            }
            if (root.TryGetProperty("discordTokenFile", out elem))
            {
                discordTokenFile = elem.GetString();
            }
            else
            {
                addLog("Message destination is not configured.", Enums.logType.WARNING);
            }
            if (root.TryGetProperty("outputPath", out elem))
            {
                outputPath = elem.GetString();
                outputPath_org = outputPath;
            }
            else
            {
                addLog("Output path is not configured.", Enums.logType.WARNING);
                addLog("The output files will be exported to the current path.", Enums.logType.WARNING);
            }
            if (root.TryGetProperty("logFile", out elem))
            {
                logPath = elem.GetString();
            }
            if (root.TryGetProperty("strategyFile", out elem))
            {
                strategyFile = elem.GetString();
            }
            else
            {
                addLog("strategyFile is not configured.", Enums.logType.WARNING);
                addLog("Any strategies won't be run.", Enums.logType.WARNING);
            }
            if (root.TryGetProperty("balanceFile", out elem))
            {
                virtualBalanceFile = elem.GetString();
            }
            else
            {
                addLog("Balance file is not configured.", Enums.logType.WARNING);
                addLog("The virtual balance will be all 0.", Enums.logType.WARNING);
            }
            if (root.TryGetProperty("latency", out elem))
            {
                oManager.latency = elem.GetInt32();
            }

            if(live)
            {
                msg_type = msgType.NOTIFICATION;
                err_msg_type = msgType.ERROR;
            }
            else
            {
                msg_type = msgType.TEST;
                err_msg_type = msgType.TEST;
            }
            string dt = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string newpath = outputPath + "/" + dt;
            if (!Directory.Exists(newpath))
            {
                Directory.CreateDirectory(newpath);
            }
            outputPath = newpath;
            logPath = outputPath + "/crypto_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".log";
            intradayPnLFile = outputPath + "/Intraday_PnL.csv";

            return true;

        }
        static private void readAPIFiles(string path)
        {
            if (Directory.Exists(path))
            {
                string[] files = Directory.GetFiles(path, "*.json");

                foreach (string file in files)
                {
                    addLog("API File:" + file);
                    Console.WriteLine(file);
                    crypto_client.readCredentials(file);
                }
            }
        }

        static private void getAPIsFromEnv(bool tradable)
        {
            Dictionary<string, string> APIs = new Dictionary<string, string>();
            List<string> mkts = new List<string>();
            string tradeState;

            System.Collections.IDictionary dict = Environment.GetEnvironmentVariables();
            if (tradable)
            {
                tradeState = "TRADABLE";
            }
            else
            {
                tradeState = "VIEWONLY";
            }

            if (APIList.Count() == 0)
            {
                foreach (var mkt in qManager._markets.Keys)
                {
                    APIList.Add(mkt.ToString().ToUpper() + "_" + tradeState);
                }
            }

            foreach (string env_name in APIList)
            {
                string env_name_all = env_name + "_KEY";
                string api_content = Environment.GetEnvironmentVariable(env_name_all);
                if (string.IsNullOrEmpty(api_content))
                {
                    addLog("API not found. env_name:" + env_name_all, Enums.logType.ERROR);
                }
                else
                {
                    string[] names = env_name_all.Split('_');
                    if (!mkts.Contains(names[0].ToLower()))
                    {
                        mkts.Add(names[0].ToLower());
                    }
                    APIs[env_name_all] = api_content.Trim('\"');
                }
                env_name_all = env_name + "_NAME";
                api_content = Environment.GetEnvironmentVariable(env_name_all);
                if (string.IsNullOrEmpty(api_content))
                {
                    addLog("API not found. env_name:" + env_name_all, Enums.logType.ERROR);
                }
                else
                {
                    string[] names = env_name_all.Split('_');
                    if (!mkts.Contains(names[0].ToLower()))
                    {
                        mkts.Add(names[0].ToLower());
                    }
                    APIs[env_name_all] = api_content.Trim('\"');
                }
            }

            foreach (string mkt in mkts)
            {
                string env_name = mkt.ToUpper() + "_" + tradeState;
                string api_name = "";
                string api_key = "";
                if (APIs.ContainsKey(env_name + "_NAME"))
                {
                    api_name = APIs[env_name + "_NAME"];
                }
                else
                {
                    addLog("The API name for " + mkt + "is not found", Enums.logType.ERROR);
                }
                if (APIs.ContainsKey(env_name + "_KEY"))
                {
                    api_key = APIs[env_name + "_KEY"];
                }
                else
                {
                    addLog("The API secret key for " + mkt + "is not found", Enums.logType.ERROR);
                }
                crypto_client.setCredentials((market)Enum.Parse(typeof(market),mkt), api_name, api_key);
            }
        }
        static private void setStrategies(string strategyFile)
        {
            if (File.Exists(strategyFile))
            {
                string fileContent = File.ReadAllText(strategyFile);
                using JsonDocument doc = JsonDocument.Parse(fileContent);
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    Strategy stg = new Strategy();
                    stg._addLog = addLog;
                    stg.setStrategy(elem);
                    stg.maker = qManager.getInstrument(stg.baseCcy, stg.quoteCcy, stg.maker_market);
                    stg.taker = qManager.getInstrument(stg.baseCcy, stg.quoteCcy, stg.taker_market);
                    stg.maker.ToBsize = stg.ToBsize;
                    stg.taker.ToBsize = stg.ToBsize;
                    strategies[stg.name] = stg;
                    strategyInfo stginfo = new strategyInfo();
                    stginfo.baseCcy = stg.baseCcy;
                    stginfo.quoteCcy = stg.quoteCcy;
                    stginfo.maker_market = stg.maker_market.ToString();
                    stginfo.taker_market = stg.taker_market.ToString();
                    stginfo.maker_symbol_market = stg.maker_symbol_market;
                    stginfo.taker_symbol_market = stg.taker_symbol_market;
                    stginfo.name = stg.name;
                    stginfo.market_impact_curve = new Dictionary<double, decimal>();
                    foreach(var d in GlobalVariables.MI_period)
                    {
                        stginfo.market_impact_curve[d] = 0;
                    }
                    strategyInfos[stg.name] = stginfo;
                    //stg = stg;
                }
            }

        }

        static private async Task<bool> tradePreparation(bool liveTrading)
        {
            try
            {
                oManager.setVirtualMode(!liveTrading);
                qManager.live = liveTrading;
                foreach (var mkt in qManager._markets)
                {
                    if (msgLogging)
                    {
                        Func<Action, Action, CancellationToken, int, Task<bool>>?  func = crypto_client.setMsgLogging(mkt.Key, outputPath,log_public);
                        if(func != null)
                        {
                            thManager.addThread(mkt.Key + "_msgLogging", func, null, null, 1);
                        }
                    }
                    if(!await qManager.connectPublicChannel(mkt.Key))
                    {
                        addLog("Failed to login public " + mkt.Key, logType.WARNING);
                        return false;
                    }
                    if (liveTrading || privateConnect)
                    {
                        if(!await oManager.connectPrivateChannel(mkt.Key))
                        {
                            addLog("Failed to login private " + mkt.Key, logType.WARNING);
                            return false;
                        }
                    }
                    connectionStates[mkt.Key.ToString()] = new connecitonStatus() { market = mkt.Key.ToString(), publicState = WebSocketState.None.ToString(), privateState = WebSocketState.None.ToString(), avgRTT = 0.0 };
                }
                lastConnectedTime = DateTime.UtcNow;

                updateLog();

                foreach (var ins in qManager.instruments.Values)
                {
                    market[] markets = [ins.market];
                    //if (ins.market == Exchange.Bybit)
                    //{
                    //    await crypto_client.subscribeBybitOrderBook(ins.baseCcy, ins.quoteCcy);
                    //}
                    //else if (ins.market == Exchange.Coinbase)
                    //{
                    //    await crypto_client.subscribeCoinbaseOrderBook(ins.baseCcy, ins.quoteCcy);
                    //}
                    //else
                    //{
                    //    await crypto_client.subscribeOrderBook(markets, ins.baseCcy, ins.quoteCcy);
                    //}
                    await crypto_client.subscribeOrderBook(markets, ins.baseCcy, ins.quoteCcy);
                    await crypto_client.subscribeTrades(markets, ins.baseCcy, ins.quoteCcy);
                }

                updateLog();

                if (oManager.getVirtualMode())
                {
                    string sodpos_filename = outputPath + "/SoD_Position_new.csv";
                    if (privateConnect || setRealPos)
                    {
                        if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys))
                        {
                            addLog("Failed to set balance", logType.WARNING);
                            return false;
                        }
                        if (!qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys))
                        {
                            addLog("Failed to set margion position", logType.WARNING);
                            return false;
                        }
                        foreach(var ex in qManager.exchanges.Values)
                        {
                            foreach(var b in ex.balance.Values)
                            {
                                b.inuse = 0;
                            }
                            foreach(var mb in ex.marginLong.Values)
                            {
                                mb.inuse = 0;
                            }
                            foreach(var mb in ex.marginShort.Values)
                            {
                                mb.inuse = 0;
                            }
                        }

                        foreach(var ex in qManager.exchanges)
                        {
                            Crypto_Trading.Exchange e_sod;
                            if (!qManager.exchanges_SoD.ContainsKey(ex.Key))
                            {
                                e_sod = new Crypto_Trading.Exchange();
                                e_sod.market = ex.Key;
                                qManager.exchanges_SoD[ex.Key] = e_sod;
                            }
                            else
                            {
                                e_sod = qManager.exchanges_SoD[ex.Key];
                            }
                            foreach(var b in ex.Value.balance)
                            {
                                Balance b_sod;
                                if(!e_sod.balance.ContainsKey(b.Key))
                                {
                                    b_sod = new Balance();
                                    e_sod.balance[b.Key] = b_sod;
                                }
                                else
                                {
                                    b_sod = e_sod.balance[b.Key];
                                }
                                b_sod.ccy = b.Value.ccy;
                                b_sod.market = b.Value.market;
                                b_sod.current_price = b.Value.current_price;
                                b_sod.valuation_pair = b.Value.valuation_pair;
                                b_sod.total = b.Value.total;
                            }
                            foreach(var l in ex.Value.marginLong)
                            {
                                BalanceMargin l_sod;
                                if(!e_sod.marginLong.ContainsKey(l.Key))
                                {
                                    l_sod = new BalanceMargin();
                                    e_sod.marginLong[l.Key] = l_sod;
                                }
                                else
                                {
                                    l_sod = e_sod.marginLong[l.Key];
                                }
                                l_sod.symbol = l.Value.symbol;
                                l_sod.market = l.Value.market;
                                l_sod.side = l.Value.side;
                                l_sod.total = l.Value.total;
                                l_sod.avg_price = l.Value.avg_price;
                                l_sod.unrealized_fee = l.Value.unrealized_fee;
                                l_sod.unrealized_interest = l.Value.unrealized_interest;
                                l_sod.unrealized_pnl = l.Value.unrealized_pnl;
                                l_sod.current_price = l.Value.current_price;
                                l_sod.leverage = l.Value.leverage;
                            }
                            foreach (var s in ex.Value.marginShort)
                            {
                                BalanceMargin s_sod;
                                if (!e_sod.marginShort.ContainsKey(s.Key))
                                {
                                    s_sod = new BalanceMargin();
                                    e_sod.marginShort[s.Key] = s_sod;
                                }
                                else
                                {
                                    s_sod = e_sod.marginShort[s.Key];
                                }
                                s_sod.symbol = s.Value.symbol;
                                s_sod.market = s.Value.market;
                                s_sod.side = s.Value.side;
                                s_sod.total = s.Value.total;
                                s_sod.avg_price = s.Value.avg_price;
                                s_sod.unrealized_fee = s.Value.unrealized_fee;
                                s_sod.unrealized_interest = s.Value.unrealized_interest;
                                s_sod.unrealized_pnl = s.Value.unrealized_pnl;
                                s_sod.current_price = s.Value.current_price;
                                s_sod.leverage = s.Value.leverage;
                            }
                        }

                        foreach (var ex in qManager.exchanges.Values)
                        {
                            ex.setInstruments(qManager.instruments);
                        }
                        foreach (var ex in qManager.exchanges_SoD.Values)
                        {
                            ex.setInstruments(qManager.instruments);
                        }
                    }
                    //else if(File.Exists(sodpos_filename))
                    //{
                    //    readSoDFile(sodpos_filename);
                    //    foreach(var ex in qManager.exchanges.Values)
                    //    {
                    //        switch(ex.market)
                    //        {
                    //            case market.bitbank:
                    //                foreach (var pos in ex.balance)
                    //                {
                    //                    if(pos.Key == "JPY")
                    //                    {
                    //                        ex.marginTotal += pos.Value.total;
                    //                    }
                    //                    else
                    //                    {
                    //                        ex.marginTotal += pos.Value.total * pos.Value.current_price / 2;
                    //                    }
                    //                }
                    //                break;
                    //            default:
                    //                foreach (var pos in ex.balance)
                    //                {
                    //                    if (pos.Key == "JPY")
                    //                    {
                    //                        ex.marginTotal += pos.Value.total;
                    //                    }
                    //                }
                    //                break;
                    //        }
                    //    }
                    //}
                    else
                    {
                        foreach (var stg in strategies)
                        {
                            Instrument takerins = stg.Value.taker;
                            Instrument makerins = stg.Value.maker;
                            Crypto_Trading.Exchange takerExchange;
                            Crypto_Trading.Exchange makerExchange;
                            if (!qManager.exchanges.ContainsKey(takerins.market))
                            {
                                takerExchange = new Crypto_Trading.Exchange();
                                takerExchange.market = takerins.market;
                                Balance jpy_balance = new Balance();
                                jpy_balance.market = takerExchange.market;
                                jpy_balance.ccy = "JPY";
                                jpy_balance.total = 3_000_000;
                                takerExchange.balance["JPY"] = jpy_balance;
                                takerExchange.marginTotal += jpy_balance.total;
                                qManager.exchanges[takerExchange.market] = takerExchange;
                            }
                            else
                            {
                                takerExchange = qManager.exchanges[takerins.market];
                            }
                            if (!qManager.exchanges.ContainsKey(makerins.market))
                            {
                                makerExchange = new Crypto_Trading.Exchange();
                                makerExchange.market = makerins.market;
                                Balance jpy_balance = new Balance();
                                jpy_balance.market = makerExchange.market;
                                jpy_balance.ccy = "JPY";
                                jpy_balance.total = 3_000_000;
                                makerExchange.balance["JPY"] = jpy_balance;
                                makerExchange.marginTotal += jpy_balance.total;
                                qManager.exchanges[makerExchange.market] = makerExchange;
                            }
                            else
                            {
                                makerExchange = qManager.exchanges[makerins.market];
                            }

                            if (takerins.quoteCcy == "JPY")
                            {
                                takerins.quoteBalance = takerExchange.balance["JPY"];
                            }
                            if (takerins.baseCcy == "JPY")
                            {
                                takerins.baseBalance = takerExchange.balance["JPY"];
                            }
                            if (makerins.quoteCcy == "JPY")
                            {
                                makerins.quoteBalance = makerExchange.balance["JPY"];
                            }
                            if (makerins.baseCcy == "JPY")
                            {
                                makerins.baseBalance = makerExchange.balance["JPY"];
                            }

                            if (!takerExchange.balance.ContainsKey(takerins.baseCcy))
                            {
                                takerExchange.balance[takerins.baseCcy] = takerins.baseBalance;
                            }

                            if (takerins.marginTrade)
                            {
                                if(!takerExchange.marginLong.ContainsKey(takerins.symbol_market))
                                {
                                    takerExchange.marginLong[takerins.symbol_market] = takerins.longPosition;
                                }
                                if (!takerExchange.marginShort.ContainsKey(takerins.symbol_market))
                                {
                                    takerExchange.marginShort[takerins.symbol_market] = takerins.shortPosition;
                                }

                                takerins.baseBalance.total = 0;// stg.Value.baseCcyQuantity / 2;
                                if (stg.Value.targetMakerPosition > 0)
                                {
                                    takerins.shortPosition.total = stg.Value.targetMakerPosition;
                                }
                                else if (stg.Value.targetMakerPosition < 0)
                                {
                                    if (takerins.marginLong)
                                    {
                                        takerins.longPosition.total = - stg.Value.targetMakerPosition;

                                    }
                                    else
                                    {
                                        takerins.baseBalance.total = - stg.Value.targetMakerPosition;
                                    }
                                }
                                takerins.longPosition.symbol = takerins.symbol;
                                takerins.longPosition.market = takerins.market;
                                takerins.longPosition.leverage = takerins.leverage;
                                takerins.shortPosition.symbol = takerins.symbol;
                                takerins.shortPosition.market = takerins.market;
                                takerins.shortPosition.leverage = takerins.leverage;
                            }
                            else
                            {
                                if(stg.Value.targetMakerPosition < 0)
                                {
                                    takerins.baseBalance.total = - stg.Value.targetMakerPosition;
                                }
                                else
                                {
                                    takerins.baseBalance.total = stg.Value.targetMakerPosition;
                                }

                            }

                            if (!makerExchange.balance.ContainsKey(makerins.baseCcy))
                            {
                                makerExchange.balance[makerins.baseCcy] = makerins.baseBalance;
                            }

                            if (makerins.marginTrade)
                            {
                                if(!makerExchange.marginLong.ContainsKey(makerins.symbol))
                                {
                                    makerExchange.marginLong[makerins.symbol_market] = makerins.longPosition;
                                }
                                if (!makerExchange.marginShort.ContainsKey(makerins.symbol))
                                {
                                    makerExchange.marginShort[makerins.symbol_market] = makerins.shortPosition;
                                }

                                makerins.baseBalance.total = 0;// stg.Value.baseCcyQuantity / 2;
                                if(stg.Value.targetMakerPosition > 0)
                                {
                                    if (makerins.marginLong)
                                    {
                                        makerins.longPosition.total = stg.Value.targetMakerPosition;
                                    }
                                    else
                                    {
                                        makerins.baseBalance.total = stg.Value.targetMakerPosition;
                                    }
                                }
                                else if(stg.Value.targetMakerPosition < 0)
                                {
                                    makerins.shortPosition.total = - stg.Value.targetMakerPosition;
                                }
                                makerins.longPosition.symbol = makerins.symbol;
                                makerins.longPosition.market = makerins.market;
                                makerins.longPosition.leverage = makerins.leverage;
                                makerins.shortPosition.symbol = makerins.symbol;
                                makerins.shortPosition.market = makerins.market;
                                makerins.shortPosition.leverage = makerins.leverage;
                            }
                            else
                            {
                                if(!makerExchange.marginShort.ContainsKey(makerins.symbol_market))
                                {
                                    makerExchange.marginShort[makerins.symbol_market] = makerins.shortPosition;
                                }
                                makerins.baseBalance.total = stg.Value.targetMakerPosition;
                                makerins.shortPosition.total = 2 * stg.Value.targetMakerPosition;
                                makerins.shortPosition.symbol = makerins.symbol;
                                makerins.shortPosition.market = makerins.market;
                                makerins.shortPosition.leverage = makerins.leverage;
                            }
                        }
                        foreach (var ins in qManager.instruments.Values)
                        {

                            decimal mid = await crypto_client.getCurrentMid(ins.market, ins.symbol);
                            ins.SoD_baseBalance.total = ins.baseBalance.total;
                            ins.SoD_baseBalance.ccy = ins.baseBalance.ccy;
                            ins.SoD_baseBalance.market = ins.baseBalance.market;
                            ins.SoD_quoteBalance.total = ins.quoteBalance.total;
                            ins.SoD_quoteBalance.ccy = ins.quoteBalance.ccy;
                            ins.SoD_quoteBalance.market = ins.quoteBalance.market;
                            if(ins.shortPosition.total > 0)
                            {
                                ins.shortPosition.avg_price = mid;
                            }
                            if(ins.longPosition.total > 0 )
                            {
                                ins.longPosition.avg_price = mid;
                            }
                            ins.shortPosition.unrealized_fee = - ins.shortPosition.total * ins.shortPosition.avg_price * ins.maker_fee;
                            ins.longPosition.unrealized_fee = - ins.longPosition.total * ins.longPosition.avg_price * ins.maker_fee;
                            ins.shortPosition.unrealized_interest = 0;//test
                            ins.SoD_longPosition.copy(ins.longPosition);
                            ins.SoD_shortPosition.copy(ins.shortPosition);
                            ins.open_mid = mid;
                        }

                        foreach (var ex in qManager.exchanges.Values)
                        {
                            ex.setInstruments(qManager.instruments);
                        }
                        foreach (var ex in qManager.exchanges_SoD.Values)
                        {
                            ex.setInstruments(qManager.instruments);
                        }
                    }
                }
                else
                {
                    bool ret;
                    ret = await setRealPosition();
                    if(!ret)
                    {
                        addLog("Failed to set real position", logType.WARNING);
                        return false;
                    }
                }

                oManager.exchanges = qManager.exchanges;
                oManager.exchanges_SoD = qManager.exchanges_SoD;
                oManager.balances = qManager.balances;

                foreach(var stg in strategies.Values)
                {
                    if(qManager.exchanges.ContainsKey(stg.maker_market))
                    {
                        stg.maker_exchange = qManager.exchanges[stg.maker_market];
                    }
                    if (qManager.exchanges.ContainsKey(stg.taker_market))
                    {
                        stg.taker_exchange = qManager.exchanges[stg.taker_market];
                    }
                }

                qManager.ready = true;

                updateLog();

                if (live)
                {
                    if (File.Exists(intradayPnLFile))
                    {
                        List<intradayPnL> pnls = new List<intradayPnL>();
                        using (StreamReader sr = new StreamReader(new FileStream(intradayPnLFile, FileMode.Open, FileAccess.Read)))
                        {
                            while (sr.ReadLine() is string line)
                            {
                                string[] items = line.Split(',');//name,oadatetime,pnl,notional
                                if (items.Length >= 4)
                                {
                                    intradayPnL pnl = new intradayPnL();
                                    pnl.strategy_name = items[0];
                                    pnl.OADatetime = double.Parse(items[1]);
                                    pnl.PnL = double.Parse(items[2]);
                                    pnl.notionalVolume = double.Parse(items[3]);
                                    intradayPnLTime = DateTime.FromOADate(pnl.OADatetime);
                                    pnls.Add(pnl);
                                }
                            }
                        }
                        if (pnls.Count > 0)
                        {
                            ws_server.processIntradayPnL(pnls);
                        }
                    }
                    else
                    {
                        addLog("Intraday PnL file not found");
                    }
                }

                string mifile = outputPath + "/Market_Impact.csv";
                readMIFile(mifile);
                string tptFile = outputPath + "/TradePerTrade.csv";
                readTradePerTrade(tptFile);

                if (liveTrading || privateConnect)
                {
                    await crypto_client.subscribeSpotOrderUpdates(qManager._markets.Keys);
                }

                updateLog();

                oManager.ready = true;

                thManager.addThread("updateQuotes", qManager.updateQuotes, qManager.updateQuotesOnClosing, qManager.updateQuotesOnError, 10000);
                thManager.addThread("updateTrades", qManager.updateTrades, qManager.updateTradesOnClosing, qManager.updateTradesOnClosing, 10000);
                thManager.addThread("updateOrders", oManager.updateOrders, oManager.updateOrdersOnClosing, oManager.updateOrdersOnError,10000);
                thManager.addThread("updateFill", oManager.updateFills, oManager.updateFillOnClosing, null, 0);
                thManager.addThread("optimize", qManager.optimize, qManager.optimizeOnClosing, qManager.optimizeOnError,1000);
                thManager.addThread("updateMI", oManager.updateMarketImpact, oManager.updateMarketImpactOnClosing, null, 0);
                thManager.addThread("orderLogging", oManager.orderLogging, oManager.ordLoggingOnClosing, oManager.ordLoggingOnError,1);

                foreach(var th in thManager.threads)
                {
                    threadStates[th.Key] = new threadStatus() { name = th.Key, isRunning = false };
                }
                queueInfos["updateQuotes"] = new queueInfo() { name = "updateQuotes", count = 0};
                queueInfos["updateTrades"] = new queueInfo() { name = "updateTrades", count = 0 };
                queueInfos["updateOrders"] = new queueInfo() { name = "updateOrders", count = 0 };
                queueInfos["updateFills"] = new queueInfo() { name = "updateFills", count = 0 };
                queueInfos["optimize"] = new queueInfo() { name = "optimize", count = 0 };

                updateLog();

                threadsStarted = true;

                if(liveTrading)
                {
                    List<DataSpotOrderUpdate> activeOrders;
                    addLog("Checking active orders...");
                    foreach (var mkt in qManager._markets.Keys)
                    {
                        activeOrders = await crypto_client.getActiveOrders(mkt);
                        if (activeOrders.Count > 0)
                        {
                            Dictionary<string, List<string>> activeOrder_ids = new Dictionary<string, List<string>>();
                            foreach (var order in activeOrders)
                            {
                                string symbol = order.symbol;
                                if (activeOrder_ids.ContainsKey(symbol))
                                {
                                    activeOrder_ids[symbol].Add(order.order_id);
                                }
                                else
                                {
                                    activeOrder_ids[symbol] = new List<string>();
                                    activeOrder_ids[symbol].Add(order.order_id);
                                }
                            }
                            foreach (var ids in activeOrder_ids)
                            {

                                if (qManager.instruments.ContainsKey(ids.Key))
                                {
                                    Instrument ins = qManager.instruments[ids.Key];
                                    addLog("Cancelling " + ids.Value.Count().ToString() + " orders of " + ids.Key);
                                    await oManager.placeCancelSpotOrders(ins, ids.Value, true, true);
                                }
                            }
                        }
                    }

                    updateLog();

                    addLog("Checking balance...");
                    
                    foreach (var stg in strategies.Values)
                    {

                        //stg.SoD_baseCcyPos = (stg.maker.SoD_baseBalance.total + stg.taker.SoD_baseBalance.total) - stg.baseCcyQuantity;
                        stg.SoD_baseCcyPos = stg.maker.SoD_net_pos + stg.taker.SoD_net_pos;
                        addLog("SoD Balance strategy " + stg.name + "  Balance:" + stg.SoD_baseCcyPos.ToString());
                        //decimal baseBalance_diff = stg.baseCcyQuantity - (stg.maker.baseBalance.total + stg.taker.baseBalance.total);
                        //decimal baseBalance_diff = - (stg.maker.net_pos + stg.taker.baseBalance.total);
                        decimal baseBalance_diff = -(stg.maker.net_pos + stg.taker.net_pos);
                        //stg.SoD_baseCcyPos = - baseBalance_diff;
                        orderSide side = orderSide.Buy;
                        if (baseBalance_diff < 0)
                        {
                            baseBalance_diff *= -1;
                            side = orderSide.Sell;
                        }
                        baseBalance_diff = Math.Round(baseBalance_diff / stg.taker.quantity_unit) * stg.taker.quantity_unit;
                        stg.lastPosAdjustment = DateTime.UtcNow;
                        addLog("The current balance of " + stg.name + " BaseCcy:" + (stg.maker.net_pos + stg.taker.net_pos).ToString() + " QuoteCcy:" + (stg.maker.quoteBalance.total + stg.taker.quoteBalance.total).ToString());
                        await stg.adjustPosition();
                    }
                }

                updateLog();

                //Latency List
                foreach (var l in oManager.Latency)
                {
                    LatencyList[l.Key] = l.Value;
                }
                foreach (var l in qManager.Latency)
                {
                    LatencyList[l.Key] = l.Value;
                }
                foreach (var l in thManager.Latency)
                {
                    LatencyList[l.Key] = l.Value;
                }
                foreach(var stg in strategies.Values)
                {
                    foreach(var l in stg.Latency)
                    {
                        LatencyList[l.Key] = l.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                addLog("An error occured while initializing the platforms.", Enums.logType.ERROR);
                addLog(ex.Message, Enums.logType.ERROR);
                Console.WriteLine("An error occured while initializing the platforms.");
                Console.WriteLine(ex.Message);
                if (ex.StackTrace != null)
                {
                    Console.WriteLine(ex.StackTrace, logType.ERROR);
                }
                return false;
            }

            return true;
        }

        static private async Task<bool> setRealPosition()
        {
            string NewSoDPosFile = outputPath + "/SoD_Position_new.csv";
            if (File.Exists(NewSoDPosFile))
            {
                addLog("New SoD file found File:" + NewSoDPosFile);

                List<balanceInfo> binfos = await readSoDFile(NewSoDPosFile);

                if (binfos.Count > 0)
                {
                    await ws_server.processBalance(binfos);
                }
                SortedDictionary<DateTime, DataFill> histFill = new SortedDictionary<DateTime, DataFill>();
                DateTime currentTime = DateTime.UtcNow;
                foreach (var mkt in qManager._markets.Keys)
                {
                    List<DataFill> temp_histFill;
                    if (mkt == market.gmocoin)
                    {
                        foreach(Instrument ins in qManager.instruments.Values)
                        {
                            if (ins.market == mkt)
                            {
                                temp_histFill = await crypto_client.getTradeHistory(mkt, ins.symbol, DateTime.UtcNow.Date);
                                foreach (var fill in temp_histFill)
                                {
                                    if (fill.filled_time == null)
                                    {
                                        fill.filled_time = currentTime;
                                    }
                                    while (histFill.ContainsKey((DateTime)fill.filled_time))
                                    {
                                        fill.filled_time += TimeSpan.FromMilliseconds(1);
                                    }
                                    histFill[fill.filled_time ?? DateTime.UtcNow] = fill;
                                }
                            }
                        }
                    }
                    else
                    {
                        temp_histFill = await crypto_client.getTradeHistory(mkt, "", DateTime.UtcNow.Date);
                        foreach (var fill in temp_histFill)
                        {
                            if (fill.filled_time == null)
                            {
                                fill.filled_time = currentTime;
                            }
                            while (histFill.ContainsKey((DateTime)fill.filled_time))
                            {
                                fill.filled_time += TimeSpan.FromMilliseconds(1);
                            }
                            histFill[fill.filled_time ?? DateTime.UtcNow] = fill;
                        }
                    }
                }
                foreach (var fill in histFill.Values)
                {
                    string symbol_market = fill.symbol_market;
                    if(qManager.exchanges.ContainsKey(fill.market))
                    {
                        Crypto_Trading.Exchange ex = qManager.exchanges[fill.market];
                        ex.updateBalance(fill);
                    }
                    if (qManager.instruments.ContainsKey(symbol_market))
                    {
                        Instrument ins = qManager.instruments[symbol_market];
                        ins.updateFills(fill);
                        filledOrderQueue.Enqueue(fill);
                    }
                }
                //Just in case, update balance again
                if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys))
                {
                    addLog("Failed to set balance", logType.WARNING);
                    return false;
                }
                if (!qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys))
                {
                    addLog("Failed to set margion position", logType.WARNING);
                    return false;
                }
            }
            else
            {
                if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys))
                {
                    addLog("Failed to set balance", logType.WARNING);
                    return false;
                }
                if (!qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys))
                {
                    addLog("Failed to set margin position", logType.WARNING);
                    return false;
                }

                using (StreamWriter sod = new StreamWriter(new FileStream(NewSoDPosFile, FileMode.Create, FileAccess.Write)))
                {
                    //Timestamp,Exchange,Margin or Spot,symbol,side(margin),quantity,avg_price(margin),current_price,valuation_pair,unrealized_fee(margin),unrealized_interest
                    sod.WriteLine("timestamp,exchange,Margin or Spot,symbol,side(margin),quantity,avg_price(margin),current_price,valuation_pair,unrealized_fee(margin),unrealized_interest(margin)");
                    DateTime current = DateTime.UtcNow;
                    foreach (var exBalance in qManager.exchanges.Values)
                    {
                        sod.Write(exBalance.OutputToFile(qManager.instruments, current));
                        sod.Flush();
                    }
                }
            }
            return true;
        }

        static private async Task<List<balanceInfo>> readSoDFile(string filename)
        {
            List<balanceInfo> binfos = new List<balanceInfo>();
            if (File.Exists(filename))
            {
                int i = 0;
                StreamReader sr = new StreamReader(new FileStream(filename, FileMode.Open, FileAccess.Read));
                Crypto_Trading.Exchange exBalance;
                while (sr.ReadLine() is string line)
                {
                    if (i > 0)
                    {
                        string[] items = line.Split(',');
                        if (items.Length >= 11)
                        {
                            string balanceType = items[2];
                            if (balanceType == "SPOT")
                            {
                                Balance b = new Balance();
                                b.market = (market)Enum.Parse(typeof(market), items[1]);
                                b.ccy = items[3];
                                b.total = decimal.Parse(items[5]);
                                b.current_price = decimal.Parse(items[7]);
                                b.valuation_pair = items[8];
                                if (qManager.exchanges_SoD.ContainsKey(b.market))
                                {
                                    exBalance = qManager.exchanges_SoD[b.market];
                                }
                                else
                                {
                                    exBalance = new Crypto_Trading.Exchange();
                                    exBalance.market = b.market;
                                    qManager.exchanges_SoD[b.market] = exBalance;
                                }
                                exBalance.balance[b.ccy] = b;


                                b = new Balance();
                                b.market = (market)Enum.Parse(typeof(market), items[1]);
                                b.ccy = items[3];
                                b.total = decimal.Parse(items[5]);
                                b.current_price = decimal.Parse(items[7]);
                                b.valuation_pair = items[8];
                                if (qManager.exchanges.ContainsKey(b.market))
                                {
                                    exBalance = qManager.exchanges[b.market];
                                }
                                else
                                {
                                    exBalance = new Crypto_Trading.Exchange();
                                    exBalance.market = b.market;
                                    qManager.exchanges[b.market] = exBalance;
                                }
                                exBalance.balance[b.ccy] = b;

                                balanceInfo bi = new balanceInfo();
                                bi.market = b.market.ToString();
                                bi.posType = "SPOT";
                                bi.symbol = b.ccy;
                                bi.side = "";
                                bi.total = b.total;
                                bi.avg_price = 0;
                                bi.current_price = b.current_price;
                                bi.valuation_pair = b.valuation_pair;
                                bi.unrealized_fee = 0;
                                bi.unrealized_interest = 0;
                                bi.isSoD = true;
                                binfos.Add(bi);
                            }
                            else if (balanceType == "MARGIN")
                            {
                                BalanceMargin bm = new BalanceMargin();
                                bm.market = (market)Enum.Parse(typeof(market), items[1]);
                                bm.symbol = items[3];
                                string str_side = items[4];
                                if (str_side.ToLower() == "long")
                                {
                                    bm.side = positionSide.Long;
                                }
                                else if (str_side.ToLower() == "short")
                                {
                                    bm.side = positionSide.Short;
                                }
                                else
                                {
                                    bm.side = positionSide.NONE;
                                }
                                bm.total = decimal.Parse(items[5]);
                                bm.avg_price = decimal.Parse(items[6]);
                                bm.current_price = decimal.Parse(items[7]);
                                bm.unrealized_fee = decimal.Parse(items[9]);
                                bm.unrealized_interest = decimal.Parse(items[10]);
                                if (qManager.exchanges_SoD.ContainsKey(bm.market))
                                {
                                    exBalance = qManager.exchanges_SoD[bm.market];
                                }
                                else
                                {
                                    exBalance = new Crypto_Trading.Exchange();
                                    exBalance.market = bm.market;
                                    qManager.exchanges_SoD[bm.market] = exBalance;
                                }
                                if (bm.side == positionSide.Long)
                                {
                                    exBalance.marginLong[bm.symbol_market] = bm;
                                }
                                else if (bm.side == positionSide.Short)
                                {
                                    exBalance.marginShort[bm.symbol_market] = bm;
                                }

                                bm = new BalanceMargin();
                                bm.market = (market)Enum.Parse(typeof(market), items[1]);
                                bm.symbol = items[3];
                                str_side = items[4];
                                if (str_side.ToLower() == "long")
                                {
                                    bm.side = positionSide.Long;
                                }
                                else if (str_side.ToLower() == "short")
                                {
                                    bm.side = positionSide.Short;
                                }
                                else
                                {
                                    bm.side = positionSide.NONE;
                                }
                                bm.total = decimal.Parse(items[5]);
                                bm.avg_price = decimal.Parse(items[6]);
                                bm.current_price = decimal.Parse(items[7]);
                                bm.unrealized_fee = decimal.Parse(items[9]);
                                bm.unrealized_interest = decimal.Parse(items[10]);
                                if (qManager.exchanges.ContainsKey(bm.market))
                                {
                                    exBalance = qManager.exchanges[bm.market];
                                }
                                else
                                {
                                    exBalance = new Crypto_Trading.Exchange();
                                    exBalance.market = bm.market;
                                    qManager.exchanges[bm.market] = exBalance;
                                }
                                if (bm.side == positionSide.Long)
                                {
                                    exBalance.marginLong[bm.symbol_market] = bm;
                                }
                                else if (bm.side == positionSide.Short)
                                {
                                    exBalance.marginShort[bm.symbol_market] = bm;
                                }

                                balanceInfo bi = new balanceInfo();
                                bi.market = bm.market.ToString();
                                bi.posType = "MARGIN";
                                bi.symbol = bm.symbol;
                                bi.side = bm.side.ToString();
                                bi.total = bm.total;
                                bi.avg_price = bm.avg_price;
                                bi.current_price = bm.current_price;
                                bi.valuation_pair = bm.symbol;
                                bi.unrealized_fee = bm.unrealized_fee;
                                bi.unrealized_interest = bm.unrealized_interest;
                                bi.isSoD = true;
                                binfos.Add(bi);
                            }
                        }
                    }
                    else
                    {
                        ++i;
                    }
                }


                foreach (var ex in qManager.exchanges.Values)
                {
                    ex.setInstruments(qManager.instruments);
                }
                foreach (var ex in qManager.exchanges_SoD.Values)
                {
                    ex.setInstruments(qManager.instruments);
                }

                foreach (Instrument ins in qManager.instruments.Values)
                {
                    if (qManager.exchanges.ContainsKey(ins.market))
                    {
                        exBalance = qManager.exchanges[ins.market];
                        if (exBalance.balance.ContainsKey(ins.baseCcy))
                        {
                            ins.baseBalance = exBalance.balance[ins.baseCcy];
                            ins.baseBalance.valuation_pair = ins.symbol_market;
                        }
                        else
                        {
                            addLog("Base currency balance not found.  pair:" + ins.symbol_market + "  ccy:" + ins.baseCcy, logType.WARNING);
                            exBalance.balance[ins.baseCcy] = ins.baseBalance;
                            ins.baseBalance.valuation_pair = ins.symbol_market;

                            balanceInfo bi = new balanceInfo();
                            bi.market = ins.market.ToString();
                            bi.posType = "SPOT";
                            bi.symbol = ins.baseCcy;
                            bi.side = "";
                            bi.total = ins.baseBalance.total;
                            bi.avg_price = 0;
                            bi.current_price = 0;
                            bi.valuation_pair = ins.symbol;
                            bi.unrealized_fee = 0;
                            bi.unrealized_interest = 0;
                            bi.isSoD = true;
                            binfos.Add(bi);

                        }
                        if (exBalance.balance.ContainsKey(ins.quoteCcy))
                        {
                            ins.quoteBalance = exBalance.balance[ins.quoteCcy];
                            ins.quoteBalance.valuation_pair = ins.symbol_market;
                        }
                        else
                        {
                            addLog("Quote currency balance not found.  pair:" + ins.symbol_market + "  ccy:" + ins.quoteCcy, logType.WARNING);
                            exBalance.balance[ins.quoteCcy] = ins.quoteBalance;
                            ins.quoteBalance.valuation_pair = ins.symbol_market;

                            balanceInfo bi = new balanceInfo();
                            bi.market = ins.market.ToString();
                            bi.posType = "SPOT";
                            bi.symbol = ins.quoteCcy;
                            bi.side = "";
                            bi.total = ins.quoteBalance.total;
                            bi.avg_price = 0;
                            bi.current_price = 0;
                            bi.valuation_pair = ins.symbol;
                            bi.unrealized_fee = 0;
                            bi.unrealized_interest = 0;
                            bi.isSoD = true;
                            binfos.Add(bi);
                        }
                        if (exBalance.marginShort.ContainsKey(ins.symbol_market))
                        {
                            ins.shortPosition = exBalance.marginShort[ins.symbol_market];
                            ins.shortPosition.leverage = ins.leverage;
                        }
                        else
                        {
                            addLog("Short position not found.  pair:" + ins.symbol_market, logType.WARNING);
                            exBalance.marginShort[ins.symbol_market] = ins.shortPosition;

                            balanceInfo bi = new balanceInfo();
                            bi.market = ins.market.ToString();
                            bi.posType = "MARGIN";
                            bi.symbol = ins.symbol;
                            bi.side = ins.shortPosition.side.ToString();
                            bi.total = ins.shortPosition.total;
                            bi.avg_price = ins.shortPosition.avg_price;
                            bi.current_price = ins.shortPosition.current_price;
                            bi.valuation_pair = ins.symbol;
                            bi.unrealized_fee = ins.shortPosition.unrealized_fee;
                            bi.unrealized_interest = ins.shortPosition.unrealized_interest;
                            bi.isSoD = true;
                            binfos.Add(bi);
                        }
                        if (exBalance.marginLong.ContainsKey(ins.symbol_market))
                        {
                            ins.longPosition = exBalance.marginLong[ins.symbol_market];
                            ins.longPosition.leverage = ins.leverage;
                        }
                        else
                        {
                            addLog("Long position not found.  pair:" + ins.symbol_market, logType.WARNING);
                            exBalance.marginLong[ins.symbol_market] = ins.longPosition;

                            balanceInfo bi = new balanceInfo();
                            bi.market = ins.market.ToString();
                            bi.posType = "MARGIN";
                            bi.symbol = ins.symbol;
                            bi.side = ins.longPosition.side.ToString();
                            bi.total = ins.longPosition.total;
                            bi.avg_price = ins.longPosition.avg_price;
                            bi.current_price = ins.longPosition.current_price;
                            bi.valuation_pair = ins.symbol;
                            bi.unrealized_fee = ins.longPosition.unrealized_fee;
                            bi.unrealized_interest = ins.longPosition.unrealized_interest;
                            bi.isSoD = true;
                            binfos.Add(bi);
                        }
                    }
                    else
                    {
                        addLog("[SetRealPosition]Exchange not found. Exchange:" + ins.market);
                    }
                    if (qManager.exchanges_SoD.ContainsKey(ins.market))
                    {
                        exBalance = qManager.exchanges_SoD[ins.market];
                        if (exBalance.balance.ContainsKey(ins.baseCcy))
                        {
                            ins.SoD_baseBalance = exBalance.balance[ins.baseCcy];
                            ins.SoD_baseBalance.valuation_pair = ins.symbol_market;
                        }
                        else
                        {
                            addLog("Base currency balance not found.  pair:" + ins.symbol_market + "  ccy:" + ins.baseCcy, logType.WARNING);
                            exBalance.balance[ins.baseCcy] = ins.SoD_baseBalance;
                            ins.SoD_baseBalance.valuation_pair = ins.symbol_market;
                        }
                        if (exBalance.balance.ContainsKey(ins.quoteCcy))
                        {
                            ins.SoD_quoteBalance = exBalance.balance[ins.quoteCcy];
                            ins.SoD_quoteBalance.valuation_pair = ins.symbol_market;
                        }
                        else
                        {
                            addLog("Quote currency balance not found.  pair:" + ins.symbol_market + "  ccy:" + ins.quoteCcy, logType.WARNING);
                            exBalance.balance[ins.quoteCcy] = ins.SoD_quoteBalance;
                            ins.SoD_quoteBalance.valuation_pair = ins.symbol_market;
                        }
                        if (exBalance.marginShort.ContainsKey(ins.symbol_market))
                        {
                            ins.SoD_shortPosition = exBalance.marginShort[ins.symbol_market];
                        }
                        else
                        {
                            addLog("Short position not found.  pair:" + ins.symbol_market, logType.WARNING);
                            exBalance.marginShort[ins.symbol_market] = ins.SoD_shortPosition;
                        }
                        if (exBalance.marginLong.ContainsKey(ins.symbol_market))
                        {
                            ins.SoD_longPosition = exBalance.marginLong[ins.symbol_market];
                        }
                        else
                        {
                            addLog("Long position not found.  pair:" + ins.symbol_market, logType.WARNING);
                            exBalance.marginLong[ins.symbol_market] = ins.SoD_longPosition;
                        }
                        if (ins.SoD_shortPosition.current_price > 0)
                        {
                            ins.open_mid = ins.SoD_shortPosition.current_price;
                        }
                        else if (ins.SoD_longPosition.current_price > 0)
                        {
                            ins.open_mid = ins.SoD_longPosition.current_price;
                        }
                        else if (ins.SoD_baseBalance.current_price > 0)
                        {
                            ins.open_mid = ins.SoD_baseBalance.current_price;
                        }
                    }
                    else
                    {
                        addLog("[SetRealPosition]Exchange not found. Exchange:" + ins.market);
                    }
                }
                //if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys))
                //{
                //    addLog("Failed to set balance", logType.WARNING);
                //    return binfos;
                //}
                //if (!qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys))
                //{
                //    addLog("Failed to set margin position", logType.WARNING);
                //    return binfos;
                //}
            }
            else
            {
                addLog("SoD File not found. Getting the position from the exchanges", logType.WARNING);
                if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys))
                {
                    addLog("Failed to set balance", logType.WARNING);
                    return binfos;
                }
                if (!qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys))
                {
                    addLog("Failed to set margin position", logType.WARNING);
                    return binfos;
                }
            }
            return binfos;
        }

        static private bool readTradePerTrade(string filename)
        {
            bool res = false;

            if(File.Exists(filename))
            {
                using (StreamReader tpt = new StreamReader(new FileStream(filename, FileMode.Open, FileAccess.Read)))
                {
                    int i = 0;
                    while (tpt.ReadLine() is string line)
                    {
                        if (i == 0)
                        {
                            ++i;
                        }
                        else
                        {
                            string[] data = line.Split(",");
                            if (strategies.ContainsKey(data[1]))
                            {
                                Strategy stg = strategies[data[1]];
                                decimal notional = decimal.Parse(data[10]) * decimal.Parse(data[11]);
                                stg.notionalVolumeB += notional;
                                stg.markupPnL += decimal.Parse(data[23]);
                                stg.skewPnL += decimal.Parse(data[24]);
                                stg.priceAdjPnL += decimal.Parse(data[25]);
                                stg.residualPnL += decimal.Parse(data[26]);
                                stg.tradingPnLB += decimal.Parse(data[28]);
                            }
                        }
                    }
                }
                res = true;
            }

            return res;
        }

        static private bool startTrading()
        {
            enabled = true;
            foreach (var stg in strategies.Values)
            {
                stg.enabled = true;
            }
            addLog("Warming up...");
            return true;
        }

        static private bool stopStrategies()
        {
            foreach (var stg in strategies.Values)
            {
                stg.enabled = false;
            }
            enabled = false;
            return true;
        }

        static private async Task<bool> stopTrading(bool error = false)
        {

            if (Interlocked.CompareExchange(ref stopTradingCalled, 1, 0) == 0)
            {
                stopStrategies();
                if (error)
                {
                    addLog("Error received. Now stopping the trading. Check the exchange to make sure all the orders are cancelled.", Enums.logType.ERROR);

                }
                else
                {
                    addLog("Stopping the trading normally");
                }
                addLog("Stopping trading process started");
                Thread.Sleep(1000);
                if (threadsStarted)
                {
                    if (oManager.ready)
                    {
                        await oManager.cancelAllOrders();
                        Thread.Sleep(1000);
                        addLog("All orders cancelled");
                        foreach (var th in thManager.threads)
                        {
                            th.Value.stop();
                        }
                    }
                }

            }
            return true;
        }

        static private async Task EoDProcess()
        {
            if(Interlocked.CompareExchange(ref EoDProcessCalled,1,0) != 0)
            {
                return;
            }
            
            stopStrategies();
            if (threadsStarted)
            {
                if (oManager.ready)
                {
                    await oManager.cancelAllOrders();
                    Thread.Sleep(1000);
                    if(live)
                    {
                        foreach (var mkt in qManager._markets.Keys)
                        {
                            List<DataSpotOrderUpdate> unknown_orders = await crypto_client.getActiveOrders(mkt);
                            if (unknown_orders.Count > 0)
                            {
                                addLog("Unknown orders found at " + mkt);
                                Dictionary<Instrument, List<string>> ordId_list = new Dictionary<Instrument, List<string>>();
                                foreach (var ord in unknown_orders)
                                {
                                    if (qManager.instruments.ContainsKey(ord.symbol_market))
                                    {
                                        Instrument ins = qManager.instruments[ord.symbol_market];
                                        if (!ordId_list.ContainsKey(ins))
                                        {
                                            ordId_list[ins] = new List<string>();
                                        }
                                        using (var f = oManager.mapping_lock.getlock())
                                        {
                                            oManager.ordIdMapping[ord.market + ord.order_id] = ord.market + ord.order_id;
                                        }
                                        oManager.orders[ord.market + ord.order_id] = ord;
                                        ordId_list[ins].Add(ord.market + ord.order_id);
                                    }
                                }
                                foreach (var item in ordId_list)
                                {
                                    await oManager.placeCancelSpotOrders(item.Key, item.Value);
                                    Thread.Sleep(1000);
                                }
                            }
                        }
                        //Just in case
                        qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys);
                        qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys);
                        //foreach (var m in qManager._markets)
                        //{
                        //    qManager.setBalance(await crypto_client.getBalance([m.Key]), [m.Key]);
                        //    qManager.setMarginPosition(await crypto_client.getMarginPos([m.Key]));
                        //}
                    }
                    
                    foreach(var stg in strategies.Values)
                    {
                        stg.adjustPosition();
                    }

                    Thread.Sleep(1000);

                    string msg = "EoD PnL\n";
                    string err = "";
                    err = await MsgDeliverer.sendMessage(msg, msg_type);
                    if (err != "")
                    {
                        addLog(err, logType.WARNING);
                    }
                    Console.WriteLine(msg);
                    await messagePnL(true);

                    if (ws_server.intradayPnLList.Count > 0)
                    {
                        using (var sw_intraday = new StreamWriter(new FileStream(intradayPnLFile, FileMode.Create, FileAccess.Write)))
                        {
                            foreach(var pnl in ws_server.intradayPnLList.Values)
                            {
                                sw_intraday.WriteLine($"{pnl.strategy_name},{pnl.OADatetime.ToString()},{pnl.PnL.ToString()},{pnl.notionalVolume.ToString()}");
                            }
                            sw_intraday.Flush();
                        }
                    }

                    Thread.Sleep(1000);

                    await outputPerformance(outputPath_org + "/performance.csv");

                    string TPT_file = outputPath + "/TradePerTrade.csv";
                    string PnlBreakDown_file = outputPath_org + "/PnLBreakDown.csv";
                    outputTradePerTrade(TPT_file,PnlBreakDown_file);

                    string dt = (DateTime.UtcNow + TimeSpan.FromDays(1)).ToString("yyyy-MM-dd");
                    string newpath = outputPath_org + "/" + dt;
                    if (!Directory.Exists(newpath))
                    {
                        Directory.CreateDirectory(newpath);
                    }


                    addLog("Exporting SoD position file...");

                    //string SoDPosFile = newpath + "/SoD_Position.csv";
                    string HistBalanceFile = outputPath_org + "/historicalBalance.csv";
                    string new_SoDFile = newpath + "/SoD_Position_new.csv";

                    await outputHistoricalBalance(HistBalanceFile, new_SoDFile);
                    //if (live)
                    //{
                    //    qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys));
                    //    qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys));
                    //}
                    

                    //StreamWriter sw = new StreamWriter(new FileStream(SoDPosFile, FileMode.Create, FileAccess.Write));
                    //using (StreamWriter sod = new StreamWriter(new FileStream(SoDPosFile, FileMode.Create, FileAccess.Write)))
                    //{
                    //    sod.WriteLine("timestamp,symbol,market,symbol_market,base_ccy,quote_ccy,baseccy_balance,quoteccy_balance,long_position,long_avgprice,long_unrealized_fee,long_unrealized_interest,short_position,short_avgprice,short_unrealized_fee,short_unrealized_interest,open_mid");
                    //    string currentTime = DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                    //    foreach (var ins in qManager.instruments.Values)
                    //    {
                    //        //decimal mid = await crypto_client.getCurrentMid(ins.market, ins.symbol);
                    //        //string line = currentTime + "," + ins.symbol + "," + ins.market + "," + ins.symbol_market + "," + ins.baseCcy + "," + ins.quoteCcy + "," + ins.baseBalance.total.ToString() + "," + ins.quoteBalance.total.ToString() + "," + ins.longPosition.total.ToString() + "," + ins.shortPosition.total.ToString() + "," + ins.mid.ToString();
                    //        string line = currentTime + "," + ins.symbol + "," + ins.market + "," + ins.symbol_market + "," + ins.baseCcy + "," + ins.quoteCcy + "," + ins.baseBalance.total.ToString() + "," + ins.quoteBalance.total.ToString()
                    //                + "," + ins.longPosition.total.ToString() + "," + ins.longPosition.avg_price.ToString() + "," + ins.longPosition.unrealized_fee.ToString() + "," + ins.longPosition.unrealized_interest.ToString()
                    //                + "," + ins.shortPosition.total.ToString() + "," + ins.shortPosition.avg_price.ToString() + "," + ins.shortPosition.unrealized_fee.ToString() + "," + ins.shortPosition.unrealized_interest.ToString() + "," + ins.mid.ToString();

                    //        sod.WriteLine(line);
                    //        sod.Flush();
                    //    }
                    //}

                    //using (StreamWriter sod = new StreamWriter(new FileStream(new_SoDFile, FileMode.Create, FileAccess.Write)))
                    //{
                    //    sod.WriteLine("timestamp,exchange,Margin or Spot,symbol,side(margin),quantity,avg_price(margin),current_price,valuation_pair,unrealized_fee(margin),unrealized_interest(margin)");
                    //    DateTime currentTime = DateTime.UtcNow;
                    //    foreach (var exBalance in qManager.exchanges)
                    //    {
                    //        Crypto_Trading.Exchange ex = exBalance.Value;
                    //        sod.Write(exBalance.Value.OutputToFile(qManager.instruments,currentTime));
                    //        sod.Flush();
                    //    }
                    //}

                    outputMI();
                    marketImpactFile.Flush();
                    marketImpactFile.Close();

                    if (LatencyList.Count > 0)
                    {
                        string latencyReport = outputPath + "/LatencyReport.csv";
                        using(StreamWriter lr = new StreamWriter(new FileStream(latencyReport,FileMode.Create,FileAccess.Write)))
                        {
                            lr.WriteLine("name,count,average[us],max[us]");
                            foreach(var l in LatencyList.Values)
                            {
                                lr.WriteLine(l.ToString());
                            }
                        }
                    }

                    foreach (var th in thManager.threads)
                    {
                        th.Value.stop();
                    }
                }
            }
        }

        static private async Task outputPerformance(string filename = "perfomance.csv")
        {
            addLog("Updating the performance file...");

            List<string> lines;
            if (!File.Exists(filename))
            {
                lines = new List<string>();
                lines.Add("date,strategy,baseBalance_open,quoteBalance_open,baseBalance_close,quoteBalance_close,baseHedge_quantity,notional_volume,open_mid,close_mid,buy_quantity,buy_avgPrice,sell_quantity,sell_avgPrice,TotalPnL,pos_diff,BBookPnL");

            }
            else
            {
                lines = File.ReadAllLines(filename).ToList();
            }
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            lines = lines
                .Where(line =>
                {
                    var cols = line.Split(',');
                    return cols.Length > 0 && cols[0] != today;
                })
                .ToList();

            foreach (var stg in strategies.Values)
            {
                decimal baseBalance_open = stg.maker.SoD_net_pos + stg.taker.SoD_net_pos;
                decimal quoteBalance_open = stg.maker.SoD_quoteBalance.total + stg.taker.SoD_quoteBalance.total;
                decimal baseBalance_close = stg.maker.baseBalance.total + stg.maker.longPosition.total - stg.maker.shortPosition.total
                                            + stg.taker.baseBalance.total + stg.taker.longPosition.total - stg.taker.shortPosition.total; ;
                decimal quoteBalance_close = stg.maker.quoteBalance.total + stg.taker.quoteBalance.total;

                decimal temp_mid;
                temp_mid = await crypto_client.getCurrentMid(stg.taker.market, stg.taker.symbol);
                if (temp_mid > 0)
                {
                    stg.taker.mid = temp_mid;
                }
                temp_mid = await crypto_client.getCurrentMid(stg.maker.market, stg.maker.symbol);
                if (temp_mid > 0)
                {
                    stg.maker.mid = temp_mid;
                }

                stg.netExposure = stg.maker.net_pos + stg.taker.net_pos;
                stg.notionalVolume = stg.maker.my_buy_notional + stg.maker.my_sell_notional;
                stg.posPnL = stg.SoD_baseCcyPos * (stg.taker.mid - stg.taker.open_mid);
                stg.tradingPnL = (stg.taker.my_sell_notional - stg.taker.my_sell_quantity * stg.taker.mid) + (stg.taker.my_buy_quantity * stg.taker.mid - stg.taker.my_buy_notional);
                stg.tradingPnL += (stg.maker.my_sell_notional - stg.maker.my_sell_quantity * stg.taker.mid) + (stg.maker.my_buy_quantity * stg.taker.mid - stg.maker.my_buy_notional);
                stg.totalFee = stg.taker.base_fee * stg.taker.mid + stg.taker.quote_fee + stg.maker.base_fee * stg.taker.mid + stg.maker.quote_fee;
                stg.totalPnL = stg.posPnL + stg.tradingPnL - stg.totalFee;

                decimal sell_avgprice = stg.maker.my_sell_quantity > 0 ? stg.maker.my_sell_notional / stg.maker.my_sell_quantity : 0;
                decimal buy_avgprice = stg.maker.my_buy_quantity > 0 ? stg.maker.my_buy_notional / stg.maker.my_buy_quantity : 0;

                decimal interest = stg.taker.realized_Interest + stg.maker.realized_Interest
                                    + stg.taker.shortPosition.unrealized_interest - stg.taker.SoD_shortPosition.unrealized_interest
                                    + stg.taker.longPosition.unrealized_interest - stg.taker.SoD_longPosition.unrealized_interest
                                    + stg.maker.shortPosition.unrealized_interest - stg.maker.SoD_shortPosition.unrealized_interest
                                    + stg.maker.longPosition.unrealized_interest - stg.maker.SoD_longPosition.unrealized_interest;
                decimal pos_diff = (stg.maker.baseBalance.total + stg.taker.baseBalance.total) * stg.taker.mid - (stg.maker.SoD_baseBalance.total + stg.taker.SoD_baseBalance.total) * stg.taker.open_mid + quoteBalance_close - quoteBalance_open;


                decimal unrealized_pnl = stg.maker.longPosition.total * (stg.taker.mid - stg.maker.longPosition.avg_price) + stg.maker.shortPosition.total * (stg.maker.shortPosition.avg_price - stg.taker.mid)
                                        + stg.taker.longPosition.total * (stg.taker.mid - stg.maker.longPosition.avg_price) + stg.taker.shortPosition.total * (stg.taker.shortPosition.avg_price - stg.taker.mid);
                decimal unrealized_sod = stg.maker.SoD_longPosition.total * (stg.taker.open_mid - stg.maker.SoD_longPosition.avg_price) + stg.maker.SoD_shortPosition.total * (stg.maker.SoD_shortPosition.avg_price - stg.taker.open_mid)
                                        + stg.taker.SoD_longPosition.total * (stg.taker.open_mid - stg.maker.SoD_longPosition.avg_price) + stg.taker.SoD_shortPosition.total * (stg.taker.SoD_shortPosition.avg_price - stg.taker.mid);
                decimal unrealized_fee = stg.maker.longPosition.unrealized_fee - stg.maker.SoD_longPosition.unrealized_fee + stg.maker.shortPosition.unrealized_fee - stg.maker.SoD_shortPosition.unrealized_fee
                                        + stg.taker.longPosition.unrealized_fee - stg.taker.SoD_longPosition.unrealized_fee + stg.taker.shortPosition.unrealized_fee - stg.taker.SoD_shortPosition.unrealized_fee;
                decimal unrealized_interest = stg.taker.shortPosition.unrealized_interest - stg.taker.SoD_shortPosition.unrealized_interest
                                    + stg.taker.longPosition.unrealized_interest - stg.taker.SoD_longPosition.unrealized_interest
                                    + stg.maker.shortPosition.unrealized_interest - stg.maker.SoD_shortPosition.unrealized_interest
                                    + stg.maker.longPosition.unrealized_interest - stg.maker.SoD_longPosition.unrealized_interest;
                decimal unrealized_diff = unrealized_pnl - unrealized_sod - unrealized_fee - unrealized_interest;

                decimal BBookPnL = (sell_avgprice - stg.maker.mid) * stg.maker.my_sell_quantity + (stg.maker.mid - buy_avgprice) * stg.maker.my_buy_quantity;

                string line = today + "," + stg.name + "," + baseBalance_open.ToString() + "," + quoteBalance_open.ToString() + ","
                    + baseBalance_close.ToString() + "," + quoteBalance_close.ToString() + "," + stg.maxMakerPosition.ToString() + "," + stg.notionalVolume.ToString() + "," + stg.taker.open_mid.ToString() + "," + stg.taker.mid.ToString() + ","
                    + stg.maker.my_buy_quantity + "," + buy_avgprice + "," + stg.maker.my_sell_quantity + "," + sell_avgprice + ","
                    + stg.totalPnL.ToString() + "," + (-interest).ToString() + "," + pos_diff.ToString() + "," + unrealized_diff.ToString() + "," + BBookPnL.ToString();

                lines.Add(line);
            }

            File.WriteAllLines(filename, lines);
        }
        static private async Task outputHistoricalBalance(string HistFile = "HitoricalBalance.csv",string DailyFile = "SoD_Position_new.csv")
        {
            addLog("Exporting Balance...");
            if (live)
            {
                qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys);
                qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys);
            }
            using StreamWriter sod = new StreamWriter(new FileStream(DailyFile, FileMode.Create, FileAccess.Write));
            sod.WriteLine("timestamp,exchange,Margin or Spot,symbol,side(margin),quantity,avg_price(margin),current_price,valuation_pair,unrealized_fee(margin),unrealized_interest(margin)");
            DateTime currentTime = DateTime.UtcNow;
            List<string> lines;
            if (!File.Exists(HistFile))
            {
                lines = new List<string>();
                lines.Add("date,exchange,type,ccy,side,amount,value(UnrealizedPnL)");
            }
            else
            {
                lines = File.ReadAllLines(HistFile).ToList();
            }
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            lines = lines
                .Where(line =>
                {
                    var cols = line.Split(',');
                    return cols.Length > 0 && cols[0] != today;
                })
                .ToList();
            decimal totalValue = 0;
            decimal totalAmount = 0;
            foreach(Crypto_Trading.Exchange ex in qManager.exchanges.Values)
            {
                bool updateUnrealized = true;
                if(ex.market == market.gmocoin && live)
                {
                    updateUnrealized = false;
                }
                sod.Write(ex.OutputToFile(qManager.instruments, currentTime,updateUnrealized));
                sod.Flush();
                string histline = ex.outputHistorical(qManager.instruments, today,false).TrimEnd('\r', '\n');
                lines.Add(histline);
                foreach(Balance b in ex.balance.Values)
                {
                    totalValue += b.total * b.current_price;
                    if(b.ccy != "JPY")
                    {
                        totalAmount += b.total * b.current_price;
                    }
                }
                foreach(BalanceMargin b in ex.marginLong.Values)
                {
                    totalValue += b.unrealized_pnl - b.unrealized_fee - b.unrealized_interest;
                    totalAmount += b.total * b.current_price;
                }
                foreach (BalanceMargin b in ex.marginShort.Values)
                {
                    totalValue += b.unrealized_pnl - b.unrealized_fee - b.unrealized_interest;
                    totalAmount -= b.total * b.current_price;
                }
            }
            lines.Add($"{today},Total,,,,{totalAmount},{totalValue}");
            File.WriteAllLines(HistFile, lines);
        }
        static private void outputTradePerTrade(string filename = "TradePerTrade.csv",string HistFile = "PnLBreakdown.csv",string today = "")
        {
            addLog("Trade per trade");
            Dictionary<string, PnLBreakDown> summary = new Dictionary<string, PnLBreakDown>();
            if (File.Exists(filename))
            {
                using (StreamReader tpt = new StreamReader(new FileStream(filename, FileMode.Open, FileAccess.Read)))
                {
                    int i = 0;
                    while (tpt.ReadLine() is string line)
                    {
                        if(i == 0)
                        {
                            ++i;
                        }
                        else
                        {
                            string[] data = line.Split(",");
                            if (!summary.ContainsKey(data[1]))
                            {
                                PnLBreakDown newdata = new PnLBreakDown();
                                newdata.strategy = data[1];
                                newdata.symbol = data[4];
                                newdata.addData(data);
                                if (strategies.ContainsKey(newdata.strategy))
                                {
                                    Strategy stg = strategies[newdata.strategy];
                                    newdata.SoDBalance = stg.maker.SoD_net_pos - stg.targetMakerPosition;
                                    newdata.EoDBalance = stg.maker.net_pos - stg.targetMakerPosition;
                                }
                                summary[data[1]] = newdata;
                            }
                            else
                            {
                                summary[data[1]].addData(data);
                            }
                        }
                    }
                }
                using (StreamWriter tpt = new StreamWriter(new FileStream(filename,FileMode.Append,FileAccess.Write)))
                {
                    foreach (var stg in strategies.Values)
                    {
                        foreach (var ts in stg.tradeSummaries.Values)
                        {
                            if (ts.maker_quantity > 0 && ts.taker_quantity > 0)
                            {
                                ts.calcPnL();
                                if (!summary.ContainsKey(ts.strategy))
                                {
                                    PnLBreakDown newdata = new PnLBreakDown();
                                    newdata.strategy = ts.strategy;
                                    newdata.symbol = ts.maker_symbolmarket;
                                    newdata.addData(ts);
                                    newdata.SoDBalance = stg.maker.SoD_net_pos - stg.targetMakerPosition;
                                    newdata.EoDBalance = stg.maker.net_pos - stg.targetMakerPosition;
                                    summary[ts.strategy] = newdata;
                                }
                                else
                                {
                                    summary[ts.strategy].addData(ts);
                                }
                            }
                            tpt.WriteLine(ts.ToString());
                        }
                        stg.tradeSummaries.Clear();
                    }
                }
            }
            else
            {
                using (StreamWriter tpt = new StreamWriter(new FileStream(filename, FileMode.Create, FileAccess.Write)))
                {
                    tpt.WriteLine("timestamp,strategy,id,BBook,maker_symbolmarket,taker_symbolmarket,maker_orderid,taker_orderid,layer,maker_side,maker_orderPrice,maker_avgprice,maker_quantity,maker_avgExecutedTime,taker_side,taker_avgprice,taker_quantity,taker_avgExecutedTime,realized_volatility,maker_markup,taker_markup,skew,maker_priceAdj,taker_priceAdj,markupPnL,skewPnL,priceAdjPnL,residualPnL,totalFee,totalPnL,avg_Latency,action,makerLatency,makerTradeImbalance,makerQuoteImbalance,makerMidRatio,takerLatency,takerTradeImbalance,takerQuoteImbalance,takerMidRatio,maxAsk,minBid");
                    foreach (var stg in strategies.Values)
                    {
                        foreach (var ts in stg.tradeSummaries.Values)
                        {
                            if (ts.maker_quantity > 0 && ts.taker_quantity > 0)
                            {
                                ts.calcPnL();
                                if (!summary.ContainsKey(ts.strategy))
                                {
                                    PnLBreakDown newdata = new PnLBreakDown();
                                    newdata.strategy = ts.strategy;
                                    newdata.symbol = ts.maker_symbolmarket;
                                    newdata.addData(ts);
                                    newdata.SoDBalance = stg.maker.SoD_net_pos - stg.targetMakerPosition;
                                    newdata.EoDBalance = stg.maker.net_pos - stg.targetMakerPosition;
                                    summary[ts.strategy] = newdata;
                                }
                                else
                                {
                                    summary[ts.strategy].addData(ts);
                                }
                            }
                            tpt.WriteLine(ts.ToString());
                        }
                        stg.tradeSummaries.Clear();
                    }
                }
            }
            
            if(today == "")
            {
                today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            }
            List<string> lines;
            if (!File.Exists(HistFile))
            {
                lines = new List<string>();
                lines.Add("date,strategy,symbol,SoDOutStanding,EoDOutStanding,count,totalAmount,notionalVolume,markupPnL,skewPnL,priceAdjPnL,residualPnL,totalFee,totalPnL,avg_Latency");
            }
            else
            {
                lines = File.ReadAllLines(HistFile).ToList();
            }
            lines = lines
                .Where(line =>
                {
                    var cols = line.Split(',');
                    return cols.Length > 0 && cols[0] != today;
                })
                .ToList();
            foreach(var s in summary.Values)
            {
                lines.Add(today + "," + s.ToString());
            }
            File.WriteAllLines(HistFile, lines);
        }
        static async Task statusCheck()
        {
            //Declare variables that store the latency and status.
            //Connection
            if(EoDProcessCalled > 0)
            {
                return;
            }
            DateTime current = DateTime.UtcNow;
            connecitonStatus status;
            qManager.checkConnections();
            oManager.checkConnections();
            foreach (var mkt in qManager._markets)
            {
                status = connectionStates[mkt.Key.ToString()];
                status.publicState = mkt.Value.ToString();
            }

            foreach (var mkt in oManager.connections)
            {
                status = connectionStates[mkt.Key.ToString()];
                status.privateState = mkt.Value.ToString();
                switch (mkt.Key)
                {
                    case market.bitbank:
                        status.avgRTT = crypto_client.bitbank_client.avgLatency() / 1000;
                        break;
                    case market.gmocoin:
                        status.avgRTT = crypto_client.gmocoin_client.avgLatency() / 1000;
                        break;
                    case market.coincheck:
                        status.avgRTT = crypto_client.coincheck_client.avgLatency() / 1000;
                        break;
                    case market.bittrade:
                        status.avgRTT = crypto_client.bittrade_client.avgLatency() / 1000;
                        break;
                    default:
                        status.avgRTT = 0;
                        break;

                }
            }

            List<string> stoppedThreads = new List<string>();
            //Thread
            foreach (var th in thManager.threads)
            {
                bool found = false;
                string st;
                if (stopTradingCalled == 0 && th.Value.isRunning == false)
                {
                    //If connection lost, try reconnect
                    //If public connection, reconnect and subscribe
                    //if private connection, reconnect, get current status, and restart
                    //if other threads, unexpected error stop trading

                    stoppedThreads.Add(th.Key);
                }
                if (th.Value.isRunning)
                {
                    st = "Running";
                }
                else
                {
                    st = "Stopped";
                }
                threadStatus th_status = threadStates[th.Key];
                th_status.isRunning = th.Value.isRunning;
                th_status.avgProcessingTime = th.Value.Latency.avgLatency;
                //if (th.Value.count > 0)
                //{
                //    th_status.avgProcessingTime = th.Value.totalElapsedTime / th.Value.count / 1000;
                //    th_status.avgProcessingTime = th.Value.Latency.avgLatency;
                //}
                //else
                //{
                //    th_status.avgProcessingTime = 0;
                //}
                threadStates[th.Key] = th_status;
            }


            if(stoppedThreads.Count > 0)
            {
                //bool currentTradingState = enabled;
                bool reconnected = false;
                foreach (var stoppedTh in stoppedThreads)
                {
                    if(reconnected == false && (stoppedTh.Contains("Public") || stoppedTh.Contains("Private")))
                    {
                        addLog("Connection Lost. Reconnecting all the connection in 5 sec", logType.WARNING);
                        await refreshConnection();
                        reconnected = true;
                    }
                    else
                    {
                        addLog("Updating thread stopped. Stopping all the process", Enums.logType.ERROR);
                    }
                    //if (stoppedTh.Contains("Public"))
                    //{
                    //    stopStrategies();
                    //    await oManager.cancelAllOrders();
                    //    string market = stoppedTh.Replace("Public", "");
                    //    addLog("Public Connection to " + market + " lost reconnecting in 5 sec", Enums.logType.WARNING);
                    //    thManager.disposeThread(stoppedTh);
                    //    Thread.Sleep(5000);
                    //    if(!await qManager.connectPublicChannel((market)Enum.Parse(typeof(market),market)))
                    //    {
                    //        addLog("Failed to reconnect public. market:" + market, logType.ERROR);
                    //        return;
                    //    }
                    //    Thread.Sleep(5000);
                    //    foreach (var ins in qManager.instruments.Values)
                    //    {
                    //        market[] markets = [ins.market];
                    //        if (market == ins.market.ToString())
                    //        {
                    //            //if (ins.market == Exchange.Bybit)
                    //            //{
                    //            //    await crypto_client.subscribeBybitOrderBook(ins.baseCcy, ins.quoteCcy);
                    //            //}
                    //            //else if (ins.market == Exchange.Coinbase)
                    //            //{
                    //            //    await crypto_client.subscribeCoinbaseOrderBook(ins.baseCcy, ins.quoteCcy);
                    //            //}
                    //            //else
                    //            //{
                    //            //    await crypto_client.subscribeOrderBook(markets, ins.baseCcy, ins.quoteCcy);
                    //            //}
                    //            await crypto_client.subscribeOrderBook(markets, ins.baseCcy, ins.quoteCcy);
                    //            await crypto_client.subscribeTrades(markets, ins.baseCcy, ins.quoteCcy);
                    //        }
                    //    }
                    //    if (oManager.getVirtualMode())
                    //    {
                    //        //if (!qManager.setVirtualBalance(virtualBalanceFile))
                    //        //{

                    //        //}
                    //    }
                    //    else
                    //    {
                    //        Task t = Task.Run(async () =>
                    //        {
                    //            await qManager.refreshAndCancelAllorders();
                    //        });
                    //        foreach (var stg_obj in qManager.strategies.Values)
                    //        {
                    //            stg_obj.maker.resetInusePosition();
                    //            stg_obj.taker.resetInusePosition();
                    //            stg_obj.live_bidprice = 0;
                    //            stg_obj.live_buyorder_id = "";
                    //            stg_obj.live_askprice = 0;
                    //            stg_obj.live_sellorder_id = "";
                    //            for (int i = 0; i < stg_obj.live_buyorders.Count; ++i)
                    //            {
                    //                stg_obj.live_buyorders[i] = "";
                    //            }
                    //            for (int i = 0; i < stg_obj.live_sellorders.Count; ++i)
                    //            {
                    //                stg_obj.live_sellorders[i] = "";
                    //            }
                    //        }
                    //        t.Wait();
                    //        if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys))
                    //        {
                    //            int i = 0;
                    //            while (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys))
                    //            {
                    //                ++i;
                    //                if(i > 5)
                    //                {
                    //                    addLog("Failed to get balance.", logType.ERROR);
                    //                }
                    //                else
                    //                {
                    //                    addLog($"Trial {i}",logType.WARNING);
                    //                }
                    //                Thread.Sleep(i * 1000);
                    //            }

                    //        }
                    //        if (!qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys))
                    //        {
                    //            int i = 0;
                    //            while (!qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys))
                    //            {
                    //                ++i;
                    //                if (i > 5)
                    //                {
                    //                    addLog("Failed to get balance", logType.ERROR);
                    //                }
                    //                else
                    //                {
                    //                    addLog($"Trial {i}", logType.WARNING);
                    //                }
                    //                Thread.Sleep(i * 1000);
                    //            }

                    //        }
                    //        foreach (var stg_obj in qManager.strategies.Values)
                    //        {
                    //            stg_obj.adjustPosition();
                    //        }
                    //    }
                    //    addLog("Reconnection completed.");
                    //}
                    //else if (stoppedTh.Contains("Private"))
                    //{
                    //    stopStrategies();
                    //    await oManager.cancelAllOrders();
                    //    string market = stoppedTh.Replace("Private", "");
                    //    addLog("Private Connection to " + market + " lost reconnecting in 5 sec", Enums.logType.WARNING);
                    //    thManager.disposeThread(stoppedTh);
                    //    Thread.Sleep(5000);
                    //    if(!await oManager.connectPrivateChannel((market)Enum.Parse(typeof(market),market)))
                    //    {
                    //        addLog("Failed to reconnect private. market:" + market, logType.ERROR);
                    //        return;
                    //    }
                    //    Thread.Sleep(5000);
                    //    market[] markets = [(market)Enum.Parse(typeof(market), market)];
                    //    if (live || privateConnect)
                    //    {
                    //        await crypto_client.subscribeSpotOrderUpdates(markets);
                    //    }
                    //    if (oManager.getVirtualMode())
                    //    {
                    //        //if (!qManager.setVirtualBalance(virtualBalanceFile))
                    //        //{

                    //        //}
                    //    }
                    //    else
                    //    {
                    //        Task t = Task.Run(async () =>
                    //        {
                    //            await qManager.refreshAndCancelAllorders();
                    //        });
                    //        foreach (var stg_obj in qManager.strategies.Values)
                    //        {
                    //            stg_obj.maker.resetInusePosition();
                    //            stg_obj.taker.resetInusePosition();
                    //            stg_obj.live_bidprice = 0;
                    //            stg_obj.live_buyorder_id = "";
                    //            stg_obj.live_askprice = 0;
                    //            stg_obj.live_sellorder_id = "";
                    //            for (int i = 0; i < stg_obj.live_buyorders.Count; ++i)
                    //            {
                    //                stg_obj.live_buyorders[i] = "";
                    //            }
                    //            for (int i = 0; i < stg_obj.live_sellorders.Count; ++i)
                    //            {
                    //                stg_obj.live_sellorders[i] = "";
                    //            }
                    //        }
                    //        t.Wait();
                    //        if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys))
                    //        {
                    //            int i = 0;
                    //            while (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys))
                    //            {
                    //                ++i;
                    //                if (i > 5)
                    //                {
                    //                    addLog("Failed to get balance.", logType.ERROR);
                    //                }
                    //                else
                    //                {
                    //                    addLog($"Trial {i}", logType.WARNING);
                    //                }
                    //                Thread.Sleep(i * 1000);
                    //            }

                    //        }
                    //        if (!qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys))
                    //        {
                    //            int i = 0;
                    //            while (!qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys))
                    //            {
                    //                ++i;
                    //                if (i > 5)
                    //                {
                    //                    addLog("Failed to get balance", logType.ERROR);
                    //                }
                    //                else
                    //                {
                    //                    addLog($"Trial {i}", logType.WARNING);
                    //                }
                    //                Thread.Sleep(i * 1000);
                    //            }

                    //        }
                    //        foreach (var stg_obj in qManager.strategies.Values)
                    //        {
                    //            stg_obj.adjustPosition();
                    //        }
                    //    }
                    //    addLog("Reconnection completed.");
                    //}

                }
                //if (currentTradingState)
                //{
                //    startTrading();
                //}
            }

            if(current - lastConnectedTime > TimeSpan.FromHours(reconnectionPeriod))
            {
                addLog("Refreshing connection...");
                await refreshConnection();
            }

            bool order_refresh = false;

            using (var olock = oManager.order_lock.getlock())
            {
                foreach(var ord in oManager.live_orders.Values)
                {
                    if((ord.status == orderStatus.WaitCancel || ord.status == orderStatus.WaitOpen || ord.status == orderStatus.WaitMod) && (ord.timestamp.HasValue == false || (ord.timestamp.HasValue && current - ord.timestamp > TimeSpan.FromSeconds(10))))
                    {
                        order_refresh = true;
                        break;
                    }
                }
            }

            if(order_refresh)
            {
                bool currentTradingState = enabled;
                addLog("There are some orders pending more than 10 sec",logType.WARNING);
                stopStrategies();
                Task t = Task.Run(async () =>
                {
                    await qManager.refreshAndCancelAllorders();
                });
                foreach (var stg_obj in qManager.strategies.Values)
                {
                    stg_obj.maker.resetInusePosition();
                    stg_obj.taker.resetInusePosition();
                    stg_obj.live_bidprice = 0;
                    stg_obj.live_buyorder_id = "";
                    stg_obj.live_askprice = 0;
                    stg_obj.live_sellorder_id = "";
                    for (int i = 0; i < stg_obj.live_buyorders.Count; ++i)
                    {
                        stg_obj.live_buyorders[i] = "";
                    }
                    for (int i = 0; i < stg_obj.live_sellorders.Count; ++i)
                    {
                        stg_obj.live_sellorders[i] = "";
                    }
                }
                t.Wait();
                if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys))
                {
                    int i = 0;
                    while (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys))
                    {
                        ++i;
                        if (i > 5)
                        {
                            addLog("Failed to get balance.", logType.ERROR);
                        }
                        else
                        {
                            addLog($"Trial {i}", logType.WARNING);
                        }
                        Thread.Sleep(i * 1000);
                    }

                }
                if (!qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys))
                {
                    int i = 0;
                    while (!qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys))
                    {
                        ++i;
                        if (i > 5)
                        {
                            addLog("Failed to get balance", logType.ERROR);
                        }
                        else
                        {
                            addLog($"Trial {i}", logType.WARNING);
                        }
                        Thread.Sleep(i * 1000);
                    }

                }
                foreach (var stg_obj in qManager.strategies.Values)
                {
                    stg_obj.adjustPosition();
                }
                if (currentTradingState)
                {
                    startTrading();
                }
            }


             queueInfos["updateQuotes"].count = qManager.ordBookQueue.Count;
            if(qManager.ordBookQueue.Count > 10)
            {
                addLog("updateQuotes  " + queueInfos["updateQuotes"].count.ToString());
            }

            queueInfos["updateTrades"].count = qManager.tradeQueue.Count;
            if (qManager.tradeQueue.Count > 10)
            {
                addLog("updateTrades  " + queueInfos["updateTrades"].count.ToString());
            }

            queueInfos["updateOrders"].count = crypto_client.ordUpdateQueue.Count;
            if (crypto_client.ordUpdateQueue.Count > 10)
            {
                addLog("updateOrders  " + queueInfos["updateOrders"].count.ToString());
            }

            queueInfos["updateFills"].count = crypto_client.fillQueue.Count;
            if (crypto_client.fillQueue.Count > 10)
            {
                addLog("updateFills  " + queueInfos["updateFills"].count.ToString());
            }

            queueInfos["optimize"].count = qManager.optQueue.Count;
            if (qManager.optQueue.Count > 10)
            {
                addLog("optimize  " + queueInfos["optimize"].count.ToString());
            }

        }

        static async private Task refreshConnection()
        {
            bool currentTradingState = enabled;
            stopStrategies();
            await oManager.cancelAllOrders();
            List<string> ConnectionThreads = new List<string>();
            foreach (var th in thManager.threads.Keys)
            {
                if (th.Contains("Public") || th.Contains("Private"))
                {
                    ConnectionThreads.Add(th);
                }
            }
            foreach (var th_name in ConnectionThreads)
            {
                thManager.disposeThread(th_name);
            }
            Thread.Sleep(5000);
            foreach (var th_name in ConnectionThreads)
            {
                if(th_name.Contains("Public"))
                {
                    string market = th_name.Replace("Public", "");
                    if (!await qManager.connectPublicChannel((market)Enum.Parse(typeof(market), market)))
                    {
                        addLog("Failed to reconnect public. market:" + market, logType.ERROR);
                        return;
                    }
                    Thread.Sleep(5000);
                    foreach (var ins in qManager.instruments.Values)
                    {
                        market[] markets = [ins.market];
                        if (market == ins.market.ToString())
                        {
                            await crypto_client.subscribeOrderBook(markets, ins.baseCcy, ins.quoteCcy);
                            await crypto_client.subscribeTrades(markets, ins.baseCcy, ins.quoteCcy);
                        }
                    }
                }
                else if(th_name.Contains("Private"))
                {
                    string market = th_name.Replace("Private", "");
                    Thread.Sleep(5000);
                    if (!await oManager.connectPrivateChannel((market)Enum.Parse(typeof(market), market)))
                    {
                        addLog("Failed to reconnect private. market:" + market, logType.ERROR);
                        return;
                    }
                    Thread.Sleep(5000);
                    market[] markets = [(market)Enum.Parse(typeof(market), market)];
                    if (live || privateConnect)
                    {
                        await crypto_client.subscribeSpotOrderUpdates(markets);
                    }
                }
            }
            if (oManager.getVirtualMode())
            {
            }
            else
            {
                Task t = Task.Run(async () =>
                {
                    await qManager.refreshAndCancelAllorders();
                });
                foreach (var stg_obj in qManager.strategies.Values)
                {
                    stg_obj.maker.resetInusePosition();
                    stg_obj.taker.resetInusePosition();
                    stg_obj.live_bidprice = 0;
                    stg_obj.live_buyorder_id = "";
                    stg_obj.live_askprice = 0;
                    stg_obj.live_sellorder_id = "";
                    for (int i = 0; i < stg_obj.live_buyorders.Count; ++i)
                    {
                        stg_obj.live_buyorders[i] = "";
                    }
                    for (int i = 0; i < stg_obj.live_sellorders.Count; ++i)
                    {
                        stg_obj.live_sellorders[i] = "";
                    }
                }
                t.Wait();
                if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys))
                {
                    int i = 0;
                    while (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys), qManager._markets.Keys))
                    {
                        ++i;
                        if (i > 5)
                        {
                            addLog("Failed to get balance.", logType.ERROR);
                        }
                        else
                        {
                            addLog($"Trial {i}", logType.WARNING);
                        }
                        Thread.Sleep(i * 1000);
                    }

                }
                if (!qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys))
                {
                    int i = 0;
                    while (!qManager.setMarginPosition(await crypto_client.getMarginPos(qManager._markets.Keys), qManager._markets.Keys))
                    {
                        ++i;
                        if (i > 5)
                        {
                            addLog("Failed to get balance", logType.ERROR);
                        }
                        else
                        {
                            addLog($"Trial {i}", logType.WARNING);
                        }
                        Thread.Sleep(i * 1000);
                    }

                }
                foreach (var stg_obj in qManager.strategies.Values)
                {
                    stg_obj.adjustPosition();
                }
            }
            lastConnectedTime = DateTime.UtcNow;
            if(currentTradingState)
            {
                startTrading();
            }
        }
        static void setStrategyInfo()
        {
            foreach(var stg in strategies.Values)
            {
                strategyInfo stginfo = strategyInfos[stg.name];
                DataSpotOrderUpdate? ord;
                using (var olock = oManager.order_lock.getlock())
                {
                    string ord_id = stg.live_buyorder_id;
                    if (oManager.orders.ContainsKey(ord_id))
                    {
                        ord = oManager.orders[ord_id];
                        if (ord.status == orderStatus.Open)
                        {
                            stginfo.bid = ord.order_price;
                            stginfo.bidSize = ord.order_quantity;
                        }
                        else
                        {
                            stginfo.bid = stg.live_bidprice;
                            stginfo.bidSize = 0;
                        }
                    }
                    else
                    {
                        stginfo.bid = stg.live_bidprice;
                        stginfo.bidSize = 0;
                    }
                    ord_id = stg.live_sellorder_id;
                    if (oManager.orders.ContainsKey(ord_id))
                    {
                        ord = oManager.orders[ord_id];
                        if (ord.status == orderStatus.Open)
                        {
                            stginfo.ask = ord.order_price;
                            stginfo.askSize = ord.order_quantity;
                        }
                        else
                        {
                            stginfo.ask = stg.live_askprice;
                            stginfo.askSize = 0;
                        }
                    }
                    else
                    {
                        stginfo.ask = stg.live_askprice;
                        stginfo.askSize = 0;
                    }
                }
                stginfo.liquidity_ask = stg.taker.adjusted_bestask.Item1;
                stginfo.liquidity_bid = stg.taker.adjusted_bestbid.Item1;

                //stg.netExposure = stg.maker.baseBalance.total + stg.taker.baseBalance.total - stg.baseCcyQuantity;
                stg.netExposure = stg.maker.net_pos + stg.taker.net_pos;
                stg.notionalVolume = stg.maker.my_buy_notional + stg.maker.my_sell_notional;
                stg.posPnL = stg.SoD_baseCcyPos * (stg.taker.mid - stg.taker.open_mid);
                stg.tradingPnL = (stg.taker.my_sell_notional - stg.taker.my_sell_quantity * stg.taker.mid) + (stg.taker.my_buy_quantity * stg.taker.mid - stg.taker.my_buy_notional);
                stg.tradingPnL += (stg.maker.my_sell_notional - stg.maker.my_sell_quantity * stg.taker.mid) + (stg.maker.my_buy_quantity * stg.taker.mid - stg.maker.my_buy_notional);
                stg.totalFee = stg.taker.base_fee * stg.taker.mid + stg.taker.quote_fee + stg.maker.base_fee * stg.taker.mid + stg.maker.quote_fee;
                stg.totalPnL = stg.posPnL +  stg.tradingPnL - stg.totalFee;

                stginfo.notionalVolume = stg.notionalVolume;
                stginfo.posPnL = stg.posPnL;
                stginfo.tradingPnL = stg.tradingPnL;
                stginfo.totalFee= stg.totalFee;
                stginfo.totalPnL = stginfo.posPnL + stginfo.tradingPnL - stginfo.totalFee;

                stginfo.notionalVolumeB = stg.notionalVolumeB;
                stginfo.markupPnL = stg.markupPnL;
                stginfo.skewPnL = stg.skewPnL;
                stginfo.priceAdjPnL = stg.priceAdjPnL;
                stginfo.residualPnL = stg.residualPnL;
                stginfo.tradingPnLB = stg.tradingPnLB;

                stginfo.mi_volume = stg.mi_volume;
                foreach (var mi in stg.market_impact_curve)
                {
                    stginfo.market_impact_curve[mi.Key] = mi.Value;
                }

                stginfo.skew = stg.skew_point;
                stginfo.markup = stg.base_markup;
                if (stginfo.bid > 0 && stginfo.ask > 0)
                {
                    stginfo.spread = stginfo.ask - stginfo.bid;
                }
                else
                {
                    stginfo.spread = 0;
                }
            }
        }
        static void setInstrumentInfo()
        {
            Crypto_Trading.Exchange ex;
            decimal marginAvailable = 0;
            decimal marginUsed = 0;
            decimal unrealized_pnl = 0;
            foreach (var ins in qManager.instruments.Values)
            {
                instrumentInfo insinfo = instrumentInfos[ins.symbol_market];
                if(qManager.exchanges.ContainsKey(ins.market))
                {
                    ex = qManager.exchanges[ins.market];
                    marginAvailable = ex.getMarginAvailability();
                    unrealized_pnl = ex.getUnrealizedPnL();
                    marginUsed = ex.marginLocked;

                    if (marginAvailable < 0)
                    {
                        addLog("Invalid Margin Availability Exchange:" + ex.market);
                        decimal used = 0;
                        foreach (var b in ex.marginLong.Values)
                        {
                            used += b.total * b.current_price / b.leverage;
                        }
                        foreach (var b in ex.marginShort.Values)
                        {
                            used += b.total * b.current_price / b.leverage;
                        }
                        addLog($"Total:{ex.marginTotal}   Used:{used}   UnrealizedPnL:{unrealized_pnl}");
                    }
                }
                else
                {
                    addLog("Exchange not exist. " + ins.market.ToString());
                    marginAvailable = 0;
                    marginUsed = 0;
                }
                insinfo.baseCcy_total = ins.baseBalance.total;
                insinfo.baseCcy_inuse = ins.baseBalance.inuse;
                insinfo.quoteCcy_total = ins.quoteBalance.total;
                insinfo.quoteCcy_inuse = ins.quoteBalance.inuse;

                insinfo.long_total = ins.longPosition.total;
                insinfo.long_inuse = ins.longPosition.inuse;
                insinfo.long_avg_price = ins.longPosition.avg_price;
                insinfo.short_total = ins.shortPosition.total;
                insinfo.short_inuse = ins.shortPosition.inuse;
                insinfo.short_avg_price = ins.shortPosition.avg_price;

                insinfo.last_price = ins.last_price;
                insinfo.notional_buy = ins.buy_notional;
                insinfo.quantity_buy = ins.buy_quantity;
                insinfo.notional_sell = ins.sell_notional;
                insinfo.quantity_sell = ins.sell_quantity;
                insinfo.realized_volatility = ins.realized_volatility;
                insinfo.avg_RV = ins.avg_RV;

                insinfo.my_notional_buy = ins.my_buy_notional;
                insinfo.my_quantity_buy = ins.my_buy_quantity;
                insinfo.my_notional_sell = ins.my_sell_notional;
                insinfo.my_quantity_sell = ins.my_sell_quantity;

                insinfo.quoteFee_total = ins.quote_fee;
                insinfo.baseFee_total = ins.base_fee;
                insinfo.mi_volume = ins.mi_volume;

                insinfo.tradeImbalance = ins.tradeImbalance;
                insinfo.quoteImbalance = ins.quoteImbalance;
                insinfo.weightedMid = ins.weightedMid;

                insinfo.marginAvailable = marginAvailable;
                insinfo.marginInuse = marginUsed;
                insinfo.leverage = ins.leverage;

                foreach (var mi in ins.market_impact_curve)
                {
                    insinfo.market_impact_curve[mi.Key] = mi.Value;
                }
            }
        }

        static async Task broadcastInfos()
        {
            string msg;
            string json;
            try
            {
                json = JsonSerializer.Serialize(instrumentInfos, js_option);
                sendingItem["data_type"] = "instrument";
                sendingItem["data"] = json;
                msg = JsonSerializer.Serialize(sendingItem, js_option);
                await ws_server.BroadcastAsync(msg);

                json = JsonSerializer.Serialize(strategyInfos, js_option);
                sendingItem["data_type"] = "strategy";
                sendingItem["data"] = json;
                msg = JsonSerializer.Serialize(sendingItem, js_option);
                await ws_server.BroadcastAsync(msg);

                json = JsonSerializer.Serialize(threadStates, js_option);
                sendingItem["data_type"] = "thread";
                sendingItem["data"] = json;
                msg = JsonSerializer.Serialize(sendingItem, js_option);
                await ws_server.BroadcastAsync(msg);

                json = JsonSerializer.Serialize(connectionStates, js_option);
                sendingItem["data_type"] = "connection";
                sendingItem["data"] = json;
                msg = JsonSerializer.Serialize(sendingItem, js_option);
                await ws_server.BroadcastAsync(msg);

                json = JsonSerializer.Serialize(queueInfos, js_option);
                sendingItem["data_type"] = "queue";
                sendingItem["data"] = json;
                msg = JsonSerializer.Serialize(sendingItem, js_option);
                await ws_server.BroadcastAsync(msg);

            }
            catch (Exception ex)
            {
                addLog("Error Occured while broadcasting messages error:" + ex.Message,Enums.logType.WARNING);
            }
        }

        static async Task timer_PeriodicMsg_Tick()
        {
            string msg = "";
            List<market> ex_list = new List<market>();
            List<DataSpotOrderUpdate> ordList;
            int count_diff = 0;

            //To keep http_client alive.
            try
            {
                if(live)
                {
                    await crypto_client.getBalance(qManager._markets.Keys);
                    foreach (var stg in strategies.Values)
                    {
                        if(!ex_list.Contains(stg.maker.market))
                        {
                            ex_list.Add(stg.maker.market);
                            ordList = await crypto_client.getActiveOrders(stg.maker.market);
                            if (ordList != null)
                            {
                                int live_orders_count;
                                using (var olock = oManager.order_lock.getlock())
                                {
                                    live_orders_count = oManager.live_orders.Count;
                                }
                                if (ordList.Count != live_orders_count)
                                {
                                    if (count_diff == 0)
                                    {
                                        ++mismatch_count;
                                        count_diff = ordList.Count - live_orders_count;
                                    }
                                    else if (count_diff == ordList.Count - live_orders_count)
                                    {
                                        ++mismatch_count;
                                        if (mismatch_count >= 3)
                                        {
                                            addLog("Order count didn't match " + stg.maker.market + ":" + ordList.Count.ToString() + " live_orders:" + live_orders_count.ToString(), logType.WARNING);
                                            using (var olock = oManager.order_lock.getlock())
                                            {
                                                List<string> removing = new List<string>();
                                                foreach (var o in oManager.live_orders)
                                                {
                                                    if ((o.Value.status != orderStatus.WaitOpen && o.Value.status != orderStatus.Open && o.Value.status != orderStatus.WaitCancel))
                                                    {
                                                        addLog(o.Value.ToString());
                                                        removing.Add(o.Key);
                                                    }
                                                    else if (o.Value.side == orderSide.NONE)
                                                    {
                                                        addLog(o.Value.ToString());
                                                    }
                                                }
                                                foreach (var r in removing)
                                                {
                                                    oManager.live_orders.Remove(r);
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        count_diff = 0;
                                        mismatch_count = 0;
                                    }
                                }
                                else
                                {
                                    mismatch_count = 0;
                                }
                            }
                        }
                    }
                }
            }
            catch(Exception e)
            {
                addLog("Error occured during keepalive request", logType.WARNING);
                addLog(e.Message, logType.WARNING);
            }

            DateTime currentTime = DateTime.UtcNow;
            DataSpotOrderUpdate ord;
            while (oManager.order_pool.Count > 0)
            {
                ord = oManager.order_pool.Peek();
                if(ord != null)
                {
                    if (ord.update_time.HasValue)
                    {
                        if (currentTime - ord.update_time.Value > TimeSpan.FromSeconds(oManager.orderLifeTime))
                        {
                            ord = oManager.order_pool.Dequeue();
                            ord.init();
                            crypto_client.ordUpdateStack.push(ord);
                        }
                        else
                        {
                            if (oManager.order_pool.Count > 10000)
                            {
                                addLog("Something wrong in order_pool. timestamp of the head:" + ord.update_time.Value.ToString(GlobalVariables.tmMsecFormat));
                            }
                            break;
                        }
                    }
                    else
                    {
                        ord = oManager.order_pool.Dequeue();
                        ord.init();
                        crypto_client.ordUpdateStack.push(ord);
                    }
                }
            }

            crypto_client.checkStackCount();

            if(logEntryStack.Count < logSize / 10)
            {
                int i = 0;
                while(i < logSize / 5)
                {
                    logEntryStack.push(new logEntry());
                    ++i;
                }
            }

            if (DateTime.UtcNow > nextMsgTime)
            {
                await messagePnL(true);
                nextMsgTime += TimeSpan.FromMinutes(msg_Interval);

                if (crypto_client.gmocoin_client.GetSocketStatePrivate() == WebSocketState.Open)
                {
                    await crypto_client.gmocoin_client.extendToken(crypto_client.gmocoin_client.token);
                }
            }

            if (DateTime.UtcNow > endTime)
            {
                addLog("Closing application at EoD.");
                await EoDProcess();
                isRunning = false;
            }
        }

        static void outputMI()
        {
            Instrument ins;
            Strategy stg;
            MarketImpact mi;
            if(marketImpactFile != null)
            {
                while(marketImpactQueue.Count > 0)
                {
                    mi = marketImpactQueue.Dequeue();
                    if(strategies.ContainsKey(mi.stg_name))
                    {
                        stg = strategies[mi.stg_name];
                        stg.update_micurve(mi);
                    }
                    if(qManager.instruments.ContainsKey(mi.symbol_market) && mi.myOrder == false)
                    {
                        ins = qManager.instruments[mi.symbol_market];
                        ins.update_micurve(mi);
                    }
                    marketImpactFile.WriteLine(mi.ToString());
                    marketImpactFile.Flush();
                    mi.init();
                    oManager.MI_stack.push(mi);
                }
            }
        }

        static public void readMIFile(string filename)
        {
            int i = 0;
            MarketImpact mi;
            Strategy stg;
            Instrument ins;
            if(File.Exists(filename))
            {
                using (StreamReader sr = new StreamReader(new FileStream(filename, FileMode.Open, FileAccess.Read)))
                {
                    while (sr.ReadLine() is string line)
                    {
                        string[] arr = line.Split(",");
                        mi = oManager.MI_stack.pop();
                        if(mi == null)
                        {
                            mi = new MarketImpact();
                        }
                        mi.FromString(arr);
                        if (mi.myOrder == false)
                        {
                            if(qManager.instruments.ContainsKey(mi.symbol_market))
                            {
                                ins = qManager.instruments[mi.symbol_market];
                                ins.update_micurve(mi);
                            }
                        }
                        else
                        {
                            if(strategies.ContainsKey(mi.stg_name))
                            {
                                stg = strategies[mi.stg_name];
                                stg.update_micurve(mi);
                            }
                        }
                    }
                }
            }
            else
            {
                addLog("Market Impact file not found.");
            }
        }

        static private void addLog(string body, Enums.logType logtype = Enums.logType.INFO)
        {
            string messageline = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   [" + logtype.ToString() + "]" + body + "\n";
            logQueue.Enqueue(messageline);
            switch (logtype)
            {
                case Enums.logType.ERROR:
                case Enums.logType.FATAL:
                    MsgDeliverer.sendMessage(messageline, err_msg_type);
                    onError();
                    break;
                default:
                    break;

            }

            logEntry log;
            log = logEntryStack.pop();
            if(log == null)
            {
                log = new logEntry();
            }
            log.logtype = logtype.ToString();
            log.msg = messageline;
            ws_server.processLog(log);
        }
        static private void onError()
        {
            stopTrading(true);
        }

        static private void updateLog()
        {
            string line;
            while(true)
            {
                line = logQueue.Dequeue();
                if(line != null)
                {
                    Console.Write(line);

                    if (logFile != null)
                    {
                        logFile.WriteLine(line);
                        logFile.Flush();
                    }
                }
                else
                {
                    break;
                }
            }
        }
    }
}