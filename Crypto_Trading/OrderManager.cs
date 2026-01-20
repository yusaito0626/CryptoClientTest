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
using System.Drawing;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
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

        Crypto_Clients.Crypto_Clients ord_client = Crypto_Clients.Crypto_Clients.GetInstance();

        public volatile int order_lock;
        public Dictionary<string, DataSpotOrderUpdate> orders;
        public Dictionary<string, DataSpotOrderUpdate> live_orders;

        public SISOQueue<DataSpotOrderUpdate> order_pool;
        public int orderLifeTime = 60; 

        const int SENDINGORD_STACK_SIZE = 1000;
        public MISOQueue<sendingOrder> sendingOrders;
        //public ConcurrentStack<sendingOrder> sendingOrdersStack;
        public LockFreeStack<sendingOrder> sendingOrdersStack;
        //public ConcurrentQueue<sendingOrder> sendingOrdersStack;
        CancellationTokenSource OrderProcessingStop;
        public Dictionary<string, string> ordIdMapping;

        public volatile int virtual_order_lock;
        public MIMOQueue<DataSpotOrderUpdate> virtual_order_queue;
        public Dictionary<string, DataSpotOrderUpdate> virtual_liveorders;
        public Dictionary<string, DataSpotOrderUpdate> disposed_orders;// The key is market + order_id, as the internal_order_id might not be exist.

        public Dictionary<string, modifingOrd> modifingOrders;
        public LockFreeStack<modifingOrd> modifingOrdStack;

        public Dictionary<string, WebSocketState> connections;

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

        public Dictionary<string, Instrument> Instruments;

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
        public LockFreeStack<MarketImpact> MI_stack;

        private OrderManager() 
        {
            this.aborting = false;
            this.updateOrderStopped = false;
            this.virtualMode = true;
            this.order_lock = 0;
            this.orders = new Dictionary<string, DataSpotOrderUpdate>();
            this.live_orders = new Dictionary<string, DataSpotOrderUpdate>();
            this.virtual_order_lock = 0;
            this.virtual_order_queue = new MIMOQueue<DataSpotOrderUpdate>();
            this.virtual_liveorders = new Dictionary<string, DataSpotOrderUpdate>();
            this.disposed_orders = new Dictionary<string, DataSpotOrderUpdate>();

            this.order_pool = new SISOQueue<DataSpotOrderUpdate>();

            this.strategies = new Dictionary<string, Strategy>();

            this.connections = new Dictionary<string, WebSocketState>();

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
            i = 0;
            while (i < MI_STACK_SIZE)
            {
                this.MI_stack.push(new MarketImpact());
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

        public bool refreshHttpClient(string market)
        {
            if(Interlocked.CompareExchange(ref this.refreshing_httpClient,1,0) == 0)
            {
                addLog("Refreshing HTTP clients...");
                this.ready = false;
                Thread.Sleep(1000);
                switch (market)
                {
                    case "bitbank":
                        this.ord_client.bitbank_client.refreshHttpClient();
                        break;
                    case "gmocoin":
                        this.ord_client.gmocoin_client.refreshHttpClient();
                        break;
                    case "coincheck":
                        this.ord_client.coincheck_client.refreshHttpClient();
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

        public async Task<bool> connectPrivateChannel(string market)
        {
            bool ret= false;
            ThreadManager thManager = ThreadManager.GetInstance();
            Func<Task<(bool, double)>> onMsg;
            Action onClosing;
            int trials = 0;
            switch (market)
            {
                case "bitbank":
                    await this.ord_client.bitbank_client.connectPrivateAsync();//This call includes creation of pubnub
                    this.connections[market] = this.ord_client.bitbank_client.GetSocketStatePrivate();
                    ret = true;
                    break;
                case "coincheck":
                    ret = await this.ord_client.coincheck_client.connectPrivateAsync();
                    while(!ret)
                    {
                        ++trials;
                        if(trials < 5)
                        {
                            Thread.Sleep(trials * 3000);
                            this.addLog("Coincheck private connection failed. Trying again. trial:" + trials.ToString(), logType.WARNING);
                            ret = await this.ord_client.coincheck_client.connectPrivateAsync();
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
                    this.ord_client.coincheck_client.onPrivateMessage = this.ord_client.onConcheckPrivateMessage;
                    onClosing = async () =>
                    {
                        await this.ord_client.coincheck_client.onClosingPrivate(this.ord_client.onConcheckPrivateMessage);
                    };
                    thManager.addThread(market + "Private", this.ord_client.coincheck_client.ListeningPrivate,onClosing,this.ord_client.coincheck_client.onListenPrivateOnError);
                    this.connections[market] = this.ord_client.coincheck_client.GetSocketStatePrivate();
                    break;
                case "bittrade":
                    ret = await this.ord_client.bittrade_client.connectPrivateAsync();
                    while (!ret)
                    {
                        ++trials;
                        if (trials < 5)
                        {
                            Thread.Sleep(trials * 3000);
                            this.addLog("Bittrade private connection failed. Trying again. trial:" + trials.ToString(), logType.WARNING);
                            ret = await this.ord_client.bittrade_client.connectPrivateAsync();
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
                    this.ord_client.bittrade_client.onPrivateMessage = this.ord_client.onBitTradePrivateMessage;
                    onClosing = async () =>
                    {
                        await this.ord_client.bittrade_client.onClosingPrivate(this.ord_client.onBitTradePrivateMessage);
                    };
                    thManager.addThread(market + "Private", this.ord_client.bittrade_client.ListeningPrivate,onClosing,this.ord_client.bittrade_client.onListenPrivateOnError);
                    this.connections[market] = this.ord_client.bittrade_client.GetSocketStatePrivate();
                    break;
                case "gmocoin":
                    ret = await this.ord_client.gmocoin_client.connectPrivateAsync();
                    while (!ret)
                    {
                        ++trials;
                        if (trials < 5)
                        {
                            Thread.Sleep(trials * 3000);
                            this.addLog("GMOCoin private connection failed. Trying again. trial:" + trials.ToString(), logType.WARNING);
                            ret = await this.ord_client.gmocoin_client.connectPrivateAsync();
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
                    this.ord_client.gmocoin_client.onPrivateMessage = this.ord_client.onGMOCoinPrivateMessage;
                    onClosing = async () =>
                    {
                        await this.ord_client.gmocoin_client.onClosingPrivate(this.ord_client.onBitTradePrivateMessage);
                    };
                    thManager.addThread(market + "Private", this.ord_client.gmocoin_client.ListeningPrivate, onClosing, this.ord_client.gmocoin_client.onListenPrivateOnError);
                    this.connections[market] = this.ord_client.gmocoin_client.GetSocketStatePrivate();
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
                    case "bitbank":
                        this.connections[market.Key] = this.ord_client.bitbank_client.GetSocketStatePrivate();
                        break;
                    case "coincheck":
                        this.connections[market.Key] = this.ord_client.coincheck_client.GetSocketStatePrivate();
                        break;
                    case "bittrade":
                        this.connections[market.Key] = this.ord_client.bittrade_client.GetSocketStatePrivate();
                        break;
                    case "gmocoin":
                        this.connections[market.Key] = this.ord_client.gmocoin_client.GetSocketStatePrivate();
                        break;
                    default:
                        break;
                }
            }
        }
        public void setInstruments(Dictionary<string, Instrument> dic)
        {
            this.Instruments = dic;
        }

        public async Task<string> placeNewSpotOrder(Instrument ins, orderSide side, orderType ordtype, decimal quantity, decimal price,positionSide pos_side = positionSide.NONE, timeInForce? timeinforce = null, bool sendNow = true,bool wait = true,string msg = "")
        {
            sendingOrder ord;
            string ordid;
            if(this.ready)
            {
                ord = this.sendingOrdersStack.pop();
                ordid = this.getInternalOrdId(ins.market);
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
                ordid = this.getInternalOrdId(ins.market);
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
            using var f = new funcContainer(this.Latency["processNewOrder"].MeasureLatency);
            DataSpotOrderUpdate? output = null;
            JsonDocument js;
            decimal quantity;
            DateTime current = DateTime.UtcNow;
            //Order Check

            //Validity check
            if(sndOrd.ins == null)
            {
                addLog("[New Order]Instrument is not specified.", logType.WARNING);
                output = this.ord_client.ordUpdateStack.pop();
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
                this.ord_client.ordUpdateQueue.Enqueue(output);
                return output;
            }
            if(sndOrd.price < 0)
            {
                addLog("[New Order]Invalid price",logType.WARNING);
                output = this.ord_client.ordUpdateStack.pop();
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
                this.ord_client.ordUpdateQueue.Enqueue(output);
                return output;
            }
            if(sndOrd.quantity <= 0)
            {
                addLog("[New Order]Invalid quantity", logType.WARNING);
                output = this.ord_client.ordUpdateStack.pop();
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
                this.ord_client.ordUpdateQueue.Enqueue(output);
                return output;
            }
            if(sndOrd.side == orderSide.NONE)
            {
                addLog("[New Order]Invalid order side", logType.WARNING);
                output = this.ord_client.ordUpdateStack.pop();
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
                this.ord_client.ordUpdateQueue.Enqueue(output);
                return output;
            }

            //if (sndOrd.price > sndOrd.ins.bestask.Item1 * (decimal)1.1 || sndOrd.price < sndOrd.ins.bestbid.Item1 * (decimal)0.9)
            //{
            //    addLog("[New Order]The price is too far away", logType.WARNING);
            //    output = this.ord_client.ordUpdateStack.pop();
            //    if (output == null)
            //    {
            //        output = new DataSpotOrderUpdate();
            //    }
            //    output.status = orderStatus.INVALID;
            //    output.timestamp = current;
            //    output.internal_order_id = sndOrd.internalOrdId;
            //    output.side = sndOrd.side;
            //    output.symbol = sndOrd.ins.symbol;
            //    output.market = sndOrd.ins.market;
            //    output.symbol_market = sndOrd.ins.symbol_market;
            //    output.order_quantity = sndOrd.quantity;
            //    output.order_price = sndOrd.price;
            //    output.filled_quantity = 0;
            //    output.average_price = 0;
            //    output.fee = 0;
            //    output.fee_asset = "";
            //    output.is_trigger_order = true;
            //    output.last_trade = "";
            //    output.msg = sndOrd.msg;
            //    output.err_code = (int)ordError.INVALID_PRICE;
            //    addLog(output.ToString(), logType.WARNING);
            //    this.ord_client.ordUpdateQueue.Enqueue(output);
            //    return output;
            //}

            //Availability Check
            decimal orderprice = sndOrd.price;
            //Spot
            if(sndOrd.pos_side == positionSide.NONE)
            {
                switch(sndOrd.side)
                {
                    case orderSide.Buy:
                        if(orderprice == 0)
                        {
                            orderprice = sndOrd.ins.bestask.Item1;
                        }
                        if(sndOrd.ins.quoteBalance.available <= orderprice * sndOrd.quantity)
                        {
                            addLog("[New Order]Insuficient availability.", logType.WARNING);
                            output = this.ord_client.ordUpdateStack.pop();
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
                            this.ord_client.ordUpdateQueue.Enqueue(output);
                            return output;
                        }
                        break;
                    case orderSide.Sell:
                        if(sndOrd.ins.baseBalance.available < sndOrd.quantity)
                        {
                            addLog("[New Order]Insuficient availability.", logType.WARNING);
                            output = this.ord_client.ordUpdateStack.pop();
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
                            this.ord_client.ordUpdateQueue.Enqueue(output);
                            return output;
                        }
                        break;
                }
            }

            if (this.virtualMode)
            {
                quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                output = this.ord_client.ordUpdateStack.pop();
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
                this.ordIdMapping[output.market + output.order_id] = sndOrd.internalOrdId;
                if (output.order_type == orderType.Limit || output.order_type == orderType.LimitMaker)
                {
                    if (output.position_side == positionSide.Long)
                    {
                        if (output.side == orderSide.Sell)
                        {
                            sndOrd.ins.longPosition.AddBalance(0, output.order_quantity);
                        }
                    }
                    else if (output.position_side == positionSide.Short)
                    {
                        if (output.side == orderSide.Buy)
                        {
                            sndOrd.ins.shortPosition.AddBalance(0, output.order_quantity);
                        }
                    }
                    else
                    {
                        switch (output.side)
                        {
                            case orderSide.Buy:
                                sndOrd.ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                                break;
                            case orderSide.Sell:
                                sndOrd.ins.baseBalance.AddBalance(0, output.order_quantity);
                                break;
                        }
                    }
                }
                this.virtual_order_queue.Enqueue(output);
                this.ord_client.ordUpdateQueue.Enqueue(output);
            }
            else if (sndOrd.ins.market == "bitbank")
            {
                quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                DateTime sendTime = DateTime.UtcNow;
                if (sndOrd.order_type == orderType.Limit)
                {
                    js = await this.ord_client.bitbank_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.order_type.ToString().ToLower(), sndOrd.side.ToString().ToLower(), sndOrd.price, quantity, sndOrd.pos_side.ToString().ToLower(), true);
                }
                else
                {
                    js = await this.ord_client.bitbank_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.order_type.ToString().ToLower(), sndOrd.side.ToString().ToLower(), sndOrd.price, quantity, sndOrd.pos_side.ToString().ToLower(), false);
                }

                if (js.RootElement.GetProperty("success").GetUInt16() == 1)
                {
                    var ord_obj = js.RootElement.GetProperty("data");
                    output = this.ord_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.order_id = ord_obj.GetProperty("order_id").GetInt64().ToString();
                    this.ordIdMapping[sndOrd.ins.market + output.order_id] = sndOrd.internalOrdId;
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
                            output.order_price = 0;
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

                    if (output.position_side == positionSide.Long)
                    {
                        if (output.side == orderSide.Sell)
                        {
                            sndOrd.ins.longPosition.AddBalance(0, output.order_quantity);
                        }
                    }
                    else if (output.position_side == positionSide.Short)
                    {
                        if (output.side == orderSide.Buy)
                        {
                            sndOrd.ins.shortPosition.AddBalance(0, output.order_quantity);
                        }
                    }
                    else
                    {
                        switch (output.side)
                        {
                            case orderSide.Buy:
                                sndOrd.ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                                break;
                            case orderSide.Sell:
                                sndOrd.ins.baseBalance.AddBalance(0, output.order_quantity);
                                break;
                        }
                    }
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    int code = js.RootElement.GetProperty("data").GetProperty("code").GetInt32();

                    output = this.ord_client.ordUpdateStack.pop();
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
                            output.err_code = code;
                            this.refreshHttpClient("bitbank");
                            break;
                        case 80002:
                            this.addLog("[bitbank]New order failed. The http client is not ready. code:" + code.ToString() + "   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            output.err_code = code;
                            break;
                        default:
                            this.addLog("[bitbank]New Order Failed   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            this.addLog(js.RootElement.GetRawText(), Enums.logType.ERROR);
                            break;
                    }

                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else if (sndOrd.ins.market == "coincheck")
            {

                this.coincheck_sw.Start();
                DateTime sendTime = DateTime.UtcNow;
                if (sndOrd.order_type == orderType.Limit)
                {
                    quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;

                    if (quantity * sndOrd.price <= 500)
                    {
                        addLog("[coincheck]The order size is too small.", logType.WARNING);
                        output = this.ord_client.ordUpdateStack.pop();
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
                        this.ord_client.ordUpdateQueue.Enqueue(output);
                        return output;
                    }
                    js = await this.ord_client.coincheck_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToLower(), sndOrd.price, quantity, "post_only");
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
                            quantity = quantity * sndOrd.ins.bestask.Item1;
                        }
                        if (quantity <= 500)
                        {
                            addLog("[coincheck]The order size is too small.", logType.WARNING);
                            output = this.ord_client.ordUpdateStack.pop();
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
                            this.ord_client.ordUpdateQueue.Enqueue(output);
                            return output;
                        }
                        js = await this.ord_client.coincheck_client.placeMarketNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToLower(), 0, quantity);
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
                            amount = quantity * sndOrd.ins.bestbid.Item1;
                        }
                        if (amount <= 500)
                        {
                            addLog("[coincheck]The order size is too small.", logType.WARNING);
                            output = this.ord_client.ordUpdateStack.pop();
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
                            this.ord_client.ordUpdateQueue.Enqueue(output);
                            return output;
                        }
                        js = await this.ord_client.coincheck_client.placeMarketNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToLower(), 0, quantity);
                    }
                }
                if (js.RootElement.GetProperty("success").GetBoolean())
                {
                    JsonElement ord_obj = js.RootElement;
                    string line = JsonSerializer.Serialize(ord_obj);
                    output = this.ord_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.order_id = ord_obj.GetProperty("id").GetInt64().ToString();
                    this.ordIdMapping[sndOrd.ins.market + output.order_id] = sndOrd.internalOrdId;
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
                            output.order_price = 0;
                            output.order_quantity = 0;
                        }
                        else
                        {
                            output.order_price = 0;
                            output.order_quantity = decimal.Parse(ord_obj.GetProperty("amount").GetString());
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
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                    output = this.ord_client.ordUpdateStack.pop();
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
                            output.order_price = 0;
                            output.order_quantity = 0;
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
                    switch (sndOrd.side)
                    {
                        case orderSide.Buy:
                            sndOrd.ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                            break;
                        case orderSide.Sell:
                            sndOrd.ins.baseBalance.AddBalance(0, output.order_quantity);
                            break;
                    }
                    output.msg = sndOrd.msg;

                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    output = this.ord_client.ordUpdateStack.pop();
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

                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else if (sndOrd.ins.market == "bittrade")
            {
                quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                decimal order_price = sndOrd.price;
                DateTime sendTime = DateTime.UtcNow;
                if (sndOrd.order_type == orderType.Limit)
                {
                    js = await this.ord_client.bittrade_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToLower(), sndOrd.price, quantity, true);
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
                    js = await this.ord_client.bittrade_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToLower(), order_price, quantity, false);
                }

                if (js.RootElement.GetProperty("status").GetString() == "ok")
                {
                    output = this.ord_client.ordUpdateStack.pop();
                    if (output == null)
                    {
                        output = new DataSpotOrderUpdate();
                    }
                    output.order_id = js.RootElement.GetProperty("data").GetString();
                    this.ordIdMapping[sndOrd.ins.market + output.order_id] = sndOrd.internalOrdId;
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
                    switch (sndOrd.side)
                    {
                        case orderSide.Buy:
                            sndOrd.ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                            break;
                        case orderSide.Sell:
                            sndOrd.ins.baseBalance.AddBalance(0, output.order_quantity);
                            break;
                    }
                    output.msg = sndOrd.msg;
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    string msg = JsonSerializer.Serialize(js);
                    this.addLog(msg, Enums.logType.ERROR);
                    output = this.ord_client.ordUpdateStack.pop();
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
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else
            {
                quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                DateTime sendTime = DateTime.UtcNow;
                output = await this.ord_client.placeNewSpotOrder(sndOrd.ins.market, sndOrd.ins.baseCcy, sndOrd.ins.quoteCcy, sndOrd.side, sndOrd.order_type, quantity, sndOrd.price, sndOrd.time_in_force);
                if (output != null)
                {
                    this.ordIdMapping[sndOrd.ins.market + output.order_id] = sndOrd.internalOrdId;
                    output.timestamp = sendTime;
                    output.symbol_market = sndOrd.ins.symbol_market;
                    output.internal_order_id = sndOrd.internalOrdId;
                    output.msg = sndOrd.msg;
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    output = this.ord_client.ordUpdateStack.pop();
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
                    this.ord_client.ordUpdateQueue.Enqueue(output);
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

            if (sndOrd.waitCancel)
            {
                if (this.orders.ContainsKey(sndOrd.ref_IntOrdId))
                {
                    ord = this.orders[sndOrd.ref_IntOrdId];
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
                    sndOrd.init();
                    this.sendingOrdersStack.push(sndOrd);
                    return null;
                }
            }
            else
            {
                if (this.orders.ContainsKey(sndOrd.ref_IntOrdId))
                {
                    ord = this.orders[sndOrd.ref_IntOrdId];
                    sndOrd.side = ord.side;
                    sndOrd.order_type = ord.order_type;
                    sndOrd.time_in_force = ord.time_in_force;
                    if(ord.status == orderStatus.Open)
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
                else
                {
                    addLog("Order not found. order id:" + sndOrd.ref_IntOrdId);
                    sndOrd.init();
                    this.sendingOrdersStack.push(sndOrd);
                    return null;
                }
            }
        }
        async public Task<DataSpotOrderUpdate?> processCanOrder(sendingOrder sndOrd)
        {
            using var f = new funcContainer(this.Latency["processCanOrder"].MeasureLatency);
            DataSpotOrderUpdate? output = null;
            JsonDocument js;
            if (this.virtualMode)
            {
                output = this.ord_client.ordUpdateStack.pop();
                if (output == null)
                {
                    output = new DataSpotOrderUpdate();
                }
                DataSpotOrderUpdate prev = this.orders[sndOrd.ref_IntOrdId];
                
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
                this.ord_client.ordUpdateQueue.Enqueue(output);
            }
            else if (sndOrd.ins.market == "bitbank")
            {
                DateTime sendTime = DateTime.UtcNow;
                DataSpotOrderUpdate prev = this.orders[sndOrd.ref_IntOrdId];
                js = await this.ord_client.bitbank_client.placeCanOrder(sndOrd.ins.symbol, prev.order_id);
                if (js.RootElement.GetProperty("success").GetUInt16() == 1)
                {
                    var ord_obj = js.RootElement.GetProperty("data");
                    output = this.ord_client.ordUpdateStack.pop();
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
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    int code = js.RootElement.GetProperty("data").GetProperty("code").GetInt32();
                    switch (code)
                    {
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
                            this.refreshHttpClient("bitbank");
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
            else if (sndOrd.ins.market == "coincheck")
            {
                DateTime sendTime = DateTime.UtcNow;
                DataSpotOrderUpdate prev = this.orders[sndOrd.ref_IntOrdId];
                js = await this.ord_client.coincheck_client.placeCanOrder(prev.order_id);
                if (js.RootElement.GetProperty("success").GetBoolean())
                {
                    var ord_obj = js.RootElement;
                    output = this.ord_client.ordUpdateStack.pop();
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
                    this.ord_client.ordUpdateQueue.Enqueue(output);
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
            else if (sndOrd.ins.market == "bittrade")
            {
                DateTime sendTime = DateTime.UtcNow;
                DataSpotOrderUpdate prev = this.orders[sndOrd.ref_IntOrdId];
                js = await this.ord_client.bittrade_client.placeCanOrder(prev.order_id);
                if (js.RootElement.GetProperty("status").GetString() == "ok")
                {
                    output = this.ord_client.ordUpdateStack.pop();
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
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else
            {
                DataSpotOrderUpdate prev = this.orders[sndOrd.ref_IntOrdId];
                output = await this.ord_client.placeCancelSpotOrder(sndOrd.ins.market, sndOrd.ins.baseCcy, sndOrd.ins.quoteCcy, prev.order_id);
                if (output != null)
                {
                    output.symbol_market = sndOrd.ins.symbol_market;
                    output.internal_order_id = sndOrd.ref_IntOrdId;
                    this.ord_client.ordUpdateQueue.Enqueue(output);
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
            List<JsonDocument> js;
            if (this.virtualMode)
            {
                foreach (var ordid in sndOrd.order_ids)
                {
                    ordObj = this.ord_client.ordUpdateStack.pop();
                    if (ordObj == null)
                    {
                        ordObj = new DataSpotOrderUpdate();
                    }
                    DataSpotOrderUpdate prev = this.orders[ordid];

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
                    this.ord_client.ordUpdateQueue.Enqueue(ordObj);
                }
            }
            else if (sndOrd.ins.market == "bitbank")
            {
                DateTime sendTime = DateTime.UtcNow;
                List<string> ord_ids = new List<string>();
                foreach(string order_id in sndOrd.order_ids)
                {
                    if(this.orders.ContainsKey(order_id) && (this.orders[order_id].status == orderStatus.Open || this.orders[order_id].status == orderStatus.WaitOpen))
                    {
                        ord_ids.Add(this.orders[order_id].order_id);
                    }
                }
                js = await this.ord_client.bitbank_client.placeCanOrders(sndOrd.ins.symbol, ord_ids);
                foreach (var elem in js)
                {
                    if (elem.RootElement.GetProperty("success").GetUInt16() == 1)
                    {
                        var ord_objs = elem.RootElement.GetProperty("data").GetProperty("orders").EnumerateArray();
                        foreach(var ord_obj in ord_objs)
                        {
                            ordObj = this.ord_client.ordUpdateStack.pop();
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
                            ordObj.internal_order_id = this.ordIdMapping[ordObj.market + ordObj.order_id];
                            ordObj.filled_quantity = 0;
                            ordObj.status = orderStatus.WaitCancel;
                            this.ord_client.ordUpdateQueue.Enqueue(ordObj);
                            output.Add(ordObj);
                        }
                        
                    }
                    else
                    {
                        int code = elem.RootElement.GetProperty("data").GetProperty("code").GetInt32();
                        switch (code)
                        {
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
                                this.refreshHttpClient("bitbank");
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
            else if (sndOrd.ins.market == "coincheck")
            {
                DateTime sendTime = DateTime.UtcNow;
                List<string> ord_ids = new List<string>();
                foreach (string order_id in sndOrd.order_ids)
                {
                    if (this.orders.ContainsKey(order_id) && (this.orders[order_id].status == orderStatus.Open || this.orders[order_id].status == orderStatus.WaitOpen))
                    {
                        ord_ids.Add(this.orders[order_id].order_id);
                    }
                }
                
                js = await this.ord_client.coincheck_client.placeCanOrders(ord_ids);
                foreach (var elem in js)
                {
                    if (elem.RootElement.GetProperty("success").GetBoolean())
                    {
                        var ord_obj = elem.RootElement;
                        ordObj = this.ord_client.ordUpdateStack.pop();
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
                        ordObj.internal_order_id = this.ordIdMapping[ordObj.market + ordObj.order_id];
                        ordObj.filled_quantity = 0;
                        ordObj.status = orderStatus.WaitCancel;
                        output.Add(ordObj);
                        this.ord_client.ordUpdateQueue.Enqueue(ordObj);
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
            else if (sndOrd.ins.market == "bittrade")
            {
                DateTime sendTime = DateTime.UtcNow;
                List<string> ord_ids = new List<string>();
                foreach (string order_id in sndOrd.order_ids)
                {
                    if (this.orders.ContainsKey(order_id) && (this.orders[order_id].status == orderStatus.Open || this.orders[order_id].status == orderStatus.WaitOpen))
                    {
                        ord_ids.Add(this.orders[order_id].order_id);
                    }
                }
                js = await this.ord_client.bittrade_client.placeCanOrders(ord_ids);
                foreach(var elem in js)
                {
                    if (elem.RootElement.GetProperty("status").GetString() == "ok")
                    {
                        ordObj = this.ord_client.ordUpdateStack.pop();
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
                        ordObj.internal_order_id = this.ordIdMapping[ordObj.market + ordObj.order_id];
                        ordObj.filled_quantity = 0;
                        ordObj.status = orderStatus.WaitCancel;
                        output.Add(ordObj);
                        this.ord_client.ordUpdateQueue.Enqueue(ordObj);
                    }
                }
                
            }
            else
            {
                DateTime sendTime = DateTime.UtcNow;
                List<string> ord_ids = new List<string>();
                foreach (string order_id in sndOrd.order_ids)
                {
                    if (this.orders.ContainsKey(order_id))
                    {
                        ordObj = await this.ord_client.placeCancelSpotOrder(sndOrd.ins.market, sndOrd.ins.baseCcy, sndOrd.ins.quoteCcy, this.orders[order_id].order_id);
                        if (ordObj != null)
                        {
                            ordObj.symbol_market = sndOrd.ins.symbol_market;
                            ordObj.internal_order_id = order_id;
                            output.Add(ordObj);
                            this.ord_client.ordUpdateQueue.Enqueue(ordObj);
                        }
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

        public async Task cancelAllOrders()
        {
            this.addLog("Cancelling all orders...");
            Instrument ins;
            Dictionary<Instrument, List<string>> order_list = new Dictionary<Instrument, List<string>>();
            while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
            {

            }
            foreach (var ord in this.live_orders.Values)
            {
                if(this.Instruments.ContainsKey(ord.symbol_market))
                {
                    ins = this.Instruments[ord.symbol_market];
                    if(!order_list.ContainsKey(ins))
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
            foreach(var list in order_list)
            {
                await this.placeCancelSpotOrders(list.Key, list.Value,true,true);
            }
            Volatile.Write(ref this.order_lock, 0);
        }


        public void pushbackFill(DataFill fill)
        {
            fill.init();
            this.ord_client.fillStack.push(fill);
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
                        this.addLog("Cancel requested. updateFills", Enums.logType.WARNING);
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
                    fill = this.ord_client.fillQueue.Dequeue();
                    while(fill != null)
                    {
                        if (this.ordIdMapping.ContainsKey(fill.market + fill.order_id))
                        {
                            start();
                            await this.processFill(fill,true);
                            if (this.Instruments.ContainsKey(fill.symbol_market))
                            {
                                ins = this.Instruments[fill.symbol_market];
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
                                    if (this.Instruments.ContainsKey(fill.symbol_market))
                                    {
                                        ins = this.Instruments[fill.symbol_market];
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
                                        this.ord_client.fillQueue.Enqueue(localfill);
                                        addLog("The order has been queued. count:" + localfill.queued_count.ToString("N0"), Enums.logType.WARNING);
                                    });
                                }
                            }
                            else
                            {
                                this.ord_client.fillQueue.Enqueue(fill);
                            }
                            break;
                        }
                        
                        fill = this.ord_client.fillQueue.Dequeue();

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
            if (this.ordIdMapping.ContainsKey(fill.market + fill.order_id))
            {
                fill.internal_order_id = this.ordIdMapping[fill.market + fill.order_id];
            }
            else
            {
                fill.internal_order_id = fill.market + fill.order_id;
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
                }
            }
            
            if (this.Instruments.ContainsKey(fill.symbol_market))
            {
                if (fill.market == "coincheck" || fill.market == "gmocoin")
                {
                    if (this.orders.ContainsKey(fill.internal_order_id))
                    {
                        DataSpotOrderUpdate filled = this.orders[fill.internal_order_id];
                        filled.average_price = fill.price;//For viewing purpose
                    }
                }
                Instrument ins;
                ins = this.Instruments[fill.symbol_market];
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
            while (this.ord_client.fillQueue.Count > 0)
            {
                DataFill fill = this.ord_client.fillQueue.Dequeue();
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
                    ord = this.ord_client.ordUpdateQueue.Dequeue();
                    //while (this.ord_client.ordUpdateQueue.TryDequeue(out ord))
                    while(ord != null)
                    {
                        start();

                        if (this.Instruments.ContainsKey(ord.symbol_market))
                        {
                            ins = this.Instruments[ord.symbol_market];
                        }
                        else
                        {
                            ord = this.ord_client.ordUpdateQueue.Dequeue();
                            continue;
                        }

                        switch (ord.status)
                        {
                            case orderStatus.INVALID:
                                this.handleINVALID(ord);
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
                        ord = this.ord_client.ordUpdateQueue.Dequeue();
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
            this.orders[ord.internal_order_id] = ord;
            this.ordLogQueue.Enqueue(ord.ToString());
        }
        public void handleWaitOpen(DataSpotOrderUpdate ord)
        {
            this.ordLogQueue.Enqueue(ord.ToString());
            ord.update_time = DateTime.UtcNow;
            this.order_pool.Enqueue(ord);
        }
        public void handleWaitMod(DataSpotOrderUpdate ord)
        {
            this.ordLogQueue.Enqueue(ord.ToString());
            ord.update_time = DateTime.UtcNow;
            this.order_pool.Enqueue(ord);
        }
        public void handleWaitCancel(DataSpotOrderUpdate ord)
        {
            DataSpotOrderUpdate prevord;

            if (this.orders.ContainsKey(ord.internal_order_id))
            {
                prevord = this.orders[ord.internal_order_id];
                if (prevord.status != orderStatus.Canceled)
                {
                    ord.filled_quantity = prevord.filled_quantity;
                    ord.order_price = prevord.order_price;
                    ord.order_quantity = prevord.order_quantity;
                    this.orders[ord.internal_order_id] = ord;
                    //while(Interlocked.CompareExchange(ref this.order_lock,1,0) != 0)
                    //{

                    //}
                    //live_orders.Remove or .Add is only called in this thread.
                    if (this.live_orders.ContainsKey(ord.internal_order_id))
                    {
                        this.live_orders[ord.internal_order_id] = ord;
                    }
                    //Volatile.Write(ref this.order_lock, 0);
                    if (this.Instruments.ContainsKey(ord.symbol_market) && this.Instruments[ord.symbol_market].live_orders.ContainsKey(ord.internal_order_id))
                    {
                        this.Instruments[ord.symbol_market].live_orders[ord.internal_order_id] = ord;
                    }
                    prevord.update_time = DateTime.UtcNow;
                    this.order_pool.Enqueue(prevord);
                }
                else
                {
                    ord.update_time = DateTime.UtcNow;
                    this.order_pool.Enqueue(ord);
                }
            }
            else
            {
                ord.update_time = DateTime.UtcNow;
                this.order_pool.Enqueue(ord);
            }
            this.ordLogQueue.Enqueue(ord.ToString());
        }
        public void handleOpen(DataSpotOrderUpdate ord)
        {
            DataSpotOrderUpdate prevord = null;
            Instrument ins = null;

            if(this.Instruments.ContainsKey(ord.symbol_market))
            {
                ins = this.Instruments[ord.symbol_market];
            }

            if (this.ordIdMapping.ContainsKey(ord.market + ord.order_id))
            {
                //1. Get if there is an older order.
                //2. If the order doesn't exist in live_order, add it othewise update from the previous.
                //3. If the order is filled, update the balance
                ord.internal_order_id = this.ordIdMapping[ord.market + ord.order_id];
                if (this.orders.ContainsKey(ord.internal_order_id))
                {
                    prevord = this.orders[ord.internal_order_id];
                    if ((ord.status < prevord.status || ord.filled_quantity < prevord.filled_quantity) && !(prevord.status == orderStatus.WaitCancel && ord.status == orderStatus.Open))
                    {
                        ord.update_time = DateTime.UtcNow;
                        this.order_pool.Enqueue(ord);
                        this.ordLogQueue.Enqueue(ord.ToString());
                        return;
                    }

                    //foreach (var stg in this.strategies)
                    //{
                    //    if (stg.Value.enabled)
                    //    {
                    //        if (ord.symbol_market == stg.Value.maker.symbol_market)
                    //        {
                    //            stg.Value.onOrdUpdate(ord, prevord);
                    //        }
                    //    }
                    //}
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
                    while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
                    {
                    }
                    //addLog("[New live order at handleOpen] " + ord.ToString());
                    this.live_orders[ord.internal_order_id] = ord;
                    Volatile.Write(ref this.order_lock, 0);
                }
                if (ins != null)
                {
                    ins.updateOrders(ord);
                    if (ins.live_orders.ContainsKey(ord.internal_order_id))
                    {
                        ins.live_orders[ord.internal_order_id] = ord;
                    }
                    else
                    {
                        while (Interlocked.CompareExchange(ref ins.order_lock, 1, 0) != 0)
                        {
                        }
                        ins.live_orders[ord.internal_order_id] = ord;
                        Volatile.Write(ref ins.order_lock, 0);
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
                    if (filled_quantity > 0 && ord.order_type != orderType.Market)
                    {
                        if(ord.position_side == positionSide.Long)
                        {
                            if(ord.side == orderSide.Sell)
                            {
                                ins.longPosition.AddBalance(0, -filled_quantity);
                            }
                        }
                        else if(ord.position_side == positionSide.Short)
                        {
                            if (ord.side == orderSide.Buy)
                            {
                                ins.shortPosition.AddBalance(0, -filled_quantity);
                            }
                        }
                        else
                        {
                            addLog("handleOpen  " + ord.ToString());
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
                if (!this.Instruments.ContainsKey(ord.symbol_market))
                {
                    ord.init();
                    this.ord_client.ordUpdateStack.push(ord);
                }
                else if (ord.queued_count % 200001 == 200000)
                {
                    addLog("Unknown Order:" , Enums.logType.WARNING);
                    addLog(ord.ToString());
                    if (ord.queued_count > 1_000_000)
                    {
                        addLog("Cancelling the order and removing from strategies...");
                        ins = this.Instruments[ord.symbol_market];
                        ord.internal_order_id = ord.market + ord.order_id;
                        this.ordIdMapping[ord.internal_order_id] = ord.market + ord.order_id;
                        //Add the order_id in the mapping and queue it again.
                        foreach (var stg in this.strategies.Values)
                        {
                            if (stg.maker.symbol_market == ord.symbol_market)
                            {
                                while (Interlocked.CompareExchange(ref stg.updating, 1, 0) != 0)
                                {

                                }
                                switch (ord.side)
                                {
                                    case orderSide.Buy:
                                        stg.live_buyorder_id = "";
                                        break;
                                    case orderSide.Sell:
                                        stg.live_sellorder_id = "";
                                        break;
                                }
                                Volatile.Write(ref stg.updating, 0);
                            }
                        }
                        this.ordLogQueue.Enqueue(ord.ToString());
                        this.placeCancelSpotOrder(ins, ord.market + ord.order_id, true, false);

                        ++(ord.queued_count);
                        this.ord_client.ordUpdateQueue.Enqueue(ord);
                    }
                    else
                    {
                        DataSpotOrderUpdate localord = ord;
                        Task.Run(() =>
                        {
                            Thread.Sleep(10);
                            ++(localord.queued_count);
                            this.ord_client.ordUpdateQueue.Enqueue(localord);
                        });
                    }
                }
                else
                {
                    ++(ord.queued_count);
                    this.ord_client.ordUpdateQueue.Enqueue(ord);
                }
            }
        }
        public void handleCancel(DataSpotOrderUpdate ord)
        {
            DataSpotOrderUpdate prevord = null;
            Instrument ins = null;
            modifingOrd mod = null;
            if(this.Instruments.ContainsKey(ord.symbol_market))
            {
                ins = this.Instruments[ord.symbol_market];
            }
            if (this.ordIdMapping.ContainsKey(ord.market + ord.order_id))
            {
                //1. Update order dictionary
                //2. Remove from live_orders
                //3. Call ins.updateOrders, remove from ins.live_orders, adjust balance
                ord.internal_order_id = this.ordIdMapping[ord.market + ord.order_id];
                if (this.orders.ContainsKey(ord.internal_order_id))
                {
                    prevord = this.orders[ord.internal_order_id];

                    foreach (var stg in this.strategies)
                    {
                        if (stg.Value.enabled)
                        {
                            if (ord.symbol_market == stg.Value.maker.symbol_market)
                            {
                                stg.Value.onOrdUpdate(ord, prevord);
                            }
                        }
                    }
                }
                else
                {
                    prevord = null;
                    foreach (var stg in this.strategies)
                    {
                        if (stg.Value.enabled)
                        {
                            if (ord.symbol_market == stg.Value.maker.symbol_market)
                            {
                                stg.Value.onOrdUpdate(ord, ord);
                            }
                        }
                    }
                }
                this.orders[ord.internal_order_id] = ord;

                if (this.live_orders.ContainsKey(ord.internal_order_id))
                {
                    while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
                    {
                    }
                    this.live_orders.Remove(ord.internal_order_id);
                    //addLog("[Live order removed at handleCancel] " + ord.ToString());
                    Volatile.Write(ref this.order_lock, 0);
                }

                if (ins != null)
                {
                    ins.updateOrders(ord);
                    
                    if (ins.live_orders.ContainsKey(ord.internal_order_id))
                    {
                        while (Interlocked.CompareExchange(ref ins.order_lock, 1, 0) != 0)
                        {
                        }
                        ins.live_orders.Remove(ord.internal_order_id);
                        Volatile.Write(ref ins.order_lock, 0);
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
                    if (filled_quantity > 0 && ord.order_type != orderType.Market)
                    {
                        if (ord.position_side == positionSide.Long)
                        {
                            if (ord.side == orderSide.Sell)
                            {
                                ins.longPosition.AddBalance(0, -filled_quantity);
                            }
                        }
                        else if (ord.position_side == positionSide.Short)
                        {
                            if (ord.side == orderSide.Buy)
                            {
                                ins.shortPosition.AddBalance(0, -filled_quantity);
                            }
                        }
                        else
                        {
                            addLog("handleCancel  " + ord.ToString());
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
                if (!this.Instruments.ContainsKey(ord.symbol_market))
                {
                    ord.init();
                    this.ord_client.ordUpdateStack.push(ord);
                }
                else if (ord.queued_count % 200001 == 200000)
                {
                    addLog("Unknown Order", Enums.logType.WARNING);
                    addLog(ord.ToString());
                    if (ord.queued_count > 1_000_000)
                    {
                        decimal filled_quantity = 0;
                        ins = this.Instruments[ord.symbol_market];
                        if (this.orders.ContainsKey(ord.market + ord.order_id))
                        {
                            DataSpotOrderUpdate prev = this.orders[ord.market + ord.order_id];
                            filled_quantity = ord.order_quantity - prev.filled_quantity;
                        }
                        else
                        {
                            filled_quantity = ord.order_quantity;
                        }
                        if (filled_quantity > 0 && ord.order_type != orderType.Market)
                        {
                            if (ord.position_side == positionSide.Long)
                            {
                                if (ord.side == orderSide.Sell)
                                {
                                    ins.longPosition.AddBalance(0, -filled_quantity);
                                }
                            }
                            else if (ord.position_side == positionSide.Short)
                            {
                                if (ord.side == orderSide.Buy)
                                {
                                    ins.shortPosition.AddBalance(0, -filled_quantity);
                                }
                            }
                            else
                            {
                                addLog("handleCancel2  " + ord.ToString());
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
                            this.ord_client.ordUpdateQueue.Enqueue(localord);
                        });
                    }
                }
                else
                {
                    ++(ord.queued_count);
                    this.ord_client.ordUpdateQueue.Enqueue(ord);
                }
            }
        }
        public void handleFilled(DataSpotOrderUpdate ord)
        {
            DataSpotOrderUpdate prevord = null;
            Instrument ins = null;

            if(this.Instruments.ContainsKey(ord.symbol_market))
            {
                ins = this.Instruments[ord.symbol_market];
            }

            if (this.ordIdMapping.ContainsKey(ord.market + ord.order_id))
            {
                //1. Strategy update
                //2. Update orders
                //3. Remove from live_orders
                //4. Update on the ins side.
                //5. update balance
                ord.internal_order_id = this.ordIdMapping[ord.market + ord.order_id];
                if (this.orders.ContainsKey(ord.internal_order_id))
                {
                    prevord = this.orders[ord.internal_order_id];
                    foreach (var stg in this.strategies)
                    {
                        if (stg.Value.enabled)
                        {
                            if (ord.symbol_market == stg.Value.maker.symbol_market)
                            {
                                stg.Value.onOrdUpdate(ord, prevord);
                            }
                        }
                    }

                    if ((ord.status < prevord.status || ord.filled_quantity < prevord.filled_quantity) && !(prevord.status == orderStatus.WaitCancel && ord.status == orderStatus.Open))
                    {
                        ord.update_time = DateTime.UtcNow;
                        this.order_pool.Enqueue(ord);
                        this.ordLogQueue.Enqueue(ord.ToString());
                        return;
                    }

                }
                else
                {
                    prevord = null;
                    foreach (var stg in this.strategies)
                    {
                        if (stg.Value.enabled)
                        {
                            if (ord.symbol_market == stg.Value.maker.symbol_market)
                            {
                                stg.Value.onOrdUpdate(ord, ord);
                            }
                        }

                    }
                }
                
                this.orders[ord.internal_order_id] = ord;
                if (ins != null)
                {
                    ins.updateOrders(ord);
                }

                if (this.live_orders.ContainsKey(ord.internal_order_id))
                {
                    while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
                    {
                    }
                    this.live_orders.Remove(ord.internal_order_id);
                    //addLog("[Live order removed at handleFilled] " + ord.ToString());
                    Volatile.Write(ref this.order_lock, 0);

                }
                if (ins != null)
                {
                    if (ins.live_orders.ContainsKey(ord.internal_order_id))
                    {
                        while (Interlocked.CompareExchange(ref ins.order_lock, 1, 0) != 0)
                        {
                        }
                        ins.live_orders.Remove(ord.internal_order_id);
                        Volatile.Write(ref ins.order_lock, 0);
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
                    if (filled_quantity > 0 && ord.order_type != orderType.Market)
                    {
                        if (ord.position_side == positionSide.Long)
                        {
                            if (ord.side == orderSide.Sell)
                            {
                                ins.longPosition.AddBalance(0, -filled_quantity);
                            }
                        }
                        else if (ord.position_side == positionSide.Short)
                        {
                            if (ord.side == orderSide.Buy)
                            {
                                ins.shortPosition.AddBalance(0, -filled_quantity);
                            }
                        }
                        else
                        {
                            addLog("handleFilled  " + ord.ToString());
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
                if (!this.Instruments.ContainsKey(ord.symbol_market))
                {
                    ord.init();
                    this.ord_client.ordUpdateStack.push(ord);
                }
                else if (ord.queued_count % 200001 == 200000)
                {
                    addLog("Unknown Order", Enums.logType.WARNING);
                    addLog(ord.ToString());
                    if (ord.queued_count > 1_000_000)
                    {
                        decimal filled_quantity = 0;
                        ins = this.Instruments[ord.symbol_market];
                        if (this.orders.ContainsKey(ord.market + ord.order_id))
                        {
                            DataSpotOrderUpdate prev = this.orders[ord.market + ord.order_id];
                            filled_quantity = ord.order_quantity - prev.filled_quantity;
                        }
                        else
                        {
                            filled_quantity = ord.order_quantity;
                        }
                        if (filled_quantity > 0 && ord.order_type != orderType.Market)
                        {
                            if (ord.position_side == positionSide.Long)
                            {
                                if (ord.side == orderSide.Sell)
                                {
                                    ins.longPosition.AddBalance(0, -filled_quantity);
                                }
                            }
                            else if (ord.position_side == positionSide.Short)
                            {
                                if (ord.side == orderSide.Buy)
                                {
                                    ins.shortPosition.AddBalance(0, -filled_quantity);
                                }
                            }
                            else
                            {
                                addLog("handleFilled2  " + ord.ToString());
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
                            this.ord_client.ordUpdateQueue.Enqueue(localord);
                        });
                    }
                }
                else
                {
                    ++(ord.queued_count);
                    this.ord_client.ordUpdateQueue.Enqueue(ord);
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
            this.order_lock = 0;
            foreach(var ins in this.Instruments.Values)
            {
                ins.order_lock = 0;
            }   
        }

        public void checkVirtualOrderQueue()
        {
            if(this.virtualMode)
            {
                DateTime current = DateTime.UtcNow;
                DataSpotOrderUpdate output = this.virtual_order_queue.Peek();
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
                        }
                        if (this.Instruments.ContainsKey(output.symbol_market))
                        {
                            Instrument ins = this.Instruments[output.symbol_market];
                            switch (output.status)
                            {
                                case orderStatus.WaitOpen:
                                    if (output.order_type == orderType.Market || (output.side == orderSide.Buy && output.order_price > ins.bestask.Item1) || (output.side == orderSide.Sell && output.order_price < ins.bestbid.Item1))
                                    {
                                        DataFill fill;
                                        fill = this.ord_client.fillStack.pop();
                                        if (fill == null)
                                        {
                                            fill = new DataFill();
                                        }
                                        update = this.ord_client.ordUpdateStack.pop();
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
                                        switch (output.side)
                                        {
                                            case orderSide.Buy:
                                                update.average_price = ins.adjusted_bestask.Item1;
                                                update.fee = feetype * update.filled_quantity * update.average_price;
                                                update.fee_asset = ins.quoteCcy;
                                                fill.fee_quote = update.fee;
                                                fill.fee_base = 0;
                                                fill.fee_unknown = 0;
                                                break;
                                            case orderSide.Sell:
                                                update.average_price = ins.adjusted_bestbid.Item1;
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
                                        this.ord_client.ordUpdateQueue.Enqueue(update);
                                        this.ord_client.fillQueue.Enqueue(fill);
                                    }
                                    else
                                    {
                                        update = this.ord_client.ordUpdateStack.pop();
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
                                            while (Interlocked.CompareExchange(ref this.virtual_order_lock, 1, 0) != 0)
                                            {
                                            }
                                            this.virtual_liveorders[update.internal_order_id] = update;
                                            Volatile.Write(ref this.virtual_order_lock, 0);
                                        }
                                        this.ord_client.ordUpdateQueue.Enqueue(update);
                                    }
                                    break;
                                case orderStatus.WaitCancel:
                                    while (Interlocked.CompareExchange(ref this.virtual_order_lock, 1, 0) != 0)
                                    {

                                    }
                                    if (this.virtual_liveorders.ContainsKey(output.internal_order_id))
                                    {

                                        update = this.ord_client.ordUpdateStack.pop();
                                        if (update == null)
                                        {
                                            update = new DataSpotOrderUpdate();
                                        }
                                        update.Copy(output);
                                        update.status = orderStatus.Canceled;
                                        this.virtual_liveorders.Remove(output.internal_order_id);
                                        this.ord_client.ordUpdateQueue.Enqueue(update);
                                    }
                                    else
                                    {
                                        //Do nothing
                                    }
                                    Volatile.Write(ref this.virtual_order_lock, 0);
                                    break;
                            }
                        }
                    }
                    output = this.virtual_order_queue.Peek();
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
                while (Interlocked.CompareExchange(ref this.virtual_order_lock, 1, 0) != 0)
                {

                }
                foreach (var item in this.virtual_liveorders)
                {
                    string key = item.Key;
                    DataSpotOrderUpdate ord = item.Value;
                    if (key != ord.internal_order_id)
                    {
                        this.addLog("The key and the order id didn't match while checking virtual orders.", Enums.logType.ERROR);
                        this.addLog($"The dictionary key:{key} The internal order id:{ord.internal_order_id}", Enums.logType.ERROR);
                        foreach(var kv in this.ordIdMapping)
                        {
                            if(kv.Value == key)
                            {
                                addLog(key + " is registered as " + kv.Key + " in the mapping");
                            }
                        }
                    }
                    if (ord.symbol_market == ins.symbol_market)
                    {
                        while(Interlocked.CompareExchange(ref ins.quotes_lock, 1, 0) != 0)
                        {

                        }
                        switch (ord.side)
                        {
                            case orderSide.Buy:
                                if (ins.bestask.Item1 < ord.order_price || (last_trade != null && last_trade.symbol + "@" + last_trade.market == ord.symbol_market && last_trade.price < ord.order_price))
                                {
                                    DataSpotOrderUpdate output;

                                    output = this.ord_client.ordUpdateStack.pop();
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
                                    fill = this.ord_client.fillStack.pop();
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
                                    fill.msg += " Best Ask:" + ins.bestask.Item1.ToString();
                                    if(last_trade != null)
                                    {
                                        fill.msg += " Last traded price:" + last_trade.price.ToString();
                                    }
                                    output.msg += " Best Ask:" + ins.bestask.Item1.ToString();
                                    if (last_trade != null)
                                    {
                                        output.msg += " Last traded price:" + last_trade.price.ToString();
                                    }
                                    this.ord_client.ordUpdateQueue.Enqueue(output);
                                    removing.Add(key);
                                    this.ord_client.fillQueue.Enqueue(fill);
                                }
                                break;
                            case orderSide.Sell:
                                if (ins.bestbid.Item1 > ord.order_price || (last_trade != null && last_trade.symbol + "@" + last_trade.market == ord.symbol_market && last_trade.price > ord.order_price))
                                {
                                    DataSpotOrderUpdate output;
                                    output = this.ord_client.ordUpdateStack.pop();
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
                                    fill = this.ord_client.fillStack.pop();
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
                                    fill.msg += " Best Bid:" + ins.bestbid.Item1.ToString();
                                    if (last_trade != null)
                                    {
                                        fill.msg += " Last traded price:" + last_trade.price.ToString();
                                    }
                                    output.msg += " Best Bid:" + ins.bestbid.Item1.ToString();
                                    if (last_trade != null)
                                    {
                                        output.msg += " Last traded price:" + last_trade.price.ToString();
                                    }
                                    this.ord_client.ordUpdateQueue.Enqueue(output);
                                    removing.Add(key);
                                    this.ord_client.fillQueue.Enqueue(fill);
                                }
                                break;
                        }
                        Volatile.Write(ref ins.quotes_lock, 0);
                    }
                }
                foreach (string key in removing)
                {
                    this.virtual_liveorders.Remove(key);
                }
                Volatile.Write(ref this.virtual_order_lock, 0);
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
                while(miQueue.Value.Count > 0)
                {
                    mi = miQueue.Value.Peek();
                    if (currentTime - mi.filled_time > TimeSpan.FromSeconds(miQueue.Key))
                    {
                        mi = miQueue.Value.Dequeue();
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

