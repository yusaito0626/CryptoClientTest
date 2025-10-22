using Crypto_Clients;
using Crypto_Trading;
using CryptoClients.Net.Enums;
using Discord;
using Discord.WebSocket;
using Enums;
using PubnubApi.EventEngine.Subscribe.Common;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Utils;



namespace Crypto_GUI
{
    public partial class Form1 : Form
    {
        string configPath = "C:/Users/yusai/Crypto_Project/configs/config.json";
        string defaultConfigPath = AppContext.BaseDirectory + "/config.json";
        string logPath = AppContext.BaseDirectory + "/crypto.log";
        string outputPath = AppContext.BaseDirectory;
        string APIsPath = "";
        List<string> APIList = new List<string>();
        string discordTokenFile = "";
        string masterFile = "";
        string virtualBalanceFile = "";
        string strategyFile = "";

        int latency = 100;

        Crypto_Clients.Crypto_Clients crypto_client = Crypto_Clients.Crypto_Clients.GetInstance();
        QuoteManager qManager = QuoteManager.GetInstance();
        OrderManager oManager = OrderManager.GetInstance();
        ThreadManager thManager = ThreadManager.GetInstance();
        MessageDeliverer MsgDeliverer = MessageDeliverer.GetInstance();

        Strategy selected_stg;
        Dictionary<string, Strategy> strategies;

        bool enabled;

        ConcurrentQueue<string> logQueue;
        ConcurrentQueue<DataFill> filledOrderQueue;

        Instrument selected_ins;

        private bool updating;
        System.Threading.Thread updatingTh;
        System.Threading.Thread quoteupdateTh;
        System.Threading.Thread tradeupdateTh;
        System.Threading.Thread orderUpdateTh;
        System.Threading.Thread bitBankTh;

        StreamWriter logFile;

        Font font_gridView;
        Font font_gridView_Bold;

        private bool threadsStarted;
        private int stopTradingCalled;
        private bool aborting;

        private bool autoStart;
        private bool live;
        private bool privateConnect;
        private bool msgLogging;

        string str_endTime;
        DateTime endTime;

        int msg_Interval;
        DateTime nextMsgTime;

        private bool monitoringMode = false;
        private string tradeEngine = "";
        ClientWebSocket info_receiver;
        byte[] ws_buffer = new byte[4096];
        MemoryStream ws_memory = new MemoryStream();

        bool masterInfoReceived = false;
        bool strategySettingReceived = false;


        public Form1()
        {
            this.aborting = false;
            this.threadsStarted = false;
            this.autoStart = false;
            this.live = false;
            this.privateConnect = true;
            this.msgLogging = false;

            this.logQueue = new ConcurrentQueue<string>();
            this.filledOrderQueue = new ConcurrentQueue<DataFill>();

            this.strategies = new Dictionary<string, Strategy>();

            this.enabled = false;

            InitializeComponent();

            this.lbl_version.Text = GlobalVariables.ver_major + ":" + GlobalVariables.ver_minor + ":" + GlobalVariables.ver_patch;
            this.Text += "   Ver." + this.lbl_version.Text;

            this.button_receiveFeed.Enabled = false;
            this.button_startTrading.Enabled = false;
            //this.button_orderTest.Enabled = false;

            this.str_endTime = "23:30:00";

            this.msg_Interval = 30;

            if (!this.readConfig())
            {
                this.addLog("Failed to read config.", Enums.logType.ERROR);
                updatingTh = new Thread(update);
                updatingTh.Start();
                return;
            }

            if (this.live)
            {
                this.BackColor = SystemColors.GradientActiveCaption;
                this.Text += " LIVE";
                foreach (TabPage tab in this.tabControl.TabPages)
                {
                    tab.BackColor = SystemColors.GradientActiveCaption;
                }
            }
            else if(this.monitoringMode)
            {
                this.Text += " Monitoring Mode";
            }

            this.nextMsgTime = DateTime.UtcNow + TimeSpan.FromMinutes(this.msg_Interval);


            this.logFile = new StreamWriter(new FileStream(this.logPath, FileMode.Create));

            this.qManager._addLog = this.addLog;
            this.oManager._addLog = this.addLog;
            this.crypto_client.setAddLog(this.addLog);
            this.thManager._addLog = this.addLog;

            //this.readAPIFiles(this.APIsPath);
            this.getAPIsFromEnv(this.live);

            //this.qManager.initializeInstruments(this.masterFile);
            this.qManager.setQueues(this.crypto_client);

            this.oManager.setOrdLogPath(this.outputPath);
            //this.oManager.setInstruments(this.qManager.instruments);
            this.oManager.filledOrderQueue = this.filledOrderQueue;

            //this.setStrategies(this.strategyFile);
            //this.qManager.strategies = this.strategies;
            //this.oManager.strategies = this.strategies;

            int i = 0;
            int numOfRow = QuoteManager.NUM_OF_QUOTES * 2 + 1;
            while (i < numOfRow)
            {
                this.gridView_Taker.Rows.Add();
                this.gridView_Maker.Rows.Add();
                this.gridView_Ins.Rows.Add();
                ++i;
            }

            this.font_gridView = new("Calibri", 9);
            this.font_gridView_Bold = new Font(this.font_gridView, FontStyle.Bold);

            this.updatingTh = new Thread(update);
            this.updatingTh.Start();

            this.stopTradingCalled = 0;
            this.button_receiveFeed.Enabled = true;

            if(!this.monitoringMode)
            {
                this.timer_statusCheck.Start();
                this.timer_PeriodicMsg.Start();
            }

            this.addLog("Application closing time:" + this.str_endTime);
        }
        private bool readConfig()
        {
            string fileContent;
            if (File.Exists(this.configPath))
            {
                fileContent = File.ReadAllText(this.configPath);
            }
            else if (File.Exists(this.defaultConfigPath))
            {
                fileContent = File.ReadAllText(this.defaultConfigPath);
            }
            else
            {
                this.addLog("Config file doesn't exist. path:" + this.configPath, Enums.logType.ERROR);
                return false;
            }
            using JsonDocument doc = JsonDocument.Parse(fileContent);
            var root = doc.RootElement;
            JsonElement elem;
            if (root.TryGetProperty("autoStart", out elem))
            {
                this.autoStart = elem.GetBoolean();
            }
            else
            {
                this.autoStart = false;
            }
            
            if (root.TryGetProperty("live", out elem))
            {
                this.live = elem.GetBoolean();
            }
            else
            {
                this.live = false;
            }
            if (root.TryGetProperty("privateConnect", out elem))
            {
                if (!this.live)
                {
                    this.privateConnect = elem.GetBoolean();
                }
            }
            else
            {
                this.privateConnect = true;
            }
            if (root.TryGetProperty("monitoringMode", out elem))
            {
                this.monitoringMode = elem.GetBoolean();
                if(monitoringMode)
                {
                    this.live = false;
                    this.privateConnect = true;
                }
            }
            else
            {
                this.privateConnect = true;
            }
            if (root.TryGetProperty("tradeEngine", out elem))
            {
                this.tradeEngine = elem.GetString();
            }
            if (root.TryGetProperty("msgLogging", out elem))
            {
                this.msgLogging = elem.GetBoolean();
            }
            else
            {
                this.msgLogging = false;
            }
            if (root.TryGetProperty("endTime", out elem))
            {
                this.str_endTime = elem.GetString();
                TimeSpan timeOfDay = TimeSpan.ParseExact(this.str_endTime, "hh\\:mm\\:ss", null);

                this.endTime = DateTime.UtcNow.Date.Add(timeOfDay);
                if (DateTime.UtcNow >= this.endTime)
                {
                    this.endTime = this.endTime.AddDays(1);
                }
            }
            else
            {
                TimeSpan timeOfDay = TimeSpan.ParseExact(this.str_endTime, "hh\\:mm\\:ss", null);

                this.endTime = DateTime.UtcNow.Date.Add(timeOfDay);
                if (DateTime.UtcNow >= this.endTime)
                {
                    this.endTime = this.endTime.AddDays(1);
                }
            }
            if (root.TryGetProperty("APIsPath", out elem))
            {
                this.APIsPath = elem.GetString();
            }
            else
            {
                this.addLog("API path is not configured.", Enums.logType.ERROR);
                return false;
            }
            if (root.TryGetProperty("APIEnvList", out elem))
            {
                foreach (var env_name in elem.EnumerateArray())
                {
                    this.APIList.Add(env_name.GetString());
                }

            }
            if (root.TryGetProperty("masterFile", out elem))
            {
                this.masterFile = elem.GetString();
            }
            else
            {
                this.addLog("Master file path is not configured.", Enums.logType.ERROR);
                return false;
            }
            if (root.TryGetProperty("discordTokenFile", out elem))
            {
                this.discordTokenFile = elem.GetString();
            }
            else
            {
                this.addLog("Message destination is not configured.", Enums.logType.WARNING);
            }
            if (root.TryGetProperty("outputPath", out elem))
            {
                this.outputPath = elem.GetString();
            }
            else
            {
                this.addLog("Output path is not configured.", Enums.logType.WARNING);
                this.addLog("The output files will be exported to the current path.", Enums.logType.WARNING);
            }
            if (root.TryGetProperty("logFile", out elem))
            {
                this.logPath = elem.GetString();
            }
            if (root.TryGetProperty("strategyFile", out elem))
            {
                this.strategyFile = elem.GetString();
            }
            else
            {
                this.addLog("strategyFile is not configured.", Enums.logType.WARNING);
                this.addLog("Any strategies won't be run.", Enums.logType.WARNING);
            }
            if (root.TryGetProperty("balanceFile", out elem))
            {
                this.virtualBalanceFile = elem.GetString();
            }
            else
            {
                this.addLog("Balance file is not configured.", Enums.logType.WARNING);
                this.addLog("The virtual balance will be all 0.", Enums.logType.WARNING);
            }
            if (root.TryGetProperty("latency", out elem))
            {
                this.oManager.latency = elem.GetInt32();
            }

            string dt = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string newpath = this.outputPath + "/" + dt;
            if (!Directory.Exists(newpath))
            {
                Directory.CreateDirectory(newpath);
            }
            this.outputPath = newpath;
            this.logPath = this.outputPath + "/crypto.log";

            return true;

        }

        private void setStrategies(string strategyFile)
        {
            if (File.Exists(strategyFile))
            {
                string fileContent = File.ReadAllText(strategyFile);
                using JsonDocument doc = JsonDocument.Parse(fileContent);
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    Strategy stg = new Strategy();
                    stg.setStrategy(elem);
                    stg.maker = this.qManager.getInstrument(stg.baseCcy, stg.quoteCcy, stg.maker_market);
                    stg.taker = this.qManager.getInstrument(stg.baseCcy, stg.quoteCcy, stg.taker_market);
                    stg.maker.ToBsize = stg.ToBsize;
                    stg.taker.ToBsize = stg.ToBsize;
                    stg._addLog = this.addLog;
                    this.strategies[stg.name] = stg;
                    //this.stg = stg;
                }
            }

        }
        private void setStrategies(Dictionary<string,strategySetting> stgSettings)
        {
            Strategy stg = new Strategy();
            foreach(var setting in stgSettings)
            {
                stg.setStrategy(setting.Value);
                stg.maker = this.qManager.getInstrument(stg.baseCcy, stg.quoteCcy, stg.maker_market);
                stg.taker = this.qManager.getInstrument(stg.baseCcy, stg.quoteCcy, stg.taker_market);
                stg.maker.ToBsize = stg.ToBsize;
                stg.taker.ToBsize = stg.ToBsize;
                stg._addLog = this.addLog;
                this.strategies[stg.name] = stg;
            }
        }
        private void readAPIFiles(string path)
        {
            if (Directory.Exists(path))
            {
                // ƒtƒ@ƒCƒ‹ˆê——‚ðŽæ“¾
                string[] files = Directory.GetFiles(path, "*.json");

                foreach (string file in files)
                {
                    this.addLog("API File:" + file);
                    this.crypto_client.readCredentials(file);
                }
            }
        }
        private void getAPIsFromEnv(bool tradable)
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

            if(this.APIList.Count() == 0)
            {
                foreach(var mkt in this.qManager._markets.Keys)
                {
                    this.APIList.Add(mkt.ToUpper() + "_" + tradeState);
                }
            }

            foreach (string env_name in APIList)
            {
                string env_name_all = env_name + "_KEY";
                string api_content = Environment.GetEnvironmentVariable(env_name_all);
                if (string.IsNullOrEmpty(api_content))
                {
                    this.addLog("API not found. env_name:" + env_name_all, Enums.logType.ERROR);
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
                    this.addLog("API not found. env_name:" + env_name_all, Enums.logType.ERROR);
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
                    this.addLog("The API name for " + mkt + "is not found", Enums.logType.ERROR);
                }
                if (APIs.ContainsKey(env_name + "_KEY"))
                {
                    api_key = APIs[env_name + "_KEY"];
                }
                else
                {
                    this.addLog("The API secret key for " + mkt + "is not found", Enums.logType.ERROR);
                }
                crypto_client.setCredentials(mkt, api_name, api_key);
            }
        }
        private void addLog(string body, Enums.logType logtype = Enums.logType.INFO)
        {
            string messageline = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   [" + logtype.ToString() + "]" + body + "\n";
            
            this.logQueue.Enqueue(messageline);
            switch (logtype)
            {
                case Enums.logType.ERROR:
                case Enums.logType.FATAL:
                    this.MsgDeliverer.sendMessage(messageline);
                    this.onError();
                    break;
                default:
                    break;

            }
        }
        private void onError()
        {
            this.stopTrading(true);
        }
        private void updateLog()
        {
            string line;
            while (this.logQueue.TryDequeue(out line))
            {
                this.textBoxMainLog.Text += line;

                if (this.logFile != null)
                {
                    this.logFile.WriteLine(line);
                    this.logFile.Flush();
                }
            }
        }
        private async void update()
        {
            Thread.Sleep(1000);
            this.BeginInvoke(this.updateLog);
            this.updating = false;
            
            if(!monitoringMode)
            {
                if (!await this.MsgDeliverer.setDiscordToken(this.discordTokenFile))
                {
                    this.addLog("Message configuration not found", Enums.logType.WARNING);
                }
            }
                while (!this.aborting)
                {
                    if (!this.updating)
                    {
                        this.updating = true;
                        this.BeginInvoke(this._update);
                        Thread.Sleep(1);
                    }
                }
        }
        private async Task connectTradeEngine()
        {
            if(this.tradeEngine == "")
            {
                this.addLog("The trade engine location is unknown", Enums.logType.ERROR);
            }
            else
            {
                bool connected = false;
                int trial = 0;
                this.info_receiver = new ClientWebSocket();
                
                var uri = new Uri(this.tradeEngine);
                while(!connected)
                {
                    try
                    {
                        this.info_receiver.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                        await this.info_receiver.ConnectAsync(uri, CancellationToken.None);
                        this.addLog("Connected to the trade engine");
                        connected = true;
                    }
                    catch (WebSocketException wse)
                    {
                        this.addLog($"WebSocketException: {wse.Message}", Enums.logType.WARNING);
                        ++trial;
                        this.info_receiver.Dispose();
                        this.info_receiver = new ClientWebSocket();
                        Thread.Sleep(5000);
                    }
                    catch (Exception ex)
                    {
                        this.addLog($"Connection failed: {ex.Message}", Enums.logType.WARNING);
                        ++trial;
                        this.info_receiver.Dispose();
                        this.info_receiver = new ClientWebSocket();
                        Thread.Sleep(5000);
                    }
                    if(!connected)
                    {
                        if(trial > 5)
                        {
                            this.addLog("Unable to connect after " + trial.ToString() + " trials",logType.ERROR);
                            this.addLog("Aborting", logType.ERROR);
                            return;
                        }
                        else
                        {
                            this.addLog("Tried " + trial.ToString() + " times",logType.WARNING);
                        }
                    }
                }
                
                this.thManager.addThread("ReceiveInfo", receiveAsync, null, null);
            }
        }

        private async Task<(bool,double)> receiveAsync()
        {
            WebSocketReceiveResult result;
            string msg;
            bool output = true;
            double latency = 0;
            switch (this.info_receiver.State)
            {
                case WebSocketState.Open:
                    do
                    {
                        result = await this.info_receiver.ReceiveAsync(new ArraySegment<byte>(this.ws_buffer), CancellationToken.None);
                        this.ws_memory.Write(this.ws_buffer, 0, result.Count);
                    } while ((!result.EndOfMessage) && this.info_receiver.State != WebSocketState.Aborted && this.info_receiver.State != WebSocketState.Closed);

                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Text:
                            msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
                            var js = JsonDocument.Parse(msg).RootElement;
                            string data_type = js.GetProperty("data_type").GetString();
                            string content = js.GetProperty("data").GetString();
                            
                            switch (data_type)
                            {
                                case "master":
                                    var masters = JsonSerializer.Deserialize<Dictionary<string, masterInfo>>(content);
                                    this.qManager.initializeInstruments(masters);
                                    this.oManager.setInstruments(this.qManager.instruments);
                                    this.BeginInvoke(() =>
                                    {
                                        foreach (string key in this.qManager.instruments.Keys)
                                        {
                                            this.comboSymbols.Items.Add(key);
                                        }
                                    });
                                    this.masterInfoReceived = true;
                                    break;
                                case "strategySetting":
                                    var stgSetting = JsonSerializer.Deserialize<Dictionary<string, strategySetting>>(content);
                                    setStrategies(stgSetting);
                                    this.qManager.strategies = this.strategies;
                                    this.oManager.strategies = this.strategies;
                                    this.BeginInvoke(() =>
                                    {
                                        foreach (string key in this.strategies.Keys)
                                        {
                                            this.comboStrategy.Items.Add(key);
                                        }
                                    });
                                    this.strategySettingReceived = true;
                                    break;
                                case "strategy":
                                    var stginfos = JsonSerializer.Deserialize<Dictionary<string, strategyInfo>>(content);
                                    foreach (var s in stginfos)
                                    {
                                        if (this.strategies.ContainsKey(s.Key))
                                        {
                                            Strategy stg = this.strategies[s.Key];
                                            stg.skew_point = s.Value.skew;
                                            stg.live_askprice = s.Value.ask;
                                            stg.live_bidprice = s.Value.bid;
                                            stg.notionalVolume = s.Value.notionalVolume;
                                            stg.tradingPnL = s.Value.tradingPnL;
                                            stg.totalFee = s.Value.totalFee;
                                            stg.totalPnL = s.Value.totalPnL;
                                        }
                                        else
                                        {
                                            Strategy stg = new Strategy();
                                            stg.name = s.Value.name;
                                            stg.baseCcy = s.Value.baseCcy;
                                            stg.quoteCcy = s.Value.quoteCcy;
                                            stg.maker_market = s.Value.maker_market;
                                            stg.taker_market = s.Value.taker_market;
                                            stg.maker_symbol_market = s.Value.maker_symbol_market;
                                            stg.taker_symbol_market = s.Value.taker_symbol_market;
                                            if (this.qManager.instruments.ContainsKey(stg.maker_symbol_market))
                                            {
                                                stg.maker = this.qManager.instruments[stg.maker_symbol_market];
                                            }
                                            if (this.qManager.instruments.ContainsKey(stg.taker_symbol_market))
                                            {
                                                stg.taker = this.qManager.instruments[stg.taker_symbol_market];
                                            }
                                            stg.skew_point = s.Value.skew;
                                            stg.live_askprice = s.Value.ask;
                                            stg.live_bidprice = s.Value.bid;
                                            stg.notionalVolume = s.Value.notionalVolume;
                                            stg.tradingPnL = s.Value.tradingPnL;
                                            stg.totalFee = s.Value.totalFee;
                                            stg.totalPnL = s.Value.totalPnL;
                                            this.strategies[s.Key] = stg;
                                            this.comboStrategy.Items.Add(s.Key);
                                        }
                                    }
                                    break;
                                case "instrument":
                                    var insinfos = JsonSerializer.Deserialize<Dictionary<string, instrumentInfo>>(content);
                                    foreach (var i in insinfos)
                                    {
                                        if (this.qManager.instruments.ContainsKey(i.Key))
                                        {
                                            Instrument ins = this.qManager.instruments[i.Key];
                                            ins.last_price = i.Value.last_price;
                                            ins.buy_notional = i.Value.notional_buy;
                                            ins.sell_notional = i.Value.notional_sell;
                                            ins.buy_quantity = i.Value.quantity_buy;
                                            ins.sell_quantity = i.Value.quantity_sell;
                                            ins.baseBalance = new Balance();
                                            ins.baseBalance.ccy = ins.baseCcy;
                                            ins.baseBalance.total = i.Value.baseCcy_total;
                                            ins.baseBalance.inuse = i.Value.baseCcy_inuse;
                                            ins.quoteBalance = new Balance();
                                            ins.quoteBalance.ccy = ins.quoteCcy;
                                            ins.quoteBalance.total = i.Value.quoteCcy_total;
                                            ins.quoteBalance.inuse = i.Value.quoteCcy_inuse;
                                            ins.my_buy_quantity = i.Value.my_quantity_buy;
                                            ins.my_buy_notional = i.Value.my_notional_buy;
                                            ins.my_sell_quantity = i.Value.my_quantity_sell;
                                            ins.my_sell_notional = i.Value.my_notional_sell;
                                            ins.quote_fee = i.Value.quoteFee_total;
                                            ins.base_fee = i.Value.baseFee_total;
                                        }
                                        else
                                        {
                                            Instrument ins = new Instrument();
                                            ins.symbol = i.Value.symbol;
                                            ins.market = i.Value.market;
                                            ins.baseCcy = i.Value.baseCcy;
                                            ins.quoteCcy = i.Value.quoteCcy;

                                            ins.last_price = i.Value.last_price;
                                            ins.buy_notional = i.Value.notional_buy;
                                            ins.sell_notional = i.Value.notional_sell;
                                            ins.buy_quantity = i.Value.quantity_buy;
                                            ins.sell_quantity = i.Value.quantity_sell;
                                            ins.baseBalance = new Balance();
                                            ins.baseBalance.ccy = ins.baseCcy;
                                            ins.baseBalance.total = i.Value.baseCcy_total;
                                            ins.baseBalance.inuse = i.Value.baseCcy_inuse;
                                            ins.quoteBalance = new Balance();
                                            ins.quoteBalance.ccy = ins.quoteCcy;
                                            ins.quoteBalance.total = i.Value.quoteCcy_total;
                                            ins.quoteBalance.inuse = i.Value.quoteCcy_inuse;
                                            ins.my_buy_quantity = i.Value.my_quantity_buy;
                                            ins.my_buy_notional = i.Value.my_notional_buy;
                                            ins.my_sell_quantity = i.Value.my_quantity_sell;
                                            ins.my_sell_notional = i.Value.my_notional_sell;
                                            ins.quote_fee = i.Value.quoteFee_total;
                                            ins.base_fee = i.Value.baseFee_total;
                                        }
                                    }
                                    break;
                                case "connection":
                                    var conninfos = JsonSerializer.Deserialize<Dictionary<string, connecitonStatus>>(content);
                                    this.BeginInvoke(this.updateConnectionStatus, conninfos);
                                    break;
                                case "thread":
                                    var thinfos = JsonSerializer.Deserialize<Dictionary<string, threadStatus>>(content);
                                    this.BeginInvoke(this.updateThreadStatus, thinfos);
                                    break;
                                case "log":
                                    var logs = JsonSerializer.Deserialize<List<logEntry>>(content);
                                    logType lType = logType.NONE;
                                    foreach (var l in logs)
                                    {
                                        switch (l.logtype)
                                        {
                                            case "INFO":
                                                lType = logType.INFO;
                                                this.addLog("[Trade Engine]" + l.msg, lType);
                                                break;
                                            case "WARNING":
                                                lType = logType.WARNING;
                                                this.addLog("[Trade Engine]" + l.msg, lType);
                                                break;
                                            case "ERROR":
                                                lType = logType.ERROR;
                                                this.addLog("[Trade Engine]" + l.msg, lType);
                                                break;
                                            case "FATAL":
                                                lType = logType.FATAL;
                                                this.addLog("[Trade Engine]" + l.msg, lType);
                                                break;
                                            default:
                                                lType = logType.NONE;
                                                this.addLog("[Trade Engine]" + l.msg, lType);
                                                break;
                                        }
                                    }
                                    break;
                                case "fill":
                                    var fills = JsonSerializer.Deserialize<List<fillInfo>>(content);
                                    this.BeginInvoke(this.updateFills, fills);
                                    break;
                                default:
                                    this.addLog(msg);
                                    break;
                            }

                            break;
                        case WebSocketMessageType.Binary:
                            break;
                        case WebSocketMessageType.Close:
                            this.addLog("Closed by server");
                            output = false;
                            msg = "Closing message[onListen]:" + Encoding.UTF8.GetString(this.ws_memory.ToArray());
                            break;
                        default:
                            msg = "";
                            break;
                    }
                    this.ws_memory.SetLength(0);
                    this.ws_memory.Position = 0;
                    break;
                case WebSocketState.None:
                case WebSocketState.Connecting:
                    //Do nothing
                    break;
                case WebSocketState.CloseReceived:
                case WebSocketState.CloseSent:
                case WebSocketState.Closed:
                case WebSocketState.Aborted:
                default:
                    Thread.Sleep(1000);
                    break;
            }
            return (true, 0);
        }
        private void updateFills(List<fillInfo> fills)
        {
            foreach (var f in fills)
            {
                this.gridView_orders.Rows.Insert(0);
                this.gridView_orders.Rows[0].Cells[0].Value = f.timestamp;
                this.gridView_orders.Rows[0].Cells[1].Value = f.market;
                this.gridView_orders.Rows[0].Cells[2].Value = f.symbol;
                this.gridView_orders.Rows[0].Cells[3].Value = f.side;
                this.gridView_orders.Rows[0].Cells[4].Value = f.fill_price;
                this.gridView_orders.Rows[0].Cells[5].Value = f.quantity;
                this.gridView_orders.Rows[0].Cells[7].Value = f.fee;
            }
        }
        private void updateConnectionStatus(Dictionary<string,connecitonStatus> conninfos)
        {
            foreach (var c in conninfos)
            {
                bool found = false;
                foreach (DataGridViewRow row in this.gridView_Connection.Rows)
                {
                    if (row.IsNewRow)
                    {
                        continue;
                    }
                    if (row.Cells[0] != null && row.Cells[0].Value.ToString() == c.Key)
                    {
                        row.Cells[1].Value = c.Value.publicState;
                        row.Cells[2].Value = c.Value.privateState;
                        row.Cells[3].Value = c.Value.avgRTT.ToString("N3");
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    this.gridView_Connection.Rows.Add(c.Key, c.Value.publicState, c.Value.privateState, c.Value.avgRTT.ToString("N3"));
                }
            }
        }
        private void updateThreadStatus(Dictionary<string,threadStatus> thinfos)
        {
            string st;
            foreach (var t in thinfos)
            {
                bool found = false;
                if (t.Value.isRunning)
                {
                    st = "Running";
                }
                else
                {
                    st = "Stopped";
                }
                foreach (DataGridViewRow row in this.gridView_ThStatus.Rows)
                {
                    if (row.IsNewRow)
                    {
                        continue;
                    }
                    if (row.Cells[0] != null && row.Cells[0].Value.ToString() == t.Key)
                    {

                        row.Cells[1].Value = st;
                        row.Cells[2].Value = t.Value.avgProcessingTime.ToString("N3");
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    this.gridView_ThStatus.Rows.Add(t.Key, st, t.Value.avgProcessingTime.ToString("N3"));
                }
            }
        }

        private void _update()
        {
            switch (this.tabControl.SelectedTab.Text)
            {
                case "Main":
                    this.update_main();
                    break;
                case "Instrument":
                    this.update_Instrument();
                    break;
                case "Strategy":
                    this.update_strategy();
                    break;
                default:
                    break;
            }
            this.updateLog();
            this.updating = false;
        }
        private void update_main()
        {
            decimal volume = 0;
            decimal tradingPL = 0;
            decimal fee = 0;
            decimal total = 0;

            if(!this.monitoringMode)
            {
                foreach (var stg in this.strategies.Values)
                {
                    if (stg.maker != null && stg.taker != null)
                    {
                        volume = stg.maker.my_buy_notional + stg.maker.my_sell_notional;
                        tradingPL = (stg.taker.my_sell_notional - stg.taker.my_sell_quantity * stg.taker.mid) + (stg.taker.my_buy_quantity * stg.taker.mid - stg.taker.my_buy_notional);
                        tradingPL += (stg.maker.my_sell_notional - stg.maker.my_sell_quantity * stg.taker.mid) + (stg.maker.my_buy_quantity * stg.taker.mid - stg.maker.my_buy_notional);
                        fee = stg.taker.base_fee * stg.taker.mid + stg.taker.quote_fee + stg.maker.base_fee * stg.taker.mid + stg.maker.quote_fee;
                        total = tradingPL - fee;
                        bool found = false;
                        foreach (DataGridViewRow row in this.gridView_PnL.Rows)
                        {
                            if (row.IsNewRow)
                            {
                                continue;
                            }
                            if (row.Cells[0] != null && row.Cells[0].Value.ToString() == stg.name)
                            {

                                row.Cells[1].Value = volume.ToString("N2");
                                row.Cells[2].Value = tradingPL.ToString("N2");
                                row.Cells[3].Value = fee.ToString("N2");
                                row.Cells[4].Value = total.ToString("N2");
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            this.gridView_PnL.Rows.Add(stg.name, volume.ToString("N2"), tradingPL.ToString("N2"), fee.ToString("N2"), total.ToString("N2"));
                        }
                    }

                }
            }
            else
            {
                foreach (var stg in this.strategies.Values)
                {
                    if (stg.maker != null && stg.taker != null)
                    {
                        volume = stg.maker.my_buy_notional + stg.maker.my_sell_notional;
                        tradingPL = (stg.taker.my_sell_notional - stg.taker.my_sell_quantity * stg.taker.mid) + (stg.taker.my_buy_quantity * stg.taker.mid - stg.taker.my_buy_notional);
                        tradingPL += (stg.maker.my_sell_notional - stg.maker.my_sell_quantity * stg.taker.mid) + (stg.maker.my_buy_quantity * stg.taker.mid - stg.maker.my_buy_notional);
                        fee = stg.taker.base_fee * stg.taker.mid + stg.taker.quote_fee + stg.maker.base_fee * stg.taker.mid + stg.maker.quote_fee;
                        total = tradingPL - fee;
                        
                    }
                    bool found = false;
                    foreach (DataGridViewRow row in this.gridView_PnL.Rows)
                    {
                        if (row.IsNewRow)
                        {
                            continue;
                        }
                        if (row.Cells[0] != null && row.Cells[0].Value.ToString() == stg.name)
                        {

                            row.Cells[1].Value = stg.notionalVolume.ToString("N2");
                            row.Cells[2].Value = stg.tradingPnL.ToString("N2");
                            row.Cells[3].Value = stg.totalFee.ToString("N2");
                            row.Cells[4].Value = stg.totalPnL.ToString("N2");
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        this.gridView_PnL.Rows.Add(stg.name, stg.notionalVolume.ToString("N2"), stg.tradingPnL.ToString("N2"), stg.totalFee.ToString("N2"), stg.totalPnL.ToString("N2"));
                    }

                }
            }
           

            
        }
        private void update_Instrument()
        {
            if (this.selected_ins != null)
            {
                this.lbl_symbol.Text = this.selected_ins.symbol;
                this.lbl_baseCcyName.Text = this.selected_ins.baseCcy;
                this.lbl_quoteCcyName.Text = this.selected_ins.quoteCcy;
                this.lbl_market.Text = this.selected_ins.market;
                this.lbl_lastprice.Text = this.selected_ins.last_price.ToString();
                this.lbl_notional.Text = (this.selected_ins.buy_notional + this.selected_ins.sell_notional).ToString("N2");
                this.lbl_baseCcyTotal.Text = this.selected_ins.baseBalance.total.ToString("N" + this.selected_ins.quantity_scale);
                this.lbl_quoteCcyTotal.Text = this.selected_ins.quoteBalance.total.ToString("N2");
                this.lbl_baseCcyInuse.Text = this.selected_ins.baseBalance.inuse.ToString("N" + this.selected_ins.quantity_scale);
                this.lbl_quoteCcyInuse.Text = this.selected_ins.quoteBalance.inuse.ToString("N2");
                this.lbl_sellQuantity.Text = this.selected_ins.my_sell_quantity.ToString("N" + this.selected_ins.quantity_scale);
                this.lbl_buyQuantity.Text = this.selected_ins.my_buy_quantity.ToString("N" + this.selected_ins.quantity_scale);
                this.lbl_sellNotional.Text = this.selected_ins.my_sell_notional.ToString("N2");
                this.lbl_buyNotional.Text = this.selected_ins.my_buy_notional.ToString("N2");
                if (this.selected_ins.my_sell_quantity > 0)
                {
                    this.lbl_sellAvgPrice.Text = (this.selected_ins.my_sell_notional / this.selected_ins.my_sell_quantity).ToString("N" + this.selected_ins.price_scale);
                }
                else
                {
                    this.lbl_sellAvgPrice.Text = "0";
                }
                if (this.selected_ins.my_buy_quantity > 0)
                {
                    this.lbl_buyAvgPrice.Text = (this.selected_ins.my_buy_notional / this.selected_ins.my_buy_quantity).ToString("N" + this.selected_ins.price_scale);
                }
                else
                {
                    this.lbl_buyAvgPrice.Text = "0";
                }
                this.lbl_baseFee.Text = this.selected_ins.base_fee.ToString("N" + this.selected_ins.quantity_scale);
                this.lbl_quoteFee.Text = this.selected_ins.quote_fee.ToString("N" + this.selected_ins.quantity_scale);
                this.updateQuotesView(this.gridView_Ins, this.selected_ins);

                //while (Interlocked.CompareExchange(ref this.selected_ins.orders_lock, 1, 0) != 0)
                //{
                //    bool stop = true;
                //}
                //this.gridView_insOrders.Rows.Clear();
                //foreach (var item in this.selected_ins.live_orders)
                //{
                //    DataSpotOrderUpdate ord = item.Value;
                //    this.gridView_insOrders.Rows.Insert(0);
                //    this.gridView_insOrders.Rows[0].Cells[0].Value = ((DateTime)ord.timestamp).ToString("HH:mm:ss.fff");
                //    this.gridView_insOrders.Rows[0].Cells[1].Value = ord.market;
                //    this.gridView_insOrders.Rows[0].Cells[2].Value = ord.symbol;
                //    this.gridView_insOrders.Rows[0].Cells[3].Value = ord.side.ToString();
                //    this.gridView_insOrders.Rows[0].Cells[4].Value = ord.order_price.ToString("N" + this.selected_ins.price_scale);
                //    this.gridView_insOrders.Rows[0].Cells[5].Value = ord.order_quantity.ToString("N" + this.selected_ins.quantity_scale);
                //    this.gridView_insOrders.Rows[0].Cells[6].Value = ord.filled_quantity.ToString("N" + this.selected_ins.quantity_scale);
                //    this.gridView_insOrders.Rows[0].Cells[7].Value = ord.status.ToString();
                //}
                //Volatile.Write(ref this.selected_ins.orders_lock, 0);
            }
        }
        private void update_strategy()
        {
            DataFill fill;
            //DataSpotOrderUpdate ord;
            while (this.filledOrderQueue.Count > 0)
            {
                if (this.filledOrderQueue.TryDequeue(out fill))
                {
                    //ord = this.oManager.orders[ord_id];
                    if (this.qManager.instruments.ContainsKey(fill.symbol_market))
                    {
                        Instrument ins = this.qManager.instruments[fill.symbol_market];
                        this.gridView_orders.Rows.Insert(0);
                        this.gridView_orders.Rows[0].Cells[0].Value = ((DateTime)fill.timestamp).ToString("HH:mm:ss.fff");
                        this.gridView_orders.Rows[0].Cells[1].Value = fill.market;
                        this.gridView_orders.Rows[0].Cells[2].Value = fill.symbol;
                        this.gridView_orders.Rows[0].Cells[3].Value = fill.side.ToString();
                        this.gridView_orders.Rows[0].Cells[4].Value = fill.price.ToString("N" + ins.price_scale);
                        this.gridView_orders.Rows[0].Cells[5].Value = fill.quantity.ToString("N" + ins.quantity_scale);
                        this.gridView_orders.Rows[0].Cells[7].Value = fill.fee_quote + fill.fee_base * fill.price;
                    }

                    this.oManager.pushbackFill(fill);
                }
            }
            if (this.selected_stg != null)
            {
                if (this.selected_stg.taker != null)
                {
                    this.lbl_takerName.Text = this.selected_stg.taker.symbol_market;
                    this.lbl_baseCcy_taker.Text = this.selected_stg.taker.baseBalance.total.ToString("N5");
                    this.lbl_quoteCcy_taker.Text = this.selected_stg.taker.quoteBalance.total.ToString("N5");
                    this.lbl_makerfee_taker.Text = this.selected_stg.taker.maker_fee.ToString("N5");
                    this.lbl_takerfee_taker.Text = this.selected_stg.taker.taker_fee.ToString("N5");
                    this.updateQuotesView(this.gridView_Taker, this.selected_stg.taker);
                    this.lbl_adjustedask.Text = this.selected_stg.taker.adjusted_bestask.Item1.ToString("N" + this.selected_stg.taker.price_scale);
                    this.lbl_adjustedbid.Text = this.selected_stg.taker.adjusted_bestbid.Item1.ToString("N" + this.selected_stg.taker.price_scale);
                }
                if (this.selected_stg.maker != null)
                {
                    this.lbl_makerName.Text = this.selected_stg.maker.symbol_market;
                    this.lbl_baseCcy_maker.Text = this.selected_stg.maker.baseBalance.total.ToString("N5");
                    this.lbl_quoteCcy_maker.Text = this.selected_stg.maker.quoteBalance.total.ToString("N5");
                    this.lbl_makerfee_maker.Text = this.selected_stg.maker.maker_fee.ToString("N5");
                    this.lbl_takerfee_maker.Text = this.selected_stg.maker.taker_fee.ToString("N5");
                    this.updateQuotesView(this.gridView_Maker, this.selected_stg.maker);
                    this.lbl_askprice.Text = this.selected_stg.live_askprice.ToString("N" + this.selected_stg.maker.price_scale);
                    this.lbl_bidprice.Text = this.selected_stg.live_bidprice.ToString("N" + this.selected_stg.maker.price_scale);
                    this.lbl_skewpoint.Text = this.selected_stg.skew_point.ToString("N");
                }
                foreach (DataGridViewRow row in this.gridView_orders.Rows)
                {
                    string symbol_market = row.Cells[2].Value + "@" + row.Cells[1].Value;
                    if (symbol_market == this.selected_stg.maker.symbol_market || symbol_market == this.selected_stg.taker.symbol_market)
                    {
                        row.Visible = true;
                    }
                    else if(!row.IsNewRow)
                    {
                        row.Visible = false;
                    }
                }
            }

            
        }
        private void updateQuotesView(DataGridView view, Instrument ins)
        {
            int i = 0;
            while (Interlocked.CompareExchange(ref ins.quotes_lock, 1, 0) != 0)
            {

            }
            view.Rows[QuoteManager.NUM_OF_QUOTES].Cells[1].Value = ins.last_price.ToString("N" + ins.price_scale);
            while (i < QuoteManager.NUM_OF_QUOTES)
            {
                if (i < ins.asks.Count)
                {
                    view.Rows[QuoteManager.NUM_OF_QUOTES - 1 - i].Cells[0].Value = ins.asks.ElementAt(i).Value.ToString("N" + ins.quantity_scale);
                    view.Rows[QuoteManager.NUM_OF_QUOTES - 1 - i].Cells[1].Value = ins.asks.ElementAt(i).Key.ToString("N" + ins.price_scale);
                }
                else
                {
                    view.Rows[QuoteManager.NUM_OF_QUOTES - 1 - i].Cells[0].Value = "";
                    view.Rows[QuoteManager.NUM_OF_QUOTES - 1 - i].Cells[1].Value = "";
                }

                ++i;
            }
            i = 0;
            foreach (var item in ins.bids.Reverse())
            {
                if (i > QuoteManager.NUM_OF_QUOTES - 1)
                {
                    break;
                }
                else
                {
                    view.Rows[QuoteManager.NUM_OF_QUOTES + 1 + i].Cells[1].Value = item.Key.ToString("N" + ins.price_scale);
                    view.Rows[QuoteManager.NUM_OF_QUOTES + 1 + i].Cells[2].Value = item.Value.ToString("N" + ins.quantity_scale);
                }
                ++i;
            }
            Volatile.Write(ref ins.quotes_lock, 0);
            while (i < QuoteManager.NUM_OF_QUOTES)
            {
                view.Rows[QuoteManager.NUM_OF_QUOTES + 1 + i].Cells[1].Value = "";
                view.Rows[QuoteManager.NUM_OF_QUOTES + 1 + i].Cells[2].Value = "";
                ++i;
            }
        }
        private void comboSymbols_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.qManager.instruments.ContainsKey(this.comboSymbols.Text))
            {
                this.selected_ins = this.qManager.instruments[this.comboSymbols.Text];
            }
            else
            {
                this.selected_ins = null;
            }
        }

        private async Task<bool> tradePreparation(bool liveTrading)
        {
            try
            {
                if(!this.monitoringMode)
                {
                    this.oManager.setVirtualMode(!liveTrading);
                    foreach (var mkt in this.qManager._markets)
                    {
                        if (this.msgLogging)
                        {
                            this.crypto_client.setMsgLogging(mkt.Key, this.outputPath);
                        }
                        await this.qManager.connectPublicChannel(mkt.Key);
                        if (liveTrading || this.privateConnect)
                        {
                            await this.oManager.connectPrivateChannel(mkt.Key);
                        }
                    }
                    if (this.oManager.getVirtualMode())
                    {
                        if (!this.qManager.setVirtualBalance(this.virtualBalanceFile))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (!this.qManager.setBalance(await this.crypto_client.getBalance(this.qManager._markets.Keys)))
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    await connectTradeEngine();
                    this.oManager.setVirtualMode(true);
                    while(this.masterInfoReceived == false || this.strategySettingReceived == false)
                    {
                        Thread.Sleep(1000);
                    }
                    foreach (var mkt in this.qManager._markets)
                    {
                        await this.qManager.connectPublicChannel(mkt.Key);
                        //await this.oManager.connectPrivateChannel(mkt.Key);
                    }
                }

                    

                foreach (var ins in this.qManager.instruments.Values)
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

                
                this.qManager.ready = true;

                if (this.monitoringMode == false && (liveTrading || this.privateConnect))
                {
                    await crypto_client.subscribeSpotOrderUpdates(this.qManager._markets.Keys);
                }

                this.oManager.ready = true;


                this.thManager.addThread("updateQuotes", this.qManager._updateQuotes,this.qManager.updateQuotesOnClosing,this.qManager.updateQuotesOnError);
                this.thManager.addThread("updateTrades", this.qManager._updateTrades,this.qManager.updateTradesOnClosing,this.qManager.updateTradesOnClosing);
                if(!this.monitoringMode)
                {
                    this.thManager.addThread("updateOrders", this.oManager._updateOrders, this.oManager.updateOrdersOnClosing, this.oManager.updateOrdersOnError);
                    this.thManager.addThread("updateFill", this.oManager._updateFill, this.oManager.updateFillOnClosing);
                    this.thManager.addThread("optimize", this.qManager._optimize, this.qManager.optimizeOnClosing, this.qManager.optimizeOnError);
                    this.thManager.addThread("orderLogging", this.oManager._orderLogging, this.oManager.ordLoggingOnClosing, this.oManager.ordLoggingOnError);
                }
                this.threadsStarted = true;
            }
            catch (Exception ex)
            {
                this.BeginInvoke(() => { this.button_receiveFeed.Enabled = false; });
                this.addLog("An error occured while initializing the platforms.", Enums.logType.ERROR);
                this.addLog(ex.Message, Enums.logType.ERROR);
                return false;
            }

            this.BeginInvoke(()=>{ this.button_startTrading.Enabled = true;});
            return true;
        }
        private bool startTrading()
        {
            this.enabled = true;
            foreach(var stg in this.strategies.Values)
            {
                stg.enabled = true;
            }
            this.addLog("Trading started.");
            return true;
        }

        private bool stopStrategies()
        {
            foreach (var stg in this.strategies.Values)
            {
                stg.enabled = false;
            }
            this.enabled = false;
            return true;
        }
        private async Task<bool> stopTrading(bool error = false)
        {

            if (Interlocked.CompareExchange(ref this.stopTradingCalled, 1, 0) == 0)
            {
                stopStrategies();
                if (error)
                {
                    this.addLog("Error received. Now stopping the trading. Check the exchange to make sure all the orders are cancelled.", Enums.logType.ERROR);

                }
                else
                {
                    this.addLog("Stopping the trading normally");
                }
                this.addLog("Stopping trading process started");
                Thread.Sleep(1000);
                if (this.threadsStarted)
                {
                    if (this.oManager.ready)
                    {
                        await this.oManager.cancelAllOrders();
                        Thread.Sleep(1000);

                        foreach (var th in this.thManager.threads)
                        {
                            th.Value.isRunning = false;
                        }
                    }
                }

            }
            return true;
        }
        private async void receiveFeed_clicked(object sender, EventArgs e)
        {
            this.button_receiveFeed.Enabled = false;
            Thread th = new Thread(async () =>
            {
                await this.tradePreparation(this.live);
            });
            th.Start();
                
            //if (await this.tradePreparation(this.live))
            //{
            //    this.button_startTrading.Enabled = true;
            //}
        }
        private async void startTrading_clicked(object sender, EventArgs e)
        {
            if (this.startTrading())
            {
                this.button_startTrading.Enabled = false;
                this.button_orderTest.Enabled = false;
            }
        }
        private async void button_stopTrading_Click(object sender, EventArgs e)
        {
            await this.stopTrading();
            this.button_startTrading.Enabled = false;
            this.button_receiveFeed.Enabled = true;
        }
        private async void test_Click(object sender, EventArgs e)
        {


            await tradeTest(this.qManager.instruments["eth_jpy@coincheck"],true);
        }
        private async Task onErrorCheck()
        {
            await this.crypto_client.bitbank_client.disconnectPublic();
        }

        private async Task tradeTest(Instrument ins, bool fillcheck)
        {
            DataSpotOrderUpdate ord;
            Stopwatch sw = new Stopwatch();
            double latency = 0;
            this.oManager.setVirtualMode(false);

            this.addLog("Testing orderManager");
            Thread.Sleep(3000);
            this.addLog("Placing a new order");
            string ordid;
            sw.Start();
            ord = await this.oManager.placeNewSpotOrder(ins, orderSide.Buy, orderType.Limit, (decimal)0.01, 600000);
            sw.Stop();
            if (ord != null)
            {
                ordid = ord.order_id;
                this.addLog(ord.ToString());
            }
            else
            {
                this.addLog("Failed to place a new order");
                return;
            }
            latency = sw.Elapsed.TotalMilliseconds;
            this.addLog("Round trip latency:" + latency.ToString("N3"));
            sw.Reset();
            Thread.Sleep(1000);
            this.addLog("Live Order Count " + this.oManager.live_orders.Count.ToString());
            Thread.Sleep(3000);
            this.addLog("modifing a order");
            sw.Start();
            ord = await this.oManager.placeModSpotOrder(ins, ordid, (decimal)0.01, 570000, false);
            sw.Stop();
            if (ord != null)
            {
                ordid = ord.order_id;
                this.addLog(ord.ToString());
            }
            else
            {
                this.addLog("Failed to place a mod order");
                return;
            }
            latency = sw.Elapsed.TotalMilliseconds;
            this.addLog("Round trip latency:" + latency.ToString("N3"));
            sw.Reset();
            Thread.Sleep(1000);
            this.addLog("Live Order Count " + this.oManager.live_orders.Count.ToString());
            if (this.oManager.live_orders.Count > 0)
            {
                this.addLog("Cancelling a order");
                //ord = this.oManager.live_orders.Values.First();
                //this.addLog(ord.ToString());
                sw.Start();
                ord = await this.oManager.placeCancelSpotOrder(ins, ordid);
                sw.Stop();
                if (ord != null)
                {
                    ordid = ord.client_order_id;
                    this.addLog(ord.ToString());
                }
                else
                {
                    this.addLog("Failed to place a can order");
                    return;
                }
                latency = sw.Elapsed.TotalMilliseconds;
                this.addLog("Round trip latency:" + latency.ToString("N3"));
                sw.Reset();
            }
            Thread.Sleep(1000);
            this.addLog("Live Order Count " + this.oManager.live_orders.Count.ToString());

            if (fillcheck)
            {
                this.addLog("Fill Check");
                sw.Start();
                this.addLog("Using limit order");
                await this.crypto_client.coincheck_client.placeNewOrder(ins.symbol, "buy", (decimal)700000, (decimal)0.01);
                //ord = await this.oManager.placeNewSpotOrder(ins, orderSide.Buy, orderType.Limit, (decimal)0.01, 700000);
                sw.Stop();
                this.addLog(ord.ToString());
                latency = sw.Elapsed.TotalMilliseconds;
                this.addLog("Round trip latency:" + latency.ToString("N3"));
                sw.Reset();
                Thread.Sleep(1000);
                this.addLog("Live Order Count " + this.oManager.live_orders.Count.ToString());

                this.addLog("Market Order");
                sw.Start();
                ord = await this.oManager.placeNewSpotOrder(ins, orderSide.Sell, orderType.Market, (decimal)0.01, 700000);
                latency = sw.Elapsed.TotalMilliseconds;
                this.addLog("Round trip latency:" + latency.ToString("N3"));
                sw.Reset();
                if (ord != null)
                {
                    this.addLog(ord.ToString());
                }
                else
                {
                    this.addLog("Market order failed.");
                }
                Thread.Sleep(1000);
                this.addLog("Live Order Count " + this.oManager.live_orders.Count.ToString());
            }

        }
        private async void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            //Stop strategy -> cancel all orders -> update all orders -> stop threads
            await this.stopTrading();

            this.aborting = true;
            Thread.Sleep(1000);

            if (this.updatingTh != null && this.updatingTh.IsAlive)
            {
                this.updatingTh.Join(2000);
            }
        }
        private async void timer_statusCheck_Tick(object sender, EventArgs e)
        {
            //Connection
            this.qManager.checkConnections();
            this.oManager.checkConnections();
            foreach (var mkt in this.qManager._markets)
            {
                bool found = false;
                foreach (DataGridViewRow row in this.gridView_Connection.Rows)
                {
                    if (row.IsNewRow)
                    {
                        continue;
                    }
                    if (row.Cells[0] != null && row.Cells[0].Value.ToString() == mkt.Key)
                    {
                        row.Cells[1].Value = mkt.Value.ToString();
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    this.gridView_Connection.Rows.Add(mkt.Key, mkt.Value.ToString(), WebSocketState.None.ToString());
                }
            }

            foreach (var mkt in this.oManager.connections)
            {
                bool found = false;
                foreach (DataGridViewRow row in this.gridView_Connection.Rows)
                {
                    if (row.IsNewRow)
                    {
                        continue;
                    }
                    if (row.Cells[0] != null && row.Cells[0].Value.ToString() == mkt.Key)
                    {
                        row.Cells[2].Value = mkt.Value.ToString();
                        switch(row.Cells[0].Value)
                        {
                            case "bitbank":
                                row.Cells[3].Value = (this.crypto_client.bitbank_client.avgLatency() / 1000).ToString("N2");
                                break;
                            case "coincheck":
                                row.Cells[3].Value = (this.crypto_client.coincheck_client.avgLatency() / 1000).ToString("N2");
                                break;
                            case "bittrade":
                                row.Cells[3].Value = (this.crypto_client.bittrade_client.avgLatency() / 1000) .ToString("N2");
                                break;
                            default:
                                row.Cells[3].Value = "None";
                                break;

                        }
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    this.gridView_Connection.Rows.Add(mkt.Key, WebSocketState.None.ToString(), mkt.Value.ToString());
                }
            }

            List<string> stoppedThreads = new List<string>();
            //Thread
            foreach (var th in this.thManager.threads)
            {
                bool found = false;
                string st;
                if (this.stopTradingCalled == 0 && th.Value.isRunning == false)
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
                foreach (DataGridViewRow row in this.gridView_ThStatus.Rows)
                {
                    if (row.IsNewRow)
                    {
                        continue;
                    }
                    if (row.Cells[0] != null && row.Cells[0].Value.ToString() == th.Key)
                    {

                        row.Cells[1].Value = st;
                        row.Cells[2].Value = (th.Value.totalElapsedTime / th.Value.count / 1000).ToString("N3");
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    this.gridView_ThStatus.Rows.Add(th.Key, st);
                }
            }

            foreach (var stoppedTh in stoppedThreads)
            {
                if (stoppedTh.Contains("Public"))
                {
                    bool currentTradingState = this.enabled;
                    stopStrategies();
                    await this.oManager.cancelAllOrders();
                    string market = stoppedTh.Replace("Public", "");
                    this.addLog("Public Connection to " + market + " lost reconnecting in 5 sec", Enums.logType.WARNING);
                    this.thManager.disposeThread(stoppedTh);
                    Thread.Sleep(5000);
                    await this.qManager.connectPublicChannel(market);
                    Thread.Sleep(5000);
                    foreach (var ins in this.qManager.instruments.Values)
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
                    if (this.oManager.getVirtualMode())
                    {
                        if (!this.qManager.setVirtualBalance(this.virtualBalanceFile))
                        {

                        }
                    }
                    else
                    {
                        if (!this.qManager.setBalance(await this.crypto_client.getBalance(this.qManager._markets.Keys)))
                        {

                        }
                    }
                    this.addLog("Reconnection completed.");
                    if(currentTradingState)
                    {
                        startTrading();
                    }
                }
                else if (stoppedTh.Contains("Private"))
                {
                    bool currentTradingState = this.enabled;
                    stopStrategies();
                    await this.oManager.cancelAllOrders();
                    string market = stoppedTh.Replace("Private", "");
                    this.addLog("Private Connection to " + market + " lost reconnecting in 5 sec", Enums.logType.WARNING);
                    this.thManager.disposeThread(stoppedTh);
                    Thread.Sleep(5000);
                    await this.oManager.connectPrivateChannel(market);
                    Thread.Sleep(5000);
                    string[] markets = [market];
                    if (this.live || this.privateConnect)
                    {
                        await crypto_client.subscribeSpotOrderUpdates(markets);
                    }
                    if (this.oManager.getVirtualMode())
                    {
                        if (!this.qManager.setVirtualBalance(this.virtualBalanceFile))
                        {

                        }
                    }
                    else
                    {
                        if (!this.qManager.setBalance(await this.crypto_client.getBalance(this.qManager._markets.Keys)))
                        {

                        }
                    }
                    this.addLog("Reconnection completed.");
                    if(currentTradingState)
                    {
                        startTrading();
                    }
                }
                else
                {
                    this.addLog("Updating thread stopped. Stopping all the process", Enums.logType.ERROR);
                }
            }
            if (this.qManager.ordBookQueue.Count() > 1000)
            {
                this.addLog("The order book queue count exceeds 1000.", Enums.logType.WARNING);
                if (this.qManager.ordBookQueue.Count() > 10000)
                {

                }
            }
            if (this.crypto_client.ordUpdateQueue.Count() > 1000)
            {
                this.addLog("The order update queue count exceeds 1000.", Enums.logType.WARNING);
            }
            if (this.crypto_client.fillQueue.Count() > 1000)
            {
                this.addLog("The fill queue count exceeds 1000.", Enums.logType.WARNING);
            }
            this.lbl_quoteUpdateCount.Text = this.qManager.ordBookQueue.Count().ToString();
            this.lbl_orderUpdateCount.Text = this.crypto_client.ordUpdateQueue.Count().ToString();
            this.lbl_fillUpdateCount.Text = this.crypto_client.fillQueue.Count().ToString();
            this.lbl_optCount.Text = this.qManager.optQueue.Count().ToString();

        }

        private async void Form1_Shown(object sender, EventArgs e)
        {
            if (this.autoStart)
            {
                this.button_orderTest.Enabled = false;
                this.button_receiveFeed.Enabled = false;
                this.button_startTrading.Enabled = false;

                await this.tradePreparation(this.live);
                if(!monitoringMode)
                {
                    this.addLog("Waiting for 5 sec", Enums.logType.INFO);
                    Thread.Sleep(5000);
                    this.startTrading();
                }
                else
                {

                }
            }
        }

        private async void timer_PeriodicMsg_Tick(object sender, EventArgs e)
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
            await this.crypto_client.getBalance(this.qManager._markets.Keys);

            if (DateTime.UtcNow > this.nextMsgTime)
            {
                foreach (var stg in this.strategies.Values)
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

                await this.MsgDeliverer.sendMessage(msg);
                this.nextMsgTime += TimeSpan.FromMinutes(this.msg_Interval);
            }
            
            

            if (DateTime.UtcNow > this.endTime)
            {
                this.addLog("Closing application at EoD.");
                await this.stopTrading(false);
                Application.Exit();
            }
        }

        private void comboStrategy_SelectedIndexChanged(object sender, EventArgs e)
        {
            if(this.strategies.ContainsKey(this.comboStrategy.Text))
            {
                this.selected_stg = this.strategies[this.comboStrategy.Text];
                this.lbl_makerName.Text = this.selected_stg.maker_symbol_market;
                this.lbl_takerName.Text = this.selected_stg.taker_symbol_market;
                this.lbl_makerfee_maker.Text = this.selected_stg.maker.maker_fee.ToString("N5");
                this.lbl_takerfee_maker.Text = this.selected_stg.maker.taker_fee.ToString("N5");
                this.lbl_makerfee_taker.Text = this.selected_stg.taker.maker_fee.ToString("N5");
                this.lbl_takerfee_taker.Text = this.selected_stg.taker.taker_fee.ToString("N5");
                this.lbl_stgSymbol.Text = this.selected_stg.baseCcy + this.selected_stg.quoteCcy;
                this.lbl_markup.Text = this.selected_stg.markup.ToString("N0");
                this.lbl_minMarkup.Text = this.selected_stg.min_markup.ToString("N0");
                this.lbl_maxSkew.Text = this.selected_stg.maxSkew.ToString("N0");
                this.lbl_tobsize.Text = this.selected_stg.ToBsize.ToString("N5");
                this.lbl_maxpos.Text = this.selected_stg.baseCcyQuantity.ToString("N5");
                this.lbl_skew.Text = this.selected_stg.skewThreshold.ToString("N0");
                this.lbl_oneside.Text = this.selected_stg.oneSideThreshold.ToString("N0");
                this.lbl_fillInterval.Text = this.selected_stg.intervalAfterFill.ToString("N2");
                this.lbl_ordUpdateTh.Text = this.selected_stg.modThreshold.ToString("N5");
            }
        }
    }
}
