using Crypto_Clients;
using Crypto_Trading;
using CryptoClients.Net.Enums;
using CryptoExchange.Net.Logging.Extensions;
using CryptoExchange.Net.SharedApis;
using Discord;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Utils;
using static System.Net.Mime.MediaTypeNames;


namespace Crypto_Linux
{
    internal class Program
    {
        static string defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        //static string defaultConfigPath = "C:\\Users\\yusai\\Crypto_Project\\configs\\config_linuxtest.json";
        static string logPath = Path.Combine(AppContext.BaseDirectory, "crypto.log");
        static string outputPath = AppContext.BaseDirectory;
        static string APIsPath = "";
        static List<string> APIList = new List<string>();
        static string discordTokenFile = "";
        static string masterFile = "";
        static string virtualBalanceFile = "";
        static string strategyFile = "";

        static Crypto_Clients.Crypto_Clients crypto_client = Crypto_Clients.Crypto_Clients.GetInstance();
        static QuoteManager qManager = QuoteManager.GetInstance();
        static OrderManager oManager = OrderManager.GetInstance();
        static ThreadManager thManager = ThreadManager.GetInstance();
        static MessageDeliverer MsgDeliverer = MessageDeliverer.GetInstance();
        static websocketServer ws_server = websocketServer.GetInstance();

        static Dictionary<string, string> sendingItem = new Dictionary<string, string>();
        static JsonSerializerOptions js_option = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        static int logSize = 10000;
        static Stack<logEntry> logEntryStack;
        static Stack<fillInfo> fillInfoStack;

        static Strategy selected_stg;
        static Dictionary<string, Strategy> strategies;

        static bool enabled;

        static ConcurrentQueue<string> logQueue;
        static ConcurrentQueue<DataFill> filledOrderQueue;

        static Instrument selected_ins;

        static StreamWriter logFile;

        static private bool threadsStarted;
        static private int stopTradingCalled;
        static private bool aborting;

        static private bool autoStart;
        static private bool live;
        static private bool privateConnect;
        static private bool msgLogging;

        static string str_endTime;
        static DateTime endTime;

        static int msg_Interval;
        static DateTime nextMsgTime;

        static Dictionary<string, strategyInfo> strategyInfos = new Dictionary<string, strategyInfo>();
        static Dictionary<string, instrumentInfo> instrumentInfos = new Dictionary<string, instrumentInfo>();
        static Dictionary<string, connecitonStatus> connectionStates = new Dictionary<string, connecitonStatus>();
        static Dictionary<string , threadStatus> threadStates = new Dictionary<string, threadStatus>();

        Thread ws_thread;

        static bool isRunning = false;
        CancellationTokenSource cts = new CancellationTokenSource();
        static async Task Main(string[] args)
        {
            Console.WriteLine("Crypto Trading App Ver." + GlobalVariables.ver_major + ":" + GlobalVariables.ver_minor + ":" + GlobalVariables.ver_patch);
            aborting = false;
            threadsStarted = false;
            autoStart = false;
            live = false;
            privateConnect = true;
            msgLogging = false;

            logQueue = new ConcurrentQueue<string>();
            filledOrderQueue = new ConcurrentQueue<DataFill>();
            logEntryStack = new Stack<logEntry>();
            fillInfoStack = new Stack<fillInfo>();
            int i = 0;
            while(i < logSize)
            {
                logEntryStack.Push(new logEntry());
                fillInfoStack.Push(new fillInfo());
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

            Dictionary<string,masterInfo> masterinfos = new Dictionary<string,masterInfo>();

            foreach(var ins in qManager.instruments)
            {
                masterInfo ms = new masterInfo();
                ms.symbol = ins.Value.symbol;
                ms.market = ins.Value.market;
                ms.baseCcy = ins.Value.baseCcy;
                ms.quoteCcy= ins.Value.quoteCcy;
                ms.taker_fee = ins.Value.taker_fee;
                ms.maker_fee = ins.Value.maker_fee;
                ms.price_unit = ins.Value.price_unit;
                ms.quantity_unit = ins.Value.quantity_unit;
                masterinfos[ins.Key] = ms;
            }
            await ws_server.setMasterInfo(masterinfos);

            Dictionary<string,strategySetting> stgSettings = new Dictionary<string,strategySetting>();
            foreach(var stg in strategies)
            {
                strategySetting setting = new strategySetting();
                setting.name = stg.Value.name;
                setting.baseCcy = stg.Value.baseCcy;
                setting.quoteCcy = stg.Value.quoteCcy;
                setting.taker_market = stg.Value.taker_market;
                setting.maker_market = stg.Value.maker_market;
                setting.markup = stg.Value.markup;
                setting.min_markup = stg.Value.min_markup;
                setting.max_skew = stg.Value.maxSkew;
                setting.skew_widening = stg.Value.skewWidening;
                setting.baseCcy_quantity = stg.Value.baseCcyQuantity;
                setting.ToBsize = stg.Value.ToBsize;
                setting.intervalAfterFill = stg.Value.intervalAfterFill;
                setting.modThreshold = stg.Value.modThreshold;
                setting.skewThreshold = stg.Value.skewThreshold;
                setting.oneSideThreshold = stg.Value.oneSideThreshold;
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
            Dictionary<string,double> avgLatency = new Dictionary<string,double>();
            double latency = 0;
            while(i < trial)
            {
                foreach(var m in qManager._markets)
                {
                    Thread.Sleep(1000);
                    sw.Start();
                    await crypto_client.getBalance([m.Key]);
                    sw.Stop();
                    latency = sw.Elapsed.TotalNanoseconds / 1000000;
                    sw.Reset();
                    addLog(m.Key + " trial " + i.ToString() + ": " + latency.ToString("N3") + " ms");
                    if(avgLatency.ContainsKey(m.Key))
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

            foreach(var l in avgLatency)
            {
                addLog("Average Latency of " + l.Key + ": " + (l.Value / trial).ToString("N3") + " ms");
            }

            Thread.Sleep(5000);
            startTrading();

            if(live)
            {
                addLog("Live Trading");
            }
            else
            {
                addLog("Simulation Mode");
            }

            isRunning = true;

            i = 0;
            while (isRunning)
            {
                sendFills();
                await statusCheck();
                updateLog();
                setInstrumentInfo();
                setStrategyInfo();
                broadcastInfos();
                ++i;
                if(i > 30)
                {
                    //string msg = "";
                    //foreach (var th in threadStates)
                    //{
                    //    msg += DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat) + " Thread Name:" + th.Key + "   Average Processing Time:" + th.Value.avgProcessingTime.ToString() + " ms\n";
                    //}
                    decimal volume = 0;
                    decimal tradingPL = 0;
                    decimal fee = 0;
                    decimal total = 0;

                    decimal volumeAll = 0;
                    decimal tradingPLAll = 0;
                    decimal feeAll = 0;
                    decimal totalAll = 0;
                    string msg = "EoD PnL";
                    foreach (var stg in strategies.Values)
                    {
                        if (stg.maker != null && stg.taker != null)
                        {
                            volume = stg.maker.my_buy_notional + stg.maker.my_sell_notional;
                            tradingPL = (stg.taker.my_sell_notional - stg.taker.my_sell_quantity * stg.taker.mid) + (stg.taker.my_buy_quantity * stg.taker.mid - stg.taker.my_buy_notional);
                            tradingPL += (stg.maker.my_sell_notional - stg.maker.my_sell_quantity * stg.taker.mid) + (stg.maker.my_buy_quantity * stg.taker.mid - stg.maker.my_buy_notional);
                            fee = stg.taker.base_fee * stg.taker.mid + stg.taker.quote_fee + stg.maker.base_fee * stg.taker.mid + stg.maker.quote_fee;
                            total = tradingPL - fee;

                            msg += DateTime.UtcNow.ToString() + " - Strategy " + stg.name + " -    Notional Volume:" + volume.ToString("N2") + " Trading PnL:" + tradingPL.ToString("N2") + " Fee:" + fee.ToString("N2") + " Total:" + total.ToString("N2") + "\n";
                            volumeAll += volume;
                            tradingPLAll += tradingPL;
                            feeAll += fee;
                            totalAll += total;
                        }
                    }
                    msg += DateTime.UtcNow.ToString() + " - All -    Notional Volume:" + volumeAll.ToString("N2") + " Trading PnL:" + tradingPLAll.ToString("N2") + " Fee:" + feeAll.ToString("N2") + " Total:" + totalAll.ToString("N2") + "\n";
                    Console.WriteLine(msg);
                    await timer_PeriodicMsg_Tick();
                    i = 0;
                }
                Thread.Sleep(1000);
            }
        }


        static private async Task sendFills()
        {
            DataFill fill;
            fillInfo fInfo;
            while (filledOrderQueue.Count > 0)
            {
                while(!filledOrderQueue.TryDequeue(out fill))
                {

                }
                if (fillInfoStack.Count > 0)
                {
                    fInfo = fillInfoStack.Pop();
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

            string dt = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string newpath = outputPath + "/" + dt;
            if (!Directory.Exists(newpath))
            {
                Directory.CreateDirectory(newpath);
            }
            outputPath = newpath;
            logPath = outputPath + "/crypto.log";

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
                    stg.setStrategy(elem);
                    stg.maker = qManager.getInstrument(stg.baseCcy, stg.quoteCcy, stg.maker_market);
                    stg.taker = qManager.getInstrument(stg.baseCcy, stg.quoteCcy, stg.taker_market);
                    stg.maker.ToBsize = stg.ToBsize;
                    stg.taker.ToBsize = stg.ToBsize;
                    stg._addLog = addLog;
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
                        crypto_client.setMsgLogging(mkt.Key, outputPath);
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
                    if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys)))
                    {
                        return false;
                    }
                }
                qManager.ready = true;

                if (liveTrading || privateConnect)
                {
                    await crypto_client.subscribeSpotOrderUpdates(qManager._markets.Keys);
                }

                oManager.ready = true;

                thManager.addThread("updateQuotes", qManager._updateQuotes, qManager.updateQuotesOnClosing, qManager.updateQuotesOnError);
                thManager.addThread("updateTrades", qManager._updateTrades, qManager.updateTradesOnClosing, qManager.updateTradesOnClosing);
                thManager.addThread("updateOrders", oManager._updateOrders, oManager.updateOrdersOnClosing, oManager.updateOrdersOnError);
                thManager.addThread("updateFill", oManager._updateFill, oManager.updateFillOnClosing);
                thManager.addThread("optimize", qManager._optimize, qManager.optimizeOnClosing, qManager.optimizeOnError);
                thManager.addThread("orderLogging", oManager._orderLogging, oManager.ordLoggingOnClosing, oManager.ordLoggingOnError);

                foreach(var th in thManager.threads)
                {
                    Console.WriteLine(th.Key);
                    threadStates[th.Key] = new threadStatus() { name = th.Key, isRunning = false };
                }

                threadsStarted = true;
            }
            catch (Exception ex)
            {
                addLog("An error occured while initializing the platforms.", Enums.logType.ERROR);
                addLog(ex.Message, Enums.logType.ERROR);

                Console.WriteLine("An error occured while initializing the platforms.");
                Console.WriteLine(ex.Message);
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
            addLog("Trading started.");
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

                        foreach (var th in thManager.threads)
                        {
                            th.Value.isRunning = false;
                        }
                    }
                }

            }
            return true;
        }

        static private async Task EoDProcess()
        {
            stopStrategies();
            if (threadsStarted)
            {
                if (oManager.ready)
                {
                    await oManager.cancelAllOrders();
                    Thread.Sleep(1000);

                    //Just in case
                    foreach(var m in qManager._markets)
                    {
                        qManager.setBalance(await crypto_client.getBalance([m.Key]));
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
                        addLog("EoD balance of " + stg.name + " BaseCcy:" + (stg.maker.baseBalance.total + stg.taker.baseBalance.total).ToString() + " QuoteCcy:" + (stg.maker.quoteBalance.total + stg.taker.quoteBalance.total).ToString());
                        addLog("Adjustment at EoD: " + side.ToString() + " " + baseBalance_diff.ToString());
                        await oManager.placeNewSpotOrder(stg.taker, side, orderType.Market, baseBalance_diff, 0);
                    }

                    Thread.Sleep(1000);
                    decimal volume = 0;
                    decimal tradingPL = 0;
                    decimal fee = 0;
                    decimal total = 0;

                    decimal volumeAll = 0;
                    decimal tradingPLAll = 0;
                    decimal feeAll = 0;
                    decimal totalAll = 0;
                    string msg = "EoD PnL";
                    foreach (var stg in strategies.Values)
                    {
                        if (stg.maker != null && stg.taker != null)
                        {
                            volume = stg.maker.my_buy_notional + stg.maker.my_sell_notional;
                            tradingPL = (stg.taker.my_sell_notional - stg.taker.my_sell_quantity * stg.taker.mid) + (stg.taker.my_buy_quantity * stg.taker.mid - stg.taker.my_buy_notional);
                            tradingPL += (stg.maker.my_sell_notional - stg.maker.my_sell_quantity * stg.taker.mid) + (stg.maker.my_buy_quantity * stg.taker.mid - stg.maker.my_buy_notional);
                            fee = stg.taker.base_fee * stg.taker.mid + stg.taker.quote_fee + stg.maker.base_fee * stg.taker.mid + stg.maker.quote_fee;
                            total = tradingPL - fee;

                            msg += DateTime.UtcNow.ToString() + " - Strategy " + stg.name + " -    Notional Volume:" + volume.ToString("N2") + " Trading PnL:" + tradingPL.ToString("N2") + " Fee:" + fee.ToString("N2") + " Total:" + total.ToString("N2") + "\n";
                            volumeAll += volume;
                            tradingPLAll += tradingPL;
                            feeAll += fee;
                            totalAll += total;
                        }
                    }
                    msg += DateTime.UtcNow.ToString() + " - All -    Notional Volume:" + volumeAll.ToString("N2") + " Trading PnL:" + tradingPLAll.ToString("N2") + " Fee:" + feeAll.ToString("N2") + " Total:" + totalAll.ToString("N2") + "\n";
                    await MsgDeliverer.sendMessage(msg);
                    //addLog(msg);
                    foreach (var th in thManager.threads)
                    {
                        th.Value.isRunning = false;
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
                connectionStates[mkt.Key] = status;
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
                connectionStates[mkt.Key] = status;
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
                if(th.Value.count > 0)
                {
                    th_status.avgProcessingTime = th.Value.totalElapsedTime / th.Value.count / 1000;
                }
                else
                {
                    th_status.avgProcessingTime = 0;
                }
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
                            if (!qManager.setVirtualBalance(virtualBalanceFile))
                            {

                            }
                        }
                        else
                        {
                            if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys)))
                            {

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
                            if (!qManager.setVirtualBalance(virtualBalanceFile))
                            {

                            }
                        }
                        else
                        {
                            if (!qManager.setBalance(await crypto_client.getBalance(qManager._markets.Keys)))
                            {

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
            
            if (qManager.ordBookQueue.Count() > 1000)
            {
                addLog("The order book queue count exceeds 1000.", Enums.logType.WARNING);
                if (qManager.ordBookQueue.Count() > 10000)
                {

                }
            }
            if (crypto_client.ordUpdateQueue.Count() > 1000)
            {
                addLog("The order update queue count exceeds 1000.", Enums.logType.WARNING);
            }
            if (crypto_client.fillQueue.Count() > 1000)
            {
                addLog("The fill queue count exceeds 1000.", Enums.logType.WARNING);
            }

        }

        static void setStrategyInfo()
        {
            foreach(var stg in strategies.Values)
            {
                strategyInfo stginfo = strategyInfos[stg.name];
                DataSpotOrderUpdate? ord;
                ord = stg.live_buyorder;
                if(ord != null)
                {
                    stginfo.bid= ord.order_price;
                    stginfo.bidSize = ord.order_quantity;
                }
                else
                {
                    stginfo.bid = 0;
                    stginfo.bidSize = 0;
                }
                ord = stg.live_sellorder;
                if(ord != null)
                {
                    stginfo.ask= ord.order_price;
                    stginfo.askSize = ord.order_quantity;
                }
                else
                {
                    stginfo.ask = 0;
                    stginfo.askSize = 0;
                }
                stginfo.liquidity_ask = stg.taker.adjusted_bestask.Item1;
                stginfo.liquidity_ask = stg.taker.adjusted_bestbid.Item1;
                stginfo.notionalVolume = stg.maker.my_buy_notional + stg.maker.my_sell_notional;
                stginfo.tradingPnL = (stg.taker.my_sell_notional - stg.taker.my_sell_quantity * stg.taker.mid) + (stg.taker.my_buy_quantity * stg.taker.mid - stg.taker.my_buy_notional);
                stginfo.tradingPnL += (stg.maker.my_sell_notional - stg.maker.my_sell_quantity * stg.taker.mid) + (stg.maker.my_buy_quantity * stg.taker.mid - stg.maker.my_buy_notional);
                stginfo.totalFee= stg.taker.base_fee * stg.taker.mid + stg.taker.quote_fee + stg.maker.base_fee * stg.taker.mid + stg.maker.quote_fee;
                stginfo.totalPnL = stginfo.tradingPnL - stginfo.totalFee;

                stginfo.skew = stg.skew_point;
                if(stginfo.bid > 0 && stginfo.ask > 0)
                {
                    stginfo.spread = stginfo.ask - stginfo.bid;
                }
                else
                {
                    stginfo.spread = 0;
                }
                strategyInfos[stg.name] = stginfo;
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

                insinfo.my_notional_buy = ins.my_buy_notional;
                insinfo.my_quantity_buy = ins.my_buy_quantity;
                insinfo.my_notional_sell = ins.my_sell_notional;
                insinfo.my_quantity_sell = ins.my_sell_quantity;

                insinfo.quoteFee_total = ins.quote_fee;
                insinfo.baseFee_total = ins.base_fee;
                instrumentInfos[ins.symbol_market] = insinfo;
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

            }
            catch (Exception ex)
            {
                addLog(ex.Message);
            }
        }

        static async Task timer_PeriodicMsg_Tick()
        {
            decimal volume = 0;
            decimal tradingPL = 0;
            decimal fee = 0;
            decimal total = 0;

            decimal volumeAll = 0;
            decimal tradingPLAll = 0;
            decimal feeAll = 0;
            decimal totalAll = 0;
            string msg = "";

            //To keep http_client alive.
            await crypto_client.getBalance(qManager._markets.Keys);

            if (DateTime.UtcNow > nextMsgTime)
            {
                foreach (var stg in strategies.Values)
                {
                    if (stg.maker != null && stg.taker != null)
                    {
                        volume = stg.maker.my_buy_notional + stg.maker.my_sell_notional;
                        tradingPL = (stg.taker.my_sell_notional - stg.taker.my_sell_quantity * stg.taker.mid) + (stg.taker.my_buy_quantity * stg.taker.mid - stg.taker.my_buy_notional);
                        tradingPL += (stg.maker.my_sell_notional - stg.maker.my_sell_quantity * stg.taker.mid) + (stg.maker.my_buy_quantity * stg.taker.mid - stg.maker.my_buy_notional);
                        fee = stg.taker.base_fee * stg.taker.mid + stg.taker.quote_fee + stg.maker.base_fee * stg.taker.mid + stg.maker.quote_fee;
                        total = tradingPL - fee;

                        msg += DateTime.UtcNow.ToString() + " - Strategy " + stg.name + " -    Notional Volume:" + volume.ToString("N2") + " Trading PnL:" + tradingPL.ToString("N2") + " Fee:" + fee.ToString("N2") + " Total:" + total.ToString("N2") + "\n";
                        volumeAll += volume;
                        tradingPLAll += tradingPL;
                        feeAll += fee;
                        totalAll += total;
                    }
                }
                msg += DateTime.UtcNow.ToString() + " - All -    Notional Volume:" + volumeAll.ToString("N2") + " Trading PnL:" + tradingPLAll.ToString("N2") + " Fee:" + feeAll.ToString("N2") + " Total:" + totalAll.ToString("N2") + "\n";

                await MsgDeliverer.sendMessage(msg);
                nextMsgTime += TimeSpan.FromMinutes(msg_Interval);
            }



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
                    MsgDeliverer.sendMessage(messageline);
                    onError();
                    break;
                default:
                    break;

            }
            
            logEntry log = logEntryStack.Pop();
            log.logtype = logtype.ToString();
            log.msg = body;
            ws_server.processLog(log);
        }
        static private void onError()
        {
            stopTrading(true);
        }

        static private  void updateLog()
        {
            string line;
            while (logQueue.TryDequeue(out line))
            {
                Console.Write(line);

                if (logFile != null)
                {
                    logFile.WriteLine(line);
                    logFile.Flush();
                }
            }
        }
    }
}