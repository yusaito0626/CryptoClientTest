using Crypto_Clients;
using Crypto_Trading;
using CryptoClients.Net.Enums;
using Discord;
using Discord.WebSocket;
using PubnubApi.EventEngine.Subscribe.Common;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Utils;



namespace Crypto_GUI
{
    public partial class Form1 : Form
    {
        const string ver_major = "0";
        const string ver_minor = "2";
        const string ver_patch = "2";
        string configPath = "C:\\Users\\yusai\\Crypto_Project\\configs\\config.json";
        string defaultConfigPath = AppContext.BaseDirectory + "\\config.json";
        string logPath = AppContext.BaseDirectory + "\\crypto.log";
        string outputPath = AppContext.BaseDirectory;
        string APIsPath = "";
        string discordTokenFile = "";
        string masterFile = "";
        string virtualBalanceFile = "";
        string strategyFile = "";

        Crypto_Clients.Crypto_Clients crypto_client = Crypto_Clients.Crypto_Clients.GetInstance();
        QuoteManager qManager = QuoteManager.GetInstance();
        OrderManager oManager = OrderManager.GetInstance();
        ThreadManager thManager = ThreadManager.GetInstance();
        MessageDeliverer MsgDeliverer = MessageDeliverer.GetInstance();

        Strategy stg;

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

        private decimal multiplier = 1;

        private bool autoStart;
        private bool live;
        private bool privateConnect;
        private bool msgLogging;

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

            InitializeComponent();

            this.lbl_version.Text = Form1.ver_major + ":" + Form1.ver_minor + ":" + Form1.ver_patch;
            this.Text += "   Ver." +  this.lbl_version.Text;
            this.lbl_multiplier.Text = this.multiplier.ToString("N0");

            this.button_receiveFeed.Enabled = false;
            this.button_startTrading.Enabled = false;
            //this.button_orderTest.Enabled = false;

            if (!this.readConfig())
            {
                this.addLog("Failed to read config.", Enums.logType.ERROR);
                updatingTh = new Thread(update);
                updatingTh.Start();
                return;
            }


            this.logFile = new StreamWriter(new FileStream(this.logPath, FileMode.Create));

            this.qManager._addLog = this.addLog;
            this.oManager._addLog = this.addLog;
            this.crypto_client.setAddLog(this.addLog);
            this.thManager._addLog = this.addLog;

            this.readAPIFiles(this.APIsPath);

            this.qManager.initializeInstruments(this.masterFile);
            this.qManager.setQueues(this.crypto_client);

            this.oManager.setOrdLogPath(this.outputPath);
            this.oManager.setInstruments(this.qManager.instruments);
            this.oManager.filledOrderQueue = this.filledOrderQueue;

            this.stg = new Strategy();
            this.stg.readStrategyFile(this.strategyFile);

            this.stg.maker = this.qManager.instruments[this.stg.maker_symbol_market];
            this.stg.taker = this.qManager.instruments[this.stg.taker_symbol_market];
            this.stg.maker.ToBsize = this.stg.ToBsize;
            this.stg.taker.ToBsize = this.stg.ToBsize;

            this.stg._addLog = this.addLog;

            this.qManager.stg = this.stg;
            this.oManager.stg = this.stg;

            this.lbl_makerName.Text = this.stg.maker_symbol_market;
            this.lbl_takerName.Text = this.stg.taker_symbol_market;
            this.lbl_makerfee_maker.Text = this.stg.maker.maker_fee.ToString("N5");
            this.lbl_takerfee_maker.Text = this.stg.maker.taker_fee.ToString("N5");
            this.lbl_makerfee_taker.Text = this.stg.taker.maker_fee.ToString("N5");
            this.lbl_takerfee_taker.Text = this.stg.taker.taker_fee.ToString("N5");
            this.lbl_stgSymbol.Text = this.stg.baseCcy + this.stg.quoteCcy;
            this.lbl_markup.Text = this.stg.markup.ToString("N0");
            this.lbl_minMarkup.Text = this.stg.min_markup.ToString("N0");
            this.lbl_maxSkew.Text = this.stg.maxSkew.ToString("N0");
            this.lbl_tobsize.Text = this.stg.ToBsize.ToString("N5");
            this.lbl_maxpos.Text = this.stg.baseCcyQuantity.ToString("N5");
            this.lbl_skew.Text = this.stg.skewThreshold.ToString("N0");
            this.lbl_oneside.Text = this.stg.oneSideThreshold.ToString("N0");
            this.lbl_fillInterval.Text = this.stg.intervalAfterFill.ToString("N2");
            this.lbl_ordUpdateTh.Text = this.stg.modThreshold.ToString("N5");

            foreach (string key in this.qManager.instruments.Keys)
            {
                this.comboSymbols.Items.Add(key);
            }

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
            this.timer_statusCheck.Start();
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
                if(!this.live)
                {
                    this.privateConnect = elem.GetBoolean();
                }
            }
            else
            {
                this.privateConnect = true;
            }
            if (root.TryGetProperty("msgLogging", out elem))
            {
                this.msgLogging = elem.GetBoolean();
            }
            else
            {
                this.msgLogging = false;
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
            return true;

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
        private void addLog(string body, Enums.logType logtype = Enums.logType.INFO)
        {
            string messageline = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   [" + logtype.ToString() + "]" + body + "\n";
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
            this.logQueue.Enqueue(messageline);
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
            if (!await this.MsgDeliverer.setDiscordToken(this.discordTokenFile))
            {
                this.addLog("Message configuration not found", Enums.logType.WARNING);
            }
            Thread.Sleep(1000);
            this.updating = false;
            while (!this.aborting)
            {
                if (!this.updating)
                {
                    this.updating = true;
                    this.BeginInvoke(this._update);
                    Thread.Sleep(1);
                }
            }
            this.BeginInvoke(this.updateLog);
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

            if (this.stg.maker != null && this.stg.taker != null)
            {
                volume = this.stg.maker.my_buy_notional + this.stg.maker.my_sell_notional;
                tradingPL = (this.stg.taker.my_sell_notional - this.stg.taker.my_sell_quantity * this.stg.taker.mid) + (this.stg.taker.my_buy_quantity * this.stg.taker.mid - this.stg.taker.my_buy_notional);
                tradingPL += (this.stg.maker.my_sell_notional - this.stg.maker.my_sell_quantity * this.stg.taker.mid) + (this.stg.maker.my_buy_quantity * this.stg.taker.mid - this.stg.maker.my_buy_notional);
                fee = this.stg.taker.base_fee * this.stg.taker.mid + this.stg.taker.quote_fee + this.stg.maker.base_fee * this.stg.taker.mid + this.stg.maker.quote_fee;
                volume *= this.multiplier;
                tradingPL *= this.multiplier;
                fee *= this.multiplier;
                total = tradingPL - fee;
                this.gridView_PnL.Rows[0].Cells[0].Value = volume.ToString("N2");
                this.gridView_PnL.Rows[0].Cells[1].Value = tradingPL.ToString("N2");
                this.gridView_PnL.Rows[0].Cells[2].Value = fee.ToString("N2");
                this.gridView_PnL.Rows[0].Cells[3].Value = total.ToString("N2");
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
                this.lbl_baseBalance.Text = this.selected_ins.baseBalance.balance.ToString("N" + this.selected_ins.quantity_scale);
                this.lbl_quoteBalance.Text = this.selected_ins.quoteBalance.balance.ToString("N2");
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
            if (this.stg.taker != null)
            {
                this.lbl_baseCcy_taker.Text = this.stg.taker.baseBalance.balance.ToString("N5");
                this.lbl_quoteCcy_taker.Text = this.stg.taker.quoteBalance.balance.ToString("N5");
                this.lbl_makerfee_taker.Text = this.stg.taker.maker_fee.ToString("N5");
                this.lbl_takerfee_taker.Text = this.stg.taker.taker_fee.ToString("N5");
                this.updateQuotesView(this.gridView_Taker, this.stg.taker);
                this.lbl_adjustedask.Text = this.stg.taker.adjusted_bestask.Item1.ToString("N" + this.stg.taker.price_scale);
                this.lbl_adjustedbid.Text = this.stg.taker.adjusted_bestbid.Item1.ToString("N" + this.stg.taker.price_scale);
            }
            if (this.stg.maker != null)
            {
                this.lbl_baseCcy_maker.Text = this.stg.maker.baseBalance.balance.ToString("N5");
                this.lbl_quoteCcy_maker.Text = this.stg.maker.quoteBalance.balance.ToString("N5");
                this.lbl_makerfee_maker.Text = this.stg.maker.maker_fee.ToString("N5");
                this.lbl_takerfee_maker.Text = this.stg.maker.taker_fee.ToString("N5");
                this.updateQuotesView(this.gridView_Maker, this.stg.maker);
                this.lbl_askprice.Text = this.stg.live_askprice.ToString("N" + this.stg.maker.price_scale);
                this.lbl_bidprice.Text = this.stg.live_bidprice.ToString("N" + this.stg.maker.price_scale);
                this.lbl_skewpoint.Text = this.stg.skew_point.ToString("N");
            }
            DataFill fill;
            //DataSpotOrderUpdate ord;
            while (this.filledOrderQueue.Count > 0)
            {
                if (this.filledOrderQueue.TryDequeue(out fill))
                {
                    //ord = this.oManager.orders[ord_id];
                    this.gridView_orders.Rows.Insert(0);
                    this.gridView_orders.Rows[0].Cells[0].Value = ((DateTime)fill.timestamp).ToString("HH:mm:ss.fff");
                    this.gridView_orders.Rows[0].Cells[1].Value = fill.market;
                    this.gridView_orders.Rows[0].Cells[2].Value = fill.symbol;
                    this.gridView_orders.Rows[0].Cells[3].Value = fill.side.ToString();
                    if (fill.symbol_market == this.stg.maker_symbol_market)
                    {
                        this.gridView_orders.Rows[0].Cells[4].Value = fill.price.ToString("N" + this.stg.maker.price_scale);
                        this.gridView_orders.Rows[0].Cells[5].Value = fill.quantity.ToString("N" + this.stg.maker.quantity_scale);
                    }
                    else
                    {
                        this.gridView_orders.Rows[0].Cells[4].Value = fill.price.ToString("N" + this.stg.taker.price_scale);
                        this.gridView_orders.Rows[0].Cells[5].Value = fill.quantity.ToString("N" + this.stg.taker.quantity_scale);
                    }
                    this.gridView_orders.Rows[0].Cells[7].Value = fill.fee_quote + fill.fee_base * fill.price;
                    this.oManager.pushbackFill(fill);
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
                this.qManager.ready = true;

                if (liveTrading || this.privateConnect)
                {
                    await crypto_client.subscribeSpotOrderUpdates(this.qManager._markets.Keys);
                    if (this.qManager._markets.ContainsKey("bitbank"))
                    {
                        this.thManager.addThread("bitbankSpotOrderUpdates", this.crypto_client.onBitbankOrderUpdates);
                    }
                }

                this.oManager.ready = true;

                this.thManager.addThread("updateQuotes", this.qManager._updateQuotes);
                this.thManager.addThread("updateTrades", this.qManager._updateTrades);
                this.thManager.addThread("updateOrders", this.oManager._updateOrders);
                this.thManager.addThread("orderLogging", this.oManager._orderLogging, this.oManager.onLoggingStopped);
                this.threadsStarted = true;
            }
            catch (Exception ex)
            {
                this.addLog("An error occured while initializing the platforms.", Enums.logType.ERROR);
                this.addLog(ex.Message, Enums.logType.ERROR);
                return false;
            }


            return true;
        }
        private async Task<bool> startTrading()
        {
            this.stg.enabled = true;
            this.timer_PeriodicMsg.Start();
            this.addLog("Trading started.");
            return true;
        }
        private async Task<bool> stopTrading(bool error = false)
        {

            if (Interlocked.CompareExchange(ref this.stopTradingCalled, 1, 0) == 0)
            {
                this.stg.enabled = false;
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
            if (await this.tradePreparation(this.live))
            {

                this.button_receiveFeed.Enabled = false;
                this.button_startTrading.Enabled = true;
            }
        }
        private async void startTrading_clicked(object sender, EventArgs e)
        {
            if (await this.startTrading())
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
            this.addLog("Disconnecting bitbank public");
            await this.crypto_client.bitbank_client.disconnectPublic();
            Thread.Sleep(1000);
            this.addLog("Disconnecting coincheck public");
            await this.crypto_client.coincheck_client.disconnectPublic();
            Thread.Sleep(1000);
            this.addLog("Disconnecting coincheck private");
            await this.crypto_client.coincheck_client.disconnectPrivate();
            Thread.Sleep(1000);



            //await tradeTest(this.qManager.instruments["eth_jpy@coincheck"],true);
        }
        private async Task onErrorCheck()
        {
            await this.crypto_client.bitbank_client.disconnectPublic();
        }

        private async Task tradeTest(Instrument ins, bool fillcheck)
        {
            DataSpotOrderUpdate ord;
            this.oManager.setVirtualMode(false);

            this.addLog("Testing orderManager");
            Thread.Sleep(3000);
            this.addLog("Placing a new order");
            string ordid;
            ord = await this.oManager.placeNewSpotOrder(ins, orderSide.Buy, orderType.Limit, (decimal)0.01, 600000);
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
            Thread.Sleep(1000);
            this.addLog("Live Order Count " + this.oManager.live_orders.Count.ToString());
            Thread.Sleep(3000);
            this.addLog("modifing a order");
            ord = await this.oManager.placeModSpotOrder(ins, ordid, (decimal)0.01, 570000, false);
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
            Thread.Sleep(1000);
            this.addLog("Live Order Count " + this.oManager.live_orders.Count.ToString());
            if (this.oManager.live_orders.Count > 0)
            {
                this.addLog("Cancelling a order");
                ord = this.oManager.live_orders.Values.First();
                this.addLog(ord.ToString());
                ord = await this.oManager.placeCancelSpotOrder(ins, ord.order_id);
                if (ord != null)
                {
                    ordid = ord.order_id;
                    this.addLog(ord.ToString());
                }
                else
                {
                    this.addLog("Failed to place a can order");
                    return;
                }
            }
            Thread.Sleep(1000);
            this.addLog("Live Order Count " + this.oManager.live_orders.Count.ToString());

            if(fillcheck)
            {
                this.addLog("Fill Check");
                ord = await this.oManager.placeNewSpotOrder(ins, orderSide.Buy, orderType.Limit, (decimal)0.01, 700000);
                this.addLog(ord.ToString());
                Thread.Sleep(1000);
                this.addLog("Live Order Count " + this.oManager.live_orders.Count.ToString());

                this.addLog("Market Order");
                ord = await this.oManager.placeNewSpotOrder(ins, orderSide.Buy, orderType.Market, (decimal)0.01, 700000);
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
                if(this.stopTradingCalled == 0 && th.Value.isRunning == false)
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
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    this.gridView_ThStatus.Rows.Add(th.Key, st);
                }
            }

            foreach(var stoppedTh in stoppedThreads)
            {
                if (stoppedTh.Contains("Public"))
                {
                    bool currentTradingState = this.stg.enabled;
                    this.stg.enabled = false;
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
                    this.stg.enabled = currentTradingState;
                }
                else if (stoppedTh.Contains("Private"))
                {
                    bool currentTradingState = this.stg.enabled;
                    this.stg.enabled = false;
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
                        
                        if (market == "bitbank")
                        {
                            this.thManager.addThread("bitbankSpotOrderUpdates", this.crypto_client.onBitbankOrderUpdates);
                        }
                        else
                        {
                            await crypto_client.subscribeSpotOrderUpdates(markets);
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
                    this.stg.enabled = currentTradingState;
                }
                else
                {
                    this.addLog("Updateing thread stopped. Stopping all the process", Enums.logType.ERROR);
                }
            }
            if(this.qManager.ordBookQueue.Count() > 1000)
            {
                this.addLog("The order book queue count exceeds 1000.", Enums.logType.WARNING);
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

        }

        private async void Form1_Shown(object sender, EventArgs e)
        {
            if (this.autoStart)
            {
                this.button_orderTest.Enabled = false;
                this.button_receiveFeed.Enabled = false;
                this.button_startTrading.Enabled = false;

                await this.tradePreparation(this.live);
                this.addLog("Waiting for 5 sec", Enums.logType.INFO);
                Thread.Sleep(5000);
                await this.startTrading();
            }
        }

        private async void timer_PeriodicMsg_Tick(object sender, EventArgs e)
        {
            decimal volume = 0;
            decimal tradingPL = 0;
            decimal fee = 0;
            decimal total = 0;
            string msg = "";

            if (this.stg.maker != null && this.stg.taker != null)
            {
                volume = this.stg.maker.my_buy_notional + this.stg.maker.my_sell_notional;
                tradingPL = (this.stg.taker.my_sell_notional - this.stg.taker.my_sell_quantity * this.stg.taker.mid) + (this.stg.taker.my_buy_quantity * this.stg.taker.mid - this.stg.taker.my_buy_notional);
                tradingPL += (this.stg.maker.my_sell_notional - this.stg.maker.my_sell_quantity * this.stg.taker.mid) + (this.stg.maker.my_buy_quantity * this.stg.taker.mid - this.stg.maker.my_buy_notional);
                fee = this.stg.taker.base_fee * this.stg.taker.mid + this.stg.taker.quote_fee + this.stg.maker.base_fee * this.stg.taker.mid + this.stg.maker.quote_fee;
                volume *= this.multiplier;
                tradingPL *= this.multiplier;
                fee *= this.multiplier;
                total = tradingPL - fee;

                msg = DateTime.UtcNow.ToString() + " Notional Volume:" + volume.ToString("N2") + " Trading PnL:" + tradingPL.ToString("N2") + " Fee:" + fee.ToString("N2") + " Total:" + total.ToString("N2");

                await this.MsgDeliverer.sendMessage(msg);
            }
        }
    }
}
