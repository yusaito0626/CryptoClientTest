using Bybit.Net.Enums;
using Coinbase.Net.Objects.Models;
using Crypto_Clients;
using CryptoClients.Net;
using CryptoClients.Net.Enums;
using CryptoExchange.Net;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Requests;
using CryptoExchange.Net.SharedApis;
using CryptoExchange.Net.Sockets;
using Enums;
using LockFreeQueue;
using LockFreeStack;
using Microsoft.VisualBasic;
using OKX.Net.Objects.Account;
using PubnubApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Data;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Diagnostics.SymbolStore;
using System.Drawing;
using System.Globalization;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Utils;
using XT.Net.Objects.Models;

namespace Crypto_Trading
{


    public class OrderManager
    {

        Crypto_Clients.Crypto_Clients crypto_client = Crypto_Clients.Crypto_Clients.GetInstance();

        public myLock order_lock = new myLock();
        public Dictionary<string, DataSpotOrderUpdate> orders;
        public Dictionary<string, DataSpotOrderUpdate> live_orders;

        public Dictionary<market, Exchange> exchanges;
        public Dictionary<market, Exchange> exchanges_SoD;
        public Dictionary<string, Balance> balances;

        public SISOQueue<DataSpotOrderUpdate> order_pool;
        public int orderLifeTime = 60; 

        const int SENDINGORD_STACK_SIZE = 1000;
        public MISOQueue<sendingOrder> sendingOrders;
        public LockFreeStack<sendingOrder> sendingOrdersStack;
        CancellationTokenSource OrderProcessingStop;

        public myLock mapping_lock = new myLock();
        public Dictionary<string, string> ordIdMapping;

        public myLock virtual_order_lock = new myLock();
        public MIMOQueue<DataSpotOrderUpdate> virtual_order_queue;
        public Dictionary<string, DataSpotOrderUpdate> virtual_liveorders;
        public Dictionary<string, DataSpotOrderUpdate> disposed_orders;// The key is market + order_id, as the internal_order_id might not be exist.

        public Dictionary<string, modifingOrd> modifingOrders;
        public LockFreeStack<modifingOrd> modifingOrdStack;

        public Dictionary<Enums.market, WebSocketState> connections;

        public string outputPath;
        FileStream f;
        StreamWriter sw;
        private bool ord_logged;
        public MISOQueue<string> ordLogQueue;
        public Thread ordLoggingTh;

        public SISOQueue<DataFill> filledOrderQueue;

        public Strategy stg;
        public Dictionary<string, Strategy> strategies;

        const int MOD_STACK_SIZE = 100;

        public Dictionary<string, Instrument> instruments;

        private bool virtualMode;
        public volatile int id_number;

        public Action<string, Enums.logType> _addLog;

        public bool ready;
        public bool aborting;
        public bool updateOrderStopped;

        public int latency = 0;

        volatile int refreshing_httpClient = 0;

        Stopwatch coincheck_sw;

        public Dictionary<string, latency> Latency;

        public Dictionary<double, SISOQueue<MarketImpact>> MI_recorder;
        public SISOQueue<MarketImpact> MI_tempQueue;
        public SISOQueue<MarketImpact> MI_outputQueue;
        const int MI_STACK_SIZE = 100000;
        const int TS_STACK_SIZE = 500000;
        public LockFreeStack<MarketImpact> MI_stack;

        public LockFreeStack<tradeSummary> TS_stack;

        private OrderManager() 
        {
            this.aborting = false;
            this.updateOrderStopped = false;
            this.virtualMode = true;
            this.orders = new Dictionary<string, DataSpotOrderUpdate>();
            this.live_orders = new Dictionary<string, DataSpotOrderUpdate>();
            this.virtual_order_queue = new MIMOQueue<DataSpotOrderUpdate>();
            this.virtual_liveorders = new Dictionary<string, DataSpotOrderUpdate>();
            this.disposed_orders = new Dictionary<string, DataSpotOrderUpdate>();

            this.order_pool = new SISOQueue<DataSpotOrderUpdate>();

            this.strategies = new Dictionary<string, Strategy>();

            this.connections = new Dictionary<Enums.market, WebSocketState>();

            this.modifingOrders = new Dictionary<string, modifingOrd>();
            this.modifingOrdStack = new LockFreeStack<modifingOrd>();

            this.ordLogQueue = new MISOQueue<string>();

            this.ord_logged = false;

            this.id_number = 0;

            int i = 0;

            this.sendingOrders = new MISOQueue<sendingOrder>();
            //this.sendingOrdersStack = new ConcurrentStack<sendingOrder>();
            //this.sendingOrdersStack = new ConcurrentQueue<sendingOrder>();
            this.sendingOrdersStack = new LockFreeStack<sendingOrder>();

            while (i < SENDINGORD_STACK_SIZE)
            {
                this.sendingOrdersStack.push(new sendingOrder());
                ++i;
            }
            this.ordIdMapping = new Dictionary<string, string>();

            i = 0;
            while (i < MOD_STACK_SIZE)
            {
                this.modifingOrdStack.push(new modifingOrd());
                ++i;
            }


            this.coincheck_sw = new Stopwatch();
            this.Latency = new Dictionary<string, latency>();
            this.Latency["processNewOrder"] = new latency("processNewOrder");
            this.Latency["processCanOrders"] = new latency("processCanOrders");
            this.Latency["processCanOrder"] = new latency("processCanOrder");

            this.OrderProcessingStop = new CancellationTokenSource();

            this.MI_stack = new LockFreeStack<MarketImpact>();
            this.TS_stack = new LockFreeStack<tradeSummary>();
            i = 0;
            while (i < MI_STACK_SIZE)
            {
                this.MI_stack.push(new MarketImpact());
                ++i;
            }
            i = 0;
            while(i < TS_STACK_SIZE)
            {
                this.TS_stack.push(new tradeSummary());
                ++i;
            }
            this.MI_recorder = new Dictionary<double, SISOQueue<MarketImpact>>();
            foreach(double d in GlobalVariables.MI_period)
            {
                this.MI_recorder[d] = new SISOQueue<MarketImpact>();
            }
            this.MI_tempQueue = new SISOQueue<MarketImpact>();

            this.ready = false;
        }

        ~OrderManager()
        {
            //this.OrderProcessingStop.Cancel();
        }

        public bool refreshHttpClient(Enums.market market)
        {
            if(Interlocked.CompareExchange(ref this.refreshing_httpClient,1,0) == 0)
            {
                addLog("Refreshing HTTP clients... market:" + market.ToString());
                this.ready = false;
                Thread.Sleep(1000);
                switch (market)
                {
                    case market.bitbank:
                        this.crypto_client.bitbank_client.refreshHttpClient();
                        break;
                    case market.gmocoin:
                        this.crypto_client.gmocoin_client.refreshHttpClient();
                        break;
                    case market.coincheck:
                        this.crypto_client.coincheck_client.refreshHttpClient();
                        break;
                    default:
                        addLog("Httpclient refresh is not configured for " + market, logType.ERROR);
                        break;
                }
                Thread.Sleep(1000);
                this.ready = true;
                Volatile.Write(ref this.refreshing_httpClient, 0);
                return true;
            }
            else
            {
                return false;
            }
            
        }

        public async Task<bool> connectPrivateChannel(Enums.market market)
        {
            bool ret= false;
            ThreadManager thManager = ThreadManager.GetInstance();
            Func<Task<(bool, double)>> onMsg;
            Action onClosing;
            int trials = 0;
            switch (market)
            {
                case market.bitbank:
                    await this.crypto_client.bitbank_client.connectPrivateAsync();//This call includes creation of pubnub
                    this.connections[market] = this.crypto_client.bitbank_client.GetSocketStatePrivate();
                    ret = true;
                    break;
                case market.coincheck:
                    ret = await this.crypto_client.coincheck_client.connectPrivateAsync();
                    while(!ret)
                    {
                        ++trials;
                        if(trials < 5)
                        {
                            Thread.Sleep(trials * 3000);
                            this.addLog("Coincheck private connection failed. Trying again. trial:" + trials.ToString(), logType.WARNING);
                            ret = await this.crypto_client.coincheck_client.connectPrivateAsync();
                        }
                        else
                        {
                            this.addLog("Failed to connect private. coincheck", logType.ERROR);
                            break;
                        }
                    }
                    if(!ret)
                    {
                        return false;
                    }
                    this.crypto_client.coincheck_client.onPrivateMessage = this.crypto_client.onConcheckPrivateMessage;
                    onClosing = async () =>
                    {
                        await this.crypto_client.coincheck_client.onClosingPrivate(this.crypto_client.onConcheckPrivateMessage);
                    };
                    thManager.addThread(market + "Private", this.crypto_client.coincheck_client.ListeningPrivate,onClosing,this.crypto_client.coincheck_client.onListenPrivateOnError);
                    this.connections[market] = this.crypto_client.coincheck_client.GetSocketStatePrivate();
                    break;
                case market.bittrade:
                    ret = await this.crypto_client.bittrade_client.connectPrivateAsync();
                    while (!ret)
                    {
                        ++trials;
                        if (trials < 5)
                        {
                            Thread.Sleep(trials * 3000);
                            this.addLog("Bittrade private connection failed. Trying again. trial:" + trials.ToString(), logType.WARNING);
                            ret = await this.crypto_client.bittrade_client.connectPrivateAsync();
                        }
                        else
                        {
                            this.addLog("Failed to connect private. bittrade", logType.ERROR);
                            break;
                        }
                    }
                    if (!ret)
                    {
                        return false;
                    }
                    this.crypto_client.bittrade_client.onPrivateMessage = this.crypto_client.onBitTradePrivateMessage;
                    onClosing = async () =>
                    {
                        await this.crypto_client.bittrade_client.onClosingPrivate(this.crypto_client.onBitTradePrivateMessage);
                    };
                    thManager.addThread(market + "Private", this.crypto_client.bittrade_client.ListeningPrivate,onClosing,this.crypto_client.bittrade_client.onListenPrivateOnError);
                    this.connections[market] = this.crypto_client.bittrade_client.GetSocketStatePrivate();
                    break;
                case market.gmocoin:
                    ret = await this.crypto_client.gmocoin_client.connectPrivateAsync();
                    while (!ret)
                    {
                        ++trials;
                        if (trials < 5)
                        {
                            Thread.Sleep(trials * 3000);
                            this.addLog("GMOCoin private connection failed. Trying again. trial:" + trials.ToString(), logType.WARNING);
                            ret = await this.crypto_client.gmocoin_client.connectPrivateAsync();
                        }
                        else
                        {
                            this.addLog("Failed to connect private. gmocoin", logType.ERROR);
                            break;
                        }
                    }
                    if (!ret)
                    {
                        return false;
                    }
                    this.crypto_client.gmocoin_client.onPrivateMessage = this.crypto_client.onGMOCoinPrivateMessage;
                    onClosing = async () =>
                    {
                        await this.crypto_client.gmocoin_client.onClosingPrivate(this.crypto_client.onBitTradePrivateMessage);
                    };
                    thManager.addThread(market + "Private", this.crypto_client.gmocoin_client.ListeningPrivate, onClosing, this.crypto_client.gmocoin_client.onListenPrivateOnError);
                    this.connections[market] = this.crypto_client.gmocoin_client.GetSocketStatePrivate();
                    break;
            }
            return ret;
        }

        public void checkConnections()
        {
            foreach (var market in this.connections)
            {
                switch (market.Key)
                {
                    case Enums.market.bitbank:
                        this.connections[market.Key] = this.crypto_client.bitbank_client.GetSocketStatePrivate();
                        break;
                    case Enums.market.coincheck:
                        this.connections[market.Key] = this.crypto_client.coincheck_client.GetSocketStatePrivate();
                        break;
                    case Enums.market.bittrade:
                        this.connections[market.Key] = this.crypto_client.bittrade_client.GetSocketStatePrivate();
                        break;
                    case Enums.market.gmocoin:
                        this.connections[market.Key] = this.crypto_client.gmocoin_client.GetSocketStatePrivate();
                        break;
                    default:
                        break;
                }
            }
        }
        public void setInstruments(Dictionary<string, Instrument> dic)
        {
            this.instruments = dic;
        }

        public async Task<string> placeNewSpotOrder(Instrument ins, orderSide side, orderType ordtype, decimal quantity, decimal price,positionSide pos_side = positionSide.NONE, timeInForce? timeinforce = null, bool sendNow = true,bool wait = true,string msg = "")
        {
            sendingOrder ord;
            string ordid;
            if(this.ready)
            {
                ord = this.sendingOrdersStack.pop();
                if(ord == null)
                {
                    ord = new sendingOrder();
                }
                ordid = this.getInternalOrdId(ins.market.ToString());
                ord.internalOrdId = ordid;
                ord.action = orderAction.New;
                ord.ins = ins;
                ord.side = side;
                ord.pos_side = pos_side;
                ord.order_type = ordtype;
                ord.quantity = quantity;
                ord.price = price;
                ord.time_in_force = timeinforce;
                ord.msg = msg;
                if (sendNow)
                {
                    if (wait)
                    {
                        await this.processNewOrder(ord);
                    }
                    else
                    {
                        this.processNewOrder(ord);
                    }
                }
                else
                {
                    this.addLog("This feature is not temporarily supported", Enums.logType.ERROR);
                    this.sendingOrders.Enqueue(ord);
                }

                return ordid;
            }
            else
            {
                return "";
            }
        }
        public async Task<string> placeCancelSpotOrder(Instrument ins, string orderId, bool sendNow = true, bool wait = true,string msg = "")
        {
            sendingOrder ord;
            if(this.ready)
            {
                ord = this.sendingOrdersStack.pop();
                ord.action = orderAction.Can;
                ord.ins = ins;
                ord.msg = msg;
                ord.ref_IntOrdId = orderId;

                if (sendNow)
                {
                    if (wait)
                    {
                        await this.processCanOrder(ord);
                    }
                    else
                    {
                        this.processCanOrder(ord);
                    }
                }
                else
                {
                    this.addLog("This feature is not temporarily supported", Enums.logType.ERROR);
                    this.sendingOrders.Enqueue(ord);
                }

                return orderId;
            }
            else
            {
                return "";
            }
        }
        public async Task<IEnumerable<string>> placeCancelSpotOrders(Instrument ins,IEnumerable<string> order_ids,bool sendNow = true,bool wait = true, string msg = "")
        {
            sendingOrder ord;
            if(this.ready)
            {
                ord = this.sendingOrdersStack.pop();
                if(ord == null)
                {
                    ord = new sendingOrder();
                }
                ord.action = orderAction.Can;
                ord.ins = ins;

                ord.order_ids = order_ids;
                ord.msg = msg;
                if (sendNow)
                {
                    if (wait)
                    {
                        await this.processCanOrders(ord);
                    }
                    else
                    {
                        this.processCanOrders(ord);
                    }
                }
                else
                {
                    this.addLog("This feature is not temporarily supported", Enums.logType.ERROR);
                    this.sendingOrders.Enqueue(ord);
                }

                return order_ids;
            }
            else
            {
                return new List<string>() ;
            }
        }
        public async Task<string> placeModSpotOrder(Instrument ins, string orderId, decimal quantity, decimal price,bool waitCancel, bool sendNow = true,bool wait = true,string msg = "")
        {
            sendingOrder ord;
            string ordid;
            if(this.ready)
            {
                ord = this.sendingOrdersStack.pop();
                ordid = this.getInternalOrdId(ins.market.ToString());
                ord.internalOrdId = ordid;
                ord.ref_IntOrdId = orderId;
                ord.action = orderAction.Mod;
                ord.ins = ins;
                ord.quantity = quantity;
                ord.price = price;
                ord.waitCancel = waitCancel;
                ord.msg = msg;
                if (sendNow)
                {
                    if (wait)
                    {
                        await this.processModOrder(ord);
                    }
                    else
                    {
                        this.processModOrder(ord);
                    }
                }
                else
                {
                    this.addLog("This feature is not temporarily supported", Enums.logType.ERROR);
                    this.sendingOrders.Enqueue(ord);
                }

                return ordid;
            }
            else
            {
                return "";
            }
        }

        async public Task<DataSpotOrderUpdate?> processNewOrder(sendingOrder sndOrd)
        {
            //Lock the quantity before sending the order, reduce later if it's failed.
            using var f = new funcContainer(this.Latency["processNewOrder"].MeasureLatency);
            DataSpotOrderUpdate? output = null;
            JsonDocument js;
            sndOrd.quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
            decimal quantity = sndOrd.quantity;
            DateTime current = DateTime.UtcNow;
            Exchange ex;
            if (this.exchanges.ContainsKey(sndOrd.ins.market))
            { 
                ex = this.exchanges[sndOrd.ins.market];
            }
            else
            {
                addLog("[New Order]The market not found.  " + sndOrd.ins.market.ToString(),logType.WARNING);

                sndOrd.init();
                this.sendingOrdersStack.push(sndOrd);
                return null;
            }
            //Order Check

            //Validity check
            if (sndOrd.ins == null)
            {
                addLog("[New Order]Instrument is not specified.", logType.WARNING);
                output = this.crypto_client.ordUpdateStack.pop();
                if (output == null)
                {
                    output = new DataSpotOrderUpdate();
                }
                output.status = orderStatus.INVALID;
                output.timestamp = current;
                output.internal_order_id = sndOrd.internalOrdId;
                output.side = sndOrd.side;
                //output.symbol = sndOrd.ins.symbol;
                //output.market = sndOrd.ins.market;
                //output.symbol_market = sndOrd.ins.symbol_market;
                output.order_quantity = sndOrd.quantity;
                output.order_price = sndOrd.price;
                output.filled_quantity = 0;
                output.average_price = 0;
                output.fee = 0;
                output.fee_asset = "";
                output.is_trigger_order = true;
                output.last_trade = "";
                output.msg = sndOrd.msg;
                output.err_code = (int)ordError.INVALID_INSTRUMENT;
                addLog(output.ToString(), logType.WARNING);
                this.crypto_client.ordUpdateQueue.Enqueue(output);
                sndOrd.init();
                this.sendingOrdersStack.push(sndOrd);
                return output;
            }
            if(sndOrd.price < 0)
            {
                addLog("[New Order]Invalid price  price:" + sndOrd.price.ToString(),logType.WARNING);
                output = this.crypto_client.ordUpdateStack.pop();
                if (output == null)
                {
                    output = new DataSpotOrderUpdate();
                }
                output.status = orderStatus.INVALID;
                output.timestamp = current;
                output.internal_order_id = sndOrd.internalOrdId;
                output.side = sndOrd.side;
                output.symbol = sndOrd.ins.symbol;
                output.market = sndOrd.ins.market;
                output.symbol_market = sndOrd.ins.symbol_market;
                output.order_quantity = sndOrd.quantity;
                output.order_price = sndOrd.price;
                output.filled_quantity = 0;
                output.average_price = 0;
                output.fee = 0;
                output.fee_asset = "";
                output.is_trigger_order = true;
                output.last_trade = "";
                output.msg = sndOrd.msg;
                output.err_code = (int)ordError.INVALID_PRICE;
                addLog(output.ToString(), logType.WARNING);
                this.crypto_client.ordUpdateQueue.Enqueue(output);
                sndOrd.init();
                this.sendingOrdersStack.push(sndOrd);
                return output;
            }
            if(sndOrd.quantity <= 0)
            {
                addLog("[New Order]Invalid quantity    quantity:" + sndOrd.quantity.ToString(), logType.WARNING);
                output = this.crypto_client.ordUpdateStack.pop();
                if (output == null)
                {
                    output = new DataSpotOrderUpdate();
                }
                output.status = orderStatus.INVALID;
                output.timestamp = current;
                output.internal_order_id = sndOrd.internalOrdId;
                output.side = sndOrd.side;
                output.symbol = sndOrd.ins.symbol;
                output.market = sndOrd.ins.market;
                output.symbol_market = sndOrd.ins.symbol_market;
                output.order_quantity = sndOrd.quantity;
                output.order_price = sndOrd.price;
                output.filled_quantity = 0;
                output.average_price = 0;
                output.fee = 0;
                output.fee_asset = "";
                output.is_trigger_order = true;
                output.last_trade = "";
                output.msg = sndOrd.msg;
                output.err_code = (int)ordError.INVALID_QUANTITY;
                addLog(output.ToString(),logType.WARNING);
                this.crypto_client.ordUpdateQueue.Enqueue(output);
                sndOrd.init();
                this.sendingOrdersStack.push(sndOrd);
                return output;
            }
            if(sndOrd.side == orderSide.NONE)
            {
                addLog("[New Order]Invalid order side  order_side:" + sndOrd.side.ToString(), logType.WARNING);
                output = this.crypto_client.ordUpdateStack.pop();
                if (output == null)
                {
                    output = new DataSpotOrderUpdate();
                }
                output.status = orderStatus.INVALID;
                output.timestamp = current;
                output.internal_order_id = sndOrd.internalOrdId;
                output.side = sndOrd.side;
                output.symbol = sndOrd.ins.symbol;
                output.market = sndOrd.ins.market;
                output.symbol_market = sndOrd.ins.symbol_market;
                output.order_quantity = sndOrd.quantity;
                output.order_price = sndOrd.price;
                output.filled_quantity = 0;
                output.average_price = 0;
                output.fee = 0;
                output.fee_asset = "";
                output.is_trigger_order = true;
                output.last_trade = "";
                output.msg = sndOrd.msg;
                output.err_code = (int)ordError.INVALID_SIDE;
                addLog(output.ToString(), logType.WARNING);
                this.crypto_client.ordUpdateQueue.Enqueue(output);
                sndOrd.init();
                this.sendingOrdersStack.push(sndOrd);
                return output;
            }

            //Availability Check
            decimal orderprice = sndOrd.price;
            List<decimal> pr = new List<decimal>();
            //Spot
            if(sndOrd.pos_side == positionSide.NONE)
            {
                switch(sndOrd.side)
                {
                    case orderSide.Buy:
                        if(sndOrd.order_type == orderType.Market)
                        {
                            if(sndOrd.ins.getWeightedAvgPrice(orderSide.Sell, [sndOrd.quantity],pr,false))
                            {
                                orderprice = pr[0];
                            }
                            else
                            {
                                orderprice = sndOrd.ins.bestask.Item1;
                            }
                        }
                        using(var balancelock = sndOrd.ins.quoteBalance.balance_lock.getlock())
                        {
                            if (sndOrd.ins.quoteBalance.available <= orderprice * sndOrd.quantity)
                            {
                                addLog("[New Order]Insuficient quote balance availability.", logType.WARNING);
                                addLog($"Availability:{sndOrd.ins.quoteBalance.available} marketPrice:{orderprice} Quantity:{quantity}", logType.WARNING);
                                output = this.crypto_client.ordUpdateStack.pop();
                                if (output == null)
                                {
                                    output = new DataSpotOrderUpdate();
                                }
                                output.status = orderStatus.INVALID;
                                output.timestamp = current;
                                output.internal_order_id = sndOrd.internalOrdId;
                                output.side = sndOrd.side;
                                output.symbol = sndOrd.ins.symbol;
                                output.market = sndOrd.ins.market;
                                output.symbol_market = sndOrd.ins.symbol_market;
                                output.order_quantity = sndOrd.quantity;
                                output.order_price = sndOrd.price;
                                output.filled_quantity = 0;
                                output.average_price = 0;
                                output.fee = 0;
                                output.fee_asset = "";
                                output.is_trigger_order = true;
                                output.last_trade = "";
                                output.msg = sndOrd.msg;
                                output.err_code = (int)ordError.INSUFFICIENT_AMOUNT;
                                addLog(output.ToString(), logType.WARNING);
                                this.crypto_client.ordUpdateQueue.Enqueue(output);
                                sndOrd.init();
                                this.sendingOrdersStack.push(sndOrd);
                                return output;
                            }
                        }
                        sndOrd.ins.quoteBalance.AddBalance(0, orderprice * sndOrd.quantity);
                        break;
                    case orderSide.Sell:
                        if (sndOrd.order_type == orderType.Market)
                        {
                            if (sndOrd.ins.getWeightedAvgPrice(orderSide.Buy, [sndOrd.quantity], pr, false))
                            {
                                orderprice = pr[0];
                            }
                            else
                            {
                                orderprice = sndOrd.ins.bestbid.Item1;
                            }
                        }
                        using(var balancelock = sndOrd.ins.baseBalance.balance_lock.getlock())
                        {
                            if (sndOrd.ins.baseBalance.available < sndOrd.quantity)
                            {
                                addLog("[New Order]Insuficient base balance availability.", logType.WARNING);
                                addLog($"Availability:{sndOrd.ins.baseBalance.available} marketPrice:{orderprice} Quantity:{quantity}", logType.WARNING);
                                output = this.crypto_client.ordUpdateStack.pop();
                                if (output == null)
                                {
                                    output = new DataSpotOrderUpdate();
                                }
                                output.status = orderStatus.INVALID;
                                output.timestamp = current;
                                output.internal_order_id = sndOrd.internalOrdId;
                                output.side = sndOrd.side;
                                output.symbol = sndOrd.ins.symbol;
                                output.market = sndOrd.ins.market;
                                output.symbol_market = sndOrd.ins.symbol_market;
                                output.order_quantity = sndOrd.quantity;
                                output.order_price = sndOrd.price;
                                output.filled_quantity = 0;
                                output.average_price = 0;
                                output.fee = 0;
                                output.fee_asset = "";
                                output.is_trigger_order = true;
                                output.last_trade = "";
                                output.msg = sndOrd.msg;
                                output.err_code = (int)ordError.INSUFFICIENT_AMOUNT;
                                addLog(output.ToString(), logType.WARNING);
                                this.crypto_client.ordUpdateQueue.Enqueue(output);
                                sndOrd.init();
                                this.sendingOrdersStack.push(sndOrd);
                                return output;
                            }
                        }
                        sndOrd.ins.baseBalance.AddBalance(0, sndOrd.quantity);
                        break;
                }
            }
            else
            {
                if ((sndOrd.side == orderSide.Buy && sndOrd.pos_side == positionSide.Long) || (sndOrd.side == orderSide.Sell && sndOrd.pos_side == positionSide.Short))
                {
                    decimal marginAvailability = ex.getMarginAvailability() - ex.marginLocked;
                    using (var ex_mlock = ex.margin_lock.getlock())
                    {
                        decimal marketPrice;
                        if (sndOrd.order_type == orderType.Market)
                        {
                            marketPrice = (sndOrd.side == orderSide.Buy) ? sndOrd.ins.bestask.Item1 : sndOrd.ins.bestbid.Item1;
                            if(sndOrd.side == orderSide.Buy)
                            {
                                if(sndOrd.ins.getWeightedAvgPrice(orderSide.Sell, [sndOrd.quantity], pr, false))
                                {
                                    marketPrice = pr[0];
                                }
                                else
                                {
                                    marketPrice = sndOrd.ins.bestask.Item1;
                                }
                            }
                            else if(sndOrd.side == orderSide.Sell)
                            {
                                if (sndOrd.ins.getWeightedAvgPrice(orderSide.Buy, [sndOrd.quantity], pr, false))
                                {
                                    marketPrice = pr[0];
                                }
                                else
                                {
                                    marketPrice = sndOrd.ins.bestbid.Item1;
                                }
                            }
                            sndOrd.price = marketPrice;
                        }
                        else
                        {
                            marketPrice = sndOrd.price;
                        }
                        if (marginAvailability < marketPrice * sndOrd.quantity)
                        {
                            addLog("[New Order]Insuficient margin availability.", logType.WARNING);
                            addLog($"Availability:{marginAvailability} marketPrice:{marketPrice} Quantity:{quantity}", logType.WARNING);
                            output = this.crypto_client.ordUpdateStack.pop();
                            if (output == null)
                            {
                                output = new DataSpotOrderUpdate();
                            }
                            output.status = orderStatus.INVALID;
                            output.timestamp = current;
                            output.internal_order_id = sndOrd.internalOrdId;
                            output.side = sndOrd.side;
                            output.symbol = sndOrd.ins.symbol;
                            output.market = sndOrd.ins.market;
                            output.symbol_market = sndOrd.ins.symbol_market;
                            output.order_quantity = sndOrd.quantity;
                            output.order_price = sndOrd.price;
                            output.filled_quantity = 0;
                            output.average_price = 0;
                            output.fee = 0;
                            output.fee_asset = "";
                            output.is_trigger_order = true;
                            output.last_trade = "";
                            output.msg = sndOrd.msg;
                            output.err_code = (int)ordError.INSUFFICIENT_AMOUNT;
                            addLog(output.ToString(), logType.WARNING);
                            this.crypto_client.ordUpdateQueue.Enqueue(output);
                            sndOrd.init();
                            this.sendingOrdersStack.push(sndOrd);
                            return output;
                        }
                    }
                    ex.updateMarginLocked(sndOrd.price * sndOrd.quantity / sndOrd.ins.leverage);
                }
                else//Close
                {
                    if(sndOrd.order_type == orderType.Market)
                    {
                        decimal marketPrice;
                        marketPrice = (sndOrd.side == orderSide.Buy) ? sndOrd.ins.bestask.Item1 : sndOrd.ins.bestbid.Item1;
                        if (sndOrd.side == orderSide.Buy)
                        {
                            if (sndOrd.ins.getWeightedAvgPrice(orderSide.Sell, [sndOrd.quantity], pr, false))
                            {
                                marketPrice = pr[0];
                            }
                            else
                            {
                                marketPrice = sndOrd.ins.bestask.Item1;
                            }
                        }
                        else if (sndOrd.side == orderSide.Sell)
                        {
                            if (sndOrd.ins.getWeightedAvgPrice(orderSide.Buy, [sndOrd.quantity], pr, false))
                            {
                                marketPrice = pr[0];
                            }
                            else
                            {
                                marketPrice = sndOrd.ins.bestbid.Item1;
                            }
                        }
                        sndOrd.price = marketPrice;
                    }
                    
                    switch (sndOrd.pos_side)
                    {
                        case positionSide.Long:
                            if (ex.marginLong.ContainsKey(sndOrd.ins.symbol_market))
                            {
                                BalanceMargin marginLong = ex.marginLong[sndOrd.ins.symbol_market];
                                using(var loslock = marginLong.balance_lock.getlock())
                                {
                                    if (marginLong.available < sndOrd.quantity)
                                    {
                                        addLog("[New Order]Insuficient marginLong availability.", logType.WARNING);
                                        addLog($"Order side:{sndOrd.side} Availability:{marginLong.total} - {marginLong.inuse} Quantity:{sndOrd.quantity}", logType.WARNING);
                                        output = this.crypto_client.ordUpdateStack.pop();
                                        if (output == null)
                                        {
                                            output = new DataSpotOrderUpdate();
                                        }
                                        output.status = orderStatus.INVALID;
                                        output.timestamp = current;
                                        output.internal_order_id = sndOrd.internalOrdId;
                                        output.side = sndOrd.side;
                                        output.symbol = sndOrd.ins.symbol;
                                        output.market = sndOrd.ins.market;
                                        output.symbol_market = sndOrd.ins.symbol_market;
                                        output.order_quantity = sndOrd.quantity;
                                        output.order_price = sndOrd.price;
                                        output.filled_quantity = 0;
                                        output.average_price = 0;
                                        output.fee = 0;
                                        output.fee_asset = "";
                                        output.is_trigger_order = true;
                                        output.last_trade = "";
                                        output.msg = sndOrd.msg;
                                        output.err_code = (int)ordError.INSUFFICIENT_AMOUNT;
                                        addLog(output.ToString(), logType.WARNING);
                                        this.crypto_client.ordUpdateQueue.Enqueue(output);
                                        sndOrd.init();
                                        this.sendingOrdersStack.push(sndOrd);
                                        return output;
                                    }
                                }
                                marginLong.AddBalance(0, sndOrd.quantity);
                            }
                            break;
                        case positionSide.Short:
                            if (ex.marginShort.ContainsKey(sndOrd.ins.symbol_market))
                            {
                                BalanceMargin marginShort = ex.marginShort[sndOrd.ins.symbol_market];
                                using (var loslock = marginShort.balance_lock.getlock())
                                {
                                    if (marginShort.available < sndOrd.quantity)
                                    {
                                        addLog("[New Order]Insuficient marginShort availability.", logType.WARNING);
                                        addLog($"Order side:{sndOrd.side} Availability:{marginShort.total} - {marginShort.inuse} Quantity:{sndOrd.quantity}", logType.WARNING);
                                        output = this.crypto_client.ordUpdateStack.pop();
                                        if (output == null)
                                        {
                                            output = new DataSpotOrderUpdate();
                                        }
                                        output.status = orderStatus.INVALID;
                                        output.timestamp = current;
                                        output.internal_order_id = sndOrd.internalOrdId;
                                        output.side = sndOrd.side;
                                        output.symbol = sndOrd.ins.symbol;
                                        output.market = sndOrd.ins.market;
                                        output.symbol_market = sndOrd.ins.symbol_market;
                                        output.order_quantity = sndOrd.quantity;
                                        output.order_price = sndOrd.price;
                                        output.filled_quantity = 0;
                                        output.average_price = 0;
                                        output.fee = 0;
                                        output.fee_asset = "";
                                        output.is_trigger_order = true;
                                        output.last_trade = "";
                                        output.msg = sndOrd.msg;
                                        output.err_code = (int)ordError.INSUFFICIENT_AMOUNT;
                                        addLog(output.ToString(), logType.WARNING);
                                        this.crypto_client.ordUpdateQueue.Enqueue(output);
                                        sndOrd.init();
                                        this.sendingOrdersStack.push(sndOrd);
                                        return output;
                                    }
                                }
                                marginShort.AddBalance(0, sndOrd.quantity);
                            }
                            break;
                    }
                }
            }

            if (this.virtualMode)
            {
                //quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                output = this.crypto_client.ordUpdateStack.pop();
                if (output == null)
                {
                    output = new DataSpotOrderUpdate();
                }
                output.isVirtual = true;
                output.status = orderStatus.WaitOpen;
                output.order_id = this.getVirtualOrdId();
                output.symbol = sndOrd.ins.symbol;
                output.market = sndOrd.ins.market;
                output.symbol_market = sndOrd.ins.symbol_market;
                output.internal_order_id = sndOrd.internalOrdId;
                output.side = sndOrd.side;
                output.position_side = sndOrd.pos_side;
                output.order_type = sndOrd.order_type;
                output.order_quantity = quantity;
                output.order_price = sndOrd.price;
                output.create_time = DateTime.UtcNow;
                output.is_trigger_order = true;
                output.last_trade = "";
                if (sndOrd.time_in_force == null)
                {
                    output.time_in_force = timeInForce.GoodTillCanceled;
                }
                else
                {
                    output.time_in_force = (timeInForce)sndOrd.time_in_force;
                }
                output.timestamp = DateTime.UtcNow;
                output.trigger_price = 0;
                output.update_time = DateTime.UtcNow;
                output.msg = sndOrd.msg;
                using (var mlock = this.mapping_lock.getlock())
                {
                    this.ordIdMapping[output.market + output.order_id] = sndOrd.internalOrdId;
                }
                this.virtual_order_queue.Enqueue(output);
                this.crypto_client.ordUpdateQueue.Enqueue(output);
            }
            else if(sndOrd.ins.market == market.gmocoin)
            {
                //quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                DateTime sendTime = DateTime.UtcNow;

                if((sndOrd.side == orderSide.Sell && sndOrd.pos_side == positionSide.Long) || (sndOrd.side == orderSide.Buy && sndOrd.pos_side == positionSide.Short))
                {
                    if (sndOrd.order_type == orderType.Market)
                    {
                        js = await this.crypto_client.gmocoin_client.placeCloseMarketOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToUpper(), 0, quantity);
                    }
                    else
                    {
                        js = await this.crypto_client.gmocoin_client.placeCloseOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToUpper(), sndOrd.price, quantity, "SOK");
                    }
                }
                else if(sndOrd.order_type == orderType.Market)
                {
                    js = await this.crypto_client.gmocoin_client.placeMarketNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToUpper(), 0, quantity);
                }
                else
                {
                    js = await this.crypto_client.gmocoin_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToUpper(), sndOrd.price, quantity, "SOK");
                }
                JsonElement result;
                if(js.RootElement.TryGetProperty("status",out result) && result.GetInt32() == 0)
                {
                    string ord_id = js.RootElement.GetProperty("data").GetString();
                    DateTime resTime = (DateTime)Functions.convertToDateTime(js.RootElement.GetProperty("responsetime").GetString(), sndOrd.ins.market);
                    //DateTime resTime = DateTime.ParseExact(js.RootElement.GetProperty("responsetime").GetString(), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                    output = this.crypto_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.order_id = ord_id;
                    using (var mlock = this.mapping_lock.getlock())
                    {
                        this.ordIdMapping[sndOrd.ins.market + output.order_id] = sndOrd.internalOrdId;
                    }
                    output.timestamp = sendTime;
                    output.symbol = sndOrd.ins.symbol;
                    output.market = sndOrd.ins.market;
                    output.internal_order_id = sndOrd.internalOrdId;
                    output.symbol_market = sndOrd.ins.symbol_market;
                    output.side = sndOrd.side;
                    output.position_side = sndOrd.pos_side;
                    output.order_type = sndOrd.order_type;
                    output.order_price = sndOrd.price;
                    output.order_quantity = quantity;
                    output.filled_quantity = 0;//Even if an executed order is passed, output 0 executed quantity as the execution will be streamed anyway.
                    output.average_price = 0;
                    output.create_time = resTime;
                    output.update_time = output.create_time;
                    output.status = orderStatus.WaitOpen;
                    output.fee = 0;
                    output.fee_asset = "";
                    output.is_trigger_order = true;
                    output.last_trade = "";
                    output.msg = sndOrd.msg;

                    //if (output.position_side == positionSide.Long)
                    //{
                    //    if (output.side == orderSide.Sell)
                    //    {
                    //        sndOrd.ins.longPosition.AddBalance(0, output.order_quantity);
                    //    }
                    //    else
                    //    {
                    //        ex.updateMarginLocked(output.order_quantity * output.order_price);
                    //    }
                    //}
                    //else if (output.position_side == positionSide.Short)
                    //{
                    //    if (output.side == orderSide.Buy)
                    //    {
                    //        sndOrd.ins.shortPosition.AddBalance(0, output.order_quantity);
                    //    }
                    //    else
                    //    {
                    //        ex.updateMarginLocked(output.order_quantity * output.order_price);
                    //    }
                    //}
                    //else
                    //{
                    //    switch (output.side)
                    //    {
                    //        case orderSide.Buy:
                    //            sndOrd.ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                    //            break;
                    //        case orderSide.Sell:
                    //            sndOrd.ins.baseBalance.AddBalance(0, output.order_quantity);
                    //            break;
                    //    }
                    //}
                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    if (output.position_side == positionSide.Long)
                    {
                        if (output.side == orderSide.Sell)
                        {
                            sndOrd.ins.longPosition.AddBalance(0, -output.order_quantity);
                        }
                        else
                        {
                            ex.updateMarginLocked(-output.order_quantity * output.order_price / sndOrd.ins.leverage);
                        }
                    }
                    else if (output.position_side == positionSide.Short)
                    {
                        if (output.side == orderSide.Buy)
                        {
                            sndOrd.ins.shortPosition.AddBalance(0, -output.order_quantity);
                        }
                        else
                        {
                            ex.updateMarginLocked(-output.order_quantity * output.order_price / sndOrd.ins.leverage);
                        }
                    }
                    else
                    {
                        switch (output.side)
                        {
                            case orderSide.Buy:
                                sndOrd.ins.quoteBalance.AddBalance(0, -output.order_price * output.order_quantity);
                                break;
                            case orderSide.Sell:
                                sndOrd.ins.baseBalance.AddBalance(0, -output.order_quantity);
                                break;
                        }
                    }
                    addLog(js.RootElement.GetRawText(), logType.WARNING);
                    JsonElement timeElem;
                    DateTime resTime;
                    if (js.RootElement.TryGetProperty("responsetime",out timeElem))
                    {
                        resTime = (DateTime)Functions.convertToDateTime(js.RootElement.GetProperty("responsetime").GetString(), sndOrd.ins.market);
                        //resTime = DateTime.ParseExact(js.RootElement.GetProperty("responsetime").GetString(), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                    }
                    else
                    {
                        resTime = DateTime.UtcNow;
                    }
                    output = this.crypto_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.order_id = "";
                    using (var mlock = this.mapping_lock.getlock())
                    {
                        this.ordIdMapping[sndOrd.ins.market + output.order_id] = sndOrd.internalOrdId;
                    }
                    output.timestamp = sendTime;
                    output.symbol = sndOrd.ins.symbol;
                    output.market = sndOrd.ins.market;
                    output.internal_order_id = sndOrd.internalOrdId;
                    output.symbol_market = sndOrd.ins.symbol_market;
                    output.side = sndOrd.side;
                    output.position_side = sndOrd.pos_side;
                    output.order_type = sndOrd.order_type;
                    output.order_price = sndOrd.price;
                    output.order_quantity = quantity;
                    output.filled_quantity = 0;//Even if an executed order is passed, output 0 executed quantity as the execution will be streamed anyway.
                    output.average_price = 0;
                    output.create_time = resTime;
                    output.update_time = output.create_time;
                    output.status = orderStatus.INVALID;
                    output.fee = 0;
                    output.fee_asset = "";
                    output.is_trigger_order = true;
                    output.last_trade = "";
                    output.msg = sndOrd.msg;

                    JsonElement js_err;
                    if (js.RootElement.TryGetProperty("messages", out js_err))
                    {
                        string err_code = js_err.GetProperty("message_code").GetString();
                        string err_msg = js_err.GetProperty("message_string").GetString();
                        if (err_code == "ERR-5003")//Request too many
                        {
                            output.err_code = (int)Enums.ordError.RATE_LIMIT_EXCEEDED;
                            this.addLog($"[gmocoin] New order failed. Too many request. Error code:{err_code}   Error message:{err_msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                        }
                        else if (err_code == "ERR-626")
                        {
                            output.err_code = (int)Enums.ordError.SERVER_BUSY;
                            this.addLog($"[gmocoin] New order failed. Server busy. Error code:{err_code}   Error message:{err_msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                        }
                        else if (err_code == "ERR-273")
                        {
                            output.err_code = (int)Enums.ordError.SERVER_BUSY;
                            this.addLog($"[gmocoin] New order failed. Server maybe be temporarily unavailable. Error code:{err_code}   Error message:{err_msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                        }
                        else
                        {
                            this.addLog($"[gmocoin] Unexpected Error.   Error code:{err_code}   Error message:{err_msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                        }
                    }

                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else if (sndOrd.ins.market == market.bitbank)
            {
                quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                DateTime sendTime = DateTime.UtcNow;
                if (sndOrd.order_type == orderType.Limit)
                {
                    js = await this.crypto_client.bitbank_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.order_type.ToString().ToLower(), sndOrd.side.ToString().ToLower(), sndOrd.price, quantity, sndOrd.pos_side.ToString().ToLower(), true);
                }
                else
                {
                    js = await this.crypto_client.bitbank_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.order_type.ToString().ToLower(), sndOrd.side.ToString().ToLower(), sndOrd.price, quantity, sndOrd.pos_side.ToString().ToLower(), false);
                }

                if (js.RootElement.GetProperty("success").GetUInt16() == 1)
                {
                    var ord_obj = js.RootElement.GetProperty("data");
                    output = this.crypto_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.order_id = ord_obj.GetProperty("order_id").GetInt64().ToString();
                    using (var mlock = this.mapping_lock.getlock())
                    {
                        this.ordIdMapping[sndOrd.ins.market + output.order_id] = sndOrd.internalOrdId;
                    }
                    output.timestamp = sendTime;
                    output.symbol = ord_obj.GetProperty("pair").GetString();
                    output.market = sndOrd.ins.market;
                    output.internal_order_id = sndOrd.internalOrdId;
                    output.symbol_market = sndOrd.ins.symbol_market;
                    string str_side = ord_obj.GetProperty("side").GetString();
                    if (str_side == "buy")
                    {
                        output.side = orderSide.Buy;
                    }
                    else if (str_side == "sell")
                    {
                        output.side = orderSide.Sell;
                    }
                    JsonElement js_posside;
                    if (ord_obj.TryGetProperty("position_side", out js_posside))
                    {
                        string? pos_side = js_posside.GetString();
                        if (pos_side != null)
                        {
                            if (pos_side.ToLower() == "long")
                            {
                                output.position_side = positionSide.Long;
                            }
                            else if (pos_side.ToLower() == "short")
                            {
                                output.position_side = positionSide.Short;
                            }
                            else
                            {
                                output.position_side = positionSide.NONE;
                            }
                        }
                    }
                    else
                    {
                        output.position_side = positionSide.NONE;
                    }
                    string str_type = ord_obj.GetProperty("type").GetString();
                    switch (str_type)
                    {
                        case "limit":
                            output.order_type = orderType.Limit;
                            output.order_price = decimal.Parse(ord_obj.GetProperty("price").GetString());
                            break;
                        case "market":
                            output.order_type = orderType.Market;
                            output.order_price = sndOrd.price;
                            break;
                        default:
                            output.order_type = orderType.Other;
                            break;
                    }
                    output.order_quantity = decimal.Parse(ord_obj.GetProperty("start_amount").GetString());
                    output.filled_quantity = 0;//Even if an executed order is passed, output 0 executed quantity as the execution will be streamed anyway.
                    output.average_price = 0;
                    output.create_time = DateTimeOffset.FromUnixTimeMilliseconds(ord_obj.GetProperty("ordered_at").GetInt64()).UtcDateTime;
                    output.update_time = output.create_time;
                    output.status = orderStatus.WaitOpen;
                    output.fee = 0;
                    output.fee_asset = "";
                    output.is_trigger_order = true;
                    output.last_trade = "";
                    output.msg = sndOrd.msg;

                    //if (output.position_side == positionSide.Long)
                    //{
                    //    if (output.side == orderSide.Sell)
                    //    {
                    //        sndOrd.ins.longPosition.AddBalance(0, output.order_quantity);
                    //    }
                    //    else
                    //    {
                    //        ex.updateMarginLocked(output.order_quantity * output.order_price);
                    //    }
                    //}
                    //else if (output.position_side == positionSide.Short)
                    //{
                    //    if (output.side == orderSide.Buy)
                    //    {
                    //        sndOrd.ins.shortPosition.AddBalance(0, output.order_quantity);
                    //    }
                    //    else
                    //    {
                    //        ex.updateMarginLocked(output.order_quantity * output.order_price);
                    //    }
                    //}
                    //else
                    //{
                    //    switch (output.side)
                    //    {
                    //        case orderSide.Buy:
                    //            sndOrd.ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                    //            break;
                    //        case orderSide.Sell:
                    //            sndOrd.ins.baseBalance.AddBalance(0, output.order_quantity);
                    //            break;
                    //    }
                    //}
                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    if (output.position_side == positionSide.Long)
                    {
                        if (output.side == orderSide.Sell)
                        {
                            sndOrd.ins.longPosition.AddBalance(0, - output.order_quantity);
                        }
                        else
                        {
                            ex.updateMarginLocked(- output.order_quantity * output.order_price / sndOrd.ins.leverage);
                        }
                    }
                    else if (output.position_side == positionSide.Short)
                    {
                        if (output.side == orderSide.Buy)
                        {
                            sndOrd.ins.shortPosition.AddBalance(0, - output.order_quantity);
                        }
                        else
                        {
                            ex.updateMarginLocked(- output.order_quantity * output.order_price / sndOrd.ins.leverage);
                        }
                    }
                    else
                    {
                        switch (output.side)
                        {
                            case orderSide.Buy:
                                sndOrd.ins.quoteBalance.AddBalance(0, - output.order_price * output.order_quantity);
                                break;
                            case orderSide.Sell:
                                sndOrd.ins.baseBalance.AddBalance(0, - output.order_quantity);
                                break;
                        }
                    }
                    int code = js.RootElement.GetProperty("data").GetProperty("code").GetInt32();

                    output = this.crypto_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.status = orderStatus.INVALID;
                    output.timestamp = sendTime;
                    output.internal_order_id = sndOrd.internalOrdId;
                    output.side = sndOrd.side;
                    output.symbol = sndOrd.ins.symbol;
                    output.market = sndOrd.ins.market;
                    output.symbol_market = sndOrd.ins.symbol_market;
                    output.order_quantity = sndOrd.quantity;
                    output.order_price = sndOrd.price;
                    output.filled_quantity = 0;
                    output.average_price = 0;
                    output.fee = 0;
                    output.fee_asset = "";
                    output.is_trigger_order = true;
                    output.last_trade = "";
                    output.msg = sndOrd.msg;

                    output.err_code = code;
                    switch (code)
                    {
                        case -1:
                            this.addLog("[bitbank]New order failed. Unexpected error   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10000:
                            this.addLog("[bitbank]New order failed. The URL doesn't exist. Error code: 10000   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            break;
                        case 10001:
                            this.addLog("[bitbank]New order failed. System error, Contact support Error code:10001   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10002:
                            this.addLog("[bitbank]New order failed. Improper Json format. Error code:10002   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            break;
                        case 10003:
                            this.addLog("[bitbank]New order failed.  System error, Contact support Error code:10003   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10005:
                            this.addLog("[bitbank]New order failed.  Timeout error. Error code:10005   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10007:
                            this.addLog("[bitbank]New order failed.  Under maintenance. Error code:10007   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            break;
                        case 10008:
                            this.addLog("[bitbank]New order failed. The system is busy. Error code:10008   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10009:
                            this.addLog("[bitbank]New order failed. Too many request. Error code:10009   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 20035:
                            this.addLog("[bitbank]New order failed. Order has not been sent within the time window. Error code:20035   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            this.refreshHttpClient(market.bitbank);
                            break;
                        case 70010:
                        case 70011:
                        case 70012:
                        case 70013:
                        case 70014:
                        case 70015:
                            this.addLog("[bitbank]New order failed. The system is busy Error code:" + code.ToString() + "   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 80001:
                            this.addLog("[bitbank]New order failed. Operation timed out. code:" + code.ToString() + "   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            this.refreshHttpClient(market.bitbank);
                            break;
                        case 80002:
                            this.addLog("[bitbank]New order failed. The http client is not ready. code:" + code.ToString() + "   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        default:
                            this.addLog("[bitbank]New Order Failed   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            this.addLog(js.RootElement.GetRawText(), Enums.logType.ERROR);
                            break;
                    }

                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else if (sndOrd.ins.market == market.coincheck)
            {

                this.coincheck_sw.Start();
                DateTime sendTime = DateTime.UtcNow;
                if (sndOrd.order_type == orderType.Limit)
                {
                    //quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;

                    if (quantity * sndOrd.price <= 500)
                    {
                        addLog("[coincheck]The order size is too small.", logType.WARNING);
                        output = this.crypto_client.ordUpdateStack.pop();
                        if (output == null)
                        {
                            output = new DataSpotOrderUpdate();
                        }
                        output.status = orderStatus.INVALID;
                        output.timestamp = sendTime;
                        output.internal_order_id = sndOrd.internalOrdId;
                        output.side = sndOrd.side;
                        output.symbol = sndOrd.ins.symbol;
                        output.market = sndOrd.ins.market;
                        output.symbol_market = sndOrd.ins.symbol_market;
                        output.order_quantity = sndOrd.quantity;
                        output.order_price = sndOrd.price;
                        output.filled_quantity = 0;
                        output.average_price = 0;
                        output.fee = 0;
                        output.fee_asset = "";
                        output.is_trigger_order = true;
                        output.last_trade = "";
                        output.msg = sndOrd.msg;
                        this.crypto_client.ordUpdateQueue.Enqueue(output);
                        sndOrd.init();
                        this.sendingOrdersStack.push(sndOrd);
                        return output;
                    }
                    js = await this.crypto_client.coincheck_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToLower(), sndOrd.price, quantity, "post_only");
                }
                else
                {
                    if (sndOrd.side == orderSide.Buy)
                    {
                        if (sndOrd.quantity < sndOrd.ins.quantity_unit * (decimal)1.1)
                        {
                            quantity = sndOrd.ins.quantity_unit * (decimal)1.1;
                        }
                        else
                        {
                            quantity = sndOrd.quantity;
                        }
                        if (sndOrd.price > 0)
                        {
                            quantity = quantity * sndOrd.price;
                        }
                        else
                        {
                            sndOrd.price = sndOrd.ins.bestask.Item1;
                            quantity = quantity * sndOrd.price;
                        }
                        if (quantity <= 500)
                        {
                            addLog("[coincheck]The order size is too small.", logType.WARNING);
                            output = this.crypto_client.ordUpdateStack.pop();
                            if (output == null)
                            {
                                output = new DataSpotOrderUpdate();
                            }
                            output.status = orderStatus.INVALID;
                            output.timestamp = sendTime;
                            output.internal_order_id = sndOrd.internalOrdId;
                            output.side = sndOrd.side;
                            output.symbol = sndOrd.ins.symbol;
                            output.market = sndOrd.ins.market;
                            output.symbol_market = sndOrd.ins.symbol_market;
                            output.order_quantity = sndOrd.quantity;
                            output.order_price = sndOrd.price;
                            output.filled_quantity = 0;
                            output.average_price = 0;
                            output.fee = 0;
                            output.fee_asset = "";
                            output.is_trigger_order = true;
                            output.last_trade = "";
                            output.msg = sndOrd.msg;
                            this.crypto_client.ordUpdateQueue.Enqueue(output);
                            sndOrd.init();
                            this.sendingOrdersStack.push(sndOrd);
                            return output;
                        }
                        js = await this.crypto_client.coincheck_client.placeMarketNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToLower(), 0, quantity);
                    }
                    else
                    {
                        quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                        decimal amount;
                        if (sndOrd.price > 0)
                        {
                            amount = quantity * sndOrd.price;
                        }
                        else
                        {
                            sndOrd.price = sndOrd.ins.bestbid.Item1;
                            amount = quantity * sndOrd.price;
                        }
                        if (amount <= 500)
                        {
                            addLog("[coincheck]The order size is too small.", logType.WARNING);
                            output = this.crypto_client.ordUpdateStack.pop();
                            if (output == null)
                            {
                                output = new DataSpotOrderUpdate();
                            }
                            output.status = orderStatus.INVALID;
                            output.timestamp = sendTime;
                            output.internal_order_id = sndOrd.internalOrdId;
                            output.side = sndOrd.side;
                            output.symbol = sndOrd.ins.symbol;
                            output.market = sndOrd.ins.market;
                            output.symbol_market = sndOrd.ins.symbol_market;
                            output.order_quantity = sndOrd.quantity;
                            output.order_price = sndOrd.price;
                            output.filled_quantity = 0;
                            output.average_price = 0;
                            output.fee = 0;
                            output.fee_asset = "";
                            output.is_trigger_order = true;
                            output.last_trade = "";
                            output.msg = sndOrd.msg;
                            this.crypto_client.ordUpdateQueue.Enqueue(output);
                            sndOrd.init();
                            this.sendingOrdersStack.push(sndOrd);
                            return output;
                        }
                        js = await this.crypto_client.coincheck_client.placeMarketNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToLower(), 0, quantity);
                    }
                }
                if (js.RootElement.GetProperty("success").GetBoolean())
                {
                    JsonElement ord_obj = js.RootElement;
                    string line = JsonSerializer.Serialize(ord_obj);
                    output = this.crypto_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.order_id = ord_obj.GetProperty("id").GetInt64().ToString();
                    using (var mlock = this.mapping_lock.getlock())
                    {
                        this.ordIdMapping[sndOrd.ins.market + output.order_id] = sndOrd.internalOrdId;
                    }
                    output.timestamp = sendTime;
                    output.symbol = ord_obj.GetProperty("pair").GetString();
                    output.market = sndOrd.ins.market;
                    output.symbol_market = sndOrd.ins.symbol_market;
                    output.internal_order_id = sndOrd.internalOrdId;
                    string str_side = ord_obj.GetProperty("order_type").GetString();
                    if (str_side == "buy" || str_side == "market_buy")
                    {
                        output.side = orderSide.Buy;
                    }
                    else if (str_side == "sell" || str_side == "market_sell")
                    {
                        output.side = orderSide.Sell;
                    }
                    if (str_side.StartsWith("market_"))//market order
                    {
                        output.order_type = orderType.Market;
                        if (str_side == "market_buy")
                        {
                            output.order_price = sndOrd.price;
                            output.order_quantity = sndOrd.quantity;
                        }
                        else
                        {
                            output.order_price = sndOrd.price;
                            output.order_quantity = sndOrd.quantity;
                        }
                    }
                    else
                    {
                        output.order_type = orderType.Limit;
                        output.order_price = decimal.Parse(ord_obj.GetProperty("rate").GetString());
                        output.order_quantity = decimal.Parse(ord_obj.GetProperty("amount").GetString());
                    }

                    output.filled_quantity = 0;//Even if an executed order is passed, output 0 executed quantity as the execution will be streamed anyway.
                    output.average_price = 0;
                    output.create_time = DateTime.Parse(ord_obj.GetProperty("created_at").GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
                    output.update_time = output.create_time;

                    output.status = orderStatus.WaitOpen;
                    output.fee = 0;
                    output.fee_asset = "";
                    output.is_trigger_order = true;
                    output.last_trade = "";
                    output.msg = sndOrd.msg;
                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                    output = this.crypto_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.timestamp = DateTime.UtcNow;
                    output.order_id = ord_obj.GetProperty("id").GetInt64().ToString();
                    output.symbol = ord_obj.GetProperty("pair").GetString();
                    output.market = sndOrd.ins.market;
                    output.symbol_market = sndOrd.ins.symbol_market;
                    output.internal_order_id = sndOrd.internalOrdId;
                    str_side = ord_obj.GetProperty("order_type").GetString();
                    if (str_side == "buy" || str_side == "market_buy")
                    {
                        output.side = orderSide.Buy;
                    }
                    else if (str_side == "sell" || str_side == "market_sell")
                    {
                        output.side = orderSide.Sell;
                    }
                    if (str_side.StartsWith("market_"))//market order
                    {
                        output.order_type = orderType.Market;
                        if (str_side == "market_buy")
                        {
                            output.order_price = sndOrd.price;
                            output.order_quantity = sndOrd.quantity;
                        }
                    }
                    else
                    {
                        output.order_type = orderType.Limit;
                        output.order_price = decimal.Parse(ord_obj.GetProperty("rate").GetString());
                        output.order_quantity = decimal.Parse(ord_obj.GetProperty("amount").GetString());
                    }
                    output.filled_quantity = 0;//Even if an executed order is passed, output 0 executed quantity as the execution will be streamed anyway.
                    output.average_price = 0;
                    output.create_time = DateTime.Parse(ord_obj.GetProperty("created_at").GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
                    output.update_time = output.create_time;

                    output.status = orderStatus.Open;
                    output.fee = 0;
                    output.fee_asset = "";
                    output.is_trigger_order = true;
                    output.last_trade = "";
                    //switch (sndOrd.side)
                    //{
                    //    case orderSide.Buy:
                    //        sndOrd.ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                    //        break;
                    //    case orderSide.Sell:
                    //        sndOrd.ins.baseBalance.AddBalance(0, output.order_quantity);
                    //        break;
                    //}
                    output.msg = sndOrd.msg;

                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    switch (sndOrd.side)
                    {
                        case orderSide.Buy:
                            sndOrd.ins.quoteBalance.AddBalance(0, - sndOrd.price * sndOrd.quantity);
                            break;
                        case orderSide.Sell:
                            sndOrd.ins.baseBalance.AddBalance(0, - sndOrd.quantity);
                            break;
                    }
                    output = this.crypto_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.status = orderStatus.INVALID;
                    output.timestamp = sendTime;
                    output.internal_order_id = sndOrd.internalOrdId;
                    output.side = sndOrd.side;
                    output.symbol = sndOrd.ins.symbol;
                    output.market = sndOrd.ins.market;
                    output.symbol_market = sndOrd.ins.symbol_market;
                    output.order_quantity = sndOrd.quantity;
                    output.order_price = sndOrd.price;
                    output.filled_quantity = 0;
                    output.average_price = 0;
                    output.fee = 0;
                    output.fee_asset = "";
                    output.is_trigger_order = true;
                    output.last_trade = "";
                    output.msg = sndOrd.msg;

                    string err = js.RootElement.GetProperty("error").GetString();
                    if (err.StartsWith("Amount"))
                    {
                        this.addLog("[coincheck] New order failed. " + err, Enums.logType.WARNING);
                    }
                    else if (err.StartsWith("Nonce"))//Nonce must be incremented
                    {
                        output.err_code = (int)Enums.ordError.NONCE_ERROR;
                        this.addLog("[coincheck] New order failed. " + err, Enums.logType.WARNING);
                    }
                    else if (err.StartsWith("Rate limit"))
                    {
                        output.err_code = (int)Enums.ordError.RATE_LIMIT_EXCEEDED;
                        this.addLog("[coincheck] New order failed. " + err, Enums.logType.WARNING);
                    }
                    else if (err.StartsWith("The httpclient"))
                    {
                        output.err_code = (int)Enums.ordError.HTTP_NOT_READY;
                        this.addLog("[coincheck] New order failed. " + err, Enums.logType.WARNING);
                    }
                    else
                    {
                        string msg = JsonSerializer.Serialize(js);
                        this.addLog("[coincheck] New order failed. " + msg, Enums.logType.ERROR);
                    }

                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else if (sndOrd.ins.market == market.bittrade)
            {
                quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                decimal order_price = sndOrd.price;
                DateTime sendTime = DateTime.UtcNow;
                if (sndOrd.order_type == orderType.Limit)
                {
                    js = await this.crypto_client.bittrade_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToLower(), sndOrd.price, quantity, true);
                }
                else
                {
                    if (sndOrd.side == orderSide.Buy)
                    {
                        order_price = Math.Round(sndOrd.ins.bestask.Item1 * (decimal)1.05 / sndOrd.ins.price_unit) * sndOrd.ins.price_unit;
                    }
                    else
                    {
                        order_price = Math.Round(sndOrd.ins.bestbid.Item1 * (decimal)0.95 / sndOrd.ins.price_unit) * sndOrd.ins.price_unit;
                    }
                    js = await this.crypto_client.bittrade_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToLower(), order_price, quantity, false);
                }

                if (js.RootElement.GetProperty("status").GetString() == "ok")
                {
                    output = this.crypto_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.order_id = js.RootElement.GetProperty("data").GetString();
                    using (var mlock = this.mapping_lock.getlock())
                    {
                        this.ordIdMapping[sndOrd.ins.market + output.order_id] = sndOrd.internalOrdId;
                    }
                    output.timestamp = sendTime;
                    output.symbol = sndOrd.ins.symbol;
                    output.market = sndOrd.ins.market;
                    output.symbol_market = sndOrd.ins.symbol_market;
                    output.internal_order_id = sndOrd.internalOrdId;
                    output.side = sndOrd.side;
                    output.order_type = sndOrd.order_type;
                    output.order_price = order_price;
                    output.order_quantity = quantity;
                    output.filled_quantity = 0;//Even if an executed order is passed, output 0 executed quantity as the execution will be streamed anyway.
                    output.average_price = 0;
                    output.create_time = DateTime.UtcNow;
                    output.update_time = output.create_time;

                    output.status = orderStatus.WaitOpen;
                    output.fee = 0;
                    output.fee_asset = "";
                    output.is_trigger_order = true;
                    output.last_trade = "";
                    //switch (sndOrd.side)
                    //{
                    //    case orderSide.Buy:
                    //        sndOrd.ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                    //        break;
                    //    case orderSide.Sell:
                    //        sndOrd.ins.baseBalance.AddBalance(0, output.order_quantity);
                    //        break;
                    //}
                    output.msg = sndOrd.msg;
                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    switch (sndOrd.side)
                    {
                        case orderSide.Buy:
                            sndOrd.ins.quoteBalance.AddBalance(0, -sndOrd.price * -sndOrd.quantity);
                            break;
                        case orderSide.Sell:
                            sndOrd.ins.baseBalance.AddBalance(0, -sndOrd.quantity);
                            break;
                    }
                    string msg = JsonSerializer.Serialize(js);
                    this.addLog(msg, Enums.logType.ERROR);
                    output = this.crypto_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.status = orderStatus.INVALID;
                    output.timestamp = sendTime;
                    output.internal_order_id = sndOrd.internalOrdId;
                    output.side = sndOrd.side;
                    output.symbol = sndOrd.ins.symbol;
                    output.market = sndOrd.ins.market;
                    output.symbol_market = sndOrd.ins.symbol_market;
                    output.order_quantity = sndOrd.quantity;
                    output.order_price = sndOrd.price;
                    output.filled_quantity = 0;
                    output.average_price = 0;
                    output.fee = 0;
                    output.fee_asset = "";
                    output.is_trigger_order = true;
                    output.last_trade = "";
                    output.msg = sndOrd.msg;
                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else
            {
                quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                DateTime sendTime = DateTime.UtcNow;
                output = await this.crypto_client.placeNewSpotOrder(sndOrd.ins.market, sndOrd.ins.baseCcy, sndOrd.ins.quoteCcy, sndOrd.side, sndOrd.order_type, quantity, sndOrd.price, sndOrd.time_in_force);
                if (output != null)
                {
                    using (var mlock = this.mapping_lock.getlock())
                    {
                        this.ordIdMapping[sndOrd.ins.market + output.order_id] = sndOrd.internalOrdId;
                    }
                    output.timestamp = sendTime;
                    output.symbol_market = sndOrd.ins.symbol_market;
                    output.internal_order_id = sndOrd.internalOrdId;
                    output.msg = sndOrd.msg;
                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    output = this.crypto_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.status = orderStatus.INVALID;
                    output.timestamp = sendTime;
                    output.internal_order_id = sndOrd.internalOrdId;
                    output.side = sndOrd.side;
                    output.symbol = sndOrd.ins.symbol;
                    output.market = sndOrd.ins.market;
                    output.symbol_market = sndOrd.ins.symbol_market;
                    output.order_quantity = sndOrd.quantity;
                    output.order_price = sndOrd.price;
                    output.filled_quantity = 0;
                    output.average_price = 0;
                    output.fee = 0;
                    output.fee_asset = "";
                    output.is_trigger_order = true;
                    output.last_trade = "";
                    output.msg = sndOrd.msg;
                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
            }
            sndOrd.init();
            //this.sendingOrdersStack.Enqueue(sndOrd);
            this.sendingOrdersStack.push(sndOrd);
            return output;
        }
        async public Task<DataSpotOrderUpdate?> processModOrder(sendingOrder sndOrd)
        {
            DataSpotOrderUpdate ord;
            modifingOrd mod;
            DataSpotOrderUpdate? output = null;
            sendingOrder sndOrd2;

            using (var olock = this.order_lock.getlock())
            {
                if (this.orders.ContainsKey(sndOrd.ref_IntOrdId))
                {
                    ord = this.orders[sndOrd.ref_IntOrdId];
                }
                else
                {
                    addLog("Order not found. order id:" + sndOrd.ref_IntOrdId);
                    sndOrd.init();
                    this.sendingOrdersStack.push(sndOrd);
                    return null;
                }
            }

            if (sndOrd.waitCancel)
            {
                mod = this.modifingOrdStack.pop();
                mod.ordId = ord.order_id;
                mod.newPrice = sndOrd.price;
                mod.newQuantity = sndOrd.quantity;
                mod.side = ord.side;
                mod.order_type = ord.order_type;
                mod.time_in_force = ord.time_in_force;
                mod.ins = sndOrd.ins;
                this.modifingOrders[sndOrd.ref_IntOrdId] = mod;
                output = await this.processCanOrder(sndOrd);

                return output;
            }
            else
            {
                sndOrd.side = ord.side;
                sndOrd.order_type = ord.order_type;
                sndOrd.time_in_force = ord.time_in_force;
                if (ord.status == orderStatus.Open)
                {
                    sndOrd2 = this.sendingOrdersStack.pop();
                    sndOrd2.copy(sndOrd);
                    sndOrd2.msg += " ModOrder from " + sndOrd.ref_IntOrdId + " ";
                    this.processCanOrder(sndOrd);
                    Thread.Sleep(1);
                    output = await this.processNewOrder(sndOrd2);
                }
                else
                {
                    sndOrd.init();
                    this.sendingOrdersStack.push(sndOrd);
                    output = null;
                }
                return output;
            }
        }
        async public Task<DataSpotOrderUpdate?> processCanOrder(sendingOrder sndOrd)
        {
            using var f = new funcContainer(this.Latency["processCanOrder"].MeasureLatency);
            DataSpotOrderUpdate? output = null;
            DataSpotOrderUpdate prev;
            using(var olock = this.order_lock.getlock())
            {
                if (this.orders.ContainsKey(sndOrd.ref_IntOrdId))
                {
                    prev = this.orders[sndOrd.ref_IntOrdId];
                }
                else
                {
                    addLog("Order not found. order id:" + sndOrd.ref_IntOrdId, logType.WARNING);
                    sndOrd.init();
                    this.sendingOrdersStack.push(sndOrd);
                    return null;
                }
            }
            JsonDocument js;
            if (this.virtualMode)
            {
                output = this.crypto_client.ordUpdateStack.pop();
                if (output == null)
                {
                    output = new DataSpotOrderUpdate();
                }
                output.isVirtual = true;
                output.order_id = prev.order_id;
                output.symbol = sndOrd.ins.symbol;
                output.market = sndOrd.ins.market;
                output.symbol_market = sndOrd.ins.symbol_market;
                output.internal_order_id = sndOrd.ref_IntOrdId;
                
                output.status = orderStatus.WaitCancel;
                output.side = prev.side;
                output.position_side = prev.position_side;
                output.order_type = prev.order_type;
                output.order_quantity = prev.order_quantity;
                output.filled_quantity = prev.filled_quantity;
                output.order_price = prev.order_price;
                output.average_price = prev.average_price;
                output.create_time = prev.create_time;
                output.fee = prev.fee;
                output.fee_asset = prev.fee_asset;
                output.is_trigger_order = prev.is_trigger_order;
                output.last_trade = prev.last_trade;
                output.time_in_force = prev.time_in_force;
                output.timestamp = DateTime.UtcNow;
                output.trigger_price = prev.trigger_price;
                output.update_time = DateTime.UtcNow;

                this.virtual_order_queue.Enqueue(output);
                this.crypto_client.ordUpdateQueue.Enqueue(output);
            }
            else if(sndOrd.ins.market == market.gmocoin)
            {
                DateTime sendTime = DateTime.UtcNow;
                js = await this.crypto_client.gmocoin_client.placeCanOrder(prev.order_id);
                JsonElement res;
                if (js.RootElement.TryGetProperty("status", out res) && res.GetInt32() == 0)
                {
                    DateTime resTime = (DateTime)Functions.convertToDateTime(js.RootElement.GetProperty("responsetime").GetString(), sndOrd.ins.market);
                    //DateTime resTime = DateTime.ParseExact(js.RootElement.GetProperty("responsetime").GetString(), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                    output = this.crypto_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.order_id = prev.order_id;
                    output.timestamp = sendTime;
                    output.update_time = resTime;
                    output.order_price = -1;
                    output.order_quantity = 0;
                    output.market = sndOrd.ins.market;
                    output.symbol = sndOrd.ins.symbol;
                    output.internal_order_id = sndOrd.ref_IntOrdId;
                    output.filled_quantity = 0;
                    output.status = orderStatus.WaitCancel;
                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    addLog(js.RootElement.GetRawText(), logType.WARNING);
                    JsonElement js_err;
                    if (js.RootElement.TryGetProperty("messages", out js_err))
                    {
                        string err_code = js_err.GetProperty("message_code").GetString();
                        string err_msg = js_err.GetProperty("message_string").GetString();
                        if (err_code == "ERR-5003")//Request too many
                        {
                            output.err_code = (int)Enums.ordError.RATE_LIMIT_EXCEEDED;
                            this.addLog($"[gmocoin] Cancel order failed. Too many request. Error code:{err_code}   Error message:{err_msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                        }
                        else if (err_code == "ERR-5122")//The order is not alive anymore. ignore
                        {

                        }
                        else if (err_code == "ERR-626")
                        {
                            output.err_code = (int)Enums.ordError.SERVER_BUSY;
                            this.addLog($"[gmocoin] New order failed. Server busy. Error code:{err_code}   Error message:{err_msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                        }
                        else if (err_code == "ERR-273")
                        {
                            output.err_code = (int)Enums.ordError.SERVER_BUSY;
                            this.addLog($"[gmocoin] New order failed. Server maybe be temporarily unavailable. Error code:{err_code}   Error message:{err_msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                        }
                        else
                        {
                            this.addLog($"[gmocoin] Unexpected Error.   Error code:{err_code}   Error message:{err_msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                        }
                    }
                    
                }
            }
            else if (sndOrd.ins.market == market.bitbank)
            {
                DateTime sendTime = DateTime.UtcNow;
                js = await this.crypto_client.bitbank_client.placeCanOrder(sndOrd.ins.symbol, prev.order_id);
                if (js.RootElement.GetProperty("success").GetUInt16() == 1)
                {
                    var ord_obj = js.RootElement.GetProperty("data");
                    output = this.crypto_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.order_id = ord_obj.GetProperty("order_id").GetInt64().ToString();
                    output.timestamp = sendTime;
                    output.order_price = -1;
                    output.order_quantity = 0;
                    output.market = sndOrd.ins.market;
                    output.symbol = sndOrd.ins.symbol;
                    output.internal_order_id = sndOrd.ref_IntOrdId;
                    output.filled_quantity = 0;
                    output.status = orderStatus.WaitCancel;
                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    int code = js.RootElement.GetProperty("data").GetProperty("code").GetInt32();
                    switch (code)
                    {
                        case -1:
                            this.addLog("[bitbank]Cancel order failed. Unexpected error   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10000:
                            this.addLog("Cancel order failed. The URL doesn't exist. Error code: 10000   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            break;
                        case 10001:
                            this.addLog("[bitbank] Cancel order failed. System error, Contact support Error code:10001   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10002:
                            this.addLog("[bitbank] Cancel order failed. Improper Json format. Error code:10002   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            break;
                        case 10003:
                            this.addLog("[bitbank] Cancel order failed.  System error, Contact support Error code:10003   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10005:
                            this.addLog("[bitbank] Cancel order failed.  Timeout error. Error code:10005   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10007:
                            this.addLog("[bitbank] Cancel order failed.  Under maintenance. Error code:10007   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            break;
                        case 10008:
                            this.addLog("[bitbank] Cancel order failed. The system is busy. Error code:10008   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10009:
                            this.addLog("[bitbank] Cancel order failed. Too many request. Error code:10009   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 50026://Already Canceled
                        case 50027://Already filled
                            break;
                        case 70010:
                        case 70011:
                        case 70012:
                        case 70013:
                        case 70014:
                        case 70015:
                            this.addLog("[bitbank] Cancel order failed. The system is busy Error code:   ord_id:" + sndOrd.internalOrdId + code.ToString(), Enums.logType.WARNING);
                            break;
                        case 80001:
                            this.addLog("[bitbank] Cancel order failed. Operation timed out. code:" + code.ToString() + "   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            this.refreshHttpClient(market.bitbank);
                            //output.err_code = code;
                            break;
                        case 80002:
                            this.addLog("[bitbank] Cancel order failed. The http client is not ready. code:" + code.ToString() + "   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            //output.err_code = code;
                            break;
                        default:
                            this.addLog("[bitbank] Cancel Order Failed   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            this.addLog(js.RootElement.GetRawText(), Enums.logType.ERROR);
                            break;
                    }
                }
            }
            else if (sndOrd.ins.market == market.coincheck)
            {
                DateTime sendTime = DateTime.UtcNow;
                js = await this.crypto_client.coincheck_client.placeCanOrder(prev.order_id);
                if (js.RootElement.GetProperty("success").GetBoolean())
                {
                    var ord_obj = js.RootElement;
                    output = this.crypto_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.order_id = ord_obj.GetProperty("id").GetInt64().ToString();
                    output.timestamp = sendTime;
                    output.order_price = -1;
                    output.order_quantity = 0;
                    output.market = sndOrd.ins.market;
                    output.symbol = sndOrd.ins.symbol;
                    output.internal_order_id = sndOrd.ref_IntOrdId;
                    output.filled_quantity = 0;
                    output.status = orderStatus.WaitCancel;
                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    string err = js.RootElement.GetProperty("error").GetString();
                    if (err.StartsWith("Amount"))
                    {
                        this.addLog("[coincheck] Cancel order failed. " + err, Enums.logType.WARNING);
                    }
                    else if (err.StartsWith("Nonce"))//Nonce must be incremented
                    {
                        output.err_code = (int)Enums.ordError.NONCE_ERROR;
                        this.addLog("[coincheck] Cancel order failed. " + err, Enums.logType.WARNING);
                    }
                    else if (err.StartsWith("Rate limit"))
                    {
                        output.err_code = (int)Enums.ordError.RATE_LIMIT_EXCEEDED;
                        this.addLog("[coincheck] Cancel order failed. " + err, Enums.logType.WARNING);
                    }
                    else if (err.StartsWith("The httpclient"))
                    {
                        output.err_code = (int)Enums.ordError.HTTP_NOT_READY;
                        this.addLog("[coincheck] Cancel order failed. " + err, Enums.logType.WARNING);
                    }
                    else
                    {
                        //string msg = JsonSerializer.Serialize(js);
                        //this.addLog(msg, Enums.logType.ERROR);
                    }
                }
            }
            else if (sndOrd.ins.market == market.bittrade)
            {
                DateTime sendTime = DateTime.UtcNow;
                js = await this.crypto_client.bittrade_client.placeCanOrder(prev.order_id);
                if (js.RootElement.GetProperty("status").GetString() == "ok")
                {
                    output = this.crypto_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.order_id = js.RootElement.GetProperty("data").GetString();
                    output.timestamp = sendTime;
                    output.order_price = -1;
                    output.order_quantity = 0;
                    output.market = sndOrd.ins.market;
                    output.symbol = sndOrd.ins.symbol;
                    output.internal_order_id = sndOrd.ref_IntOrdId;
                    output.filled_quantity = 0;
                    output.status = orderStatus.WaitCancel;
                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else
            {
                output = await this.crypto_client.placeCancelSpotOrder(sndOrd.ins.market, sndOrd.ins.baseCcy, sndOrd.ins.quoteCcy, prev.order_id);
                if (output != null)
                {
                    output.symbol_market = sndOrd.ins.symbol_market;
                    output.internal_order_id = sndOrd.ref_IntOrdId;
                    this.crypto_client.ordUpdateQueue.Enqueue(output);
                }
            }
            sndOrd.init();
            this.sendingOrdersStack.push(sndOrd);
            return output;
        }

        async public Task<List<DataSpotOrderUpdate>> processCanOrders(sendingOrder sndOrd)
        {
            using var f = new funcContainer(this.Latency["processCanOrders"].MeasureLatency);
            List<DataSpotOrderUpdate> output = new List<DataSpotOrderUpdate>();
            DataSpotOrderUpdate? ordObj = null;
            List<JsonDocument> js_list;
            JsonDocument js;
            if (this.virtualMode)
            {
                foreach (var ordid in sndOrd.order_ids)
                {
                    ordObj = this.crypto_client.ordUpdateStack.pop();
                    if (ordObj == null)
                    {
                        ordObj = new DataSpotOrderUpdate();
                    }
                    DataSpotOrderUpdate prev;
                    using (var olock = this.order_lock.getlock())
                    {
                        if(this.orders.ContainsKey(ordid))
                        {
                            prev = this.orders[ordid];
                        }
                        else
                        {
                            addLog("[processCanOrders]Order not found. order id:" + ordid, logType.WARNING);
                            continue;
                        }
                    }

                    ordObj.isVirtual = true;
                    ordObj.order_id = prev.order_id;
                    ordObj.symbol = sndOrd.ins.symbol;
                    ordObj.market = sndOrd.ins.market;
                    ordObj.symbol_market = sndOrd.ins.symbol_market;
                    ordObj.internal_order_id = ordid;
                    ordObj.status = orderStatus.WaitCancel;
                    ordObj.side = prev.side;
                    ordObj.position_side = prev.position_side;
                    ordObj.order_type = prev.order_type;
                    ordObj.order_quantity = prev.order_quantity;
                    ordObj.filled_quantity = prev.filled_quantity;
                    ordObj.order_price = prev.order_price;
                    ordObj.average_price = prev.average_price;
                    ordObj.create_time = prev.create_time;
                    ordObj.fee = prev.fee;
                    ordObj.fee_asset = prev.fee_asset;
                    ordObj.is_trigger_order = prev.is_trigger_order;
                    ordObj.last_trade = prev.last_trade;
                    ordObj.time_in_force = prev.time_in_force;
                    ordObj.timestamp = DateTime.UtcNow;
                    ordObj.trigger_price = prev.trigger_price;
                    ordObj.update_time = DateTime.UtcNow;

                    this.virtual_order_queue.Enqueue(ordObj);
                    this.crypto_client.ordUpdateQueue.Enqueue(ordObj);
                }
            }
            else if (sndOrd.ins.market == market.gmocoin)
            {
                DateTime sendTime = DateTime.UtcNow;
                List<string> ord_ids = new List<string>();
                using(var olock = this.order_lock.getlock())
                {
                    foreach (string order_id in sndOrd.order_ids)
                    {
                        if (this.orders.ContainsKey(order_id) && (this.orders[order_id].status == orderStatus.Open || this.orders[order_id].status == orderStatus.WaitOpen))
                        {
                            ord_ids.Add(this.orders[order_id].order_id);
                        }
                    }
                }
                
                js = await this.crypto_client.gmocoin_client.placeCanOrders(ord_ids);
                JsonElement res;
                if (js.RootElement.TryGetProperty("status", out res) && res.GetInt32() == 0)
                {
                    JsonElement js_data;
                    if (js.RootElement.TryGetProperty("data", out js_data))
                    {
                        JsonElement successes;
                        JsonElement fails;
                        if(js_data.TryGetProperty("success",out successes))
                        {
                            foreach (var elem in successes.EnumerateArray())
                            {
                                ordObj = this.crypto_client.ordUpdateStack.pop();
                                if (ordObj == null)
                                {
                                    ordObj = new DataSpotOrderUpdate();
                                }
                                ordObj.order_id = elem.GetInt64().ToString();
                                ordObj.timestamp = sendTime;
                                ordObj.order_price = -1;
                                ordObj.order_quantity = 0;
                                ordObj.market = sndOrd.ins.market;
                                ordObj.symbol = sndOrd.ins.symbol;
                                ordObj.symbol_market = sndOrd.ins.symbol_market;
                                using (var mlock = this.mapping_lock.getlock())
                                {
                                    ordObj.internal_order_id = this.ordIdMapping[ordObj.market + ordObj.order_id];
                                }
                                ordObj.filled_quantity = 0;
                                ordObj.status = orderStatus.WaitCancel;
                                this.crypto_client.ordUpdateQueue.Enqueue(ordObj);
                                output.Add(ordObj);
                            }
                        }
                        if(js_data.TryGetProperty("failed",out fails))
                        {
                            foreach (var elem in fails.EnumerateArray())
                            {
                                string code = elem.GetProperty("message_code").GetString();
                                string msg = elem.GetProperty("message_string").GetString();
                                if (code == "ERR-5003")//Request too many
                                {
                                    this.addLog($"[gmocoin] Cancel order failed. Too many request. Error code:{code}   ord_id:{sndOrd.internalOrdId}   msg:{msg}", Enums.logType.WARNING);
                                }
                                else if (code == "ERR-5122")//The order is not alive anymore. ignore
                                {

                                }
                                else if (code == "ERR-626")
                                {
                                    this.addLog($"[gmocoin] New order failed. Server busy. Error code:{code}   Error message:{msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                                }
                                else if (code == "ERR-273")
                                {
                                    this.addLog($"[gmocoin] New order failed. Server maybe be temporarily unavailable. Error code:{code}   Error message:{msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                                }
                                else
                                {
                                    this.addLog($"[gmocoin] Unexpected Error. Error code:{code}   ord_id:{sndOrd.internalOrdId}   msg:{msg}", Enums.logType.ERROR);
                                }
                            }
                        }
                    }
                }
                else
                {
                    addLog(js.RootElement.GetRawText(), logType.WARNING);
                    JsonElement js_err;
                    if (js.RootElement.TryGetProperty("messages", out js_err))
                    {
                        string err_code = js_err.GetProperty("message_code").GetString();
                        string err_msg = js_err.GetProperty("message_string").GetString();
                        if (err_code == "ERR-5003")//Request too many
                        {
                            this.addLog($"[gmocoin] Cancel order failed. Too many request. Error code:{err_code}   Error message:{err_msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                        }
                        else if (err_code == "ERR-5122")//The order is not alive anymore. ignore
                        {

                        }
                        else if (err_code == "ERR-626")
                        {
                            this.addLog($"[gmocoin] New order failed. Server busy. Error code:{err_code}   Error message:{err_msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                        }
                        else if (err_code == "ERR-273")
                        {
                            this.addLog($"[gmocoin] New order failed. Server maybe be temporarily unavailable. Error code:{err_code}   Error message:{err_msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                        }
                        else
                        {
                            this.addLog($"[gmocoin] Unexpected Error.   Error code:{err_code}   Error message:{err_msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                        }
                    }
                    
                    JsonElement js_data;
                    if(js.RootElement.TryGetProperty("data",out js_data))
                    {
                        JsonElement successes;
                        JsonElement fails;
                        if (js_data.TryGetProperty("success", out successes))
                        {
                            foreach (var elem in successes.EnumerateArray())
                            {
                                ordObj = this.crypto_client.ordUpdateStack.pop();
                                if (ordObj == null)
                                {
                                    ordObj = new DataSpotOrderUpdate();
                                }
                                ordObj.order_id = elem.GetInt64().ToString();
                                ordObj.timestamp = sendTime;
                                ordObj.order_price = -1;
                                ordObj.order_quantity = 0;
                                ordObj.market = sndOrd.ins.market;
                                ordObj.symbol = sndOrd.ins.symbol;
                                ordObj.symbol_market = sndOrd.ins.symbol_market;
                                using (var mlock = this.mapping_lock.getlock())
                                {
                                    ordObj.internal_order_id = this.ordIdMapping[ordObj.market + ordObj.order_id];
                                }
                                ordObj.filled_quantity = 0;
                                ordObj.status = orderStatus.WaitCancel;
                                this.crypto_client.ordUpdateQueue.Enqueue(ordObj);
                                output.Add(ordObj);
                            }
                        }
                        if (js_data.TryGetProperty("failed", out fails))
                        {
                            foreach (var elem in fails.EnumerateArray())
                            {
                                string code = elem.GetProperty("message_code").GetString();
                                string msg = elem.GetProperty("message_string").GetString();
                                if (code == "ERR-5003")//Request too many
                                {
                                    this.addLog($"[gmocoin] Cancel order failed. Too many request. Error code:{code}   ord_id:{sndOrd.internalOrdId}   msg:{msg}", Enums.logType.WARNING);
                                }
                                else if (code == "ERR-5122")//The order is not alive anymore. ignore
                                {

                                }
                                else if (code == "ERR-626")
                                {
                                    ordObj.err_code = (int)Enums.ordError.SERVER_BUSY;
                                    this.addLog($"[gmocoin] New order failed. Server busy. Error code:{code}   Error message:{msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                                }
                                else if (code == "ERR-273")
                                {
                                    ordObj.err_code = (int)Enums.ordError.SERVER_BUSY;
                                    this.addLog($"[gmocoin] New order failed. Server maybe be temporarily unavailable. Error code:{code}   Error message:{msg}   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                                }
                                else
                                {
                                    this.addLog($"[gmocoin] Unexpected Error. Error code:{code}   ord_id:{sndOrd.internalOrdId}   msg:{msg}", Enums.logType.ERROR);
                                }
                            }
                        }
                    }
                }
            }
            else if (sndOrd.ins.market == market.bitbank)
            {
                DateTime sendTime = DateTime.UtcNow;
                List<string> ord_ids = new List<string>();
                using (var olock = this.order_lock.getlock())
                {
                    foreach (string order_id in sndOrd.order_ids)
                    {
                        if (this.orders.ContainsKey(order_id) && (this.orders[order_id].status == orderStatus.Open || this.orders[order_id].status == orderStatus.WaitOpen))
                        {
                            ord_ids.Add(this.orders[order_id].order_id);
                        }
                    }
                }
                    
                js_list = await this.crypto_client.bitbank_client.placeCanOrders(sndOrd.ins.symbol, ord_ids);
                foreach (var elem in js_list)
                {
                    if (elem.RootElement.GetProperty("success").GetUInt16() == 1)
                    {
                        var ord_objs = elem.RootElement.GetProperty("data").GetProperty("orders").EnumerateArray();
                        foreach(var ord_obj in ord_objs)
                        {
                            ordObj = this.crypto_client.ordUpdateStack.pop();
                            if (ordObj == null)
                            {
                                ordObj = new DataSpotOrderUpdate();
                            }
                            ordObj.order_id = ord_obj.GetProperty("order_id").GetInt64().ToString();
                            ordObj.timestamp = sendTime;
                            ordObj.order_price = -1;
                            ordObj.order_quantity = 0;
                            ordObj.market = sndOrd.ins.market;
                            ordObj.symbol = sndOrd.ins.symbol;
                            ordObj.symbol_market = sndOrd.ins.symbol_market;
                            using (var mlock = this.mapping_lock.getlock())
                            {
                                ordObj.internal_order_id = this.ordIdMapping[ordObj.market + ordObj.order_id];
                            }
                            ordObj.filled_quantity = 0;
                            ordObj.status = orderStatus.WaitCancel;
                            this.crypto_client.ordUpdateQueue.Enqueue(ordObj);
                            output.Add(ordObj);
                        }
                        
                    }
                    else
                    {
                        int code = elem.RootElement.GetProperty("data").GetProperty("code").GetInt32();
                        switch (code)
                        {
                            case -1:
                                this.addLog("[bitbank]Cancel order failed. Unexpected error", Enums.logType.WARNING);
                                ordObj = this.crypto_client.ordUpdateStack.pop();
                                if (ordObj == null)
                                {
                                    ordObj = new DataSpotOrderUpdate();
                                }
                                ordObj.status = orderStatus.CancelFailed;
                                ordObj.timestamp = sendTime;
                                ordObj.internal_order_id = "";
                                ordObj.side = sndOrd.side;
                                ordObj.symbol = sndOrd.ins.symbol;
                                ordObj.market = sndOrd.ins.market;
                                ordObj.symbol_market = sndOrd.ins.symbol_market;
                                ordObj.order_quantity = sndOrd.quantity;
                                ordObj.order_price = sndOrd.price;
                                ordObj.filled_quantity = 0;
                                ordObj.average_price = 0;
                                ordObj.fee = 0;
                                ordObj.fee_asset = "";
                                ordObj.is_trigger_order = true;
                                ordObj.last_trade = "";
                                ordObj.msg = sndOrd.msg;
                                this.crypto_client.ordUpdateQueue.Enqueue(ordObj);
                                break;
                            case 10000:
                                this.addLog("[bitbank]Cancel order failed. The URL doesn't exist. Error code: 10000   orderCount:" + ord_ids.Count.ToString(), Enums.logType.ERROR);
                                break;
                            case 10001:
                                this.addLog("[bitbank]Cancel order failed. System error, Contact support Error code:10001   orderCount:" + ord_ids.Count.ToString(), Enums.logType.WARNING);
                                break;
                            case 10002:
                                this.addLog("[bitbank]Cancel order failed. Improper Json format. Error code:10002   orderCount:" + ord_ids.Count.ToString(), Enums.logType.ERROR);
                                break;
                            case 10003:
                                this.addLog("[bitbank]Cancel order failed.  System error, Contact support Error code:10003   orderCount:" + ord_ids.Count.ToString(), Enums.logType.WARNING);
                                break;
                            case 10005:
                                this.addLog("[bitbank]Cancel order failed.  Timeout error. Error code:10005   orderCount:" + ord_ids.Count.ToString(), Enums.logType.WARNING);
                                break;
                            case 10007:
                                this.addLog("[bitbank]Cancel order failed.  Under maintenance. Error code:10007   orderCount:" + ord_ids.Count.ToString(), Enums.logType.ERROR);
                                break;
                            case 10008:
                                this.addLog("[bitbank]Cancel order failed. The system is busy. Error code:10008   orderCount:" + ord_ids.Count.ToString(), Enums.logType.WARNING);
                                break;
                            case 10009:
                                this.addLog("[bitbank]Cancel order failed. Too many request. Error code:10009   orderCount:" + ord_ids.Count.ToString(), Enums.logType.WARNING);
                                break;
                            case 20035:
                                this.addLog("[bitbank]Cancel order failed. Order has not been sent within the time window.", Enums.logType.WARNING);
                                ordObj = this.crypto_client.ordUpdateStack.pop();
                                if (ordObj == null)
                                {
                                    ordObj = new DataSpotOrderUpdate();
                                }
                                ordObj.status = orderStatus.CancelFailed;
                                ordObj.timestamp = sendTime;
                                ordObj.internal_order_id = "";
                                ordObj.side = sndOrd.side;
                                ordObj.symbol = sndOrd.ins.symbol;
                                ordObj.market = sndOrd.ins.market;
                                ordObj.symbol_market = sndOrd.ins.symbol_market;
                                ordObj.order_quantity = sndOrd.quantity;
                                ordObj.order_price = sndOrd.price;
                                ordObj.filled_quantity = 0;
                                ordObj.average_price = 0;
                                ordObj.fee = 0;
                                ordObj.fee_asset = "";
                                ordObj.is_trigger_order = true;
                                ordObj.last_trade = "";
                                ordObj.msg = sndOrd.msg;
                                this.crypto_client.ordUpdateQueue.Enqueue(ordObj);
                                break;
                            case 50026://Already Canceled
                            case 50027://Already filled
                                break;
                            case 70010:
                            case 70011:
                            case 70012:
                            case 70013:
                            case 70014:
                            case 70015:
                                this.addLog("[bitbank]Cancel order failed. The system is busy Error code:" + code.ToString() + "   orderCount:" + ord_ids.Count.ToString(), Enums.logType.WARNING);
                                break;
                            case 80001:
                                this.addLog("[bitbank]Cancel order failed. Operation timed out. code:" + code.ToString() + "   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                                this.refreshHttpClient(market.bitbank);
                                //output.err_code = code;
                                break;
                            case 80002:
                                this.addLog("[bitbank]Cancel order failed. The http client is not ready. code:" + code.ToString() + "   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                                //output.err_code = code;
                                break;
                            default:
                                this.addLog("[bitbank]Cancel Order Failed   orderCount:" + ord_ids.Count.ToString(), Enums.logType.ERROR);
                                this.addLog(elem.RootElement.GetRawText(), Enums.logType.ERROR);
                                break;
                        }

                    }
                }
                    
            }
            else if (sndOrd.ins.market == market.coincheck)
            {
                DateTime sendTime = DateTime.UtcNow;
                List<string> ord_ids = new List<string>();
                using(var olock = this.order_lock.getlock())
                {
                    foreach (string order_id in sndOrd.order_ids)
                    {
                        if (this.orders.ContainsKey(order_id) && (this.orders[order_id].status == orderStatus.Open || this.orders[order_id].status == orderStatus.WaitOpen))
                        {
                            ord_ids.Add(this.orders[order_id].order_id);
                        }
                    }
                }
                
                js_list = await this.crypto_client.coincheck_client.placeCanOrders(ord_ids);
                foreach (var elem in js_list)
                {
                    if (elem.RootElement.GetProperty("success").GetBoolean())
                    {
                        var ord_obj = elem.RootElement;
                        ordObj = this.crypto_client.ordUpdateStack.pop();
                        if (ordObj == null)
                        {
                            ordObj = new DataSpotOrderUpdate();
                        }
                        ordObj.order_id = ord_obj.GetProperty("id").GetInt64().ToString();
                        ordObj.timestamp = sendTime;
                        ordObj.order_price = -1;
                        ordObj.order_quantity = 0;
                        ordObj.market = sndOrd.ins.market;
                        ordObj.symbol = sndOrd.ins.symbol;
                        using (var mlock = this.mapping_lock.getlock())
                        {
                            ordObj.internal_order_id = this.ordIdMapping[ordObj.market + ordObj.order_id];
                        }
                        ordObj.filled_quantity = 0;
                        ordObj.status = orderStatus.WaitCancel;
                        output.Add(ordObj);
                        this.crypto_client.ordUpdateQueue.Enqueue(ordObj);
                    }
                    else
                    {
                        string err = elem.RootElement.GetProperty("error").GetString();
                        if (err.StartsWith("Amount"))
                        {
                            this.addLog("[coincheck] Cancel order failed. " + err, Enums.logType.WARNING);
                        }
                        else if (err.StartsWith("Nonce"))//Nonce must be incremented
                        {
                            this.addLog("[coincheck] Cancel order failed. " + err, Enums.logType.WARNING);
                        }
                        else if (err.StartsWith("Rate limit"))
                        {
                            this.addLog("[coincheck] Cancel order failed. " + err, Enums.logType.WARNING);
                        }
                        else if (err.StartsWith("The httpclient"))
                        {
                            this.addLog("[coincheck] Cancel order failed. " + err, Enums.logType.WARNING);
                        }
                        else
                        {
                            //string msg = JsonSerializer.Serialize(js);
                            //this.addLog(msg, Enums.logType.ERROR);
                        }
                    }
                }
                
                
            }
            else if (sndOrd.ins.market == market.bittrade)
            {
                DateTime sendTime = DateTime.UtcNow;
                List<string> ord_ids = new List<string>();
                using(var olock = this.order_lock.getlock())
                {
                    foreach (string order_id in sndOrd.order_ids)
                    {
                        if (this.orders.ContainsKey(order_id) && (this.orders[order_id].status == orderStatus.Open || this.orders[order_id].status == orderStatus.WaitOpen))
                        {
                            ord_ids.Add(this.orders[order_id].order_id);
                        }
                    }
                }
                    
                js_list = await this.crypto_client.bittrade_client.placeCanOrders(ord_ids);
                foreach(var elem in js_list)
                {
                    if (elem.RootElement.GetProperty("status").GetString() == "ok")
                    {
                        ordObj = this.crypto_client.ordUpdateStack.pop();
                        if (ordObj == null)
                        {
                            ordObj = new DataSpotOrderUpdate();
                        }
                        ordObj.order_id = elem.RootElement.GetProperty("data").GetString();
                        ordObj.timestamp = sendTime;
                        ordObj.order_price = -1;
                        ordObj.order_quantity = 0;
                        ordObj.market = sndOrd.ins.market;
                        ordObj.symbol = sndOrd.ins.symbol;
                        using (var mlock = this.mapping_lock.getlock())
                        {
                            ordObj.internal_order_id = this.ordIdMapping[ordObj.market + ordObj.order_id];
                        }
                        ordObj.filled_quantity = 0;
                        ordObj.status = orderStatus.WaitCancel;
                        output.Add(ordObj);
                        this.crypto_client.ordUpdateQueue.Enqueue(ordObj);
                    }
                }
                
            }
            else
            {
                DateTime sendTime = DateTime.UtcNow;
                List<string> ord_ids = new List<string>();
                foreach (string order_id in sndOrd.order_ids)
                {
                    string ordid;
                    using (var olock = this.order_lock.getlock())
                    {
                        if (this.orders.ContainsKey(order_id))
                        {
                            ordid = this.orders[order_id].order_id;
                        }
                        else
                        {
                            addLog("[processCanOrders]Order not found. order id:" + order_id, logType.WARNING);
                            continue;
                        }
                    }
                    ordObj = await this.crypto_client.placeCancelSpotOrder(sndOrd.ins.market, sndOrd.ins.baseCcy, sndOrd.ins.quoteCcy, ordid);
                    if (ordObj != null)
                    {
                        ordObj.symbol_market = sndOrd.ins.symbol_market;
                        ordObj.internal_order_id = order_id;
                        output.Add(ordObj);
                        this.crypto_client.ordUpdateQueue.Enqueue(ordObj);
                    }
                }
            }
            sndOrd.init();
            this.sendingOrdersStack.push(sndOrd);
            return output;
        }
        public void processingOrders(CancellationToken cancellationToken)
        {
            int i = 0;
            sendingOrder ord;
            while (true)
            {
                ord = this.sendingOrders.Dequeue();
                if(ord != null)
                {
                    switch (ord.action)
                    {
                        case orderAction.New:
                            this.processNewOrder(ord);
                            break;
                        case orderAction.Mod:
                            this.processModOrder(ord);
                            break;
                        case orderAction.Can:
                            this.processCanOrder(ord);
                            break;
                    }
                    i = 0;
                }
                else
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    ++i;
                    if (i > 1000)
                    {
                        i = 0;
                        Thread.Sleep(0);
                    }
                }
            }
        }
        public async Task refreshAndCancelAllorders(Enums.market market = market.NONE)
        {
            if (market == market.NONE)
            {
                this.addLog("Cancelling all the orders including unknown.", Enums.logType.WARNING);
                //await this.oManager.cancelAllOrders();
                //Thread.Sleep(1000);
                this.addLog("Requesting order list....", Enums.logType.WARNING);

                foreach (market mkt in this.connections.Keys)
                {
                    addLog("Order List of " + mkt, logType.WARNING);
                    List<DataSpotOrderUpdate> ordList = await this.crypto_client.getActiveOrders(mkt);
                    int i = 0;
                    while (ordList == null)
                    {
                        ++i;
                        addLog("Failed to get active orders. Retrying..." + i.ToString(), Enums.logType.WARNING);
                        this.refreshHttpClient(mkt);
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
                    using (var mlock = this.mapping_lock.getlock())
                    {
                        foreach (DataSpotOrderUpdate ord in ordList)
                        {

                            if (this.ordIdMapping.ContainsKey(ord.market + ord.order_id))
                            {
                                ord.internal_order_id = this.ordIdMapping[ord.market + ord.order_id];
                            }
                            else
                            {
                                ord.internal_order_id = ord.market + ord.order_id;
                                this.ordIdMapping[ord.market + ord.order_id] = ord.market + ord.order_id;
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
                            using (var olock = this.order_lock.getlock())
                            {
                                this.orders[ord.internal_order_id] = ord;
                            }
                        }
                    }

                    if (id_list.Count > 0)
                    {
                        foreach (var keyValue in id_list)
                        {
                            Thread.Sleep(1000);//Make sure the cancel orders are executed
                            this.addLog("Cancelling...", Enums.logType.WARNING);
                            await this.placeCancelSpotOrders(keyValue.Key, keyValue.Value, true, true);
                        }
                        this.addLog("Order cancelled.", Enums.logType.WARNING);
                        Thread.Sleep(1000);
                        this.addLog("Double check the orders...", Enums.logType.WARNING);
                        ordList = await this.crypto_client.getActiveOrders(mkt);
                        if (ordList.Count > 0)
                        {
                            addLog("Failed to cancel all the orders. Remaining active orders:" + ordList.Count.ToString("N0"), Enums.logType.WARNING);

                        }
                        else
                        {
                            addLog("All orders cancelled.", Enums.logType.WARNING);
                        }
                    }
                    else
                    {
                        addLog("Active order not found");
                    }
                }
                Thread.Sleep(1000);
                using (var olock = this.order_lock.getlock())
                {
                    this.live_orders.Clear();
                    foreach (Instrument ins in this.instruments.Values)
                    {
                        ins.resetInusePosition();
                    }
                }
            }
            else
            {
                this.addLog("Cancelling all the orders of " + market + " including unknown.", Enums.logType.WARNING);
                //await this.oManager.cancelAllOrders();
                Thread.Sleep(1000);
                this.addLog("Requesting order list....", Enums.logType.WARNING);
                this.addLog("ordUpdateStack.Count:" + this.crypto_client.ordUpdateStack.Count.ToString("N0"));
                this.addLog("order_pool.Count:" + this.order_pool.Count.ToString("N0"));
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
                using (var mlock = this.mapping_lock.getlock())
                {
                    foreach (DataSpotOrderUpdate ord in ordList)
                    {
                        if (this.ordIdMapping.ContainsKey(ord.market + ord.order_id))
                        {
                            ord.internal_order_id = this.ordIdMapping[ord.market + ord.order_id];
                        }
                        else
                        {
                            ord.internal_order_id = ord.market + ord.order_id;
                            this.ordIdMapping[ord.market + ord.order_id] = ord.market + ord.order_id;
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
                        using (var olock = this.order_lock.getlock())
                        {
                            this.orders[ord.internal_order_id] = ord;
                        }
                    }
                }
                if (id_list.Count > 0)
                {
                    foreach (var keyValue in id_list)
                    {
                        Thread.Sleep(1000);//Make sure the cancel orders are executed
                        this.addLog("Cancelling...", Enums.logType.WARNING);
                        await this.placeCancelSpotOrders(keyValue.Key, keyValue.Value, true, true);
                    }
                    this.addLog("Order cancelled.", Enums.logType.WARNING);
                }
                else
                {
                    addLog("Active order not found");
                }
                Thread.Sleep(1000);
                using (var olock = this.order_lock.getlock())
                {
                    List<string> removing = new List<string>();
                    foreach (var ord in this.live_orders)
                    {
                        if (ord.Value.market == market)
                        {
                            removing.Add(ord.Key);
                        }
                    }
                    foreach (var id in removing)
                    {
                        this.live_orders.Remove(id);
                    }
                    foreach (Instrument ins in this.instruments.Values)
                    {
                        if (ins.market == market)
                        {
                            ins.resetInusePosition();
                        }
                    }
                }
            }
        }
        public async Task cancelAllOrders()
        {
            this.addLog("Cancelling all orders...");
            Instrument ins;
            Dictionary<Instrument, List<string>> order_list = new Dictionary<Instrument, List<string>>();
            using (var olock = this.order_lock.getlock())
            {
                foreach (var ord in this.live_orders.Values)
                {
                    if (this.instruments.ContainsKey(ord.symbol_market))
                    {
                        ins = this.instruments[ord.symbol_market];
                        if (!order_list.ContainsKey(ins))
                        {
                            order_list[ins] = new List<string>();
                        }
                        order_list[ins].Add(ord.internal_order_id);
                        //this.placeCancelSpotOrder(ins, ord.internal_order_id, true);
                    }
                    else
                    {
                        addLog("Unknown symbol.", logType.WARNING);
                        addLog(ord.ToString(), logType.WARNING);
                    }
                }
            }
            
            foreach(var list in order_list)
            {
                await this.placeCancelSpotOrders(list.Key, list.Value,true,true);
            }
        }


        public void pushbackFill(DataFill fill)
        {
            fill.init();
            this.crypto_client.fillStack.push(fill);
        }
       
        public async Task<bool> updateMarketImpact(Action start, Action end, CancellationToken ct, int spinningMax)
        {
            bool res = true;
            try
            {
                while (true)
                {
                    start();
                    this.checkMIRecorder(DateTime.UtcNow);
                    end();
                    if (ct.IsCancellationRequested)
                    {
                        this.addLog("Cancel requested. updateMarketImpact", Enums.logType.WARNING);
                        break;
                    }
                    Thread.Sleep(10);
                }
            }
            catch(Exception ex) 
            {
                this.addLog("Error recieved within updateMarketImpact");
                this.addLog(ex.Message, Enums.logType.WARNING);
                if (ex.StackTrace != null)
                {
                    this.addLog(ex.StackTrace, Enums.logType.WARNING);
                }
                res = false;
            }
            return res;
        }
        public void updateMarketImpactOnClosing()
        {
            
        }

        public async Task<bool> updateFills(Action start, Action end, CancellationToken ct, int spinningMax)
        {
            DataFill fill = null;
            MarketImpact mi = null;
            Instrument ins = null;
            var spinner = new SpinWait();
            bool ret = true;
            try
            {
                while (true)
                {
                    fill = this.crypto_client.fillQueue.Dequeue();
                    while(fill != null)
                    {
                        bool idMapping = false;
                        using (var mlock = this.mapping_lock.getlock())
                        {
                            idMapping = this.ordIdMapping.ContainsKey(fill.market + fill.order_id);
                        }
                        
                        if (idMapping)
                        {
                            start();
                            await this.processFill(fill,true);
                            if (this.instruments.ContainsKey(fill.symbol_market))
                            {
                                ins = this.instruments[fill.symbol_market];
                            }
                            spinner.Reset();
                            end();
                        }
                        else
                        {
                            ++(fill.queued_count);
                            if (fill.queued_count % 100001 == 100000)
                            {
                                addLog("Unknown fill received.", Enums.logType.WARNING);
                                if(fill.queued_count > 1000000)//Run as normal since we need to handle the fill anyway
                                {
                                    start();
                                    addLog("Handle the fill anyway", Enums.logType.WARNING);
                                    fill.msg += " The original order not found.";
                                    await this.processFill(fill,true);
                                    if (this.instruments.ContainsKey(fill.symbol_market))
                                    {
                                        ins = this.instruments[fill.symbol_market];
                                    }
                                    spinner.Reset();
                                    end();
                                }
                                else
                                {
                                    DataFill localfill = fill;
                                    Task.Run(() =>
                                    {
                                        Thread.Sleep(10);
                                        this.crypto_client.fillQueue.Enqueue(localfill);
                                        addLog("The order has been queued. count:" + localfill.queued_count.ToString("N0"), Enums.logType.WARNING);
                                    });
                                }
                            }
                            else
                            {
                                this.crypto_client.fillQueue.Enqueue(fill);
                            }
                            break;
                        }
                        
                        fill = this.crypto_client.fillQueue.Dequeue();

                    }
                    if(ct.IsCancellationRequested)
                    {
                        this.addLog("Cancel requested. updateFills", Enums.logType.WARNING);
                        break;
                    }
                    spinner.SpinOnce();
                    if (spinningMax > 0 && spinner.Count >= spinningMax)
                    {
                        Thread.Yield();
                        spinner.Reset();
                    }
                }
            }
            catch(Exception ex)
            {
                this.addLog("Error recieved with in updateFills");
                this.addLog(ex.Message, Enums.logType.WARNING);
                if(ex.StackTrace != null)
                {
                    this.addLog(ex.StackTrace, Enums.logType.WARNING);
                }
                ret = false;
            }
            return ret;
        }

        public async Task processFill(DataFill fill,bool stgRunning = true)
        {
            MarketImpact mi;
            using (var mlock = this.mapping_lock.getlock())
            {
                if (this.ordIdMapping.ContainsKey(fill.market + fill.order_id))
                {
                    fill.internal_order_id = this.ordIdMapping[fill.market + fill.order_id];
                }
                else
                {
                    fill.internal_order_id = fill.market + fill.order_id;
                }
            }
            using (var olock = this.order_lock.getlock())
            {
                if (this.orders.ContainsKey(fill.internal_order_id))
                {
                    DataSpotOrderUpdate filled = this.orders[fill.internal_order_id];
                    if (fill.market == market.coincheck || fill.market == market.gmocoin)
                    {
                        filled.average_price = fill.price;//For viewing purpose
                    }
                    fill.msg = filled.msg;
                }
            }
                
            if (stgRunning)
            {
                foreach (var stg in this.strategies)
                {
                    if (stg.Value.maker.symbol_market == fill.symbol_market)
                    {
                        await stg.Value.onFill(fill);
                        mi = this.MI_stack.pop();
                        if (mi == null)
                        {
                            mi = new MarketImpact();
                        }
                        mi.startRecording(fill, stg.Value.taker,stg.Value.name);
                        this.MI_recorder[0].Enqueue(mi);
                    }
                    else if(stg.Value.taker.symbol_market == fill.symbol_market)
                    {
                        await stg.Value.onFill(fill);
                    }
                }
            }
            if (this.exchanges.ContainsKey(fill.market))
            {
                Exchange ex = this.exchanges[fill.market];
                ex.updateBalance(fill);
            }
            else
            {
                addLog($"{fill.market} not found");
                foreach (var ex in this.exchanges)
                {
                    addLog($"{ex.Key}");
                }
            }
            if (this.instruments.ContainsKey(fill.symbol_market))
            {
                Instrument ins;
                ins = this.instruments[fill.symbol_market];
                ins.updateFills(fill);
                if (ins.readyToTrade && fill.timestamp.HasValue && fill.filled_time.HasValue)
                {
                    fill.downStreamLatency = (fill.timestamp.Value - fill.filled_time.Value).TotalMilliseconds - ins.getTheoLatency(fill.timestamp.Value) + ins.base_latency;
                }
            }
            this.ordLogQueue.Enqueue(fill.ToString());
            this.filledOrderQueue.Enqueue(fill);
        }

        public async void updateFillOnClosing()
        {
            while (this.crypto_client.fillQueue.Count > 0)
            {
                DataFill fill = this.crypto_client.fillQueue.Dequeue();
                if(fill != null)
                {
                    await this.processFill(fill,true);
                }
            }
        }
        public async Task<bool> updateOrders(Action start,Action end,CancellationToken ct,int spinningMax)
        {
            DataSpotOrderUpdate ord;
            DataSpotOrderUpdate prevord;
            Instrument ins = null;
            modifingOrd mod;
            var spinner = new SpinWait();
            bool ret = true;
            DateTime takerPosAdjustment = DateTime.UtcNow;
            try
            {
                while (true)
                {
                    ord = this.crypto_client.ordUpdateQueue.Dequeue();
                    //while (this.ord_client.ordUpdateQueue.TryDequeue(out ord))
                    while(ord != null)
                    {
                        start();

                        if (this.instruments.ContainsKey(ord.symbol_market))
                        {
                            ins = this.instruments[ord.symbol_market];
                        }
                        else
                        {
                            ord = this.crypto_client.ordUpdateQueue.Dequeue();
                            continue;
                        }

                        switch (ord.status)
                        {
                            case orderStatus.INVALID:
                                this.handleINVALID(ord);
                                break;
                            case orderStatus.CancelFailed:
                                Dictionary<string,bool> stg_state = new Dictionary<string,bool>();
                                foreach(var stg in this.strategies)
                                {
                                    if(stg.Value.taker.market == ord.market || stg.Value.maker.market == ord.market)
                                    {
                                        stg_state[stg.Key] = stg.Value.enabled;
                                        stg.Value.enabled = false;
                                    }
                                }
                                Thread.Sleep(100);
                                Task.Run(async () => { await this.refreshAndCancelAllorders(ord.market); });
                                foreach(var stg in this.strategies)
                                {
                                    if(stg_state.ContainsKey(stg.Key))
                                    {
                                        stg.Value.enabled = stg_state[stg.Key];
                                    }
                                }
                                break;
                            case orderStatus.WaitOpen:
                                this.handleWaitOpen(ord);
                                break;
                            case orderStatus.WaitMod:
                                //Undefined
                                this.handleWaitMod(ord);
                                break;
                            case orderStatus.WaitCancel:
                                this.handleWaitCancel(ord);
                                break;
                            case orderStatus.Open:
                                this.handleOpen(ord);
                                break;
                            case orderStatus.Canceled:
                                this.handleCancel(ord);
                                break;
                            case orderStatus.Filled:
                                this.handleFilled(ord);
                                break;
                            default:
                                addLog("Unknown type of order.", logType.WARNING);
                                addLog(ord.ToString(), logType.WARNING);
                                break;
                        }
                        ord = this.crypto_client.ordUpdateQueue.Dequeue();
                        spinner.Reset();
                        end();
                    }
                    if (ct.IsCancellationRequested)
                    {
                        this.addLog("Cancel requested. updateOrders", Enums.logType.WARNING);
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
                this.addLog("Error recieved with in updateOrders");
                this.addLog(ex.Message, Enums.logType.WARNING);
                if (ex.StackTrace != null)
                {
                    this.addLog(ex.StackTrace, Enums.logType.WARNING);
                }
                ret = false;
            }
            return ret;
        }

        public void handleINVALID(DataSpotOrderUpdate ord)
        {
            foreach (var stg in this.strategies)
            {
                if (stg.Value.taker.symbol_market == ord.symbol_market)
                {
                    if (ord.err_code == (int)Enums.ordError.NONCE_ERROR)
                    {
                        addLog("Taker order failed. Resending.", logType.WARNING);
                        this.placeNewSpotOrder(stg.Value.taker, ord.side, ord.order_type, ord.order_quantity, ord.order_price);
                    }
                    else if (ord.err_code == (int)Enums.ordError.RATE_LIMIT_EXCEEDED)
                    {
                        if (DateTime.UtcNow - stg.Value.lastPosAdjustment > TimeSpan.FromSeconds(1))
                        {
                            stg.Value.lastPosAdjustment = DateTime.UtcNow;
                            Thread.Sleep(1000);
                            //decimal diff_amount = stg.Value.maker.baseBalance.total + stg.Value.taker.baseBalance.total - stg.Value.baseCcyQuantity;
                            decimal diff_amount = stg.Value.maker.net_pos+ stg.Value.taker.net_pos;
                            orderSide side = orderSide.Sell;
                            if (diff_amount < 0)
                            {
                                side = orderSide.Buy;
                                diff_amount *= -1;
                            }
                            diff_amount = Math.Round(diff_amount / stg.Value.taker.quantity_unit) * stg.Value.taker.quantity_unit;
                            this.placeNewSpotOrder(stg.Value.taker, side, orderType.Market, diff_amount, 0, positionSide.NONE, null, true);
                        }
                    }
                }
                if(stg.Value.stg_orders_dict.ContainsKey(ord.internal_order_id))
                {
                    stg.Value.stg_orders_dict[ord.internal_order_id] = 0;
                }
            }
            using (var olock = this.order_lock.getlock())
            {
                this.orders[ord.internal_order_id] = ord;
            }
            this.ordLogQueue.Enqueue(ord.ToString());
        }
        public void handleWaitOpen(DataSpotOrderUpdate ord)
        {
            this.ordLogQueue.Enqueue(ord.ToString());
            //ord.update_time = DateTime.UtcNow;
            using (var olock = this.order_lock.getlock())
            {
                if (this.orders.ContainsKey(ord.internal_order_id))
                {
                    DataSpotOrderUpdate prev = this.orders[ord.internal_order_id];
                    prev.msg = ord.msg;
                    this.order_pool.Enqueue(ord);
                }
                else
                {
                    this.orders[ord.internal_order_id] = ord;
                }
            }
        }
        public void handleWaitMod(DataSpotOrderUpdate ord)
        {
            this.ordLogQueue.Enqueue(ord.ToString());
            //ord.update_time = DateTime.UtcNow;
            this.order_pool.Enqueue(ord);
        }
        public void handleWaitCancel(DataSpotOrderUpdate ord)
        {
            DataSpotOrderUpdate prevord;

            using (var olock = this.order_lock.getlock())
            {
                if (this.orders.ContainsKey(ord.internal_order_id))
                {
                    prevord = this.orders[ord.internal_order_id];
                    if (prevord.status != orderStatus.Canceled)
                    {
                        ord.filled_quantity = prevord.filled_quantity;
                        ord.order_price = prevord.order_price;
                        ord.order_quantity = prevord.order_quantity;
                        ord.msg = prevord.msg;
                        this.orders[ord.internal_order_id] = ord;
                        if (this.live_orders.ContainsKey(ord.internal_order_id))
                        {
                            this.live_orders[ord.internal_order_id] = ord;
                        }

                        if (this.instruments.ContainsKey(ord.symbol_market))
                        {
                            using(var ilock = this.instruments[ord.symbol_market].order_lock.getlock())
                            {
                                if(this.instruments[ord.symbol_market].live_orders.ContainsKey(ord.internal_order_id))
                                {
                                    this.instruments[ord.symbol_market].live_orders[ord.internal_order_id] = ord;
                                }
                            }
                        }
                        prevord.update_time = DateTime.UtcNow;
                        this.order_pool.Enqueue(prevord);
                    }
                    else
                    {
                        ord.update_time = DateTime.UtcNow;
                        this.order_pool.Enqueue(ord);
                    }
                    this.ordLogQueue.Enqueue(ord.ToString());
                }
                else
                {
                    ord.update_time = DateTime.UtcNow;
                    this.order_pool.Enqueue(ord);
                    this.ordLogQueue.Enqueue(ord.ToString());
                }
            }

            
        }
        public void handleOpen(DataSpotOrderUpdate ord)
        {
            DataSpotOrderUpdate prevord = null;
            Instrument ins = null;
            Exchange ex = null;
            if (this.exchanges.ContainsKey(ord.market))
            {
                ex = this.exchanges[ord.market];
            }

            if (this.instruments.ContainsKey(ord.symbol_market))
            {
                ins = this.instruments[ord.symbol_market];
            }
            bool idMapping = false;
            using (var mlock = this.mapping_lock.getlock())
            {
                idMapping = this.ordIdMapping.ContainsKey(ord.market + ord.order_id);
                if(idMapping)
                {
                    ord.internal_order_id = this.ordIdMapping[ord.market + ord.order_id];
                }
            }
            if (idMapping)
            {
                //1. Get if there is an older order.
                //2. If the order doesn't exist in live_order, add it othewise update from the previous.
                //3. If the order is filled, update the balance
                using (var olock = this.order_lock.getlock())
                {
                    if (this.orders.ContainsKey(ord.internal_order_id))
                    {
                        prevord = this.orders[ord.internal_order_id];
                        ord.msg = prevord.msg;
                        if(ord.order_price == 0 && prevord.order_price > 0)
                        {
                            ord.order_price = prevord.order_price;
                        }
                        if ((ord.status < prevord.status || ord.filled_quantity < prevord.filled_quantity) && !(prevord.status == orderStatus.WaitCancel && ord.status == orderStatus.Open))
                        {
                            this.ordLogQueue.Enqueue(ord.ToString());
                            ord.update_time = DateTime.UtcNow;
                            this.order_pool.Enqueue(ord);
                            return;
                        }

                    }
                    else
                    {
                        prevord = null;
                    }

                    this.orders[ord.internal_order_id] = ord;

                    if (this.live_orders.ContainsKey(ord.internal_order_id))
                    {
                        this.live_orders[ord.internal_order_id] = ord;
                    }
                    else
                    {
                        this.live_orders[ord.internal_order_id] = ord;
                    }
                }
                foreach (var stg in this.strategies)
                {
                    if (stg.Value.enabled)
                    {
                        if (ord.symbol_market == stg.Value.maker.symbol_market)
                        {
                            if (prevord == null)
                            {
                                stg.Value.onOrdUpdate(ord, ord);
                            }
                            else
                            {
                                stg.Value.onOrdUpdate(ord, prevord);
                            }
                        }
                    }
                }

                if (ins != null)
                {
                    ins.updateOrders(ord);
                    using(var olock = ins.order_lock.getlock())
                    {
                        ins.live_orders[ord.internal_order_id] = ord;
                    }
                    decimal filled_quantity;
                    if(prevord != null)
                    {
                        filled_quantity = ord.filled_quantity - prevord.filled_quantity;
                    }
                    else
                    {
                        filled_quantity = ord.filled_quantity;
                    }
                    if (filled_quantity > 0 /*&& ord.order_type != orderType.Market*/)
                    {
                        if(ord.position_side == positionSide.Long)
                        {
                            if(ord.side == orderSide.Sell)
                            {
                                ins.longPosition.AddBalance(0, -filled_quantity);
                            }
                            else if(ex != null)
                            {
                                ex.updateMarginLocked(-filled_quantity * ord.order_price / ins.leverage);
                            }
                        }
                        else if(ord.position_side == positionSide.Short)
                        {
                            if (ord.side == orderSide.Buy)
                            {
                                ins.shortPosition.AddBalance(0, -filled_quantity);
                            }
                            else if (ex != null)
                            {
                                ex.updateMarginLocked(-filled_quantity * ord.order_price / ins.leverage);
                            }
                        }
                        else
                        {
                            switch (ord.side)
                            {
                                case orderSide.Buy:
                                    ins.quoteBalance.AddBalance(0, -filled_quantity * ord.order_price);
                                    break;
                                case orderSide.Sell:
                                    ins.baseBalance.AddBalance(0, -filled_quantity);
                                    break;

                            }
                        }
                    }
                }
                if (prevord != null)
                {
                    prevord.update_time = DateTime.UtcNow;
                    this.order_pool.Enqueue(prevord);
                }
                this.ordLogQueue.Enqueue(ord.ToString());
            }
            else
            {//If the mapping doesn't exist, which means the order from the exchange reaches here before the new order processing.
                if (!this.instruments.ContainsKey(ord.symbol_market))
                {
                    ord.init();
                    this.crypto_client.ordUpdateStack.push(ord);
                }
                else if (ord.queued_count % 200001 == 200000)
                {
                    addLog("Unknown Order:" , Enums.logType.WARNING);
                    addLog(ord.ToString());
                    if (ord.queued_count > 1_000_000)
                    {
                        addLog("Cancelling the order and removing from strategies...");
                        ins = this.instruments[ord.symbol_market];
                        ord.internal_order_id = ord.market + ord.order_id;
                        using (var mlock = this.mapping_lock.getlock())
                        {
                            this.ordIdMapping[ord.internal_order_id] = ord.market + ord.order_id;
                        }
                        //Add the order_id in the mapping and queue it again.
                        foreach (var stg in this.strategies.Values)
                        {
                            if (stg.maker.symbol_market == ord.symbol_market)
                            {
                                using(var ulock = stg.updating.getlock())
                                {
                                    switch (ord.side)
                                    {
                                        case orderSide.Buy:
                                            stg.live_buyorder_id = "";
                                            for (int i = 0; i < stg.live_buyorders.Count; ++i)
                                            {
                                                stg.live_buyorders[i] = "";
                                            }
                                            break;
                                        case orderSide.Sell:
                                            stg.live_sellorder_id = "";
                                            for (int i = 0; i < stg.live_sellorders.Count; ++i)
                                            {
                                                stg.live_sellorders[i] = "";
                                            }
                                            break;
                                    }
                                }
                            }
                        }
                        this.ordLogQueue.Enqueue(ord.ToString());
                        this.placeCancelSpotOrder(ins, ord.market + ord.order_id, true, false);

                        ++(ord.queued_count);
                        this.crypto_client.ordUpdateQueue.Enqueue(ord);
                    }
                    else
                    {
                        DataSpotOrderUpdate localord = ord;
                        Task.Run(() =>
                        {
                            Thread.Sleep(10);
                            ++(localord.queued_count);
                            this.crypto_client.ordUpdateQueue.Enqueue(localord);
                        });
                    }
                }
                else
                {
                    ++(ord.queued_count);
                    this.crypto_client.ordUpdateQueue.Enqueue(ord);
                }
            }
        }
        public void handleCancel(DataSpotOrderUpdate ord)
        {
            DataSpotOrderUpdate prevord = null;
            Instrument ins = null;
            modifingOrd mod = null; 
            Exchange ex = null;
            if (this.exchanges.ContainsKey(ord.market))
            {
                ex = this.exchanges[ord.market];
            }
            if (this.instruments.ContainsKey(ord.symbol_market))
            {
                ins = this.instruments[ord.symbol_market];
            }
            bool idMapping = false;
            using (var mlock = this.mapping_lock.getlock())
            {
                idMapping = this.ordIdMapping.ContainsKey(ord.market + ord.order_id);
                if(idMapping)
                {
                    ord.internal_order_id = this.ordIdMapping[ord.market + ord.order_id];
                }
            }

            if (idMapping)
            {
                //1. Update order dictionary
                //2. Remove from live_orders
                //3. Call ins.updateOrders, remove from ins.live_orders, adjust balance
                using (var olock = this.order_lock.getlock())
                {
                    if (this.orders.ContainsKey(ord.internal_order_id))
                    {
                        prevord = this.orders[ord.internal_order_id];
                        ord.msg = prevord.msg;
                    }
                    else
                    {
                        prevord = null;
                    }
                    this.orders[ord.internal_order_id] = ord;

                    if (this.live_orders.ContainsKey(ord.internal_order_id))
                    {
                        this.live_orders.Remove(ord.internal_order_id);
                        //addLog("[Live order removed at handleCancel] " + ord.ToString());
                    }
                }

                foreach (var stg in this.strategies)
                {
                    if (stg.Value.enabled)
                    {
                        if (ord.symbol_market == stg.Value.maker.symbol_market)
                        {
                            if(prevord == null)
                            {
                                stg.Value.onOrdUpdate(ord, ord);
                            }
                            else
                            {
                                stg.Value.onOrdUpdate(ord, prevord);
                            }
                        }
                    }
                }


                if (ins != null)
                {
                    ins.updateOrders(ord);

                    using(var olock = ins.order_lock.getlock())
                    {
                        if (ins.live_orders.ContainsKey(ord.internal_order_id))
                        {
                            ins.live_orders.Remove(ord.internal_order_id);
                        }

                    }
                    decimal filled_quantity;//cancelled quantity + unprocessed filled quantity
                    if (prevord != null)
                    {
                        filled_quantity = ord.order_quantity - prevord.filled_quantity;
                    }
                    else
                    {
                        filled_quantity = ord.order_quantity;
                    }
                    if (filled_quantity > 0 /*&& ord.order_type != orderType.Market*/)
                    {
                        if (ord.position_side == positionSide.Long)
                        {
                            if (ord.side == orderSide.Sell)
                            {
                                ins.longPosition.AddBalance(0, -filled_quantity);
                            }
                            else if (ex != null)
                            {
                                ex.updateMarginLocked(-filled_quantity * ord.order_price / ins.leverage);
                            }
                        }
                        else if (ord.position_side == positionSide.Short)
                        {
                            if (ord.side == orderSide.Buy)
                            {
                                ins.shortPosition.AddBalance(0, -filled_quantity);
                            }
                            else if (ex != null)
                            {
                                ex.updateMarginLocked(-filled_quantity * ord.order_price / ins.leverage);
                            }
                        }
                        else
                        {
                            switch (ord.side)
                            {
                                case orderSide.Buy:
                                    ins.quoteBalance.AddBalance(0, -filled_quantity * ord.order_price);
                                    break;
                                case orderSide.Sell:
                                    ins.baseBalance.AddBalance(0, -filled_quantity);
                                    break;

                            }
                        }
                    }
                }


                if (this.modifingOrders.ContainsKey(ord.internal_order_id))
                {
                    mod = this.modifingOrders[ord.internal_order_id];

                    if (ord.status == orderStatus.Canceled)
                    {
                        this.placeNewSpotOrder(mod.ins, mod.side, mod.order_type, mod.newQuantity, mod.newPrice, positionSide.NONE, mod.time_in_force, true);
                        this.modifingOrders.Remove(ord.internal_order_id);
                        mod.init();
                        this.modifingOrdStack.push(mod);
                    }
                    else if (ord.status == orderStatus.Filled)
                    {
                        this.modifingOrders.Remove(ord.internal_order_id);
                        mod.init();
                        this.modifingOrdStack.push(mod);
                    }
                }
                if (prevord != null)
                {
                    prevord.update_time = DateTime.UtcNow;
                    this.order_pool.Enqueue(prevord);
                    prevord = null;
                }
                this.ordLogQueue.Enqueue(ord.ToString());
            }
            else
            {//If the mapping doesn't exist, which means the order from the exchange reaches here before the new order processing.
                if (!this.instruments.ContainsKey(ord.symbol_market))
                {
                    ord.init();
                    this.crypto_client.ordUpdateStack.push(ord);
                }
                else if (ord.queued_count % 200001 == 200000)
                {
                    addLog("Unknown Order", Enums.logType.WARNING);
                    addLog(ord.ToString());
                    if (ord.queued_count > 1_000_000)
                    {
                        decimal filled_quantity = 0;
                        using (var olock = this.order_lock.getlock())
                        {
                            string removing = "";
                            foreach (var o in this.live_orders)
                            {
                                if (o.Value.market == ord.market && o.Value.order_id == ord.order_id)
                                {
                                    removing = o.Key;
                                    break;
                                }
                            }
                            if (removing != "")
                            {
                                this.live_orders.Remove(removing);
                            }
                            if (this.orders.ContainsKey(ord.market + ord.order_id))
                            {
                                DataSpotOrderUpdate prev = this.orders[ord.market + ord.order_id];
                                filled_quantity = ord.order_quantity - prev.filled_quantity;
                            }
                            else
                            {
                                filled_quantity = ord.order_quantity;
                            }
                        }
                        
                        ins = this.instruments[ord.symbol_market];
                        if (filled_quantity > 0 /*&& ord.order_type != orderType.Market*/)
                        {
                            if (ord.position_side == positionSide.Long)
                            {
                                if (ord.side == orderSide.Sell)
                                {
                                    ins.longPosition.AddBalance(0, -filled_quantity);
                                }
                                else if (ex != null)
                                {
                                    ex.updateMarginLocked(-filled_quantity * ord.order_price / ins.leverage);
                                }
                            }
                            else if (ord.position_side == positionSide.Short)
                            {
                                if (ord.side == orderSide.Buy)
                                {
                                    ins.shortPosition.AddBalance(0, -filled_quantity);
                                }
                                else if (ex != null)
                                {
                                    ex.updateMarginLocked(-filled_quantity * ord.order_price / ins.leverage);
                                }
                            }
                            else
                            {
                                switch (ord.side)
                                {
                                    case orderSide.Buy:
                                        ins.quoteBalance.AddBalance(0, -filled_quantity * ord.order_price);
                                        break;
                                    case orderSide.Sell:
                                        ins.baseBalance.AddBalance(0, -filled_quantity);
                                        break;

                                }
                            }
                        }
                    }
                    else
                    {
                        DataSpotOrderUpdate localord = ord;
                        Task.Run(() =>
                        {
                            Thread.Sleep(10);
                            ++(localord.queued_count);
                            this.crypto_client.ordUpdateQueue.Enqueue(localord);
                        });
                    }
                }
                else
                {
                    ++(ord.queued_count);
                    this.crypto_client.ordUpdateQueue.Enqueue(ord);
                }
            }
        }
        public void handleFilled(DataSpotOrderUpdate ord)
        {
            DataSpotOrderUpdate prevord = null;
            Instrument ins = null;
            Exchange ex = null;
            if (this.exchanges.ContainsKey(ord.market))
            {
                ex = this.exchanges[ord.market];
            }

            if (this.instruments.ContainsKey(ord.symbol_market))
            {
                ins = this.instruments[ord.symbol_market];
            }
            bool idMapping = false;
            using (var mlock = this.mapping_lock.getlock())
            {
                idMapping = this.ordIdMapping.ContainsKey(ord.market + ord.order_id);
                if(idMapping)
                {
                    ord.internal_order_id = this.ordIdMapping[ord.market + ord.order_id];
                }
            }
            if (idMapping)
            {
                //1. Strategy update
                //2. Update orders
                //3. Remove from live_orders
                //4. Update on the ins side.
                //5. update balance
                using (var olock = this.order_lock.getlock())
                {
                    if (this.orders.ContainsKey(ord.internal_order_id))
                    {
                        prevord = this.orders[ord.internal_order_id];
                        ord.msg = prevord.msg;
                        if(ord.order_price == 0 && prevord.order_price > 0)
                        {
                            ord.order_price = prevord.order_price;
                        }

                        if ((ord.status < prevord.status || ord.filled_quantity < prevord.filled_quantity) && !(prevord.status == orderStatus.WaitCancel && ord.status == orderStatus.Open))
                        {
                            this.ordLogQueue.Enqueue(ord.ToString());
                            ord.update_time = DateTime.UtcNow;
                            this.order_pool.Enqueue(ord);
                            return;
                        }

                    }
                    else
                    {
                        prevord = null;
                    }

                    this.orders[ord.internal_order_id] = ord;
                    if (this.live_orders.ContainsKey(ord.internal_order_id))
                    {
                        this.live_orders.Remove(ord.internal_order_id);
                        //addLog("[Live order removed at handleFilled] " + ord.ToString());
                    }
                }

                foreach (var stg in this.strategies)
                {
                    if (stg.Value.enabled)
                    {
                        if (ord.symbol_market == stg.Value.maker.symbol_market)
                        {
                            if(prevord == null)
                            {
                                stg.Value.onOrdUpdate(ord, ord);
                            }
                            else
                            {
                                stg.Value.onOrdUpdate(ord, prevord);
                            }
                        }
                    }
                }

                if (ins != null)
                {

                    ins.updateOrders(ord);
                    using(var olock = ins.order_lock.getlock())
                    {
                        if (ins.live_orders.ContainsKey(ord.internal_order_id))
                        {
                            ins.live_orders.Remove(ord.internal_order_id);
                        }
                    }
                    decimal filled_quantity;
                    if(prevord != null)
                    {
                        filled_quantity = ord.order_quantity - prevord.filled_quantity;
                    }
                    else
                    {
                        filled_quantity = ord.order_quantity;
                    }
                    if (filled_quantity > 0 /*&& ord.order_type != orderType.Market*/)
                    {
                        if (ord.position_side == positionSide.Long)
                        {
                            if (ord.side == orderSide.Sell)
                            {
                                ins.longPosition.AddBalance(0, -filled_quantity);
                            }
                            else if (ex != null)
                            {
                                ex.updateMarginLocked(-filled_quantity * ord.order_price / ins.leverage);
                            }
                        }
                        else if (ord.position_side == positionSide.Short)
                        {
                            if (ord.side == orderSide.Buy)
                            {
                                ins.shortPosition.AddBalance(0, -filled_quantity);
                            }
                            else if (ex != null)
                            {
                                ex.updateMarginLocked(-filled_quantity * ord.order_price / ins.leverage);
                            }
                        }
                        else
                        {
                            switch (ord.side)
                            {
                                case orderSide.Buy:
                                    ins.quoteBalance.AddBalance(0, -filled_quantity * ord.order_price);
                                    break;
                                case orderSide.Sell:
                                    ins.baseBalance.AddBalance(0, -filled_quantity);
                                    break;

                            }
                        }
                    }
                }

                if(prevord != null)
                {
                    prevord.update_time = DateTime.UtcNow;
                    this.order_pool.Enqueue(prevord);
                }
                this.ordLogQueue.Enqueue(ord.ToString());
            }
            else
            {//If the mapping doesn't exist, which means the order from the exchange reaches here before the new order processing.
                if (!this.instruments.ContainsKey(ord.symbol_market))
                {
                    ord.init();
                    this.crypto_client.ordUpdateStack.push(ord);
                }
                else if (ord.queued_count % 200001 == 200000)
                {
                    addLog("Unknown Order", Enums.logType.WARNING);
                    addLog(ord.ToString());
                    if (ord.queued_count > 1_000_000)
                    {
                        decimal filled_quantity = 0;
                        ins = this.instruments[ord.symbol_market];
                        using (var olock = this.order_lock.getlock())
                        {
                            if (this.orders.ContainsKey(ord.market + ord.order_id))
                            {
                                DataSpotOrderUpdate prev = this.orders[ord.market + ord.order_id];
                                filled_quantity = ord.order_quantity - prev.filled_quantity;
                            }
                            else
                            {
                                filled_quantity = ord.order_quantity;
                            }
                        }

                        if (filled_quantity > 0/* && ord.order_type != orderType.Market*/)
                        {
                            if (ord.position_side == positionSide.Long)
                            {
                                if (ord.side == orderSide.Sell)
                                {
                                    ins.longPosition.AddBalance(0, -filled_quantity);
                                }
                                else if (ex != null)
                                {
                                    ex.updateMarginLocked(-filled_quantity * ord.order_price / ins.leverage);
                                }
                            }
                            else if (ord.position_side == positionSide.Short)
                            {
                                if (ord.side == orderSide.Buy)
                                {
                                    ins.shortPosition.AddBalance(0, -filled_quantity);
                                }
                                else if (ex != null)
                                {
                                    ex.updateMarginLocked(-filled_quantity * ord.order_price / ins.leverage);
                                }
                            }
                            else
                            {
                                switch (ord.side)
                                {
                                    case orderSide.Buy:
                                        ins.quoteBalance.AddBalance(0, -filled_quantity * ord.order_price);
                                        break;
                                    case orderSide.Sell:
                                        ins.baseBalance.AddBalance(0, -filled_quantity);
                                        break;

                                }
                            }
                        }
                    }
                    else
                    {
                        DataSpotOrderUpdate localord = ord;
                        Task.Run(() =>
                        {
                            Thread.Sleep(10);
                            ++(localord.queued_count);
                            this.crypto_client.ordUpdateQueue.Enqueue(localord);
                        });
                    }
                }
                else
                {
                    ++(ord.queued_count);
                    this.crypto_client.ordUpdateQueue.Enqueue(ord);
                }
            }
        }
        public async void updateOrdersOnClosing()
        {
            //while(this.ord_client.ordUpdateQueue.Count() > 0)
            //{
            //    await this._updateOrders();
            //}
        }

        public void updateOrdersOnError()
        {
            
        }

        public void checkVirtualOrderQueue()
        {
            if(this.virtualMode)
            {
                using var volock = this.virtual_order_lock.getlock();
                decimal peek_DequeueCount;
                decimal peek_EnqueueCount;
                DateTime current = DateTime.UtcNow;
                DataSpotOrderUpdate output = this.virtual_order_queue.Peek();
                peek_DequeueCount = this.virtual_order_queue._Dequeues;
                peek_EnqueueCount = this.virtual_order_queue._Enqueues;
                DataSpotOrderUpdate output_temp;
                DataSpotOrderUpdate update = null;
                while (output != null)
                {
                    if (current - output.update_time < TimeSpan.FromMilliseconds(this.latency))
                    {
                        break;
                    }
                    else
                    {
                        output_temp = this.virtual_order_queue.Dequeue();
                        if(output != output_temp)
                        {
                            addLog("Peek and Dequeue works different");
                            if(output != null)
                            {
                                addLog("output:" + output.ToString());
                            }
                            if (output_temp != null)
                            {
                                addLog("output_temp:" + output_temp.ToString());
                            }
                            List<int> id_check = this.virtual_order_queue.checkQueueSequence();
                            addLog($"Peek Enqueue Count:{peek_EnqueueCount} Peek Dequeue Count:{peek_DequeueCount} Dequeue Enqueue Count:{this.virtual_order_queue._Enqueues} Dequeue Dequeue Count:{this.virtual_order_queue._Dequeues}", logType.ERROR);
                        }
                        if (this.instruments.ContainsKey(output.symbol_market))
                        {
                            Instrument ins = this.instruments[output.symbol_market];
                            switch (output.status)
                            {
                                case orderStatus.WaitOpen:
                                    if (output.order_type == orderType.Market)
                                    {
                                        DataFill fill;
                                        fill = this.crypto_client.fillStack.pop();
                                        if (fill == null)
                                        {
                                            fill = new DataFill();
                                        }
                                        update = this.crypto_client.ordUpdateStack.pop();
                                        if (update == null)
                                        {
                                            update = new DataSpotOrderUpdate();
                                        }
                                        update.Copy(output);
                                        update.update_time = current;
                                        update.status = orderStatus.Filled;
                                        update.filled_quantity = output.order_quantity;
                                        decimal feetype = 0;
                                        if (output.order_type == orderType.Market)
                                        {
                                            feetype = ins.taker_fee;
                                        }
                                        else
                                        {
                                            feetype = ins.maker_fee;
                                        }
                                        List<decimal> pr = new List<decimal>();
                                        switch (output.side)
                                        {
                                            case orderSide.Buy:
                                                ins.getWeightedAvgPrice(orderSide.Sell, [update.filled_quantity], pr,false);
                                                update.average_price = pr[0];
                                                update.fee = feetype * update.filled_quantity * update.average_price;
                                                update.fee_asset = ins.quoteCcy;
                                                fill.fee_quote = update.fee;
                                                fill.fee_base = 0;
                                                fill.fee_unknown = 0;
                                                break;
                                            case orderSide.Sell:
                                                ins.getWeightedAvgPrice(orderSide.Buy, [update.filled_quantity], pr, false);
                                                update.average_price = pr[0];
                                                update.fee = feetype * update.filled_quantity * update.average_price;
                                                update.fee_asset = ins.quoteCcy;
                                                fill.fee_quote = update.fee;
                                                fill.fee_base = 0;
                                                fill.fee_unknown = 0;
                                                break;
                                        }
                                        fill.order_id = update.order_id;
                                        fill.internal_order_id = update.internal_order_id;
                                        fill.symbol = ins.symbol;
                                        fill.market = ins.market;
                                        fill.symbol_market = ins.symbol_market;
                                        fill.side = update.side;
                                        fill.position_side = update.position_side;
                                        fill.quantity = update.filled_quantity;
                                        fill.price = update.average_price;
                                        fill.timestamp = update.timestamp;
                                        fill.filled_time = fill.timestamp;
                                        fill.order_type = update.order_type;
                                        switch(fill.side)
                                        {
                                            case orderSide.Buy:
                                                if (fill.position_side == positionSide.Short && exchanges.ContainsKey(fill.market))
                                                {
                                                    Exchange ex = exchanges[fill.market];
                                                    if (ex.marginShort.ContainsKey(fill.symbol_market))
                                                    {
                                                        BalanceMargin bm = ex.marginShort[fill.symbol_market];
                                                        fill.profit_loss = (bm.avg_price - fill.price) * fill.quantity;
                                                    }
                                                }
                                                break;
                                            case orderSide.Sell:
                                                if (fill.position_side == positionSide.Long && exchanges.ContainsKey(fill.market))
                                                {
                                                    Exchange ex = exchanges[fill.market];
                                                    if (ex.marginLong.ContainsKey(fill.symbol_market))
                                                    {
                                                        BalanceMargin bm = ex.marginLong[fill.symbol_market];
                                                        fill.profit_loss = (fill.price - bm.avg_price) * fill.quantity;
                                                    }
                                                }
                                                break;
                                        }
                                        this.crypto_client.ordUpdateQueue.Enqueue(update);
                                        this.crypto_client.fillQueue.Enqueue(fill);
                                    }
                                    else if((output.side == orderSide.Buy && output.order_price > ins.bestask.Item1) || (output.side == orderSide.Sell && output.order_price < ins.bestbid.Item1))
                                    {
                                        update = this.crypto_client.ordUpdateStack.pop();
                                        if (update == null)
                                        {
                                            update = new DataSpotOrderUpdate();
                                        }
                                        update.Copy(output);
                                        update.status = orderStatus.Canceled;
                                        this.crypto_client.ordUpdateQueue.Enqueue(update);
                                    }
                                    else
                                    {
                                        update = this.crypto_client.ordUpdateStack.pop();
                                        if (update == null)
                                        {
                                            update = new DataSpotOrderUpdate();
                                        }
                                        update.Copy(output);
                                        update.status = orderStatus.Open;
                                        update.update_time = current;
                                        
                                        if (this.virtual_liveorders.ContainsKey(update.internal_order_id))
                                        {
                                            this.virtual_liveorders[update.internal_order_id] = update;
                                        }
                                        else
                                        {

                                            this.virtual_liveorders[update.internal_order_id] = update;
                                        }
                                        this.crypto_client.ordUpdateQueue.Enqueue(update);
                                    }
                                    break;
                                case orderStatus.WaitCancel:
                                    if (this.virtual_liveorders.ContainsKey(output.internal_order_id))
                                    {
                                        update = this.crypto_client.ordUpdateStack.pop();
                                        if (update == null)
                                        {
                                            update = new DataSpotOrderUpdate();
                                        }
                                        update.Copy(output);
                                        update.status = orderStatus.Canceled;
                                        this.virtual_liveorders.Remove(output.internal_order_id);
                                        this.crypto_client.ordUpdateQueue.Enqueue(update);
                                    }
                                    else
                                    {
                                        //Do nothing
                                    }
                                    break;
                            }
                        }
                    }
                    output = this.virtual_order_queue.Peek();
                    output_temp = this.virtual_order_queue.Peek();
                    if(output != output_temp)
                    {

                    }
                    peek_DequeueCount = this.virtual_order_queue._Dequeues;
                    peek_EnqueueCount = this.virtual_order_queue._Enqueues;
                }
            }
        }
        public void checkVirtualOrders(Instrument ins,DataTrade? last_trade = null)
        {
            List<string> removing = new List<string>();
            DateTime current = DateTime.UtcNow;
            if (this.virtualMode)
            {
                this.checkVirtualOrderQueue();
                using (var volock = this.virtual_order_lock.getlock())
                {
                    foreach (var item in this.virtual_liveorders)
                    {
                        string key = item.Key;
                        DataSpotOrderUpdate ord = item.Value;
                        if (key != ord.internal_order_id)
                        {
                            this.addLog("The key and the order id didn't match while checking virtual orders.", Enums.logType.WARNING);
                            this.addLog($"The dictionary key:{key} The internal order id:{ord.internal_order_id}", Enums.logType.WARNING);
                            this.addLog(ord.ToString(), Enums.logType.WARNING);
                            using (var mlock = this.mapping_lock.getlock())
                            {
                                foreach (var kv in this.ordIdMapping)
                                {
                                    if (kv.Value == key)
                                    {
                                        addLog(key + " is registered as " + kv.Key + " in the mapping");
                                        //this.addLog(this.orders[key].ToString());
                                    }
                                }
                            }
                        }
                        if(ord.status != orderStatus.Open && ord.status != orderStatus.WaitCancel && ord.status != orderStatus.WaitMod)
                        {
                            addLog("[checkVirtualOrder]Invalid order status.",logType.ERROR);
                            addLog(ord.ToString(), logType.ERROR);
                            return;
                        }
                        if (ord.symbol_market == ins.symbol_market)
                        {
                            using (var qlock = ins.quotes_lock.getlock())
                            {
                                switch (ord.side)
                                {
                                    case orderSide.Buy:
                                        if ((ins.bestask.Item1 < ord.order_price && ins.bestbid.Item1 < ord.order_price) || (last_trade != null && last_trade.symbol + "@" + last_trade.market.ToString() == ord.symbol_market && last_trade.price < ord.order_price))
                                        {
                                            DataSpotOrderUpdate output;

                                            output = this.crypto_client.ordUpdateStack.pop();
                                            if (output == null)
                                            {
                                                output = new DataSpotOrderUpdate();
                                            }
                                            output.Copy(ord);
                                            output.status = orderStatus.Filled;
                                            output.filled_quantity = ord.order_quantity;
                                            output.average_price = ord.order_price;
                                            output.fee = ins.maker_fee * output.filled_quantity * output.average_price;
                                            output.fee_asset = ins.quoteCcy;
                                            output.update_time = DateTime.UtcNow;
                                            DataFill fill;
                                            fill = this.crypto_client.fillStack.pop();
                                            if (fill == null)
                                            {
                                                fill = new DataFill();
                                            }
                                            fill.order_id = ord.order_id;
                                            fill.symbol = ins.symbol;
                                            fill.market = ins.market;
                                            fill.symbol_market = ins.symbol_market;
                                            fill.internal_order_id = output.internal_order_id;
                                            fill.side = ord.side;
                                            fill.position_side = ord.position_side;
                                            fill.quantity = output.filled_quantity;
                                            fill.price = output.average_price;
                                            fill.fee_quote = output.fee;
                                            fill.fee_base = 0;
                                            fill.fee_unknown = 0;
                                            fill.timestamp = output.timestamp;
                                            fill.filled_time = fill.timestamp;
                                            fill.order_type = ord.order_type;
                                            //Add profit_loss if it's a closing order.
                                            if(fill.position_side == positionSide.Short && exchanges.ContainsKey(fill.market))
                                            {
                                                Exchange ex = exchanges[fill.market];
                                                if(ex.marginShort.ContainsKey(fill.symbol_market))
                                                {
                                                    BalanceMargin bm = ex.marginShort[fill.symbol_market];
                                                    fill.profit_loss = (bm.avg_price - fill.price) * fill.quantity;
                                                }
                                            }
                                            this.crypto_client.ordUpdateQueue.Enqueue(output);
                                            removing.Add(key);
                                            this.crypto_client.fillQueue.Enqueue(fill);
                                        }
                                        break;
                                    case orderSide.Sell:
                                        if ((ins.bestbid.Item1 > ord.order_price && ins.bestask.Item1 > ord.order_price) || (last_trade != null && last_trade.symbol + "@" + last_trade.market.ToString() == ord.symbol_market && last_trade.price > ord.order_price))
                                        {
                                            DataSpotOrderUpdate output;
                                            output = this.crypto_client.ordUpdateStack.pop();
                                            if (output == null)
                                            {
                                                output = new DataSpotOrderUpdate();
                                            }
                                            output.Copy(ord);
                                            output.status = orderStatus.Filled;
                                            output.filled_quantity = ord.order_quantity;
                                            output.average_price = ord.order_price;
                                            output.fee = ins.maker_fee * output.filled_quantity * output.average_price;
                                            output.fee_asset = ins.quoteCcy;
                                            output.update_time = DateTime.UtcNow;
                                            DataFill fill;
                                            fill = this.crypto_client.fillStack.pop();
                                            if (fill == null)
                                            {
                                                fill = new DataFill();
                                            }
                                            fill.order_id = ord.order_id;
                                            fill.symbol = ins.symbol;
                                            fill.market = ins.market;
                                            fill.symbol_market = ins.symbol_market;
                                            fill.internal_order_id = output.internal_order_id;
                                            fill.side = ord.side;
                                            fill.position_side = ord.position_side;
                                            fill.quantity = output.filled_quantity;
                                            fill.price = output.average_price;
                                            fill.fee_quote = output.fee;
                                            fill.fee_base = 0;
                                            fill.fee_unknown = 0;
                                            fill.timestamp = output.timestamp;
                                            fill.filled_time = fill.timestamp;
                                            fill.order_type = ord.order_type;
                                            if (fill.position_side == positionSide.Long && exchanges.ContainsKey(fill.market))
                                            {
                                                Exchange ex = exchanges[fill.market];
                                                if (ex.marginLong.ContainsKey(fill.symbol_market))
                                                {
                                                    BalanceMargin bm = ex.marginLong[fill.symbol_market];
                                                    fill.profit_loss = (fill.price - bm.avg_price) * fill.quantity;
                                                }
                                            }
                                            this.crypto_client.ordUpdateQueue.Enqueue(output);
                                            removing.Add(key);
                                            this.crypto_client.fillQueue.Enqueue(fill);
                                        }
                                        break;
                                }
                            }
                        }
                    }
                    foreach (string key in removing)
                    {
                        this.virtual_liveorders.Remove(key);
                    }
                }
            }
        }

        public void checkMIRecorder(DateTime currentTime)
        {
            MarketImpact mi;
            foreach(var miQueue in this.MI_recorder)
            {
                while(this.MI_tempQueue.Count > 0)
                {
                    miQueue.Value.Enqueue(this.MI_tempQueue.Dequeue());
                }
                mi = miQueue.Value.Peek();
                while (mi != null)
                {
                    if (currentTime - mi.filled_time > TimeSpan.FromSeconds(miQueue.Key))
                    {
                        miQueue.Value.Dequeue();
                        if (miQueue.Key > 0)
                        {
                            mi.recordPrice(currentTime);
                        }
                        this.MI_tempQueue.Enqueue(mi);
                    }
                    else
                    {
                        break;
                    }
                    mi = miQueue.Value.Peek();
                }
            }

            while (this.MI_tempQueue.Count > 0)
            {
                mi = this.MI_tempQueue.Dequeue();
                //output to file
                this.MI_outputQueue.Enqueue(mi);
            }
        }

        public void setOrdLogPath(string logPath)
        {
            this.outputPath = logPath;
            string filename = this.outputPath + "/orderlog_" + DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss") + ".csv";
            this.f = new FileStream(filename, FileMode.Create, FileAccess.Write);
            this.sw = new StreamWriter(f);
        }

        public async Task<bool> orderLogging(Action start, Action end, CancellationToken ct, int spinningMax)
        {
            string line;
            var spinner = new SpinWait();
            bool ret = true;
            try
            {
                while (true)
                {
                    line = this.ordLogQueue.Dequeue();
                    //while (this.ordLogQueue.TryDequeue(out line))
                    while(line != null)
                    {
                        start();
                        this.sw.WriteLine(line);
                        line = this.ordLogQueue.Dequeue();
                        //this.sw.Flush();
                        end();
                        this.ord_logged = true;
                    }
                    this.sw.Flush();
                    Thread.Sleep(1000);
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
                }
            }
            catch (Exception ex)
            {
                this.addLog(ex.Message, Enums.logType.WARNING);
                ret = false;
            }
            return ret;

        }

        public void ordLoggingOnClosing()
        {
            string line;
            while (this.ordLogQueue.Count > 0)
            {
                line = this.ordLogQueue.Dequeue();
                //if (this.ordLogQueue.TryDequeue(out line))
                if(line != null)
                {
                    this.sw.WriteLine(line);
                    this.ord_logged = true;
                }
                else
                {
                    break;
                }
            }
            this.sw.Flush();
            this.sw.Close();
            if (this.ord_logged)
            {
                this.ord_logged = false;
            }
            else
            {
                File.Delete(this.f.Name);
            }
        }

        public void ordLoggingOnError()
        {
            string line;
            while (this.ordLogQueue.Count > 0)
            {
                line = this.ordLogQueue.Dequeue();
                //if (this.ordLogQueue.TryDequeue(out line))
                if (line != null)
                {
                    this.sw.WriteLine(line);
                    this.ord_logged = true;
                }
                else
                {
                    break;
                }
            }
            this.sw.Flush();
            this.sw.Close();
            if (this.ord_logged)
            {
                this.ord_logged = false;
            }
            else
            {
                File.Delete(this.f.Name);
            }
        }

        public bool setVirtualMode(bool newValue)
        {
            if(newValue)
            {
                this.addLog("The virtual mode turned on.");
                this.virtualMode = newValue;
            }
            else
            {
                this.addLog("The virtual mode turned off. Orders will go to real markets");
                this.virtualMode = newValue;
            }
            return this.virtualMode;
        }
        public bool getVirtualMode()
        {
            return this.virtualMode;
        }

        private string getVirtualOrdId()
        {
            string ordid = "Virtual" + DateTime.UtcNow.ToString("yyyyMMdd");
            this.id_number = Interlocked.Increment(ref this.id_number);
            ordid += id_number.ToString("D8");
            return ordid;   
        }

        private string getInternalOrdId(string market)
        {
            string ordid = market + DateTime.UtcNow.ToString("yyyyMMdd");
            this.id_number = Interlocked.Increment(ref this.id_number);
            ordid += id_number.ToString("D8");
            return ordid;
        }

        public void addLog(string line,Enums.logType logtype = Enums.logType.INFO)
        {
            this._addLog("[OrderManager]" + line,logtype);
        }


        private static OrderManager _instance;
        private static readonly object _lockObject = new object();

        public static OrderManager GetInstance()
        {
            lock (_lockObject)
            {
                if (_instance == null)
                {
                    _instance = new OrderManager();
                }
                return _instance;
            }
        }
    }

    public class sendingOrder
    {
        public string? internalOrdId;
        public string? ref_IntOrdId;
        public orderAction action;
        //public string ordId;
        public orderSide side;
        public positionSide pos_side;
        public orderType order_type;
        public timeInForce? time_in_force;
        public decimal price;
        public decimal quantity;
        public Instrument? ins;
        public bool waitCancel;
        public string? msg;

        public IEnumerable<string>? order_ids;

        public void init()
        {
            this.internalOrdId = "";
            this.ref_IntOrdId = "";
            this.action = orderAction.NONE;
            //this.ordId = "";
            this.side = orderSide.NONE;
            this.pos_side = positionSide.NONE;
            this.order_type = orderType.NONE;
            this.time_in_force = timeInForce.NONE;
            this.price = 0;
            this.quantity = 0;
            this.ins = null;
            this.waitCancel = false;
            this.msg = "";
            this.order_ids = null;
        }

        public void copy(sendingOrder org)
        {
            this.internalOrdId = org.internalOrdId;
            this.ref_IntOrdId = org.ref_IntOrdId;
            this.action = org.action;
            this.side = org.side;
            this.pos_side = org.pos_side;
            this.order_type = org.order_type;
            this.time_in_force = org.time_in_force;
            this.price = org.price;
            this.quantity = org.quantity;
            this.ins = org.ins;
            this.waitCancel = org.waitCancel;
            this.msg = org.msg;

            if(org.order_ids != null)
            {
                this.order_ids = new List<string>(org.order_ids);
            }
            else
            {
                this.order_ids = null;
            }
        }
    }

    public class modifingOrd
    {
        public string ordId;
        public orderSide side;
        public orderType order_type;
        public timeInForce time_in_force;
        public decimal newPrice;
        public decimal newQuantity;
        public Instrument? ins;

        public modifingOrd() { }
        public void init()
        {
            this.ordId = "";
            this.side = orderSide.NONE;
            this.order_type = orderType.NONE;
            this.time_in_force = timeInForce.NONE;
            this.newPrice = 0;
            this.newQuantity = 0;
            this.ins = null;
        }
    }
}

