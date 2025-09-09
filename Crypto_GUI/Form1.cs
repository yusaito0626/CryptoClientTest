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

        Instrument selected_ins;

        System.Threading.Thread updatingTh;
        System.Threading.Thread quoteupdateTh;
        System.Threading.Thread tradeupdateTh;
        System.Threading.Thread orderUpdateTh;
        public Form1()
        {
            InitializeComponent();

            this.logQueue = new ConcurrentQueue<string>();

            this.qManager.addLog = this.addLog;
            this.oManager.addLog = this.addLog;
            this.cl.addLog = this.addLog;


            cl.readCredentials(Exchange.Coinbase, "C:\\Users\\yusai\\coinbase_viewonly.json");
            cl.readCredentials(Exchange.Bybit, "C:\\Users\\yusai\\bybit_tradable.json");


            string master_file = "C:\\Users\\yusai\\crypto_master.csv";

            this.qManager.initializeInstruments(master_file);
            this.qManager.setQueues(this.cl);

            this.oManager.setInstruments(this.qManager.instruments);
            this.oManager.setOrderClient(this.cl);

            this.stg = new Strategy();
            this.stg.readStrategyFile("C:\\Users\\yusai\\strategy.json");

            this.stg.maker = this.qManager.instruments[this.stg.maker_symbol_market];
            this.stg.taker = this.qManager.instruments[this.stg.taker_symbol_market];

            this.lbl_makerName.Text = this.stg.maker_symbol_market;
            this.lbl_takerName.Text = this.stg.taker_symbol_market;

            foreach (string key in this.qManager.instruments.Keys)
            {
                this.comboSymbols.Items.Add(key);
            }

            int i = 0;
            int numOfRow = QuoteManager.NUM_OF_QUOTES * 2 + 1;
            while(i < numOfRow)
            {
                this.gridView_Taker.Rows.Add();
                this.gridView_Maker.Rows.Add();
                this.gridView_Ins.Rows.Add();
                ++i;
            }

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
            }
        }

        private void update()
        {
            Thread.Sleep(1000);
            while (true)
            {
                this.Invoke(this._update);
            }
            string filepath = "C:\\Users\\yusai\\log.csv";
            string symbol_market = "";
            Instrument ins;
            using (FileStream f = new FileStream(filepath, FileMode.Create, FileAccess.Write))
            {
                using (StreamWriter s = new StreamWriter(f))
                {
                    //while (true)
                    //{
                    //    DataOrderBook msg;
                    //    string strMsg;
                    //    string bestbidask;
                    //    while (cl.ordBookQueue.TryDequeue(out msg))
                    //    {
                    //        symbol_market = msg.symbol.ToUpper() + "@" + msg.market;
                    //        if (instruments.ContainsKey(symbol_market))
                    //        {
                    //            ins = instruments[symbol_market];
                    //            ins.updateQuotes(msg);
                    //            strMsg = ins.ToString("Quote5");
                    //            if (ins.market == Exchange.Bybit)
                    //            {
                    //                bestbidask = "Ask:" + ins.adjusted_bestask.Item1.ToString("N2") + " Bid:" + ins.adjusted_bestbid.Item1.ToString("N2") + "\n";
                    //                bestbidask += "Spread:" + (ins.adjusted_bestask.Item1 - ins.adjusted_bestbid.Item1).ToString("N2");
                    //            }
                    //            else if (ins.market == Exchange.Coinbase)
                    //            {
                    //                this.BeginInvoke(updatetext1, strMsg);
                    //            }
                    //        }
                    //        else
                    //        {
                    //            strMsg = "[ERROR] The symbol market did not found. " + symbol_market;
                    //        }
                    //        Console.WriteLine(strMsg);
                    //        s.WriteLine(strMsg);
                    //        //Console.WriteLine(msg.ToString());
                    //        //s.WriteLine(msg.ToString());
                    //        cl.pushToOrderBookStack(msg);
                    //    }
                    //    while (cl.strQueue.TryDequeue(out strMsg))
                    //    {
                    //        Console.WriteLine(strMsg);
                    //        s.WriteLine(strMsg);
                    //    }
                    //}
                }
            }
        }

        private void _update()
        {
            switch (this.tabControl.SelectedTab.Text)
            {
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

        private void update_Instrument()
        {
            if (this.selected_ins != null)
            {
                this.lbl_symbol.Text = this.selected_ins.symbol;
                this.lbl_market.Text = this.selected_ins.market;
                this.lbl_lastprice.Text = this.selected_ins.last_price.ToString();
                this.lbl_notional.Text = (this.selected_ins.buy_notional + this.selected_ins.sell_notional).ToString("N2");
                this.updateQuotesView(this.gridView_Ins,this.selected_ins);
            }
        }

        private void update_strategy()
        {
            if (this.stg.taker != null)
            {
                this.updateQuotesView(this.gridView_Taker, this.stg.taker);
            }
            if(this.stg.maker != null)
            {
                this.updateQuotesView(this.gridView_Maker, this.stg.maker);
            }
        }

        private void updateQuotesView(DataGridView view,Instrument ins)
        {
            int i = 0;
            while (Interlocked.CompareExchange(ref ins.quotes_lock, 1, 0) != 0)
            {

            }
            view.Rows[QuoteManager.NUM_OF_QUOTES].Cells[1].Value = ins.last_price.ToString("N2");
            while (i < QuoteManager.NUM_OF_QUOTES)
            {
                if (i < ins.asks.Count)
                {
                    view.Rows[QuoteManager.NUM_OF_QUOTES - 1 - i].Cells[0].Value = ins.asks.ElementAt(i).Value.ToString("N5");
                    view.Rows[QuoteManager.NUM_OF_QUOTES - 1 - i].Cells[1].Value = ins.asks.ElementAt(i).Key.ToString("N2");
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
                    view.Rows[QuoteManager.NUM_OF_QUOTES + 1 + i].Cells[1].Value = item.Key.ToString("N2");
                    view.Rows[QuoteManager.NUM_OF_QUOTES + 1 + i].Cells[2].Value = item.Value.ToString("N5");
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

        private async void button1_Click(object sender, EventArgs e)
        {
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
            await cl.subscribeSpotOrderUpdates([Exchange.Bybit]);
            this.quoteupdateTh = new System.Threading.Thread(this.qManager.updateQuotes);
            this.tradeupdateTh = new System.Threading.Thread(this.qManager.updateTrades);
            this.orderUpdateTh = new System.Threading.Thread(this.oManager.updateOrders);
            this.quoteupdateTh.Start();
            this.tradeupdateTh.Start();
            this.orderUpdateTh.Start();
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

        private async void button2_Click(object sender, EventArgs e)
        {
            string ordId;
            Instrument ins = this.qManager.instruments["ETHUSDT@Bybit"];
           
            this.addLog("Testing orderManager");
            Thread.Sleep(3000);
            this.addLog("Placing a new order");
            ordId = await this.oManager.placeNewSpotOrder(ins, CryptoExchange.Net.SharedApis.SharedOrderSide.Buy, CryptoExchange.Net.SharedApis.SharedOrderType.Limit, (decimal)0.001, 4000);
            this.addLog(ordId);
            Thread.Sleep(1000);
            this.addLog("Live Order Count " + this.oManager.live_orders.Count.ToString());
            Thread.Sleep(3000);
            this.addLog("modifing a order");
            ordId = await this.oManager.placeModSpotOrder(ins, ordId, (decimal)0.001, 3900, true);
            this.addLog(ordId);
            Thread.Sleep(1000);
            this.addLog("Live Order Count " + this.oManager.live_orders.Count.ToString());
            this.addLog("Cancelling a order");
            ordId = this.oManager.live_orders.Values.First().order_id;
            ordId = await this.oManager.placeCancelSpotOrder(ins, ordId);
            this.addLog(ordId);
            Thread.Sleep(1000);
            this.addLog("Live Order Count " + this.oManager.live_orders.Count.ToString());
        }
    }
}
