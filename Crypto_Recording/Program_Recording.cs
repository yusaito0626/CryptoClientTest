using Crypto_Trading;
using Enums;
using LockFreeQueue;
using LockFreeStack;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using Utils;


namespace Crypto_Linux
{
    internal class Program
    {
        static string defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        static string logPath = Path.Combine(AppContext.BaseDirectory, "recording.log");
        static string outputPath = AppContext.BaseDirectory;
        static string outputPath_org = AppContext.BaseDirectory;
        static List<string> APIList = new List<string>();
        static string masterFile = "";
        static string discordTokenFile = "";


        static Crypto_Clients.Crypto_Clients crypto_client = Crypto_Clients.Crypto_Clients.GetInstance();
        static QuoteManager qManager = QuoteManager.GetInstance();
        static OrderManager oManager = OrderManager.GetInstance();
        static ThreadManager thManager = ThreadManager.GetInstance();
        static MessageDeliverer MsgDeliverer = MessageDeliverer.GetInstance();

        static msgType err_msg_type = msgType.LOGGING;

        static Dictionary<string, string> sendingItem = new Dictionary<string, string>();
        static JsonSerializerOptions js_option = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        static int logSize = 50000;
        static LockFreeStack<logEntry> logEntryStack;
        static Dictionary<string, connecitonStatus> connectionStates = new Dictionary<string, connecitonStatus>();
        static Dictionary<string, threadStatus> threadStates = new Dictionary<string, threadStatus>();

        static public Dictionary<string, Strategy> strategies;

        static bool enabled;

        static SISOQueue<string> logQueue;

        static StreamWriter logFile;

        static private bool threadsStarted;
        static private int stopTradingCalled;
        static private bool live;
        static private bool test;
        static private bool privateConnect;
        static private bool getSoDPos;
        static private bool msgLogging;

        static string str_endTime;
        static DateTime endTime;

        static int msg_Interval;
        static DateTime nextMsgTime;

        static DateTime? intradayPnLTime = null;

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

            Console.WriteLine("Crypto Recording App Ver." + GlobalVariables.ver_major + ":" + GlobalVariables.ver_minor + ":" + GlobalVariables.ver_patch);
            threadsStarted = false;
            live = true;
            test = false;
            privateConnect = false;
            getSoDPos = true;
            msgLogging = false;

            logEntryStack = new LockFreeStack<logEntry>();
            int i = 0;
            while (i < logSize)
            {
                logEntryStack.push(new logEntry());
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

            nextMsgTime = DateTime.UtcNow + TimeSpan.FromMinutes(msg_Interval);


            logFile = new StreamWriter(new FileStream(logPath, FileMode.Create));

            qManager._addLog = addLog;
            oManager._addLog = addLog;
            crypto_client.setAddLog(addLog);
            thManager._addLog = addLog;

            qManager.initializeInstruments(masterFile);
            qManager.setQueues(crypto_client);

            oManager.setOrdLogPath(outputPath);
            oManager.setInstruments(qManager.instruments);

            //readAPIFiles(APIsPath);
            if (test && privateConnect)
            {
                getAPIsFromEnv(true);
            }
            else
            {
                getAPIsFromEnv(live);
            }

            updateLog();

            if (!await MsgDeliverer.setDiscordToken(discordTokenFile))
            {
                addLog("Message configuration not found", Enums.logType.WARNING);
            }


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

            addLog("Recording Started.");

            isRunning = true;

            i = 0;
            try
            {
                while (isRunning)
                {
                    await statusCheck();

                    ThreadPool.GetAvailableThreads(out int worker, out int io);
                    ThreadPool.GetMaxThreads(out int maxWorker, out int maxIo);

                    if (maxWorker - worker > 20)
                    {
                        addLog($"Worker Threads: {maxWorker - worker}/{maxWorker}");
                    }

                    ++i;
                    if (i > 30)
                    {
                        await timer_PeriodicMsg_Tick();
                        i = 0;
                    }
                    updateLog();

                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                addLog("Error occured during the logging process.  Message:" + ex.Message, logType.ERROR);
                if (ex.StackTrace != null)
                {
                    addLog(ex.StackTrace, Enums.logType.WARNING);
                }
            }
            finally
            {
                if (isRunning)
                {
                    isRunning = false;
                }
            }

            addLog("Exit the main process...");
            Thread.Sleep(1000);
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
            if (root.TryGetProperty("privateConnect", out elem))
            {
                privateConnect = elem.GetBoolean();
            }
            else
            {
                privateConnect = false;
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
            if (root.TryGetProperty("masterFile", out elem))
            {
                masterFile = elem.GetString();
            }
            else
            {
                addLog("Master file path is not configured.", Enums.logType.ERROR);
                return false;
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
            if (root.TryGetProperty("discordTokenFile", out elem))
            {
                discordTokenFile = elem.GetString();
            }
            else
            {
                addLog("Message destination is not configured.", Enums.logType.WARNING);
            }

            if (live)
            {
                err_msg_type = msgType.ERROR;
            }
            else
            {
                err_msg_type = msgType.TEST;
            }
            string dt = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string newpath = outputPath + "/" + dt;
            if (!Directory.Exists(newpath))
            {
                Directory.CreateDirectory(newpath);
            }
            outputPath = newpath;
            logPath = outputPath + "/RecordingLog_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".log";
            
            return true;

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
                crypto_client.setCredentials((market)Enum.Parse(typeof(market), mkt), api_name, api_key);
            }
        }

        static private async Task<bool> tradePreparation(bool liveTrading)
        {
            try
            {
                oManager.setVirtualMode(true);
                qManager.live = liveTrading;
                foreach (var mkt in qManager._markets)
                {
                    if (msgLogging)
                    {
                        Func<Action, Action, CancellationToken, int, Task<bool>>? func = crypto_client.setMsgLogging(mkt.Key, outputPath,true);
                        if (func != null)
                        {
                            thManager.addThread(mkt.Key + "_msgLogging", func, null, null, 1);
                        }
                    }
                    if (!await qManager.connectPublicChannel(mkt.Key))
                    {
                        addLog("Failed to login public " + mkt.Key, logType.WARNING);
                        return false;
                    }
                    if (privateConnect)
                    {
                        if (!await oManager.connectPrivateChannel(mkt.Key))
                        {
                            addLog("Failed to login private " + mkt.Key, logType.WARNING);
                            return false;
                        }
                    }
                    connectionStates[mkt.Key.ToString()] = new connecitonStatus() { market = mkt.Key.ToString(), publicState = WebSocketState.None.ToString(), privateState = WebSocketState.None.ToString(), avgRTT = 0.0 };
                }

                updateLog();

                foreach (var ins in qManager.instruments.Values)
                {
                    market[] markets = [ins.market];
                    await crypto_client.subscribeOrderBook(markets, ins.baseCcy, ins.quoteCcy);
                    await crypto_client.subscribeTrades(markets, ins.baseCcy, ins.quoteCcy);
                }

                updateLog();

                if (privateConnect)
                {
                    await crypto_client.subscribeSpotOrderUpdates(qManager._markets.Keys);
                }

                updateLog();

                oManager.ready = true;

                
                foreach (var th in thManager.threads)
                {
                    threadStates[th.Key] = new threadStatus() { name = th.Key, isRunning = false };
                }

                updateLog();

                threadsStarted = true;

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


        

        static private async Task EoDProcess()
        {
            if (Interlocked.CompareExchange(ref EoDProcessCalled, 1, 0) != 0)
            {
                return;
            }

            if (threadsStarted)
            {
                foreach (var th in thManager.threads)
                {
                    th.Value.stop();
                }
            }
        }

        static async Task statusCheck()
        {
            //Declare variables that store the latency and status.
            //Connection
            if (EoDProcessCalled > 0)
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
                threadStates[th.Key] = th_status;
            }


            if (stoppedThreads.Count > 0)
            {
                bool currentTradingState = enabled;

                foreach (var stoppedTh in stoppedThreads)
                {
                    if (stoppedTh.Contains("Public"))
                    {
                        string market = stoppedTh.Replace("Public", "");
                        addLog("Public Connection to " + market + " lost reconnecting in 5 sec", Enums.logType.WARNING);
                        thManager.disposeThread(stoppedTh);
                        Thread.Sleep(5000);
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
                        addLog("Reconnection completed.");
                    }
                    else if (stoppedTh.Contains("Private"))
                    {
                        string market = stoppedTh.Replace("Private", "");
                        addLog("Private Connection to " + market + " lost reconnecting in 5 sec", Enums.logType.WARNING);
                        thManager.disposeThread(stoppedTh);
                        Thread.Sleep(5000);
                        if (!await oManager.connectPrivateChannel((market)Enum.Parse(typeof(market), market)))
                        {
                            addLog("Failed to reconnect private. market:" + market, logType.ERROR);
                            return;
                        }
                        Thread.Sleep(5000);
                        market[] markets = [(market)Enum.Parse(typeof(market), market)];
                        if (privateConnect)
                        {
                            await crypto_client.subscribeSpotOrderUpdates(markets);
                        }
                        addLog("Reconnection completed.");
                    }
                    else
                    {
                        addLog("Updating thread stopped. Stopping all the process", Enums.logType.ERROR);
                    }
                }
            }
        }

        static async Task timer_PeriodicMsg_Tick()
        {
            string msg = "";
            List<market> ex_list = new List<market>();

            DateTime currentTime = DateTime.UtcNow;

            crypto_client.checkStackCount();

            if (logEntryStack.Count < logSize / 10)
            {
                int i = 0;
                while (i < logSize / 5)
                {
                    logEntryStack.push(new logEntry());
                    ++i;
                }
            }

            if (DateTime.UtcNow > nextMsgTime)
            {
                nextMsgTime += TimeSpan.FromMinutes(msg_Interval);

                if (crypto_client.gmocoin_client.GetSocketStatePrivate() == WebSocketState.Open)
                {
                    await crypto_client.gmocoin_client.extendToken(crypto_client.gmocoin_client.token);
                }
            }
            foreach (market mkt in qManager._markets.Keys)
            { 
                switch(mkt)
                {
                    case market.bitbank:
                        Console.WriteLine("bitbank: " + crypto_client.bitbank_client.currentMsg);
                        break;
                    case market.gmocoin:
                        Console.WriteLine("gmocoin: " + crypto_client.gmocoin_client.currentMsg);
                        break;
                    case market.coincheck:
                        break;
                    case market.bittrade:
                        break;
                    default:
                        break;
                }
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
                    MsgDeliverer.sendMessage(messageline, err_msg_type);
                    onError();
                    break;
                default:
                    break;

            }

            logEntry log;
            log = logEntryStack.pop();
            if (log == null)
            {
                log = new logEntry();
            }
            log.logtype = logtype.ToString();
            log.msg = messageline;
        }
        static private void onError()
        {

        }

        static private void updateLog()
        {
            string line;
            while (true)
            {
                line = logQueue.Dequeue();
                if (line != null)
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