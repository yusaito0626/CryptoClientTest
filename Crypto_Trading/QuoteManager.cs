using Crypto_Clients;
using CryptoExchange.Net;
using CryptoExchange.Net.SharedApis;
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Enums;
using System.Diagnostics;
using Utils;
using LockFreeStack;
using LockFreeQueue;

namespace Crypto_Trading
{
    public class QuoteManager
    {
        private Crypto_Clients.Crypto_Clients crypto_client = Crypto_Clients.Crypto_Clients.GetInstance();
        public Dictionary<string, Instrument> instruments;
        public Dictionary<string, Instrument> ins_bymaster;

        public Dictionary<string, ExchangeBalance> exchange_balances;
        public Dictionary<string, ExchangeBalance> SoD_exchange_balances;
        public Dictionary<string, Balance> balances;

        public Dictionary<string, WebSocketState> _markets;

        public MIMOQueue<DataOrderBook> ordBookQueue;
        private LockFreeStack<DataOrderBook> ordBookStack;

        public MIMOQueue<DataTrade> tradeQueue;
        private LockFreeStack<DataTrade> tradeStack;

        public MIMOQueue<Strategy> optQueue;

        public Strategy? stg;
        public Dictionary<string, Strategy> strategies;

        private OrderManager oManager;

        public Action<string, Enums.logType> _addLog;

        public const int NUM_OF_QUOTES = 5;

        public bool ready;
        public bool aborting;

        public bool live;
        public bool monitoring;

        Stopwatch sw_updateQuotes;
        Stopwatch sw_updateTrades;
        Stopwatch sw_optimize;

        public Dictionary<string, latency> Latency;
        QuoteManager() 
        {
            this.instruments = new Dictionary<string, Instrument>();
            this.ins_bymaster = new Dictionary<string, Instrument>();
            this.exchange_balances = new Dictionary<string, ExchangeBalance>();
            this.SoD_exchange_balances = new Dictionary<string, ExchangeBalance>();
            this.balances = new Dictionary<string, Balance>();
            this._markets = new Dictionary<string, WebSocketState>();
            this.strategies = new Dictionary<string, Strategy>();
            this.optQueue = new MIMOQueue<Strategy>();
            this.oManager = OrderManager.GetInstance();
            this.ready = false;
            //this._addLog = Console.WriteLine;
            this.aborting = false;

            this.sw_updateQuotes = new Stopwatch();
            this.sw_updateTrades = new Stopwatch();
            this.sw_optimize = new Stopwatch();

            this.Latency = new Dictionary<string, latency>();

            this.live = false;
            this.monitoring = false;
        }

        
        public async Task<bool> connectPublicChannel(string market)
        {
            bool ret = false;
            ThreadManager thManager = ThreadManager.GetInstance();
            Func<Task<(bool, double)>> onMsg;
            Action onClosing;
            int trials = 0;
            switch (market)
            {
                case "bitbank":
                    ret = await this.crypto_client.bitbank_client.connectPublicAsync();
                    while (!ret)
                    {
                        ++trials;
                        if (trials < 5)
                        {
                            Thread.Sleep(trials * 3000);
                            //Console.WriteLine("Bitbank public connection failed. Trying again. trial:" + trials.ToString());
                            this.addLog("Bitbank public connection failed. Trying again. trial:" + trials.ToString(), logType.WARNING);
                            ret = await this.crypto_client.bitbank_client.connectPublicAsync();
                        }
                        else
                        {
                            this.addLog("Failed to connect public. bitbank", logType.ERROR);
                            break;
                        }
                    }
                    if (!ret)
                    {
                        return false;
                    }
                    this.crypto_client.bitbank_client.onMessage = this.crypto_client.onBitbankMessage;
                    onClosing = async () =>
                    {
                        await this.crypto_client.bitbank_client.onClosing(this.crypto_client.onBitbankMessage);
                    };
                    thManager.addThread(market + "Public", this.crypto_client.bitbank_client.Listening,onClosing,this.crypto_client.bitbank_client.onListenOnError);
                    this._markets[market] = this.crypto_client.bitbank_client.GetSocketStatePublic();
                    break;
                case "gmocoin":
                    ret = await this.crypto_client.gmocoin_client.connectPublicAsync();
                    while (!ret)
                    {
                        ++trials;
                        if (trials < 5)
                        {
                            Thread.Sleep(trials * 3000);
                            this.addLog("GMO Coin public connection failed. Trying again. trial:" + trials.ToString(), logType.WARNING);
                            ret = await this.crypto_client.gmocoin_client.connectPublicAsync();
                        }
                        else
                        {
                            this.addLog("Failed to connect public. GMO Coin", logType.ERROR);
                            break;
                        }
                    }
                    if (!ret)
                    {
                        return false;
                    }
                    this.crypto_client.gmocoin_client.onMessage = this.crypto_client.onGMOCoinMessage;
                    onClosing = async () =>
                    {
                        await this.crypto_client.gmocoin_client.onClosing(this.crypto_client.onGMOCoinMessage);
                    };
                    thManager.addThread(market + "Public", this.crypto_client.gmocoin_client.Listening, onClosing, this.crypto_client.gmocoin_client.onListenOnError);
                    this._markets[market] = this.crypto_client.gmocoin_client.GetSocketStatePublic();
                    break;
                case "coincheck":
                    ret = await this.crypto_client.coincheck_client.connectPublicAsync();
                    while (!ret)
                    {
                        ++trials;
                        if (trials < 5)
                        {
                            Thread.Sleep(trials * 3000);
                            this.addLog("Coincheck public connection failed. Trying again. trial:" + trials.ToString(), logType.WARNING);
                            ret = await this.crypto_client.coincheck_client.connectPublicAsync();
                        }
                        else
                        {
                            this.addLog("Failed to connect public. coincheck", logType.ERROR);
                            break;
                        }
                    }
                    if (!ret)
                    {
                        return false;
                    }
                    this.crypto_client.coincheck_client.onMessage = this.crypto_client.onCoincheckMessage;
                    onClosing = async () =>
                    {
                        await this.crypto_client.coincheck_client.onClosing(this.crypto_client.onCoincheckMessage);
                    };
                    thManager.addThread(market + "Public", this.crypto_client.coincheck_client.Listening,onClosing,this.crypto_client.coincheck_client.onListenOnError);
                    this._markets[market] = this.crypto_client.coincheck_client.GetSocketStatePublic();
                    break;
                case "bittrade":
                    ret = await this.crypto_client.bittrade_client.connectPublicAsync();
                    while (!ret)
                    {
                        ++trials;
                        if (trials < 5)
                        {
                            Thread.Sleep(trials * 3000);
                            this.addLog("Bittrade public connection failed. Trying again. trial:" + trials.ToString(), logType.WARNING);
                            ret = await this.crypto_client.bittrade_client.connectPublicAsync();
                        }
                        else
                        {
                            this.addLog("Failed to connect public. bittrade", logType.ERROR);
                            break;
                        }
                    }
                    if (!ret)
                    {
                        return false;
                    }
                    this.crypto_client.bittrade_client.onMessage = this.crypto_client.onBitTradeMessage;
                    onClosing = async () =>
                    {
                        await this.crypto_client.bittrade_client.onClosing(this.crypto_client.onBitTradeMessage);
                    };
                    thManager.addThread(market + "Public", this.crypto_client.bittrade_client.Listening,onClosing,this.crypto_client.bittrade_client.onListenOnError);
                    this._markets[market] = this.crypto_client.bittrade_client.GetSocketStatePublic();
                    break;
            }
            return ret;
        }
        public void checkConnections()
        {
            foreach (var market in this._markets)
            {
                switch (market.Key)
                {
                    case "bitbank":
                        this._markets[market.Key] = this.crypto_client.bitbank_client.GetSocketStatePublic();
                        break;
                    case "gmocoin":
                        this._markets[market.Key] = this.crypto_client.gmocoin_client.GetSocketStatePublic();
                        break;
                    case "coincheck":
                        this._markets[market.Key] = this.crypto_client.coincheck_client.GetSocketStatePublic();
                        break;
                    case "bittrade":
                        this._markets[market.Key] = this.crypto_client.bittrade_client.GetSocketStatePublic();
                        break;
                    default:
                        break;
                }
            }
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
                        if(!this._markets.ContainsKey(ins.market))
                        {
                            this._markets[ins.market] = WebSocketState.None;
                        }
                    }
                }
                return true;
            }
            else
            {
                this.addLog("The master file doesn't exist. Filename:" + masterfile, Enums.logType.ERROR);
                return false;
            }
        }
        public void initializeInstruments(Dictionary<string,masterInfo> masters)
        {
            Instrument ins;
            foreach(var m in masters)
            {
                ins = new Instrument();
                ins.initialize(m.Value);
                this.instruments[ins.symbol_market] = ins;
                this.ins_bymaster[ins.master_symbol + "@" + ins.market] = ins;
                if (!this._markets.ContainsKey(ins.market))
                {
                    this._markets[ins.market] = WebSocketState.None;
                }
            }
        }

        public Instrument getInstrument(string baseCcy,string quoteCcy, string market)
        {
            Instrument output = null;
            foreach(var ins in this.instruments.Values)
            {
                if(ins.baseCcy == baseCcy && ins.quoteCcy == quoteCcy && ins.market == market)
                {
                    output = ins;
                    break;
                }
            }
            return output;
        }
        public bool setVirtualBalance(string balanceFile)
        {
            if (File.Exists(balanceFile))
            {
                int i = 0;
                foreach (string line in File.ReadLines(balanceFile))
                {
                    if (i == 0)
                    {
                        ++i;
                    }
                    else
                    {
                        string[] items = line.Split(",");

                        string key = items[1] + "@" + items[0];
                        if (this.balances.ContainsKey(key))
                        {
                            this.balances[key].total = decimal.Parse(items[2]);
                        }
                        else
                        {
                            Balance balance = new Balance();
                            balance.ccy = items[1];
                            balance.market = items[0];
                            balance.total = decimal.Parse(items[2]);
                            this.balances[key] = balance;
                            foreach (var ins in this.instruments.Values)
                            {
                                if (ins.market == balance.market)
                                {
                                    if (ins.quoteCcy == balance.ccy)
                                    {
                                        ins.quoteBalance = balance;
                                    }
                                    else if (ins.baseCcy == balance.ccy)
                                    {
                                        ins.baseBalance = balance;
                                    }
                                    balance.valuation_pair = ins.symbol_market;
                                }
                            }
                        }

                    }
                }
                return true;
            }
            else
            {
                this.addLog("The balance file doesn't exist. Filename:" + balanceFile, Enums.logType.ERROR);
                return false;
            }
        }
        public bool setBalance(DataBalance[] results)
        {
            bool output = true;
            string key;
            foreach(var exBalance in this.exchange_balances.Values)
            {
                exBalance.marginAvailability = 0;
            }
            foreach(var item in results)
            {
                key = item.asset.ToUpper() + "@" + item.market;
                if (this.balances.ContainsKey(key))
                {
                    this.balances[key].total = item.total;
                    this.balances[key].inuse = item.total - item.available;
                }
                else
                {
                    Balance balance = new Balance();
                    balance.ccy = item.asset.ToUpper();
                    balance.market = item.market;
                    balance.total = item.total;
                    balance.inuse = item.total - item.available;
                    this.balances[key] = balance;
                    foreach (var ins in this.instruments.Values)
                    {
                        if (ins.market == balance.market)
                        {
                            if (ins.quoteCcy == balance.ccy)
                            {
                                ins.quoteBalance = balance;
                            }
                            else if (ins.baseCcy == balance.ccy)
                            {
                                ins.baseBalance = balance;
                                if (ins.quoteCcy == "JPY")
                                {
                                    balance.valuation_pair = ins.symbol_market;
                                }
                            }
                        }
                    }
                }
                ExchangeBalance exBalance;
                if(this.exchange_balances.ContainsKey(item.market))
                {
                    exBalance = this.exchange_balances[item.market];
                }
                else
                {
                    exBalance = new ExchangeBalance();
                    exBalance.market = item.market;
                    this.exchange_balances[item.market] = exBalance;
                }
                exBalance.balance[item.asset.ToUpper()] = this.balances[key];
                switch(item.market)
                {
                    case "bitbank":
                        if(item.asset.ToUpper() == "JPY")
                        {
                            exBalance.marginAvailability += item.total;
                        }
                        else
                        {
                            exBalance.marginAvailability += item.total / 2;
                        }
                        break;
                    case "gmocoin":
                        if(item.asset.ToUpper() == "JPY")
                        {
                            exBalance.marginAvailability += item.total;
                        }
                        break;
                }
            }
            return output;
        }
        public bool setMarginPosition(DataMarginPos[] mar_pos)
        {
            bool output = true;
            foreach (var item in mar_pos)
            {
                if (this.instruments.ContainsKey(item.symbol_market))
                {
                    ExchangeBalance exBalance;
                    if (this.exchange_balances.ContainsKey(item.market))
                    {
                        exBalance = this.exchange_balances[item.market];
                    }
                    else
                    {
                        exBalance = new ExchangeBalance();
                        exBalance.market = item.market;
                        this.exchange_balances[item.market] = exBalance;
                    }

                    Instrument ins = this.instruments[item.symbol_market];
                    if(item.side == positionSide.Long)
                    {
                        ins.longPosition.setPosition(item);
                        exBalance.marginLong[item.symbol] = ins.longPosition;
                        exBalance.marginNotionalAmount += ins.longPosition.avg_price * ins.longPosition.total;
                    }
                    else if(item.side == positionSide.Short)
                    {
                        ins.shortPosition.setPosition(item);
                        exBalance.marginShort[item.symbol] = ins.shortPosition;
                        exBalance.marginNotionalAmount += ins.shortPosition.avg_price * ins.shortPosition.total;
                    }
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
                        this.addLog("Unknown Symbol.  " + key, Enums.logType.WARNING);
                    }
                }
                else
                {
                    this.addLog("Failed to receive fee information.  Exchange:" + subResult.Exchange, Enums.logType.ERROR);
                    output = false;
                }
            }
            return output;
        }
        public async Task<bool> updateQuotes(Action start, Action end, CancellationToken ct, int spinningMax)
        {
            Instrument ins;
            DataOrderBook msg;
            string symbol_market;
            var spinner = new SpinWait();
            bool ret = true;
            try
            {
                while (true)
                {
                    msg = this.ordBookQueue.Dequeue();
                    //while (this.ordBookQueue.TryDequeue(out msg))
                    while(msg != null)
                    {
                        start();
                        symbol_market = msg.symbol + "@" + msg.market;
                        if (this.instruments.ContainsKey(symbol_market))
                        {
                            ins = instruments[symbol_market];
                            ins.updateQuotes(msg);
                            foreach (var stg in this.strategies)
                            {
                                if (stg.Value.enabled)
                                {
                                    if (symbol_market == stg.Value.taker.symbol_market)
                                    {
                                        if (Interlocked.CompareExchange(ref stg.Value.queued, 1, 0) == 0)
                                        {
                                            this.optQueue.Enqueue(stg.Value);
                                        }
                                    }
                                    else if (symbol_market == stg.Value.maker.symbol_market && !this.oManager.getVirtualMode())
                                    {
                                        //stg.Value.onMakerQuotes(msg);
                                        if (Interlocked.CompareExchange(ref stg.Value.queued, 1, 0) == 0)
                                        {
                                            this.optQueue.Enqueue(stg.Value);
                                        }
                                    }
                                }

                            }
                            //if(this.monitoring == false && ins.mid != ins.prev_mid)
                            //{
                            //    oManager.checkMIRecorder(DateTime.UtcNow);
                            //}
                            this.oManager.checkVirtualOrders(ins);
                        }
                        else
                        {
                            this.addLog("The symbol doesn't exist. Instrument:" + symbol_market, Enums.logType.WARNING);
                        }
                        msg.init();
                        this.ordBookStack.push(msg);
                        msg = this.ordBookQueue.Dequeue();
                        spinner.Reset();
                        end();
                    }
                    if (ct.IsCancellationRequested)
                    {
                        this.addLog("Cancel requested. updateQuotes", Enums.logType.WARNING);
                        break;
                    }
                    spinner.SpinOnce();
                    if (spinningMax > 0 && spinner.Count >= spinningMax)
                    {
                        Thread.Yield();
                        spinner.Reset();
                    }

                    // 2) まだ空ならイベント待機（低CPU）
                    //if (_queue.IsEmpty)
                    //{
                    //    _signal.Wait();
                    //    _signal.Reset();
                    //}
                }
            }
            catch (Exception ex)
            {
                this.addLog("Error recieved with in updateQuotes");
                this.addLog(ex.Message, Enums.logType.WARNING);
                if (ex.StackTrace != null)
                {
                    this.addLog(ex.StackTrace, Enums.logType.WARNING);
                }
                ret = false;
            }
            return ret;
            
        }

        public void updateQuotesOnClosing()
        {
            Instrument ins;
            DataOrderBook msg;
            string symbol_market;
            while (this.ordBookQueue.Count > 0)
            {
                msg = this.ordBookQueue.Dequeue();
                if (msg != null)
                {
                    symbol_market = msg.symbol + "@" + msg.market;
                    if (this.instruments.ContainsKey(symbol_market))
                    {
                        ins = instruments[symbol_market];
                        ins.updateQuotes(msg);
                        this.oManager.checkVirtualOrders(ins);
                    }
                    else
                    {
                        this.addLog("The symbol doesn't exist. Instrument:" + symbol_market, Enums.logType.WARNING);
                    }
                    msg.init();
                    this.ordBookStack.push(msg);
                }
                else
                {
                    break;
                }
            }
        }

        public void updateQuotesOnError()
        {
            foreach(var ins in this.instruments.Values)
            {
                ins.quotes_lock = 0;
            }
            this.oManager.virtual_order_lock = 0;
        }

        public async Task refreshAndCancelAllorders(string market = "")
        {
            if(market =="")
            {
                this.addLog("Cancelling all the orders including unknown.", Enums.logType.WARNING);
                //await this.oManager.cancelAllOrders();
                //Thread.Sleep(1000);
                this.addLog("Requesting order list....", Enums.logType.WARNING);

                if (this.live)
                {
                    foreach (string mkt in this._markets.Keys)
                    {
                        addLog("Order List of " + mkt, logType.WARNING);
                        List<DataSpotOrderUpdate> ordList = await this.crypto_client.getActiveOrders(mkt);
                        int i = 0;
                        while (ordList == null)
                        {
                            ++i;
                            addLog("Failed to get active orders. Retrying..." + i.ToString(), Enums.logType.WARNING);
                            this.oManager.refreshHttpClient(mkt);
                            Thread.Sleep(1000);
                            ordList = await this.crypto_client.getActiveOrders(mkt);
                            if (i >= 5)
                            {
                                addLog("Failed to get active orders.", Enums.logType.ERROR);
                                break;
                            }
                        }

                        this.addLog("The number of active orders:" + ordList.Count.ToString("N0"), Enums.logType.WARNING);
                        Dictionary<Instrument, List<string>> id_list = new Dictionary<Instrument, List<string>>();
                        foreach (DataSpotOrderUpdate ord in ordList)
                        {
                            if (this.oManager.ordIdMapping.ContainsKey(ord.market + ord.order_id))
                            {
                                ord.internal_order_id = this.oManager.ordIdMapping[ord.market + ord.order_id];
                            }
                            else
                            {
                                ord.internal_order_id = ord.market + ord.order_id;
                                this.oManager.ordIdMapping[ord.market + ord.order_id] = ord.market + ord.order_id;
                            }
                            if (this.instruments.ContainsKey(ord.symbol_market))
                            {
                                Instrument ins = this.instruments[ord.symbol_market];
                                if (!id_list.ContainsKey(ins))
                                {
                                    id_list[ins] = new List<string>();
                                }
                                id_list[ins].Add(ord.internal_order_id);
                            }
                            this.oManager.orders[ord.internal_order_id] = ord;
                        }
                        if (id_list.Count > 0)
                        {
                            foreach (var keyValue in id_list)
                            {
                                Thread.Sleep(1000);//Make sure the cancel orders are executed
                                this.addLog("Cancelling...", Enums.logType.WARNING);
                                await this.oManager.placeCancelSpotOrders(keyValue.Key, keyValue.Value, true, true);
                            }
                            this.addLog("Order cancelled.", Enums.logType.WARNING);
                        }
                        else
                        {
                            addLog("Active order not found");
                        }
                    }
                    Thread.Sleep(1000);
                    while (Interlocked.CompareExchange(ref this.oManager.order_lock, 1, 0) != 0)
                    {

                    }
                    this.oManager.live_orders.Clear();
                    foreach(Instrument ins in this.instruments.Values)
                    {
                        ins.resetInusePosition();
                    }
                    Volatile.Write(ref this.oManager.order_lock, 0);
                }
                else
                {
                    await this.oManager.cancelAllOrders();
                }
            }
            else
            {
                this.addLog("Cancelling all the orders of " + market + " including unknown.", Enums.logType.WARNING);
                //await this.oManager.cancelAllOrders();
                Thread.Sleep(1000);
                this.addLog("Requesting order list....", Enums.logType.WARNING);
                this.addLog("ordUpdateStack.Count:" + this.crypto_client.ordUpdateStack.Count.ToString("N0"));
                this.addLog("order_pool.Count:" + this.oManager.order_pool.Count.ToString("N0"));
                if(this.live)
                {
                    List<DataSpotOrderUpdate> ordList = await this.crypto_client.getActiveOrders(market);
                    int i = 0;
                    while (ordList == null)
                    {
                        ++i;
                        addLog("Failed to get active orders. Retrying..." + i.ToString(), Enums.logType.WARNING);

                        ordList = await this.crypto_client.getActiveOrders(market);
                        if (i >= 5)
                        {
                            addLog("Failed to get active orders.", Enums.logType.ERROR);
                            break;
                        }
                    }

                    this.addLog("The number of active orders:" + ordList.Count.ToString("N0"), Enums.logType.WARNING);
                    Dictionary<Instrument, List<string>> id_list = new Dictionary<Instrument, List<string>>();
                    foreach (DataSpotOrderUpdate ord in ordList)
                    {
                        if (this.oManager.ordIdMapping.ContainsKey(ord.market + ord.order_id))
                        {
                            ord.internal_order_id = this.oManager.ordIdMapping[ord.market + ord.order_id];
                        }
                        else
                        {
                            ord.internal_order_id = ord.market + ord.order_id;
                            this.oManager.ordIdMapping[ord.market + ord.order_id] = ord.market + ord.order_id;
                        }
                        if (this.instruments.ContainsKey(ord.symbol_market))
                        {
                            Instrument ins = this.instruments[ord.symbol_market];
                            if (!id_list.ContainsKey(ins))
                            {
                                id_list[ins] = new List<string>();
                            }
                            id_list[ins].Add(ord.internal_order_id);
                        }
                        this.oManager.orders[ord.internal_order_id] = ord;
                    }
                    if (id_list.Count > 0)
                    {
                        foreach (var keyValue in id_list)
                        {
                            Thread.Sleep(1000);//Make sure the cancel orders are executed
                            this.addLog("Cancelling...", Enums.logType.WARNING);
                            await this.oManager.placeCancelSpotOrders(keyValue.Key, keyValue.Value, true, true);
                        }
                        this.addLog("Order cancelled.", Enums.logType.WARNING);
                    }
                    else
                    {
                        addLog("Active order not found");
                    }
                    Thread.Sleep(1000);
                    while (Interlocked.CompareExchange(ref this.oManager.order_lock, 1, 0) != 0)
                    {

                    }
                    List<string> removing = new List<string>();
                    foreach (var ord in this.oManager.live_orders)
                    {
                        if (ord.Value.market == market)
                        {
                            removing.Add(ord.Key);
                        }
                    }
                    foreach (var id in removing)
                    {
                        this.oManager.live_orders.Remove(id);
                    }

                    foreach (Instrument ins in this.instruments.Values)
                    {
                        if(ins.market == market)
                        {
                            ins.resetInusePosition();
                        }
                    }
                    Volatile.Write(ref this.oManager.order_lock, 0);
                }
                else
                {
                    await this.oManager.cancelAllOrders();
                }
            }
        }
        public async Task<bool> optimize(Action start, Action end, CancellationToken ct, int spinningMax)
        {
            Strategy stg;
            var spinner = new SpinWait();
            bool ret = true;
            bool refresh = false;
            try
            {
                while (true)
                {
                    stg =  this.optQueue.Dequeue();
                    //while (this.optQueue.TryDequeue(out stg))
                    while(stg != null)
                    {
                        if(!this.oManager.ready)
                        {
                            int i = 0;
                            while(!this.oManager.ready)
                            {
                                Thread.Sleep(1);
                                ++i;
                                if(i > 10000)
                                {
                                    this.addLog("Order Manager is not ready", logType.ERROR);
                                    return false;
                                }
                            }
                            Task t = Task.Run(async ()=>
                            {
                                await this.refreshAndCancelAllorders();
                            });
                            foreach(var stg_obj in this.strategies.Values)
                            {
                                while (Interlocked.CompareExchange(ref stg_obj.updating, 1, 0) != 0)
                                {

                                }
                                stg_obj.maker.resetInusePosition();
                                stg_obj.taker.resetInusePosition();
                                stg_obj.live_bidprice = 0;
                                stg_obj.live_buyorder_id = "";
                                stg_obj.live_askprice = 0;
                                stg_obj.live_sellorder_id = "";
                                Volatile.Write(ref stg_obj.updating, 0);
                            }
                            t.Wait();
                            Thread.Sleep(1000);
                            addLog("Resetting positions...");
                            foreach (var m in _markets)
                            {
                                setBalance(await crypto_client.getBalance([m.Key]));
                            }
                            foreach (var stg_obj in this.strategies.Values)
                            {
                                stg_obj.adjustPosition();
                            }
                            addLog("All the strategy orders have been reset.");
                        }
                        else if(stg.maker.market == "bitbank" && (this.crypto_client.bitbank_client.pubnubReconnected || this.crypto_client.bitbank_client.pubnubReconnecting))
                        {
                            int i = 0;
                            while (!this.crypto_client.bitbank_client.pubnubReconnected)
                            {
                                Thread.Sleep(1);
                                ++i;
                                if (i > 10000)
                                {
                                    this.addLog("Pubnub is not connected.", logType.ERROR);
                                    return false;
                                }
                            }
                            Task t = Task.Run(async () =>
                            {
                                await this.refreshAndCancelAllorders();
                            });
                            foreach (var stg_obj in this.strategies.Values)
                            {
                                while (Interlocked.CompareExchange(ref stg_obj.updating, 1, 0) != 0)
                                {

                                }
                                stg_obj.maker.resetInusePosition();
                                stg_obj.taker.resetInusePosition();
                                stg_obj.live_bidprice = 0;
                                stg_obj.live_buyorder_id = "";
                                stg_obj.live_askprice = 0;
                                stg_obj.live_sellorder_id = "";
                                Volatile.Write(ref stg_obj.updating, 0);
                            }
                            t.Wait();
                            Thread.Sleep(1000);
                            addLog("Resetting positions...");
                            foreach (var m in _markets)
                            {
                                setBalance(await crypto_client.getBalance([m.Key]));
                            }
                            foreach (var stg_obj in this.strategies.Values)
                            {
                                stg_obj.adjustPosition();
                            }
                            this.crypto_client.bitbank_client.pubnubReconnected = false;
                            addLog("All the strategy orders have been reset.");
                        }
                        start();
                        if(!await stg.updateOrders())
                        {
                            refresh = true;
                        }
                        Volatile.Write(ref stg.queued, 0);
                        spinner.Reset();
                        end();
                        if (refresh)
                        {
                            refresh = false;
                            foreach (var mkt in this._markets.Keys)
                            {
                                this.oManager.refreshHttpClient(mkt);
                            }
                            Task t = Task.Run(async () =>
                            {
                                await this.refreshAndCancelAllorders(stg.maker.market);
                            });
                            foreach (var stg_obj in this.strategies.Values)
                            {
                                while (Interlocked.CompareExchange(ref stg_obj.updating, 1, 0) != 0)
                                {

                                }
                                stg_obj.maker.resetInusePosition();
                                stg_obj.taker.resetInusePosition();
                                stg_obj.live_bidprice = 0;
                                stg_obj.live_buyorder_id = "";
                                stg_obj.live_askprice = 0;
                                stg_obj.live_sellorder_id = "";
                                Volatile.Write(ref stg_obj.updating, 0);
                            }
                            t.Wait();
                            addLog("Resetting positions...");
                            Thread.Sleep(1000);
                            foreach (var m in _markets)
                            {
                                setBalance(await crypto_client.getBalance([m.Key]));
                            }
                            foreach (var stg_obj in this.strategies.Values)
                            {
                                stg.adjustPosition();
                            }
                            addLog("All the strategy orders have been reset.");
                        }
                        stg = this.optQueue.Dequeue();
                    }
                    if (ct.IsCancellationRequested)
                    {
                        this.addLog("Cancel requested. optimize", Enums.logType.WARNING);
                        break;
                    }
                    spinner.SpinOnce();
                    if (spinningMax > 0 && spinner.Count >= spinningMax)
                    {
                        Thread.Yield();
                        spinner.Reset();
                    }

                    // 2) まだ空ならイベント待機（低CPU）
                    //if (_queue.IsEmpty)
                    //{
                    //    _signal.Wait();
                    //    _signal.Reset();
                    //}
                }
            }
            catch (Exception ex)
            {
                this.addLog("Error recieved with in optimize");
                this.addLog(ex.Message, Enums.logType.WARNING);
                if (ex.StackTrace != null)
                {
                    this.addLog(ex.StackTrace, Enums.logType.WARNING);
                }
                ret = false;
            }
            return ret;

        }


        public async void optimizeOnClosing()
        {
            //Strategy stg;
            //while(this.optQueue.Count() > 0)
            //{
            //    if (this.optQueue.TryDequeue(out stg))
            //    {
            //        await stg.updateOrders();
            //    }
            //}
        }
        public void optimizeOnError()
        {
            foreach(var stg in this.strategies.Values)
            {
                stg.updating = 0;
                stg.queued = 0;
                stg.taker.quotes_lock = 0;
                stg.maker.quotes_lock = 0;
            }
            this.oManager.order_lock = 0;
        }
        public async Task<bool> updateTrades(Action start, Action end, CancellationToken ct, int spinningMax)
        {
            Instrument ins;
            DataTrade msg;
            MarketImpact mi;
            string symbol_market;
            var spinner = new SpinWait();
            bool ret = true;
            try
            {
                while (true)
                {
                    msg = this.tradeQueue.Dequeue();
                    //while (this.tradeQueue.TryDequeue(out msg))
                    while(msg != null)
                    {
                        start();
                        this.sw_updateTrades.Start();
                        symbol_market = msg.symbol + "@" + msg.market;
                        foreach (var stg in this.strategies)
                        {
                            if (stg.Value.enabled)
                            {
                                if (symbol_market == stg.Value.maker.symbol_market)
                                {
                                    stg.Value.onTrades(msg);
                                }
                            }
                        }
                        if (this.instruments.ContainsKey(symbol_market))
                        {
                            ins = instruments[symbol_market];
                            ins.updateTrade(msg);

                            if(!this.monitoring)
                            {
                                mi = oManager.MI_stack.pop();
                                if (mi != null)
                                {
                                    mi = new MarketImpact();
                                }
                                mi.startRecording(msg, ins);
                                this.oManager.MI_recorder[0].Enqueue(mi);
                            }
                            this.oManager.checkVirtualOrders(ins, msg);
                        }
                        else
                        {
                            this.addLog("The symbol doesn't exist. Instrument:" + symbol_market, Enums.logType.WARNING);
                        }
                        msg.init();
                        this.tradeStack.push(msg);
                        spinner.Reset();
                        msg = this.tradeQueue.Dequeue();
                        end();
                    }
                    if (ct.IsCancellationRequested)
                    {
                        this.addLog("Cancel requested. updateTrades", Enums.logType.WARNING);
                        break;
                    }

                    spinner.SpinOnce();
                    if (spinningMax > 0 && spinner.Count >= spinningMax)
                    {
                        Thread.Yield();
                        spinner.Reset();
                    }

                    // 2) まだ空ならイベント待機（低CPU）
                    //if (_queue.IsEmpty)
                    //{
                    //    _signal.Wait();
                    //    _signal.Reset();
                    //}
                }
            }
            catch (Exception ex)
            {
                this.addLog("Error recieved with in updateTrades");
                this.addLog(ex.Message, Enums.logType.WARNING);
                if (ex.StackTrace != null)
                {
                    this.addLog(ex.StackTrace, Enums.logType.WARNING);
                }
                ret = false;
            }
            return ret;
        }

        public void updateTradesOnClosing()
        {
            Instrument ins;
            DataTrade msg;
            string symbol_market;
            while (this.tradeQueue.Count > 0)
            {
                msg = this.tradeQueue.Dequeue();
                if (msg != null)
                {
                    symbol_market = msg.symbol + "@" + msg.market;
                    if (this.instruments.ContainsKey(symbol_market))
                    {
                        ins = instruments[symbol_market];
                        ins.updateTrade(msg);
                        this.oManager.checkVirtualOrders(ins, msg);
                    }
                    else
                    {
                        this.addLog("The symbol doesn't exist. Instrument:" + symbol_market, Enums.logType.WARNING);
                    }
                    msg.init();
                    this.tradeStack.push(msg);
                }
                else
                {
                    break;
                }
            }
        }

        public void updateTradeOnError()
        {
            this.oManager.virtual_order_lock = 0;
        }

        public void addLog(string line,logType logtype = logType.INFO)
        {
            this._addLog("[QuoteManager]" + line, logtype);
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
