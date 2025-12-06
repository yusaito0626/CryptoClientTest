using Binance.Net.Enums;
using Crypto_Clients;
using Crypto_Trading;
using CryptoClients.Net.Enums;
using CryptoExchange.Net.Logging.Extensions;
using CryptoExchange.Net.SharedApis;
using Discord;
using Enums;
using PubnubApi.EndPoint;
using System;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
//using Terminal.Gui;
using Utils;
using LockFreeStack;
using LockFreeQueue;
//using static Terminal.Gui.View;


namespace Crypto_Linux
{
    internal class Program
    {
        static string defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        static string logPath = Path.Combine(AppContext.BaseDirectory, "crypto.log");
        static string outputPath = AppContext.BaseDirectory;
        static string outputPath_org = AppContext.BaseDirectory;
        static string APIsPath = "";
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

        static Strategy selected_stg;
        static public Dictionary<string, Strategy> strategies;

        static bool enabled;

        static SISOQueue<string> logQueue;
        static SISOQueue<DataFill> filledOrderQueue;

        static Instrument selected_ins;

        static StreamWriter logFile;

        static private bool threadsStarted;
        static private int stopTradingCalled;
        static private bool aborting;

        static private bool autoStart;
        static private bool live;
        static private bool test;
        static private bool privateConnect;
        static private bool msgLogging;

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

        Thread ws_thread;

        static bool isRunning = false;
        CancellationTokenSource cts = new CancellationTokenSource();


        static int totalWidth;
        static int totalHeight;

        static int mainWidth;
        static int logWidth;
        static int mainHeight;
        static int commandHeight;

        const int LOGBUF_SIZE = 100;
        static string[] log_buffer = new string[LOGBUF_SIZE];
        static int log_index = 0;

        static int mismatch_count = 0;

        static int EoDProcessCalled = 0;
        static async Task Main(string[] args)
        {

            Console.CancelKeyPress += async (sender, e) =>
            {
                addLog("Terminating the app...");
                e.Cancel = true;
                await EoDProcess();
                isRunning = false;
            };

            AppDomain.CurrentDomain.ProcessExit += async (sender, eventArgs) =>
            {
                addLog("SIGTERM detected");

                await EoDProcess();
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
            aborting = false;
            threadsStarted = false;
            autoStart = false;
            live = false;
            test = false;
            privateConnect = true;
            msgLogging = false;

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

            //readAPIFiles(APIsPath);
            getAPIsFromEnv(live);

            foreach (var ins in qManager.instruments.Values)
            {
                instrumentInfo insInfo = new instrumentInfo();
                insInfo.symbol = ins.symbol;
                insInfo.market = ins.market;
                insInfo.symbol_market = ins.symbol_market;
                insInfo.baseCcy = ins.baseCcy;
                insInfo.quoteCcy = ins.quoteCcy;
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
                ms.market = ins.Value.market;
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
                setting.taker_market = stg.Value.taker_market;
                setting.maker_market = stg.Value.maker_market;
                setting.order_throttle = stg.Value.order_throttle;
                setting.markup = stg.Value.markup;
                setting.min_markup = stg.Value.min_markup;
                setting.max_skew = stg.Value.maxSkew;
                setting.skew_widening = stg.Value.skewWidening;
                setting.baseCcy_quantity = stg.Value.baseCcyQuantity;
                setting.ToBsize = stg.Value.ToBsize;
                setting.ToBsizeMultiplier = stg.Value.ToBsizeMultiplier;
                setting.intervalAfterFill = stg.Value.intervalAfterFill;
                setting.modThreshold = stg.Value.modThreshold;
                setting.skewThreshold = stg.Value.skewThreshold;
                setting.oneSideThreshold = stg.Value.oneSideThreshold;
                setting.decaying_time = stg.Value.markup_decay_basetime;
                setting.markupMultiplier = stg.Value.RVMarkup_multiplier;
                setting.markup_adjustment = stg.Value.markupAdjustment;
                setting.maxBaseMarkup = stg.Value.max_baseMarkup;
                setting.predictFill = stg.Value.predictFill;
                setting.skew_type = stg.Value.skew_type.ToString();
                setting.skew_step = stg.Value.skew_step;
                stgSettings[stg.Key] = setting;
            }
            await ws_server.setStrategySetting(stgSettings);

            if (!await MsgDeliverer.setDiscordToken(discordTokenFile))
            {
                addLog("Message configuration not found", Enums.logType.WARNING);
            }
            await tradePreparation(live);

            ws_server.StartAsync(CancellationToken.None);

            addLog("Latency check");

            i = 0;
            int trial = 3;
            Stopwatch sw = new Stopwatch();
            Dictionary<string, double> avgLatency = new Dictionary<string, double>();
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
                ++i;
            }

            foreach (var l in avgLatency)
            {
                addLog("Average Latency of " + l.Key + ": " + (l.Value / trial).ToString("N3") + " ms");
            }
            Thread.Sleep(5000);

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
                await testFunc();
            }


            isRunning = true;

            i = 0;
            while (isRunning)
            {
                sendFills();
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
                    string msg = stgPnLMsg();
                    Console.WriteLine(msg);
                    await timer_PeriodicMsg_Tick();
                    i = 0;
                }
                updateLog();

                Thread.Sleep(1000);
            }
            addLog("Exitting the main process...");
            Thread.Sleep(2000);
        }

        static private string stgPnLMsg()
        {
            decimal volumeAll = 0;
            decimal posPnLAll = 0;
            decimal tradingPLAll = 0;
            decimal feeAll = 0;
            decimal totalAll = 0;
            decimal prev_notionalAll = 0;
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
                    msg += "Maker Latency:\n";
                    msg += "<QuotesUpdate> All count:" + stg.maker.count_Allquotes.ToString("N0") + "  Latent Feed:" + stg.maker.count_Latentquotes.ToString("N0") + "\n";
                    msg += "<Trades> All count:" + stg.maker.count_AllTrade.ToString("N0") + "  Latent Feed:" + stg.maker.count_LatentTrade.ToString("N0") + "\n";
                    msg += "<OrderUpdate> All count:" + stg.maker.count_AllOrderUpdates.ToString("N0") + "  Latent Feed:" + stg.maker.count_LatentOrderUpdates.ToString("N0") + "\n";
                    msg += "<Fill> All count:" + stg.maker.count_AllFill.ToString("N0") + "  Latent Feed:" + stg.maker.count_LatentFill.ToString("N0") + "\n";
                    stg.maker.count_Allquotes = 0;
                    stg.maker.count_Latentquotes = 0;
                    stg.maker.count_AllTrade = 0;
                    stg.maker.count_LatentTrade = 0;
                    stg.maker.count_AllOrderUpdates = 0;
                    stg.maker.count_LatentOrderUpdates = 0;
                    stg.maker.count_AllFill = 0;
                    stg.maker.count_LatentFill = 0;
                    msg += "Cumulative Count:\n";
                    msg += "<QuotesUpdate> All count:" + stg.maker.cum_Allquotes.ToString("N0") + "  Latent Feed:" + stg.maker.cum_Latentquotes.ToString("N0") + "\n";
                    msg += "<Trades> All count:" + stg.maker.cum_AllTrade.ToString("N0") + "  Latent Feed:" + stg.maker.cum_LatentTrade.ToString("N0") + "\n";
                    msg += "<OrderUpdate> All count:" + stg.maker.cum_AllOrderUpdates.ToString("N0") + "  Latent Feed:" + stg.maker.cum_LatentOrderUpdates.ToString("N0") + "\n";
                    msg += "<Fill> All count:" + stg.maker.cum_AllFill.ToString("N0") + "  Latent Feed:" + stg.maker.cum_LatentFill.ToString("N0") + "\n";

                    stg.netExposure = stg.maker.baseBalance.total + stg.taker.baseBalance.total - stg.baseCcyQuantity;
                    stg.notionalVolume = stg.maker.my_buy_notional + stg.maker.my_sell_notional;
                    stg.posPnL = stg.SoD_baseCcyPos * (stg.taker.mid - stg.taker.open_mid);
                    stg.tradingPnL = (stg.taker.my_sell_notional - stg.taker.my_sell_quantity * stg.taker.mid) + (stg.taker.my_buy_quantity * stg.taker.mid - stg.taker.my_buy_notional);
                    stg.tradingPnL += (stg.maker.my_sell_notional - stg.maker.my_sell_quantity * stg.taker.mid) + (stg.maker.my_buy_quantity * stg.taker.mid - stg.maker.my_buy_notional);
                    stg.totalFee = stg.taker.base_fee * stg.taker.mid + stg.taker.quote_fee + stg.maker.base_fee * stg.taker.mid + stg.maker.quote_fee;
                    stg.totalPnL = stg.posPnL + stg.tradingPnL - stg.totalFee;

                    msg += DateTime.UtcNow.ToString() + " - Strategy " + stg.name + " -    \nNotional Volume:" + stg.notionalVolume.ToString("N2") + "\nNet Exposure:" + stg.netExposure.ToString("N" + stg.maker.quantity_scale) + "    [Maker Balance:" + stg.maker.baseBalance.total.ToString("N" + stg.maker.quantity_scale) + "]\nPosition PnL:" + stg.posPnL.ToString("N2") + "\nTrading PnL:" + stg.tradingPnL.ToString("N2") + "\nFee:" + stg.totalFee.ToString("N2") + "\nTotal:" + stg.totalPnL.ToString("N2") + "\n";

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
                    msg += "markup_bid:" + stg.temp_markup_bid.ToString("N2") + "  markup_ask:" + stg.temp_markup_ask.ToString("N2") + "   Base markup:" + stg.prev_markup.ToString("N2")  + "\n";

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
            return msg;
        }

        static private async Task testFunc()
        {
            Console.WriteLine("New stack and queue");

            Stopwatch sw = new Stopwatch();
            sw.Start();
            Thread.Sleep(1);
            sw.Stop();
            sw.Reset();
            sw.Start();
            Thread.Sleep(1);
            sw.Stop();
            sw.Reset();
            sw.Start();
            Thread.Sleep(1);
            sw.Stop();
            sw.Reset();

            LockFreeStack<logEntry> stack = new LockFreeStack<logEntry>(300000);
            MIMOQueue<logEntry> queue = new MIMOQueue<logEntry>(300000);

            int i = 0;
            while(i < 300000)
            {
                stack.push(new logEntry());
                ++i;
            }
            Console.WriteLine(stack.Count().ToString());
            sw.Start();
            var t1 = Task.Run(() =>
            {
                Console.WriteLine("Task1 started");
                int j = 0;
                while( j < 500000)
                {
                    if(stack.Count() < 1000)
                    {
                        int m = 0;
                        while(m < 1000)
                        {
                            stack.push(new logEntry());
                            ++m;
                        }
                    }
                    logEntry l = stack.pop();
                    //Console.WriteLine(stack.Count().ToString());
                    l.msg = "A-" + j.ToString();
                    ++j;
                    queue.Enqueue(l);
                }
                Console.WriteLine("Task1 completed");
            });
            var t2 = Task.Run(() =>
            {
                Console.WriteLine("Task2 started");
                int j = 0;
                while (j < 500000)
                {
                    if (stack.Count() < 1000)
                    {
                        int m = 0;
                        while (m < 1000)
                        {
                            stack.push(new logEntry());
                            ++m;
                        }
                    }
                    logEntry l = stack.pop();
                    //Console.WriteLine(stack.Count().ToString());
                    l.msg = "B-" + j.ToString();
                    ++j;
                    queue.Enqueue(l);
                }
                Console.WriteLine("Task2 completed");
            });
            var t3 = Task.Run(() =>
            {
                Console.WriteLine("Task3 started");
                int k = 0;
                int prev_msg_A = -1;
                int current_msg_A = -1;
                int prev_msg_B = -1;
                int current_msg_B = -1;
                string[] msg;
                while (true)
                {
                    logEntry l = queue.Dequeue();
                    if(l != null)
                    {
                        msg = l.msg.Split("-");
                        if (msg[0] == "A")
                        {
                            current_msg_A = int.Parse(msg[1]);
                            if (prev_msg_A >= 0 && current_msg_A != prev_msg_A + 1)
                            {
                                Console.WriteLine("A prev_msg:" + prev_msg_A.ToString() + " current:" + current_msg_A.ToString());
                            }
                            prev_msg_A = current_msg_A;
                        }
                        else if (msg[0] == "B")
                        {
                            current_msg_B = int.Parse(msg[1]);
                            if (prev_msg_B >= 0 && current_msg_B != prev_msg_B + 1)
                            {
                                Console.WriteLine("B prev_msg:" + prev_msg_B.ToString() + " current:" + current_msg_B.ToString());
                            }
                            prev_msg_B = current_msg_B;
                        }
                        else
                        {
                            Console.WriteLine("Something wrong.");
                        }
                            
                        l.msg = "";
                        stack.push(l);
                        ++k;
                        if(k >= 1000000)
                        {
                            break;
                        }
                    }
                }
                Console.WriteLine("Task3 completed");
            });
            t1.Wait();
            t2.Wait();
            t3.Wait();
            sw.Stop();

            Console.WriteLine("Time:" + sw.Elapsed.TotalMilliseconds + " msec");
            sw.Reset();
            Console.WriteLine("Concurrent Queue and Stack");
            ConcurrentStack<logEntry> cstack = new ConcurrentStack<logEntry>();
            ConcurrentQueue<logEntry> cqueue = new ConcurrentQueue<logEntry>();

            i = 0;
            while (i < 300000)
            {
                cstack.Push(new logEntry());
                ++i;
            }

            sw.Start();
            var t4 = Task.Run(() =>
            {
                Console.WriteLine("Task1 started");
                int j = 0;
                while (j < 500000)
                {
                    if (cstack.Count < 1000)
                    {
                        int m = 0;
                        while (m < 1000)
                        {
                            cstack.Push(new logEntry());
                            ++m;
                        }
                    }
                    logEntry l;
                    int chk = 0;
                    while(!cstack.TryPop(out l))
                    {
                        ++chk;
                        if(chk > 10000000)
                        {
                            Console.WriteLine("Stuck in the loop");
                            Console.WriteLine(cstack.Count.ToString());
                            break;
                        }
                    }
                    chk = 0;
                    l.msg = "A-" + j.ToString();
                    ++j;
                    cqueue.Enqueue(l);
                }
                Console.WriteLine("Task1 completed");
            });
            var t5 = Task.Run(() =>
            {
                Console.WriteLine("Task2 started");
                int j = 0;
                while (j < 500000)
                {
                    if (cstack.Count < 1000)
                    {
                        int m = 0;
                        while (m < 1000)
                        {
                            cstack.Push(new logEntry());
                            ++m;
                        }
                    }
                    logEntry l;

                    int chk = 0;
                    while (!cstack.TryPop(out l))
                    {
                        ++chk;
                        if (chk > 10000000)
                        {
                            Console.WriteLine("Stuck in the loop");
                            Console.WriteLine(cstack.Count.ToString());
                            break;
                        }
                    }
                    chk = 0;
                    //Console.WriteLine(stack.Count().ToString());
                    l.msg = "B-" + j.ToString();
                    ++j;
                    cqueue.Enqueue(l);
                }
                Console.WriteLine("Task2 completed");
            });
            var t6 = Task.Run(() =>
            {
                Console.WriteLine("Task3 started");
                int k = 0;
                int prev_msg_A = -1;
                int current_msg_A = -1;
                int prev_msg_B = -1;
                int current_msg_B = -1;
                string[] msg;
                while (true)
                {
                    logEntry l;
                    int chk = 0;
                    while(!cqueue.TryDequeue(out l))
                    {
                        ++chk;
                        if(chk == 10000)
                        {
                            chk = 0;
                            Thread.Yield();
                        }
                    }
                    if (l != null)
                    {
                        msg = l.msg.Split("-");
                        if (msg[0] == "A")
                        {
                            current_msg_A = int.Parse(msg[1]);
                            if (prev_msg_A >= 0 && current_msg_A != prev_msg_A + 1)
                            {
                                Console.WriteLine("A prev_msg:" + prev_msg_A.ToString() + " current:" + current_msg_A.ToString());
                            }
                            prev_msg_A = current_msg_A;
                        }
                        else if (msg[0] == "B")
                        {
                            current_msg_B = int.Parse(msg[1]);
                            if (prev_msg_B >= 0 && current_msg_B != prev_msg_B + 1)
                            {
                                Console.WriteLine("B prev_msg:" + prev_msg_B.ToString() + " current:" + current_msg_B.ToString());
                            }
                            prev_msg_B = current_msg_B;
                        }
                        else
                        {
                            Console.WriteLine("Something wrong.");
                        }

                        l.msg = "";
                        cstack.Push(l);
                        ++k;
                        if (k >= 1000000)
                        {
                            break;
                        }
                    }
                }
                Console.WriteLine("Task3 completed");
            });
            t4.Wait();
            t5.Wait();
            t6.Wait();
            sw.Stop();
            Console.WriteLine("Time:" + sw.Elapsed.TotalMilliseconds + " msec");

            Console.WriteLine("Completed");

            isRunning = false;
        }


        static private async Task sendFills()
        {
            DataFill fill;
            fillInfo fInfo;
            while (filledOrderQueue.Count() > 0)
            {
                fill = filledOrderQueue.Dequeue();
                //while(!filledOrderQueue.TryDequeue(out fill))
                //{

                //}
                if(fill != null)
                {
                    if (fillInfoStack.Count() > 0)
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
                        fInfo.market = fill.market;
                        fInfo.symbol = fill.symbol;
                        fInfo.side = fill.side.ToString();
                        fInfo.fill_price = fill.price.ToString("N" + ins.price_scale);
                        fInfo.quantity = fill.quantity.ToString("N" + ins.quantity_scale);
                        fInfo.fee = (fill.fee_quote + fill.fee_base * fill.price).ToString();
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
            if (root.TryGetProperty("autoStart", out elem))
            {
                autoStart = elem.GetBoolean();
            }
            else
            {
                autoStart = false;
            }
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
            if (root.TryGetProperty("msgLogging", out elem))
            {
                msgLogging = elem.GetBoolean();
            }
            else
            {
                msgLogging = false;
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
                    APIList.Add(mkt.ToUpper() + "_" + tradeState);
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
                crypto_client.setCredentials(mkt, api_name, api_key);
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
                    stginfo.maker_market = stg.maker_market;
                    stginfo.taker_market = stg.taker_market;
                    stginfo.maker_symbol_market = stg.maker_symbol_market;
                    stginfo.taker_symbol_market = stg.taker_symbol_market;
                    stginfo.name = stg.name;
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

                foreach (var mkt in qManager._markets)
                {
                    if (msgLogging)
                    {
                        Func<Action, Action, CancellationToken, int, Task<bool>>?  func = crypto_client.setMsgLogging(mkt.Key, outputPath);
                        if(func != null)
                        {
                            thManager.addThread(mkt.Key + "_msgLogging", func, null, null, 1);
                        }
                    }
                    await qManager.connectPublicChannel(mkt.Key);
                    if (liveTrading || privateConnect)
                    {
                        await oManager.connectPrivateChannel(mkt.Key);
                    }
                    connectionStates[mkt.Key] = new connecitonStatus() { market = mkt.Key, publicState = WebSocketState.None.ToString(), privateState = WebSocketState.None.ToString(), avgRTT = 0.0 };
                }

                foreach (var ins in qManager.instruments.Values)
                {
                    string[] markets = [ins.market];
                    if (ins.market == Exchange.Bybit)
                    {
                        await crypto_client.subscribeBybitOrderBook(ins.baseCcy, ins.quoteCcy);
                    }
                    else if (ins.market == Exchange.Coinbase)
                    {
                        await crypto_client.subscribeCoinbaseOrderBook(ins.baseCcy, ins.quoteCcy);
                    }
                    else
                    {
                        await crypto_client.subscribeOrderBook(markets, ins.baseCcy, ins.quoteCcy);
                    }
                    await crypto_client.subscribeTrades(markets, ins.baseCcy, ins.quoteCcy);
                }

                if (oManager.getVirtualMode())
                {
                    if (!qManager.setVirtualBalance(virtualBalanceFile))
                    {
                        return false;
                    }
                    foreach(var stg in strategies)
                    {
                        stg.Value.taker.baseBalance.total = stg.Value.baseCcyQuantity / 2;
                        stg.Value.maker.baseBalance.total = stg.Value.baseCcyQuantity / 2;
                    }
                }
                else
                {
                    
                    string SoDPosFile = outputPath + "/SoD_Position.csv";
                    if (!File.Exists(SoDPosFile))
                    {
                        if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys)))
                        {
                            return false;
                        }
                        StreamWriter sw = new StreamWriter(new FileStream(SoDPosFile, FileMode.Create, FileAccess.Write));
                        sw.WriteLine("timestamp,symbol,market,symbol_market,base_ccy,quote_ccy,baseccy_balance,quoteccy_balance,open_mid");
                        string currentTime = DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                        foreach (var ins in qManager.instruments.Values)
                        {
                            decimal mid = await crypto_client.getCurrentMid(ins.market, ins.symbol);
                            string line = currentTime + "," + ins.symbol + "," + ins.market + "," + ins.symbol_market + "," + ins.baseCcy + "," + ins.quoteCcy + "," + ins.baseBalance.total.ToString() + "," + ins.quoteBalance.total.ToString() + "," + mid.ToString();
                            ins.SoD_baseBalance.total = ins.baseBalance.total;
                            ins.SoD_baseBalance.ccy = ins.baseBalance.ccy;
                            ins.SoD_baseBalance.market = ins.baseBalance.market;
                            ins.SoD_quoteBalance.total = ins.quoteBalance.total;
                            ins.SoD_quoteBalance.ccy = ins.quoteBalance.ccy;
                            ins.SoD_quoteBalance.market = ins.quoteBalance.market;
                            ins.open_mid = mid;
                            sw.WriteLine(line);
                            sw.Flush();
                        }
                        sw.Close();
                        sw.Dispose();
                    }
                    else
                    {
                        addLog("SoD file found File:" + SoDPosFile);
                        StreamReader sr = new StreamReader(new FileStream(SoDPosFile, FileMode.Open, FileAccess.Read));
                        while(sr.ReadLine() is string line)
                        {
                            if(line.StartsWith("timestamp"))
                            {
                                continue;
                            }
                            string[] items = line.Split(',');
                            if(items.Length >= 8)
                            {
                                string symbol_market = items[3];
                                if (qManager.instruments.ContainsKey(symbol_market))
                                {
                                    Instrument ins = qManager.instruments[symbol_market];
                                    ins.SoD_baseBalance.ccy = ins.baseCcy;
                                    ins.SoD_baseBalance.market = ins.market;
                                    ins.SoD_baseBalance.total = decimal.Parse(items[6]);
                                    ins.SoD_quoteBalance.ccy = ins.quoteCcy;
                                    ins.SoD_quoteBalance.market = ins.market;
                                    ins.SoD_quoteBalance.total = decimal.Parse(items[7]);
                                    ins.open_mid = decimal.Parse(items[8]);

                                    ins.baseBalance.total = ins.SoD_baseBalance.total;
                                    ins.baseBalance.ccy = ins.SoD_baseBalance.ccy;
                                    ins.baseBalance.market = ins.SoD_baseBalance.market;
                                    ins.quoteBalance.total = ins.SoD_quoteBalance.total;
                                    ins.quoteBalance.ccy = ins.SoD_quoteBalance.ccy;
                                    ins.quoteBalance.market = ins.SoD_quoteBalance.market;
                                }
                            }
                        }
                        SortedDictionary<DateTime,DataFill> histFill = new SortedDictionary<DateTime, DataFill>();
                        foreach (var mkt in qManager._markets.Keys)
                        {
                            DateTime currentTime = DateTime.UtcNow;
                            List<DataFill> temp_histFill = await crypto_client.getTradeHistory(mkt, DateTime.UtcNow.Date);
                            foreach(var fill in temp_histFill)
                            {
                                if(fill.filled_time == null)
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
                        foreach (var fill in histFill.Values)
                        {
                            string symbol_market = fill.symbol_market;
                            if (qManager.instruments.ContainsKey(symbol_market))
                            {
                                Instrument ins = qManager.instruments[symbol_market];
                                ins.updateFills(fill);
                                filledOrderQueue.Enqueue(fill);
                            }
                        }
                        //Just in case, update balance again
                        if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys)))
                        {
                            return false;
                        }
                    }
                }
                qManager.ready = true;

                if(File.Exists(intradayPnLFile))
                {
                    using (StreamReader sr = new StreamReader(new FileStream(intradayPnLFile, FileMode.Open, FileAccess.Read)))
                    {
                        List<intradayPnL> pnls = new List<intradayPnL>();
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
                        if (pnls.Count > 0)
                        {
                            ws_server.processIntradayPnL(pnls);
                        }
                    }
                }
                else
                {
                    addLog("Intraday PnL file not found");
                }

                if (liveTrading || privateConnect)
                {
                    await crypto_client.subscribeSpotOrderUpdates(qManager._markets.Keys);
                }

                oManager.ready = true;

                thManager.addThread("updateQuotes", qManager.updateQuotes, qManager.updateQuotesOnClosing, qManager.updateQuotesOnError, 10000);
                thManager.addThread("updateTrades", qManager.updateTrades, qManager.updateTradesOnClosing, qManager.updateTradesOnClosing, 10000);
                thManager.addThread("updateOrders", oManager.updateOrders, oManager.updateOrdersOnClosing, oManager.updateOrdersOnError,10000);
                thManager.addThread("updateFill", oManager.updateFills, oManager.updateFillOnClosing, null, 0);
                thManager.addThread("optimize", qManager.optimize, qManager.optimizeOnClosing, qManager.optimizeOnError,1000);
                thManager.addThread("orderLogging", oManager.orderLogging, oManager.ordLoggingOnClosing, oManager.ordLoggingOnError,1);

                foreach(var th in thManager.threads)
                {
                    Console.WriteLine(th.Key);
                    threadStates[th.Key] = new threadStatus() { name = th.Key, isRunning = false };
                }
                queueInfos["updateQuotes"] = new queueInfo() { name = "updateQuotes", count = 0};
                queueInfos["updateTrades"] = new queueInfo() { name = "updateTrades", count = 0 };
                queueInfos["updateOrders"] = new queueInfo() { name = "updateOrders", count = 0 };
                queueInfos["updateFills"] = new queueInfo() { name = "updateFills", count = 0 };
                queueInfos["optimize"] = new queueInfo() { name = "optimize", count = 0 };

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

                    addLog("Checking balance...");
                    
                    foreach (var stg in strategies.Values)
                    {

                        stg.SoD_baseCcyPos = (stg.maker.SoD_baseBalance.total + stg.taker.SoD_baseBalance.total) - stg.baseCcyQuantity;
                        addLog("SoD Balance strategy " + stg.name + "  Balance:" + stg.SoD_baseCcyPos.ToString());
                        decimal baseBalance_diff = stg.baseCcyQuantity - (stg.maker.baseBalance.total + stg.taker.baseBalance.total);
                        //stg.SoD_baseCcyPos = - baseBalance_diff;
                        orderSide side = orderSide.Buy;
                        if (baseBalance_diff < 0)
                        {
                            baseBalance_diff *= -1;
                            side = orderSide.Sell;
                        }
                        baseBalance_diff = Math.Round(baseBalance_diff / stg.taker.quantity_unit) * stg.taker.quantity_unit;
                        stg.lastPosAdjustment = DateTime.UtcNow;
                        addLog("The current balance of " + stg.name + " BaseCcy:" + (stg.maker.baseBalance.total + stg.taker.baseBalance.total).ToString() + " QuoteCcy:" + (stg.maker.quoteBalance.total + stg.taker.quoteBalance.total).ToString());
                        addLog("Adjustment at the start: " + side.ToString() + " " + baseBalance_diff.ToString());
                        if(baseBalance_diff > 0)
                        {
                            await oManager.placeNewSpotOrder(stg.taker, side, orderType.Market, baseBalance_diff, 0, null, true, false);
                        }

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
                                        oManager.ordIdMapping[ord.market + ord.order_id] = ord.market + ord.order_id;
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
                        foreach (var m in qManager._markets)
                        {
                            qManager.setBalance(await crypto_client.getBalance([m.Key]));
                        }
                    }
                    

                    

                    foreach(var stg in strategies.Values)
                    {
                        decimal baseBalance_diff = stg.baseCcyQuantity - (stg.maker.baseBalance.total + stg.taker.baseBalance.total);
                        orderSide side = orderSide.Buy;
                        if(baseBalance_diff < 0)
                        {
                            baseBalance_diff *= -1;
                            side = orderSide.Sell;
                        }
                        baseBalance_diff = Math.Round(baseBalance_diff / stg.taker.quantity_unit) * stg.taker.quantity_unit;
                        addLog("EoD balance of " + stg.name + " BaseCcy:" + (stg.maker.baseBalance.total + stg.taker.baseBalance.total).ToString() + " QuoteCcy:" + (stg.maker.quoteBalance.total + stg.taker.quoteBalance.total).ToString());
                        if(baseBalance_diff >= stg.taker.quantity_unit)
                        {
                            addLog("Adjustment at EoD: " + side.ToString() + " " + baseBalance_diff.ToString());
                            oManager.placeNewSpotOrder(stg.taker, side, orderType.Market, baseBalance_diff, 0, null, true, false);
                        }
                    }

                    Thread.Sleep(1000);

                    string msg = "EoD PnL\n" + stgPnLMsg();

                    await MsgDeliverer.sendMessage(msg, msg_type);

                    addLog(msg);

                    if(ws_server.intradayPnLList.Count > 0)
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

                    if(live)
                    {
                        addLog("Updating the performance file...");
                        string performanceFile = outputPath_org + "/performance.csv";

                        List<string> lines;
                        if (!File.Exists(performanceFile))
                        {
                            lines = new List<string>();
                            lines.Add("date,strategy,baseBalance_open,quoteBalance_open,baseBalance_close,quoteBalance_close,baseHedge_quantity,notional_volume,open_mid,close_mid,TotalPnL,pos_diff");

                        }
                        else
                        {
                            lines = File.ReadAllLines(performanceFile).ToList();
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
                            decimal baseBalance_open = stg.maker.SoD_baseBalance.total + stg.taker.SoD_baseBalance.total;
                            decimal quoteBalance_open = stg.maker.SoD_quoteBalance.total + stg.taker.SoD_quoteBalance.total;
                            decimal baseBalance_close = stg.maker.baseBalance.total + stg.taker.baseBalance.total;
                            decimal quoteBalance_close = stg.maker.quoteBalance.total + stg.taker.quoteBalance.total;

                            stg.taker.mid = await crypto_client.getCurrentMid(stg.taker.market, stg.taker.symbol);
                            stg.maker.mid = await crypto_client.getCurrentMid(stg.maker.market, stg.taker.symbol);

                            decimal notionalVolume = stg.maker.my_buy_notional + stg.maker.my_sell_notional;

                            decimal totalPnL = (baseBalance_open - stg.baseCcyQuantity) * (stg.taker.mid - stg.taker.open_mid)
                                + (stg.taker.my_sell_notional - stg.taker.my_sell_quantity * stg.taker.mid) + (stg.taker.my_buy_quantity * stg.taker.mid - stg.taker.my_buy_notional)
                                + (stg.maker.my_sell_notional - stg.maker.my_sell_quantity * stg.taker.mid) + (stg.maker.my_buy_quantity * stg.taker.mid - stg.maker.my_buy_notional)
                                - (stg.taker.base_fee * stg.taker.mid + stg.taker.quote_fee + stg.maker.base_fee * stg.taker.mid + stg.maker.quote_fee);
                            decimal pos_diff = baseBalance_close * stg.taker.mid - baseBalance_open * stg.taker.open_mid - stg.baseCcyQuantity * (stg.taker.mid - stg.taker.open_mid)
                                + quoteBalance_close - quoteBalance_open;

                            string line = today + "," + stg.name + "," + baseBalance_open.ToString() + "," + quoteBalance_open.ToString() + ","
                                + baseBalance_close.ToString() + "," + quoteBalance_close.ToString() + "," + stg.baseCcyQuantity.ToString() + "," + notionalVolume.ToString() + "," + stg.taker.open_mid.ToString() + "," + stg.taker.mid.ToString() + ","
                                + totalPnL.ToString() + "," + pos_diff.ToString();

                            lines.Add(line);
                        }

                        File.WriteAllLines(performanceFile, lines);
                    }
                    
                    string dt = (DateTime.UtcNow + TimeSpan.FromDays(1)).ToString("yyyy-MM-dd");
                    string newpath = outputPath_org + "/" + dt;
                    if (!Directory.Exists(newpath))
                    {
                        Directory.CreateDirectory(newpath);
                    }
                    
                    addLog("Exporting SoD position file...");
                    string SoDPosFile = newpath + "/SoD_Position.csv";
                    qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys));

                    StreamWriter sw = new StreamWriter(new FileStream(SoDPosFile, FileMode.Create, FileAccess.Write));
                    sw.WriteLine("timestamp,symbol,market,symbol_market,base_ccy,quote_ccy,baseccy_balance,quoteccy_balance,open_mid");
                    string currentTime = DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                    foreach (var ins in qManager.instruments.Values)
                    {
                        //decimal mid = await crypto_client.getCurrentMid(ins.market, ins.symbol);
                        string line = currentTime + "," + ins.symbol + "," + ins.market + "," + ins.symbol_market + "," + ins.baseCcy + "," + ins.quoteCcy + "," + ins.baseBalance.total.ToString() + "," + ins.quoteBalance.total.ToString() + "," + ins.mid.ToString();
                        //ins.SoD_baseBalance.total = ins.baseBalance.total;
                        //ins.SoD_baseBalance.ccy = ins.baseBalance.ccy;
                        //ins.SoD_baseBalance.market = ins.baseBalance.market;
                        //ins.SoD_quoteBalance.total = ins.quoteBalance.total;
                        //ins.SoD_quoteBalance.ccy = ins.quoteBalance.ccy;
                        //ins.SoD_quoteBalance.market = ins.quoteBalance.market;
                        //ins.open_mid = mid;
                        sw.WriteLine(line);
                        sw.Flush();
                    }
                    sw.Close();
                    sw.Dispose();

                    foreach (var th in thManager.threads)
                    {
                        th.Value.stop();
                    }
                }
            }
        }

        static async Task statusCheck()
        {
            //Declare variables that store the latency and status.
            //Connection
            connecitonStatus status;
            qManager.checkConnections();
            oManager.checkConnections();
            foreach (var mkt in qManager._markets)
            {
                status = connectionStates[mkt.Key];
                status.publicState = mkt.Value.ToString();
            }

            foreach (var mkt in oManager.connections)
            {
                status = connectionStates[mkt.Key];
                status.privateState = mkt.Value.ToString();
                switch (mkt.Key)
                {
                    case "bitbank":
                        status.avgRTT = crypto_client.bitbank_client.avgLatency() / 1000;
                        break;
                    case "coincheck":
                        status.avgRTT = crypto_client.coincheck_client.avgLatency() / 1000;
                        break;
                    case "bittrade":
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
                bool currentTradingState = enabled;

                foreach (var stoppedTh in stoppedThreads)
                {
                    if (stoppedTh.Contains("Public"))
                    {
                        stopStrategies();
                        await oManager.cancelAllOrders();
                        string market = stoppedTh.Replace("Public", "");
                        addLog("Public Connection to " + market + " lost reconnecting in 5 sec", Enums.logType.WARNING);
                        thManager.disposeThread(stoppedTh);
                        Thread.Sleep(5000);
                        await qManager.connectPublicChannel(market);
                        Thread.Sleep(5000);
                        foreach (var ins in qManager.instruments.Values)
                        {
                            string[] markets = [ins.market];
                            if (market == ins.market)
                            {
                                if (ins.market == Exchange.Bybit)
                                {
                                    await crypto_client.subscribeBybitOrderBook(ins.baseCcy, ins.quoteCcy);
                                }
                                else if (ins.market == Exchange.Coinbase)
                                {
                                    await crypto_client.subscribeCoinbaseOrderBook(ins.baseCcy, ins.quoteCcy);
                                }
                                else
                                {
                                    await crypto_client.subscribeOrderBook(markets, ins.baseCcy, ins.quoteCcy);
                                }
                                await crypto_client.subscribeTrades(markets, ins.baseCcy, ins.quoteCcy);
                            }
                        }
                        if (oManager.getVirtualMode())
                        {
                            //if (!qManager.setVirtualBalance(virtualBalanceFile))
                            //{

                            //}
                        }
                        else
                        {
                            Task t = Task.Run(async () =>
                            {
                                await qManager.refreshAndCancelAllorders();
                            });
                            foreach (var stg_obj in qManager.strategies.Values)
                            {
                                stg_obj.maker.baseBalance.inuse = 0;
                                stg_obj.maker.quoteBalance.inuse = 0;
                                stg_obj.live_bidprice = 0;
                                stg_obj.live_buyorder_id = "";
                                stg_obj.live_askprice = 0;
                                stg_obj.live_sellorder_id = "";
                            }
                            t.Wait();
                            if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys)))
                            {

                            }
                            foreach (var stg_obj in qManager.strategies.Values)
                            {
                                stg_obj.adjustPosition();
                            }
                        }
                        addLog("Reconnection completed.");
                    }
                    else if (stoppedTh.Contains("Private"))
                    {
                        stopStrategies();
                        await oManager.cancelAllOrders();
                        string market = stoppedTh.Replace("Private", "");
                        addLog("Private Connection to " + market + " lost reconnecting in 5 sec", Enums.logType.WARNING);
                        thManager.disposeThread(stoppedTh);
                        Thread.Sleep(5000);
                        await oManager.connectPrivateChannel(market);
                        Thread.Sleep(5000);
                        string[] markets = [market];
                        if (live || privateConnect)
                        {
                            await crypto_client.subscribeSpotOrderUpdates(markets);
                        }
                        if (oManager.getVirtualMode())
                        {
                            //if (!qManager.setVirtualBalance(virtualBalanceFile))
                            //{

                            //}
                        }
                        else
                        {
                            Task t = Task.Run(async () =>
                            {
                                await qManager.refreshAndCancelAllorders();
                            });
                            foreach (var stg_obj in qManager.strategies.Values)
                            {
                                stg_obj.maker.baseBalance.inuse = 0;
                                stg_obj.maker.quoteBalance.inuse = 0;
                                stg_obj.live_bidprice = 0;
                                stg_obj.live_buyorder_id = "";
                                stg_obj.live_askprice = 0;
                                stg_obj.live_sellorder_id = "";
                            }
                            t.Wait();
                            if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys)))
                            {

                            }
                            foreach (var stg_obj in qManager.strategies.Values)
                            {
                                stg_obj.adjustPosition();
                            }
                        }
                        addLog("Reconnection completed.");
                    }
                    else
                    {
                        addLog("Updating thread stopped. Stopping all the process", Enums.logType.ERROR);
                    }
                }
                if (currentTradingState)
                {
                    startTrading();
                }
            }


             queueInfos["updateQuotes"].count = qManager.ordBookQueue.Count();
            if(qManager.ordBookQueue.Count() > 10)
            {
                addLog("updateQuotes  " + queueInfos["updateQuotes"].count.ToString());
            }

            queueInfos["updateTrades"].count = qManager.tradeQueue.Count();
            if (qManager.tradeQueue.Count() > 10)
            {
                addLog("updateTrades  " + queueInfos["updateTrades"].count.ToString());
            }

            queueInfos["updateOrders"].count = crypto_client.ordUpdateQueue.Count();
            if (crypto_client.ordUpdateQueue.Count() > 10)
            {
                addLog("updateOrders  " + queueInfos["updateOrders"].count.ToString());
            }

            queueInfos["updateFills"].count = crypto_client.fillQueue.Count();
            if (crypto_client.fillQueue.Count() > 10)
            {
                addLog("updateFills  " + queueInfos["updateFills"].count.ToString());
            }

            queueInfos["optimize"].count = qManager.optQueue.Count();
            if (qManager.optQueue.Count() > 10)
            {
                addLog("optimize  " + queueInfos["optimize"].count.ToString());
            }

        }

        static void setStrategyInfo()
        {
            foreach(var stg in strategies.Values)
            {
                strategyInfo stginfo = strategyInfos[stg.name];
                DataSpotOrderUpdate? ord;
                if(oManager.orders.ContainsKey(stg.live_buyorder_id))
                {
                    ord = oManager.orders[stg.live_buyorder_id];
                    if(ord.status == orderStatus.Open)
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
                if (oManager.orders.ContainsKey(stg.live_sellorder_id))
                {
                    ord = oManager.orders[stg.live_sellorder_id];
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

                stginfo.liquidity_ask = stg.taker.adjusted_bestask.Item1;
                stginfo.liquidity_ask = stg.taker.adjusted_bestbid.Item1;

                stg.netExposure = stg.maker.baseBalance.total + stg.taker.baseBalance.total - stg.baseCcyQuantity;
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
            foreach(var ins in qManager.instruments.Values)
            {
                instrumentInfo insinfo = instrumentInfos[ins.symbol_market];

                insinfo.baseCcy_total = ins.baseBalance.total;
                insinfo.baseCcy_inuse = ins.baseBalance.inuse;
                insinfo.quoteCcy_total = ins.quoteBalance.total;
                insinfo.quoteCcy_inuse = ins.quoteBalance.inuse;

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
            List<DataSpotOrderUpdate> ordList = new List<DataSpotOrderUpdate>();
            int count_diff = 0;

            //To keep http_client alive.
            try
            {
                await crypto_client.getBalance(qManager._markets.Keys);

                if(live)
                {
                    foreach (var stg in strategies.Values)
                    {
                        ordList = await crypto_client.getActiveOrders(stg.maker.market);
                        if (ordList != null)
                        {
                            int live_orders_count = oManager.live_orders.Count;
                            if (ordList.Count != live_orders_count)
                            {
                                if(count_diff == 0)
                                {
                                    ++mismatch_count;
                                    count_diff = ordList.Count - live_orders_count;
                                }
                                else if(count_diff == ordList.Count - live_orders_count)
                                {
                                    ++mismatch_count;
                                    if (mismatch_count >= 3)
                                    {
                                        addLog("Order count didn't match " + stg.maker.market + ":" + ordList.Count.ToString() + " live_orders:" + live_orders_count.ToString(), logType.WARNING);
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
            catch(Exception e)
            {
                addLog("Error occured during keepalive request", logType.WARNING);
                addLog(e.Message, logType.WARNING);
            }

            DateTime currentTime = DateTime.UtcNow;
            DataSpotOrderUpdate ord;
            while (oManager.order_pool.Count() > 0)
            {
                //while (!oManager.SISO_order_pool.TryPeek(out ord))
                //{
                //}
                ord = oManager.order_pool.Peak();
                if(ord != null)
                {
                    if (ord.update_time.HasValue)
                    {
                        if (currentTime - ord.update_time.Value > TimeSpan.FromSeconds(oManager.orderLifeTime))
                        {
                            //while (!oManager.SISO_order_pool.TryDequeue(out ord))
                            //{
                            //}
                            ord = oManager.order_pool.Dequeue();
                            ord.init();
                            crypto_client.ordUpdateStack.push(ord);
                        }
                        else
                        {
                            if (oManager.order_pool.Count() > 10000)
                            {
                                addLog("Something wrong in order_pool. timestamp of the head:" + ord.update_time.Value.ToString(GlobalVariables.tmMsecFormat));
                            }
                            break;
                        }
                    }
                    else
                    {
                        //while (!oManager.SISO_order_pool.TryDequeue(out ord))
                        //{
                        //}
                        ord = oManager.order_pool.Dequeue();
                        ord.init();
                        crypto_client.ordUpdateStack.push(ord);
                    }
                }
            }

            crypto_client.checkStackCount();

            if(logEntryStack.Count() < logSize / 10)
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
                msg = stgPnLMsg();
                await MsgDeliverer.sendMessage(msg,msg_type);
                nextMsgTime += TimeSpan.FromMinutes(msg_Interval);

                foreach (var stg in strategies.Values)
                {
                    addLog("Internal Latency of onFill");
                    addLog("Part1:" + stg.onFill_latency1.ToString("N3") + "micro sec");
                    addLog("Part2:" + stg.onFill_latency2.ToString("N3") + "micro sec");
                    addLog("Part3:" + stg.onFill_latency3.ToString("N3") + "micro sec");
                    addLog("Placing Orders:" + stg.placingOrderLatencyOnFill.ToString("N3") + "micro sec");
                    addLog("Placing Orders[updateOrder]:" + stg.placingOrderLatencyUpdate.ToString("N3") + "micro sec");
                }
            }

            //foreach(var l in oManager.Latency)
            //{
            //    addLog($"Process Time[{l.Key}]    average:{l.Value.avgLatency.ToString("N3")} count:{l.Value.count.ToString("N0")}");
            //}

            if (DateTime.UtcNow > endTime)
            {
                addLog("Closing application at EoD.");
                await EoDProcess();
                isRunning = false;
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
            //int i = 0;
            //while(!logEntryStack.TryPop(out log))
            //{
            //    ++i;
            //    if(i > 100000)
            //    {
            //        log = new logEntry();
            //    }
            //}
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
            //while (logQueue.TryDequeue(out line))
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