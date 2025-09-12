using Crypto_Clients;
using CryptoExchange.Net;
using CryptoExchange.Net.SharedApis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Crypto_Trading
{
    public class QuoteManager
    {
        public Dictionary<string, Instrument> instruments;
        public Dictionary<string, Instrument> ins_bymaster;
        public Dictionary<string, Balance> balances;

        public List<string> markets;

        public ConcurrentQueue<DataOrderBook> ordBookQueue;
        private ConcurrentStack<DataOrderBook> ordBookStack;

        public ConcurrentQueue<DataTrade> tradeQueue;
        private ConcurrentStack<DataTrade> tradeStack;

        public Strategy? stg;

        public Action<string> addLog;

        public const int NUM_OF_QUOTES = 5;
        QuoteManager() 
        {
            this.instruments = new Dictionary<string, Instrument>();
            this.ins_bymaster = new Dictionary<string, Instrument>();
            this.balances = new Dictionary<string, Balance>();
            this.markets = new List<string>(); 
            this.addLog = Console.WriteLine;
        }
        public void setQueues(Crypto_Clients.Crypto_Clients client)
        {
            this.ordBookQueue = client.ordBookQueue;
            this.ordBookStack = client.ordBookStack;
            this.tradeQueue = client.tradeQueue;
            this.tradeStack = client.tradeStack;
        }
        public bool initializeInstruments(string masterfile)
        {
            Instrument ins;
            if (File.Exists(masterfile))
            {
                int i = 0;
                foreach (string line in File.ReadLines(masterfile))
                {
                    if(i == 0)
                    {
                        ++i;
                    }
                    else
                    {
                        ins = new Instrument();
                        ins.initialize(line);
                        this.instruments[ins.symbol_market] = ins;
                        this.ins_bymaster[ins.master_symbol + "@" + ins.market] = ins;
                        if(!this.markets.Contains(ins.market))
                        {
                            this.markets.Add(ins.market);
                        }
                    }
                }
                return true;
            }
            else
            {
                this.addLog("[ERROR] The master file doesn't exist. Filename:" + masterfile);
                return false;
            }
        }
        public bool setBalance(ExchangeWebResult<SharedBalance[]>[] results)
        {
            bool output = true;
            string key;
            foreach (var subResult in results)
            {
                if(subResult.Success)
                {
                    foreach (var data in subResult.Data)
                    {
                        key = data.Asset + "@" + subResult.Exchange;
                        if(this.balances.ContainsKey(key))
                        {
                            this.balances[key].balance = data.Available;
                        }
                        else
                        {
                            Balance balance = new Balance();
                            balance.ccy = data.Asset;
                            balance.market = subResult.Exchange;
                            balance.balance = data.Available;
                            this.balances[key] = balance;
                            foreach(var ins in this.instruments.Values)
                            {
                                if(ins.market == balance.market)
                                {
                                    if(ins.quoteCcy == balance.ccy)
                                    {
                                        ins.quoteBalance = balance;
                                    }
                                    else if(ins.baseCcy == balance.ccy)
                                    {
                                        ins.baseBalance = balance;
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    this.addLog("[ERROR]Failed to receive balance information.  Exchange:" + subResult.Exchange);
                    output = false;
                }
            }
            return output;
        }
        public bool setFees(ExchangeWebResult<SharedFee>[] results,string master_symbol)
        {
            bool output = true;
            string key;
            Instrument ins;
            foreach(var subResult in results)
            {
                if(subResult.Success)
                {
                    key = master_symbol + "@" + subResult.Exchange;
                    if(this.ins_bymaster.ContainsKey(key))
                    {
                        ins = this.ins_bymaster[key];
                        ins.taker_fee = subResult.Data.TakerFee / 100;
                        ins.maker_fee = subResult.Data.MakerFee / 100;
                    }
                    else
                    {
                        this.addLog("[WARNING] Unknown Symbol.  " + key);
                    }
                }
                else
                {
                    this.addLog("[ERROR] Failed to receive fee information.  Exchange:" + subResult.Exchange);
                    output = false;
                }
            }
            return output;
        }

        public async void updateQuotes()
        {
            int i = 0;
            Instrument ins;
            DataOrderBook msg;
            string symbol_market;
            while (true)
            {
                if(this.ordBookQueue.TryDequeue(out msg))
                {
                    symbol_market = msg.symbol + "@" + msg.market;
                    if (this.instruments.ContainsKey(symbol_market))
                    {
                        ins = instruments[symbol_market];
                        ins.updateQuotes(msg);
                        if(symbol_market == this.stg.taker.symbol_market)
                        {
                            await this.stg.updateOrders();
                        }
                    }
                    else
                    {
                        this.addLog("[WARNING] The symbol doesn't exist. Instrument:" + symbol_market);
                    }
                    msg.init();
                    this.ordBookStack.Push(msg);
                    i = 0;
                }
                else
                {
                    ++i;
                    if(i > 100000)
                    {
                        i = 0;
                        Thread.Sleep(0);
                    }
                }
            }
        }
        public void updateTrades()
        {
            int i = 0;
            Instrument ins;
            DataTrade msg;
            string symbol_market;
            while (true)
            {
                if (this.tradeQueue.TryDequeue(out msg))
                {
                    symbol_market = msg.symbol + "@" + msg.market;
                    if (this.instruments.ContainsKey(symbol_market))
                    {
                        ins = instruments[symbol_market];
                        ins.updateTrade(msg);
                    }
                    else
                    {
                        this.addLog("[WARNING] The symbol doesn't exist. Instrument:" + symbol_market);
                    }
                    msg.init();
                    this.tradeStack.Push(msg);
                    i = 0;
                }
                else
                {
                    ++i;
                    if (i > 100000)
                    {
                        i = 0;
                        Thread.Sleep(0);
                    }
                }
            }
        }

        private static QuoteManager _instance;
        private static readonly object _lockObject = new object();

        public static QuoteManager GetInstance()
        {
            lock (_lockObject)
            {
                if (_instance == null)
                {
                    _instance = new QuoteManager();
                }
                return _instance;
            }
        }
    }
}
