using Crypto_Clients;
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

        public ConcurrentQueue<DataOrderBook> ordBookQueue;
        private ConcurrentStack<DataOrderBook> ordBookStack;

        public ConcurrentQueue<DataTrade> tradeQueue;
        private ConcurrentStack<DataTrade> tradeStack;

        public Action<string> addLog;
        QuoteManager() 
        {
            this.instruments = new Dictionary<string, Instrument>();
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

        public void updateQuotes()
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
