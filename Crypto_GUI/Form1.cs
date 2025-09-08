using Crypto_Clients;
using Crypto_Trading;
using CryptoClients.Net.Enums;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Crypto_GUI
{
    public partial class Form1 : Form
    {
        Crypto_Clients.Crypto_Clients cl = new Crypto_Clients.Crypto_Clients();
        QuoteManager qManager = QuoteManager.GetInstance();

        Instrument selected_ins;

        System.Threading.Thread updatingTh;
        System.Threading.Thread quoteupdateTh;
        System.Threading.Thread tradeupdateTh;
        public Form1()
        {
            InitializeComponent();

            this.qManager.addLog = this.addLog;
            this.cl.addLog = this.addLog;

            cl.readCredentials(Exchange.Coinbase, "C:\\Users\\yusai\\coinbase_viewonly.json");
            cl.readCredentials(Exchange.Bybit, "C:\\Users\\yusai\\bybit_viewonly.json");

            string master_file = "C:\\Users\\yusai\\crypto_master.csv";

            this.qManager.initializeInstruments(master_file);
            this.qManager.setQueues(this.cl);

            foreach (string key in this.qManager.instruments.Keys)
            {
                this.comboSymbols.Items.Add(key);
            }

            updatingTh = new Thread(update);
            updatingTh.Start();
        }

        private async void addLog(string line)
        {

        }

        private void updatetext1(string str)
        {
            this.labeltest1.Text = str;
        }
        private void update()
        {
            Thread.Sleep(1000);
            while(true)
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
            }
        }

        private void update_Instrument()
        {
            if(this.selected_ins != null)
            {
                this.lbl_symbol.Text = this.selected_ins.symbol;
                this.lbl_market.Text = this.selected_ins.market;
                this.lbl_lastprice.Text = this.selected_ins.last_price.ToString();
                this.lbl_notional.Text = (this.selected_ins.buy_notional + this.selected_ins.sell_notional).ToString("N2");
                this.lbl_quotes.Text = this.selected_ins.ToString("Quote5");
            }
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            this.labeltest1.Text = "button_clicked";
            foreach (var ins in this.qManager.instruments.Values)
            {
                string[] markets = [ins.market];
                if(ins.market == Exchange.Bybit)
                {
                    await cl.subscribeBybitOrderBook(ins.baseCcy, ins.quoteCcy);
                }
                else if(ins.market == Exchange.Coinbase)
                {
                    await cl.subscribeCoinbaseOrderBook(ins.baseCcy, ins.quoteCcy);
                }
                else
                {
                    await cl.subscribeOrderBook(markets,ins.baseCcy,ins.quoteCcy);
                }
                await cl.subscribeTrades(markets, ins.baseCcy, ins.quoteCcy);
                this.labeltest1.Text += ins.symbol;
            }
            this.quoteupdateTh = new System.Threading.Thread(this.qManager.updateQuotes);
            this.tradeupdateTh = new System.Threading.Thread(this.qManager.updateTrades);
            this.quoteupdateTh.Start();
            this.tradeupdateTh.Start();
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
    }
}
