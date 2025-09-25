using Crypto_Clients;
using Crypto_Trading;
using CryptoClients.Net.Enums;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Crypto_GUI
{
    public partial class Form1 : Form
    {
        Crypto_Clients.Crypto_Clients cl = new Crypto_Clients.Crypto_Clients();
        QuoteManager qManager = QuoteManager.GetInstance();
        OrderManager oManager = OrderManager.GetInstance();

        Strategy stg;

        ConcurrentQueue<string> logQueue;
        ConcurrentQueue<string> filledOrderQueue;

        Instrument selected_ins;

        System.Threading.Thread updatingTh;
        System.Threading.Thread quoteupdateTh;
        System.Threading.Thread tradeupdateTh;
        System.Threading.Thread orderUpdateTh;
        System.Threading.Thread bitBankTh;

        StreamWriter logFile;

        Font font_gridView;
        Font font_gridView_Bold;
        public Form1()
        {
            InitializeComponent();

            this.logQueue = new ConcurrentQueue<string>();
            this.filledOrderQueue = new ConcurrentQueue<string>();

            this.logFile = new StreamWriter(new FileStream("C:\\Users\\yusai\\Crypto.log", FileMode.Create));

            this.qManager.addLog = this.addLog;
            this.oManager.addLog = this.addLog;
            this.cl.setAddLog(this.addLog);

            cl.readCredentials(Exchange.Coinbase, "C:\\Users\\yusai\\coinbase_viewonly.json");
            cl.readCredentials(Exchange.Bybit, "C:\\Users\\yusai\\bybit_viewonly.json");
            cl.readCredentials("bitbank", "C:\\Users\\yusai\\bitbank_tradable.json");
            cl.readCredentials("coincheck", "C:\\Users\\yusai\\coincheck_viewonly.json");

            string master_file = "C:\\Users\\yusai\\crypto_master.csv";

            this.qManager.initializeInstruments(master_file);
            this.qManager.setQueues(this.cl);

            this.oManager.setInstruments(this.qManager.instruments);
            this.oManager.setOrderClient(this.cl);
            this.oManager.filledOrderQueue = this.filledOrderQueue;

            this.stg = new Strategy();
            this.stg.readStrategyFile("C:\\Users\\yusai\\strategy.json");

            this.stg.maker = this.qManager.instruments[this.stg.maker_symbol_market];
            this.stg.taker = this.qManager.instruments[this.stg.taker_symbol_market];

            this.qManager.stg = this.stg;
            this.oManager.stg = this.stg;

            this.lbl_makerName.Text = this.stg.maker_symbol_market;
            this.lbl_takerName.Text = this.stg.taker_symbol_market;
            this.lbl_makerfee_maker.Text = this.stg.maker.maker_fee.ToString("N5");
            this.lbl_takerfee_maker.Text = this.stg.maker.taker_fee.ToString("N5");
            this.lbl_makerfee_taker.Text = this.stg.taker.maker_fee.ToString("N5");
            this.lbl_takerfee_taker.Text = this.stg.taker.taker_fee.ToString("N5");
            this.lbl_markup.Text = this.stg.markup.ToString("N");
            this.lbl_tobsize.Text = this.stg.ToBsize.ToString("N5");
            this.lbl_maxpos.Text = this.stg.baseCcyQuantity.ToString("N5");
            this.lbl_skew.Text = this.stg.skewThreshold.ToString("N");
            this.lbl_oneside.Text = this.stg.oneSideThreshold.ToString("N");
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

            updatingTh = new Thread(update);
            updatingTh.Start();
        }

        private void addLog(string line)
        {
            this.logQueue.Enqueue(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   " + line + "\n");
        }
        private void updateLog()
        {
            string line;
            while (this.logQueue.TryDequeue(out line))
            {
                this.textBoxMainLog.Text += line;
                this.logFile.WriteLine(line);
                this.logFile.Flush();
            }
        }

        private void update()
        {
            Thread.Sleep(1000);
            while (true)
            {
                this.Invoke(this._update);
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
        }
        private void update_main()
        {
            decimal volume = 0;
            decimal tradingPL = 0;
            decimal fee = 0;
            decimal total = 0;

            if(this.stg.maker != null && this.stg.taker != null)
            {
                volume = this.stg.maker.my_buy_notional + this.stg.taker.my_sell_notional;
                tradingPL = (this.stg.taker.my_sell_notional - this.stg.taker.my_sell_quantity * this.stg.taker.mid) + (this.stg.taker.my_buy_quantity * this.stg.taker.mid - this.stg.taker.my_buy_notional);
                tradingPL += (this.stg.maker.my_sell_notional - this.stg.maker.my_sell_quantity * this.stg.maker.mid) + (this.stg.maker.my_buy_quantity * this.stg.maker.mid - this.stg.maker.my_buy_notional);
                fee = this.stg.taker.total_fee + this.stg.maker.total_fee;
                volume *= 1000;
                tradingPL *= 1000;
                fee *= 1000;                            
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
                this.lbl_market.Text = this.selected_ins.market;
                this.lbl_lastprice.Text = this.selected_ins.last_price.ToString();
                this.lbl_notional.Text = (this.selected_ins.buy_notional + this.selected_ins.sell_notional).ToString("N2");
                this.lbl_baseBalance.Text = this.selected_ins.baseBalance.balance.ToString("N" + this.selected_ins.quantity_scale);
                this.lbl_quoteBalance.Text = this.selected_ins.quoteBalance.balance.ToString("N" + this.selected_ins.quantity_scale);
                this.updateQuotesView(this.gridView_Ins, this.selected_ins);
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
            string ord_id;
            DataSpotOrderUpdate ord;
            while (this.filledOrderQueue.Count > 0)
            {
                if (this.filledOrderQueue.TryDequeue(out ord_id))
                {
                    ord = this.oManager.orders[ord_id];
                    this.gridView_orders.Rows.Insert(0);
                    this.gridView_orders.Rows[0].Cells[0].Value = ((DateTime)ord.timestamp).ToString("HH:mm:ss.fff");
                    this.gridView_orders.Rows[0].Cells[1].Value = ord.market;
                    this.gridView_orders.Rows[0].Cells[2].Value = ord.symbol;
                    this.gridView_orders.Rows[0].Cells[3].Value = ord.side.ToString();
                    if (ord.symbol_market == this.stg.maker_symbol_market)
                    {
                        this.gridView_orders.Rows[0].Cells[4].Value = ord.average_price.ToString("N" + this.stg.maker.price_scale);
                        this.gridView_orders.Rows[0].Cells[5].Value = ord.filled_quantity.ToString("N" + this.stg.maker.quantity_scale);
                    }
                    else
                    {
                        this.gridView_orders.Rows[0].Cells[4].Value = ord.average_price.ToString("N" + this.stg.taker.price_scale);
                        this.gridView_orders.Rows[0].Cells[5].Value = ord.filled_quantity.ToString("N" + this.stg.taker.quantity_scale);
                    }
                    this.gridView_orders.Rows[0].Cells[6].Value = ord.fee_asset;
                    this.gridView_orders.Rows[0].Cells[7].Value = ord.fee;

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

        private async void button1_Click(object sender, EventArgs e)
        {
            await this.cl.connectAsync();

            this.qManager.setBalance(await this.cl.getBalance(this.qManager.markets));
            //this.qManager.setFees(await this.cl.getFees([Exchange.Bybit, Exchange.Coinbase], this.stg.baseCcy, this.stg.quoteCcy),this.stg.baseCcy + this.stg.quoteCcy);

            foreach (var ins in this.qManager.instruments.Values)
            {
                string[] markets = [ins.market];
                if (ins.market == Exchange.Bybit)
                {
                    await cl.subscribeBybitOrderBook(ins.baseCcy, ins.quoteCcy);
                }
                else if (ins.market == Exchange.Coinbase)
                {
                    await cl.subscribeCoinbaseOrderBook(ins.baseCcy, ins.quoteCcy);
                }
                else
                {
                    await cl.subscribeOrderBook(markets, ins.baseCcy, ins.quoteCcy);
                }
                await cl.subscribeTrades(markets, ins.baseCcy, ins.quoteCcy);
            }

            await cl.subscribeSpotOrderUpdates(this.qManager.markets);

            this.quoteupdateTh = new System.Threading.Thread(this.qManager.updateQuotes);
            this.tradeupdateTh = new System.Threading.Thread(this.qManager.updateTrades);
            this.orderUpdateTh = new System.Threading.Thread(this.oManager.updateOrders);
            this.quoteupdateTh.Start();
            this.tradeupdateTh.Start();
            this.orderUpdateTh.Start();
        }

        private async void button2_Click(object sender, EventArgs e)
        {
            DataSpotOrderUpdate ord;
            Instrument ins = this.qManager.instruments["eth_jpy@bitbank"];

            this.oManager.setVirtualMode(false);

            this.addLog("Testing orderManager");
            Thread.Sleep(3000);
            this.addLog("Placing a new order");
            string ordid;
            ord = await this.oManager.placeNewSpotOrder(ins, orderSide.Buy, orderType.Limit, (decimal)0.001, 600000);
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
            ord = await this.oManager.placeModSpotOrder(ins, ordid, (decimal)0.001, 590000, false);
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

            this.addLog("Fill Check");
            ord = await this.oManager.placeNewSpotOrder(ins, orderSide.Buy, orderType.Limit, (decimal)0.001, 620000);
            this.addLog(ord.ToString());
            Thread.Sleep(1000);
            this.addLog("Live Order Count " + this.oManager.live_orders.Count.ToString());

            this.addLog("Market Order");
            ord = await this.oManager.placeNewSpotOrder(ins, orderSide.Buy, orderType.Market, (decimal)0.001, 620000);
            this.addLog(ord.ToString());
            Thread.Sleep(1000);
            this.addLog("Live Order Count " + this.oManager.live_orders.Count.ToString());

        }

        private void button3_Click(object sender, EventArgs e)
        {
            this.addLog("[INFO] Strategy enabled");
            this.stg.enabled = true;
        }
    }
}
