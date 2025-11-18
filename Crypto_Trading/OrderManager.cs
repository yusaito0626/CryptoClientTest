using Bybit.Net.Enums;
using Coinbase.Net.Objects.Models;
using Crypto_Clients;
using CryptoClients.Net;
using CryptoClients.Net.Enums;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Requests;
using CryptoExchange.Net.SharedApis;
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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using XT.Net.Objects.Models;

using Enums;

namespace Crypto_Trading
{


    public class OrderManager
    {

        Crypto_Clients.Crypto_Clients ord_client = Crypto_Clients.Crypto_Clients.GetInstance();

        public volatile int order_lock;
        public Dictionary<string, DataSpotOrderUpdate> orders;
        public Dictionary<string, DataSpotOrderUpdate> live_orders;

        public Queue<DataSpotOrderUpdate> order_pool;
        public int orderLifeTime = 60; 

        const int SENDINGORD_STACK_SIZE = 1000;
        public ConcurrentQueue<sendingOrder> sendingOrders;
        public ConcurrentStack<sendingOrder> sendingOrdersStack;
        public volatile int push_count;
        public volatile int pop_count;
        Thread processingOrdTh;
        CancellationTokenSource OrderProcessingStop;
        public Dictionary<string, string> ordIdMapping;

        public volatile int virtual_order_lock;
        public Dictionary<string, DataSpotOrderUpdate> virtual_liveorders;
        public Dictionary<string, DataSpotOrderUpdate> disposed_orders;// The key is market + order_id, as the internal_order_id might not be exist.

        public Dictionary<string, modifingOrd> modifingOrders;
        public ConcurrentStack<modifingOrd> modifingOrdStack;

        public Dictionary<string, WebSocketState> connections;

        public string outputPath;
        FileStream f;
        StreamWriter sw;
        private bool ord_logged;
        public ConcurrentQueue<string> ordLogQueue;
        public Thread ordLoggingTh;

        public ConcurrentQueue<DataFill> filledOrderQueue;

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

        Stopwatch sw_updateOrders;
        Stopwatch sw_updateFills;

        private OrderManager() 
        {
            this.aborting = false;
            this.updateOrderStopped = false;
            this.virtualMode = true;
            this.order_lock = 0;
            this.orders = new Dictionary<string, DataSpotOrderUpdate>();
            this.live_orders = new Dictionary<string, DataSpotOrderUpdate>();
            this.virtual_order_lock = 0;
            this.virtual_liveorders = new Dictionary<string, DataSpotOrderUpdate>();
            this.disposed_orders = new Dictionary<string, DataSpotOrderUpdate>();

            this.order_pool = new Queue<DataSpotOrderUpdate>();

            this.strategies = new Dictionary<string, Strategy>();

            this.connections = new Dictionary<string, WebSocketState>();

            this.modifingOrders = new Dictionary<string, modifingOrd>();
            this.modifingOrdStack = new ConcurrentStack<modifingOrd>();

            this.ordLogQueue = new ConcurrentQueue<string>();

            this.ord_logged = false;

            this.id_number = 0;

            int i = 0;

            this.sendingOrders = new ConcurrentQueue<sendingOrder>();
            this.sendingOrdersStack = new ConcurrentStack<sendingOrder>();
            this.push_count = 0;
            this.pop_count = 0;

            while(i < SENDINGORD_STACK_SIZE)
            {
                this.sendingOrdersStack.Push(new sendingOrder());
                ++i;
            }
            this.ordIdMapping = new Dictionary<string, string>();

            i = 0;
            while (i < MOD_STACK_SIZE)
            {
                this.modifingOrdStack.Push(new modifingOrd());
                ++i;
            }

            //this._addLog = Console.WriteLine;
            this.sw_updateOrders = new Stopwatch();
            this.sw_updateFills = new Stopwatch();

            this.OrderProcessingStop = new CancellationTokenSource();
            //this.processingOrdTh = new Thread(()=>
            //{
            //    this.processingOrders(this.OrderProcessingStop.Token);
            //});
            //this.processingOrdTh.Start();

            this.ready = false;
        }

        ~OrderManager()
        {
            //this.OrderProcessingStop.Cancel();
        }

        public async Task connectPrivateChannel(string market)
        {
            ThreadManager thManager = ThreadManager.GetInstance();
            Func<Task<(bool, double)>> onMsg;
            Action onClosing;
            switch (market)
            {
                case "bitbank":
                    await this.ord_client.bitbank_client.connectPrivateAsync();//This call includes creation of pubnub
                    this.connections[market] = this.ord_client.bitbank_client.GetSocketStatePrivate();
                    break;
                case "coincheck":
                    await this.ord_client.coincheck_client.connectPrivateAsync();
                    this.ord_client.coincheck_client.onPrivateMessage = this.ord_client.onConcheckPrivateMessage;
                    onClosing = async () =>
                    {
                        await this.ord_client.coincheck_client.onClosingPrivate(this.ord_client.onConcheckPrivateMessage);
                    };
                    thManager.addThread(market + "Private", this.ord_client.coincheck_client.ListeningPrivate,onClosing,this.ord_client.coincheck_client.onListenPrivateOnError);
                    this.connections[market] = this.ord_client.coincheck_client.GetSocketStatePrivate();
                    break;
                case "bittrade":
                    await this.ord_client.bittrade_client.connectPrivateAsync();
                    this.ord_client.bittrade_client.onPrivateMessage = this.ord_client.onBitTradePrivateMessage;
                    onClosing = async () =>
                    {
                        await this.ord_client.bittrade_client.onClosingPrivate(this.ord_client.onBitTradePrivateMessage);
                    };
                    thManager.addThread(market + "Private", this.ord_client.bittrade_client.ListeningPrivate,onClosing,this.ord_client.bittrade_client.onListenPrivateOnError);
                    this.connections[market] = this.ord_client.bittrade_client.GetSocketStatePrivate();
                    break;
            }
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
                    default:
                        break;
                }
            }
        }
        public void startOrderLogging()
        {
            this.ordLoggingTh = new Thread(() =>
            {
                this.ordLogging();
            });
            this.ordLoggingTh.Start();
        }
        public void setInstruments(Dictionary<string, Instrument> dic)
        {
            this.Instruments = dic;
        }

        public async Task<string> placeNewSpotOrder(Instrument ins, orderSide side, orderType ordtype, decimal quantity, decimal price, timeInForce? timeinforce = null,bool sendNow = true,bool wait = false)
        {
            sendingOrder ord;
            string ordid;
            while(!this.sendingOrdersStack.TryPop(out ord))
            {

            }
            Interlocked.Increment(ref this.pop_count);
            ordid = this.getInternalOrdId(ins.market);
            ord.internalOrdId = ordid;
            ord.action = orderAction.New;
            ord.ins = ins;
            ord.side = side;
            ord.order_type = ordtype;
            ord.quantity = quantity;
            ord.price = price;
            ord.time_in_force = timeinforce;
            if(sendNow)
            {
                if(wait)
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
        public async Task<string> placeCancelSpotOrder(Instrument ins, string orderId, bool sendNow = true, bool wait = false)
        {
            sendingOrder ord;
            while (!this.sendingOrdersStack.TryPop(out ord))
            {

            }
            Interlocked.Increment(ref this.pop_count);
            //ordid = this.getInternalOrdId(ins.market);
            //ord.internalOrdId = ordid;
            ord.action = orderAction.Can;
            ord.ins = ins;

            ord.ref_IntOrdId = orderId;

            if(sendNow)
            {
                if(wait)
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
        public async Task<IEnumerable<string>> placeCancelSpotOrders(Instrument ins,IEnumerable<string> order_ids,bool sendNow = true,bool wait = false)
        {
            sendingOrder ord;
            while (!this.sendingOrdersStack.TryPop(out ord))
            {

            }
            Interlocked.Increment(ref this.pop_count);
            ord.action = orderAction.Can;
            ord.ins = ins;

            ord.order_ids = order_ids;

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
        public async Task<string> placeModSpotOrder(Instrument ins, string orderId, decimal quantity, decimal price,bool waitCancel, bool sendNow = true,bool wait = false)
        {
            sendingOrder ord;
            string ordid;
            while (!this.sendingOrdersStack.TryPop(out ord))
            {

            }
            Interlocked.Increment(ref this.pop_count);
            ordid = this.getInternalOrdId(ins.market);
            ord.internalOrdId = ordid;
            ord.ref_IntOrdId = orderId;
            ord.action = orderAction.Mod;
            ord.ins = ins;
            ord.quantity = quantity;
            ord.price = price;
            ord.waitCancel = waitCancel;
            if(sendNow)
            {
                if(wait)
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

        async public Task<DataSpotOrderUpdate?> processNewOrder(sendingOrder sndOrd)
        {
            DataSpotOrderUpdate? output = null;
            JsonDocument js;
            decimal quantity;
            if (this.virtualMode)
            {
                quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                while (!this.ord_client.ordUpdateStack.TryPop(out output))
                {

                }
                output.isVirtual = true;
                output.order_id = this.getVirtualOrdId();
                output.symbol = sndOrd.ins.symbol;
                output.market = sndOrd.ins.market;
                output.symbol_market = sndOrd.ins.symbol_market;
                output.internal_order_id = sndOrd.internalOrdId;
                output.side = sndOrd.side;
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
                Thread.Sleep(this.latency);
                if (sndOrd.order_type == orderType.Market || (sndOrd.side == orderSide.Buy && sndOrd.price > sndOrd.ins.bestask.Item1) || (sndOrd.side == orderSide.Sell && sndOrd.price < sndOrd.ins.bestbid.Item1))
                {
                    DataFill fill;
                    while (!this.ord_client.fillStack.TryPop(out fill))
                    {

                    }
                    output.timestamp = DateTime.UtcNow;
                    output.update_time = DateTime.UtcNow;
                    output.status = orderStatus.Filled;
                    output.filled_quantity = quantity;
                    if(sndOrd.order_type == orderType.Limit || sndOrd.order_type == orderType.LimitMaker)
                    {
                        switch (sndOrd.side)
                        {
                            case orderSide.Buy:
                                sndOrd.ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                                break;
                            case orderSide.Sell:
                                sndOrd.ins.baseBalance.AddBalance(0, output.order_quantity);
                                break;
                        }
                    }
                    decimal feetype = 0;
                    if (sndOrd.order_type == orderType.Market)
                    {
                        feetype = sndOrd.ins.taker_fee;
                    }
                    else
                    {
                        feetype = sndOrd.ins.maker_fee;
                    }
                    switch (sndOrd.side)
                    {
                        case orderSide.Buy:
                            output.average_price = sndOrd.ins.adjusted_bestask.Item1;
                            output.fee = feetype * output.filled_quantity * output.average_price;
                            output.fee_asset = sndOrd.ins.quoteCcy;
                            fill.fee_quote = output.fee;
                            fill.fee_base = 0;
                            fill.fee_unknown = 0;
                            break;
                        case orderSide.Sell:
                            output.average_price = sndOrd.ins.adjusted_bestbid.Item1;
                            output.fee = feetype * output.filled_quantity;
                            output.fee_asset = sndOrd.ins.baseCcy;
                            fill.fee_quote = 0;
                            fill.fee_base = output.fee;
                            fill.fee_unknown = 0;
                            break;
                    }
                    fill.order_id = output.order_id;
                    fill.internal_order_id = output.internal_order_id;
                    fill.symbol = sndOrd.ins.symbol;
                    fill.market = sndOrd.ins.market;
                    fill.symbol_market = sndOrd.ins.symbol_market;
                    fill.side = output.side;
                    fill.quantity = output.filled_quantity;
                    fill.price = output.average_price;
                    fill.timestamp = output.timestamp;
                    fill.filled_time = fill.timestamp;
                    fill.order_type = output.order_type;
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                    this.ord_client.fillQueue.Enqueue(fill);
                }
                else
                {
                    output.status = orderStatus.WaitOpen;
                    output.filled_quantity = 0;
                    output.average_price = 0;
                    output.fee = 0;
                    output.fee_asset = "";
                    this.orders[output.internal_order_id] = output;
                    string ordId = output.order_id;
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

                    }
                    output.isVirtual = true;
                    output.order_id = ordId;
                    output.symbol = sndOrd.ins.symbol;
                    output.market = sndOrd.ins.market;
                    output.internal_order_id = sndOrd.internalOrdId;
                    output.symbol_market = sndOrd.ins.symbol_market;
                    output.side = sndOrd.side;
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
                    output.status = orderStatus.Open;
                    output.filled_quantity = 0;
                    output.average_price = 0;
                    output.fee = 0;
                    output.fee_asset = "";
                    switch (sndOrd.side)
                    {
                        case orderSide.Buy:
                            sndOrd.ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                            break;
                        case orderSide.Sell:
                            sndOrd.ins.baseBalance.AddBalance(0, output.order_quantity);
                            break;
                    }
                    if (this.virtual_liveorders.ContainsKey(output.internal_order_id))
                    {
                        this.virtual_liveorders[output.internal_order_id] = output;
                    }
                    else
                    {
                        while (Interlocked.CompareExchange(ref this.virtual_order_lock, 1, 0) != 0)
                        {
                        }
                        this.virtual_liveorders[output.internal_order_id] = output;
                        Volatile.Write(ref this.virtual_order_lock, 0);
                    }
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else if (sndOrd.ins.market == "bitbank")
            {
                quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                DateTime sendTime = DateTime.UtcNow;
                if (sndOrd.order_type == orderType.Limit)
                {
                    js = await this.ord_client.bitbank_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.order_type.ToString().ToLower(), sndOrd.side.ToString().ToLower(), sndOrd.price, quantity, true);
                }
                else
                {
                    js = await this.ord_client.bitbank_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.order_type.ToString().ToLower(), sndOrd.side.ToString().ToLower(), sndOrd.price, quantity, false);
                }

                if (js.RootElement.GetProperty("success").GetUInt16() == 1)
                {
                    var ord_obj = js.RootElement.GetProperty("data");
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

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
                    switch (sndOrd.side)
                    {
                        case orderSide.Buy:
                            sndOrd.ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                            break;
                        case orderSide.Sell:
                            sndOrd.ins.baseBalance.AddBalance(0, output.order_quantity);
                            break;
                    }
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    int code = js.RootElement.GetProperty("data").GetProperty("code").GetInt32();
                    switch (code)
                    {
                        case 10000:
                            this.addLog("New order failed. The URL doesn't exist. Error code: 10000   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            break;
                        case 10001:
                            this.addLog("New order failed. System error, Contact support Error code:10001   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10002:
                            this.addLog("New order failed. Improper Json format. Error code:10002   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            break;
                        case 10003:
                            this.addLog("New order failed.  System error, Contact support Error code:10003   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10005:
                            this.addLog("New order failed.  Timeout error. Error code:10005   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10007:
                            this.addLog("New order failed.  Under maintenance. Error code:10007   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            break;
                        case 10008:
                            this.addLog("New order failed. The system is busy. Error code:10008   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10009:
                            this.addLog("New order failed. Too many request. Error code:10009   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 70010:
                        case 70011:
                        case 70012:
                        case 70013:
                        case 70014:
                        case 70015:
                            this.addLog("New order failed. The system is busy Error code:" + code.ToString() + "   ord_id:" + sndOrd.internalOrdId , Enums.logType.WARNING);
                            break;
                        default:
                            this.addLog("New Order Failed   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            this.addLog(js.RootElement.GetRawText(), Enums.logType.ERROR);
                            break;
                    }
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

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
            else if (sndOrd.ins.market == "coincheck")
            {
                DateTime sendTime = DateTime.UtcNow;
                if (sndOrd.order_type == orderType.Limit)
                {
                    quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                    js = await this.ord_client.coincheck_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToLower(), sndOrd.price, quantity, "post_only");
                }
                else
                {
                    //decimal order_price;
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
                        quantity = quantity * sndOrd.ins.adjusted_bestask.Item1;
                        js = await this.ord_client.coincheck_client.placeMarketNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToLower(), 0, quantity);
                    }
                    else
                    {
                        quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                        js = await this.ord_client.coincheck_client.placeMarketNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToLower(), 0, quantity);
                    }
                    //Market Order
                }
                if (js.RootElement.GetProperty("success").GetBoolean())
                {
                    JsonElement ord_obj = js.RootElement;
                    string line = JsonSerializer.Serialize(ord_obj);
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

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
                    //this.orders.TryAdd(output.order_id, output);
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

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
                    string err = js.RootElement.GetProperty("error").GetString();
                    if (err.StartsWith("Amount"))
                    {
                        this.addLog(err, Enums.logType.WARNING);
                    }
                    else
                    {
                        string msg = JsonSerializer.Serialize(js);
                        this.addLog(msg, Enums.logType.ERROR);
                    }
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

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
            else if (sndOrd.ins.market == "bittrade")
            {
                quantity = Math.Round(sndOrd.quantity / sndOrd.ins.quantity_unit) * sndOrd.ins.quantity_unit;
                decimal order_price = sndOrd.price;
                DateTime sendTime = DateTime.UtcNow;
                if (sndOrd.order_type== orderType.Limit)
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
                    //Market Order
                    js = await this.ord_client.bittrade_client.placeNewOrder(sndOrd.ins.symbol, sndOrd.side.ToString().ToLower(), order_price, quantity, false);
                }

                if (js.RootElement.GetProperty("status").GetString() == "ok")
                {
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

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
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

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
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

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
            this.sendingOrdersStack.Push(sndOrd);
            Interlocked.Increment(ref this.push_count);
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
                    while (!this.modifingOrdStack.TryPop(out mod))
                    {

                    }
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
                    this.sendingOrdersStack.Push(sndOrd);
                    Interlocked.Increment(ref this.push_count);
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
                        while(!this.sendingOrdersStack.TryPop(out sndOrd2))
                        {

                        }
                        Interlocked.Increment(ref this.pop_count);
                        sndOrd2.copy(sndOrd);
                        sndOrd2.msg += " ModOrder from " + sndOrd.ref_IntOrdId + " ";
                        this.processCanOrder(sndOrd);
                        Thread.Sleep(1);
                        output = await this.processNewOrder(sndOrd2);
                    }
                    else
                    {
                        sndOrd.init();
                        this.sendingOrdersStack.Push(sndOrd);
                        Interlocked.Increment(ref this.push_count);
                        output = null;
                    }
                    return output;
                }
                else
                {
                    addLog("Order not found. order id:" + sndOrd.ref_IntOrdId);
                    sndOrd.init();
                    this.sendingOrdersStack.Push(sndOrd);
                    Interlocked.Increment(ref this.push_count);
                    return null;
                }
            }
        }
        async public Task<DataSpotOrderUpdate?> processCanOrder(sendingOrder sndOrd)
        {
            DataSpotOrderUpdate? output = null;
            JsonDocument js;
            if (this.virtualMode)
            {
                Thread.Sleep(this.latency);
                while (!this.ord_client.ordUpdateStack.TryPop(out output))
                {

                }
                DataSpotOrderUpdate prev = this.orders[sndOrd.ref_IntOrdId];
                output.isVirtual = true;
                output.order_id = prev.order_id;
                output.symbol = sndOrd.ins.symbol;
                output.market = sndOrd.ins.market;
                output.symbol_market = sndOrd.ins.symbol_market;
                output.internal_order_id = sndOrd.ref_IntOrdId;
                if (prev.filled_quantity == prev.order_quantity)
                {
                    output.status = orderStatus.Filled;
                }
                else
                {
                    output.status = orderStatus.Canceled;
                }
                output.side = prev.side;
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
                while (Interlocked.CompareExchange(ref this.virtual_order_lock, 1, 0) != 0)
                {

                }
                if (this.virtual_liveorders.ContainsKey(prev.internal_order_id))
                {
                    this.virtual_liveorders.Remove(prev.internal_order_id);
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    output.init();
                    this.ord_client.ordUpdateStack.Push(output);
                    output = null;
                }
                Volatile.Write(ref this.virtual_order_lock, 0);
            }
            else if (sndOrd.ins.market == "bitbank")
            {
                DateTime sendTime = DateTime.UtcNow;
                DataSpotOrderUpdate prev = this.orders[sndOrd.ref_IntOrdId];
                js = await this.ord_client.bitbank_client.placeCanOrder(sndOrd.ins.symbol, prev.order_id);
                if (js.RootElement.GetProperty("success").GetUInt16() == 1)
                {
                    var ord_obj = js.RootElement.GetProperty("data");
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {
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
                            this.addLog("Cancel order failed. System error, Contact support Error code:10001   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10002:
                            this.addLog("Cancel order failed. Improper Json format. Error code:10002   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            break;
                        case 10003:
                            this.addLog("Cancel order failed.  System error, Contact support Error code:10003   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10005:
                            this.addLog("Cancel order failed.  Timeout error. Error code:10005   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10007:
                            this.addLog("Cancel order failed.  Under maintenance. Error code:10007   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            break;
                        case 10008:
                            this.addLog("Cancel order failed. The system is busy. Error code:10008   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
                            break;
                        case 10009:
                            this.addLog("Cancel order failed. Too many request. Error code:10009   ord_id:" + sndOrd.internalOrdId, Enums.logType.WARNING);
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
                            this.addLog("Cancel order failed. The system is busy Error code:   ord_id:" + sndOrd.internalOrdId + code.ToString(), Enums.logType.WARNING);
                            break;
                        default:
                            this.addLog("Cancel Order Failed   ord_id:" + sndOrd.internalOrdId, Enums.logType.ERROR);
                            this.addLog(js.RootElement.GetRawText(), Enums.logType.ERROR);
                            break;
                    }
                    //while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    //{

                    //}
                    //output.status = orderStatus.INVALID;
                    //output.timestamp = sendTime;
                    //output.internal_order_id = sndOrd.internalOrdId;
                    //output.side = sndOrd.side;
                    //output.symbol = sndOrd.ins.symbol;
                    //output.market = sndOrd.ins.market;
                    //output.symbol_market = sndOrd.ins.symbol_market;
                    //output.order_quantity = sndOrd.quantity;
                    //output.order_price = sndOrd.price;
                    //output.filled_quantity = 0;
                    //output.average_price = 0;
                    //output.fee = 0;
                    //output.fee_asset = "";
                    //output.is_trigger_order = true;
                    //output.last_trade = "";
                    //output.msg = sndOrd.msg;
                    //this.ord_client.ordUpdateQueue.Enqueue(output);
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
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {
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
            }
            else if (sndOrd.ins.market == "bittrade")
            {
                DateTime sendTime = DateTime.UtcNow;
                DataSpotOrderUpdate prev = this.orders[sndOrd.ref_IntOrdId];
                js = await this.ord_client.bittrade_client.placeCanOrder(prev.order_id);
                if (js.RootElement.GetProperty("status").GetString() == "ok")
                {
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {
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
            this.sendingOrdersStack.Push(sndOrd);
            Interlocked.Increment(ref this.push_count);
            return output;
        }

        async public Task<List<DataSpotOrderUpdate>> processCanOrders(sendingOrder sndOrd)
        {
            List<DataSpotOrderUpdate> output = new List<DataSpotOrderUpdate>();
            DataSpotOrderUpdate? ordObj = null;
            List<JsonDocument> js;
            if (this.virtualMode)
            {
                Thread.Sleep(this.latency);
                foreach (var ordid in sndOrd.order_ids)
                {
                    while (!this.ord_client.ordUpdateStack.TryPop(out ordObj))
                    {

                    }
                    DataSpotOrderUpdate prev = this.orders[ordid];
                    ordObj.isVirtual = true;
                    ordObj.order_id = prev.order_id;
                    ordObj.symbol = sndOrd.ins.symbol;
                    ordObj.market = sndOrd.ins.market;
                    ordObj.symbol_market = sndOrd.ins.symbol_market;
                    ordObj.internal_order_id = ordid;
                    if (prev.filled_quantity == prev.order_quantity)
                    {
                        ordObj.status = orderStatus.Filled;
                    }
                    else
                    {
                        ordObj.status = orderStatus.Canceled;
                    }
                    ordObj.side = prev.side;
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

                    while (Interlocked.CompareExchange(ref this.virtual_order_lock, 1, 0) != 0)
                    {

                    }
                    if (this.virtual_liveorders.ContainsKey(prev.internal_order_id))
                    {
                        output.Add(ordObj);
                        this.virtual_liveorders.Remove(prev.internal_order_id);
                        this.ord_client.ordUpdateQueue.Enqueue(ordObj);
                    }
                    else
                    {
                        ordObj.init();
                        this.ord_client.ordUpdateStack.Push(ordObj);
                        ordObj = null;
                    }
                    Volatile.Write(ref this.virtual_order_lock, 0);
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
                            while (!this.ord_client.ordUpdateStack.TryPop(out ordObj))
                            {
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
                                this.addLog("Cancel order failed. The URL doesn't exist. Error code: 10000   orderCount:" + ord_ids.Count.ToString(), Enums.logType.ERROR);
                                break;
                            case 10001:
                                this.addLog("Cancel order failed. System error, Contact support Error code:10001   orderCount:" + ord_ids.Count.ToString(), Enums.logType.WARNING);
                                break;
                            case 10002:
                                this.addLog("Cancel order failed. Improper Json format. Error code:10002   orderCount:" + ord_ids.Count.ToString(), Enums.logType.ERROR);
                                break;
                            case 10003:
                                this.addLog("Cancel order failed.  System error, Contact support Error code:10003   orderCount:" + ord_ids.Count.ToString(), Enums.logType.WARNING);
                                break;
                            case 10005:
                                this.addLog("Cancel order failed.  Timeout error. Error code:10005   orderCount:" + ord_ids.Count.ToString(), Enums.logType.WARNING);
                                break;
                            case 10007:
                                this.addLog("Cancel order failed.  Under maintenance. Error code:10007   orderCount:" + ord_ids.Count.ToString(), Enums.logType.ERROR);
                                break;
                            case 10008:
                                this.addLog("Cancel order failed. The system is busy. Error code:10008   orderCount:" + ord_ids.Count.ToString(), Enums.logType.WARNING);
                                break;
                            case 10009:
                                this.addLog("Cancel order failed. Too many request. Error code:10009   orderCount:" + ord_ids.Count.ToString(), Enums.logType.WARNING);
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
                                this.addLog("Cancel order failed. The system is busy Error code:" + code.ToString() + "   orderCount:" + ord_ids.Count.ToString(), Enums.logType.WARNING);
                                break;
                            default:
                                this.addLog("Cancel Order Failed   orderCount:" + ord_ids.Count.ToString(), Enums.logType.ERROR);
                                this.addLog(elem.RootElement.GetRawText(), Enums.logType.ERROR);
                                break;
                        }
                        //foreach(string ordid in sndOrd.order_ids)
                        //{
                        //    while (!this.ord_client.ordUpdateStack.TryPop(out ordObj))
                        //    {

                        //    }
                        //    DataSpotOrderUpdate prev = this.orders[ordid];

                        //    ordObj.status = orderStatus.Open;
                        //    ordObj.timestamp = sendTime;
                        //    ordObj.internal_order_id = ordid;
                        //    ordObj.side = prev.side;
                        //    ordObj.symbol = prev.symbol;
                        //    ordObj.market = prev.market;
                        //    ordObj.symbol_market = prev.symbol_market;
                        //    ordObj.order_quantity = prev.order_quantity;
                        //    ordObj.order_price = prev.order_price;
                        //    ordObj.filled_quantity = 0;
                        //    ordObj.average_price = 0;
                        //    ordObj.fee = 0;
                        //    ordObj.fee_asset = "";
                        //    ordObj.is_trigger_order = true;
                        //    ordObj.last_trade = "";
                        //    ordObj.msg = sndOrd.msg;
                        //    this.ord_client.ordUpdateQueue.Enqueue(ordObj);
                        //}

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
                        while (!this.ord_client.ordUpdateStack.TryPop(out ordObj))
                        {
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
                        //foreach (string ordid in sndOrd.order_ids)
                        //{
                        //    while (!this.ord_client.ordUpdateStack.TryPop(out ordObj))
                        //    {

                        //    }
                        //    DataSpotOrderUpdate prev = this.orders[ordid];

                        //    ordObj.status = orderStatus.Open;
                        //    ordObj.timestamp = sendTime;
                        //    ordObj.internal_order_id = ordid;
                        //    ordObj.side = prev.side;
                        //    ordObj.symbol = prev.symbol;
                        //    ordObj.market = prev.market;
                        //    ordObj.symbol_market = prev.symbol_market;
                        //    ordObj.order_quantity = prev.order_quantity;
                        //    ordObj.order_price = prev.order_price;
                        //    ordObj.filled_quantity = 0;
                        //    ordObj.average_price = 0;
                        //    ordObj.fee = 0;
                        //    ordObj.fee_asset = "";
                        //    ordObj.is_trigger_order = true;
                        //    ordObj.last_trade = "";
                        //    ordObj.msg = sndOrd.msg;
                        //    this.ord_client.ordUpdateQueue.Enqueue(ordObj);
                        //}
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
                        while (!this.ord_client.ordUpdateStack.TryPop(out ordObj))
                        {
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
            this.sendingOrdersStack.Push(sndOrd);
            Interlocked.Increment(ref this.push_count);
            return output;
        }
        public void processingOrders(CancellationToken cancellationToken)
        {
            int i = 0;
            sendingOrder ord;
            while (true)
            {
                if(this.sendingOrders.TryDequeue(out ord))
                {
                    switch(ord.action)
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
                    //ord.init();
                    //this.sendingOrdersStack.Push(ord);
                    i = 0;
                }
                else
                {
                    if(cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    ++i;
                    if(i > 1000)
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
            while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
            {

            }
            foreach (var ord in this.live_orders.Values)
            {
                if(this.Instruments.ContainsKey(ord.symbol_market))
                {
                    ins = this.Instruments[ord.symbol_market];
                    this.placeCancelSpotOrder(ins, ord.internal_order_id, true);
                }
                else
                {
                    addLog("Unknown order.", logType.WARNING);
                    addLog(ord.ToString(), logType.WARNING);
                }
            }
            Volatile.Write(ref this.order_lock, 0);
        }


        public void pushbackFill(DataFill fill)
        {
            fill.init();
            this.ord_client.fillStack.Push(fill);
        }

        public async Task<bool> updateFills(Action start, Action end, CancellationToken ct, int spinningMax)
        {
            DataFill fill;
            Instrument ins = null;
            var spinner = new SpinWait();
            bool ret = true;
            try
            {
                while (true)
                {
                    while (this.ord_client.fillQueue.TryDequeue(out fill))
                    {
                        if (this.ordIdMapping.ContainsKey(fill.market + fill.order_id))
                        {
                            start();
                            fill.internal_order_id = this.ordIdMapping[fill.market + fill.order_id];
                            foreach (var stg in this.strategies)
                            {
                                if (stg.Value.maker.symbol_market == fill.symbol_market)
                                {
                                    await stg.Value.onFill(fill);
                                }
                            }
                            if (this.Instruments.ContainsKey(fill.symbol_market))
                            {
                                if (fill.market == "coincheck")
                                {
                                    if (this.orders.ContainsKey(fill.internal_order_id))
                                    {
                                        DataSpotOrderUpdate filled = this.orders[fill.internal_order_id];
                                        filled.average_price = fill.price;//For viewing purpose
                                    }
                                }
                                ins = this.Instruments[fill.symbol_market];
                                ins.updateFills(fill);
                            }
                            this.ordLogQueue.Enqueue(fill.ToString());
                            this.filledOrderQueue.Enqueue(fill);
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
                                    fill.internal_order_id = fill.market + fill.order_id;
                                    foreach (var stg in this.strategies)
                                    {
                                        if (stg.Value.maker.symbol_market == fill.symbol_market)
                                        {
                                            await stg.Value.onFill(fill);
                                        }
                                    }
                                    if (this.Instruments.ContainsKey(fill.symbol_market))
                                    {
                                        if (fill.market == "coincheck")
                                        {
                                            if (this.orders.ContainsKey(fill.internal_order_id))
                                            {
                                                DataSpotOrderUpdate filled = this.orders[fill.internal_order_id];
                                                filled.average_price = fill.price;//For viewing purpose
                                            }
                                        }
                                        ins = this.Instruments[fill.symbol_market];
                                        ins.updateFills(fill);
                                    }
                                    this.ordLogQueue.Enqueue(fill.ToString());
                                    this.filledOrderQueue.Enqueue(fill);
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

                    // 2) まだ空ならイベント待機（低CPU）
                    //if (_queue.IsEmpty)
                    //{
                    //    _signal.Wait();
                    //    _signal.Reset();
                    //}
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

        public async void updateFillOnClosing()
        {
            //while (this.ord_client.fillQueue.Count() > 0)
            //{
            //    await this._updateFill();
            //}
        }
        public async Task<bool> updateOrders(Action start,Action end,CancellationToken ct,int spinningMax)
        {
            DataSpotOrderUpdate ord;
            DataSpotOrderUpdate prevord;
            Instrument ins = null;
            modifingOrd mod;
            var spinner = new SpinWait();
            bool ret = true;
            try
            {
                while (true)
                {
                    while (this.ord_client.ordUpdateQueue.TryDequeue(out ord))
                    {
                        start();

                        if (this.Instruments.ContainsKey(ord.symbol_market))
                        {
                            ins = this.Instruments[ord.symbol_market];
                        }
                        if (ord.status == orderStatus.INVALID)
                        {
                            this.orders[ord.internal_order_id] = ord;
                            this.ordLogQueue.Enqueue(ord.ToString());
                        }
                        else if (ord.status == orderStatus.WaitOpen)
                        {
                            this.ordLogQueue.Enqueue(ord.ToString());
                        }
                        else if (ord.status == orderStatus.WaitMod)
                        {
                            //Undefined
                            this.ordLogQueue.Enqueue(ord.ToString());
                        }
                        else if (ord.status == orderStatus.WaitCancel)
                        {
                            if (this.orders.ContainsKey(ord.internal_order_id))
                            {
                                prevord = this.orders[ord.internal_order_id];
                                if (prevord.status != orderStatus.Canceled)
                                {
                                    this.orders[ord.internal_order_id] = ord;
                                    if (this.live_orders.ContainsKey(ord.internal_order_id))
                                    {
                                        this.live_orders[ord.internal_order_id] = ord;
                                    }
                                    if(ins != null)
                                    {
                                        ins.live_orders[ord.internal_order_id] = ord;
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
                            if (this.ordIdMapping.ContainsKey(ord.market + ord.order_id))
                            {
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

                                    if (ord.status < prevord.status || ord.filled_quantity < prevord.filled_quantity)
                                    {
                                        if(prevord.status == orderStatus.WaitCancel && ord.status == orderStatus.Open)
                                        {
                                            this.orders[ord.internal_order_id] = ord;
                                            this.live_orders[ord.internal_order_id] = ord;
                                            if (ins != null)
                                            {
                                                ins.live_orders[ord.internal_order_id] = ord;
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
                                        this.orders[ord.internal_order_id] = ord;
                                        if (ins != null)
                                        {
                                            ins.orders[ord.internal_order_id] = ord;
                                        }
                                        if (ord.status == orderStatus.Open)
                                        {
                                            if (this.live_orders.ContainsKey(ord.internal_order_id))
                                            {
                                                this.live_orders[ord.internal_order_id] = ord;
                                            }
                                            else
                                            {
                                                while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
                                                {
                                                }
                                                this.live_orders[ord.internal_order_id] = ord;
                                                Volatile.Write(ref this.order_lock, 0);
                                            }
                                            if (ins != null)
                                            {
                                                //ins.live_orders[ord.client_order_id] = ord;
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
                                                decimal filled_quantity = ord.filled_quantity - prevord.filled_quantity;
                                                if (filled_quantity > 0 && ord.order_type != orderType.Market)
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
                                        else if (this.live_orders.ContainsKey(ord.internal_order_id))
                                        {
                                            while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
                                            {
                                            }
                                            this.live_orders.Remove(ord.internal_order_id);
                                            Volatile.Write(ref this.order_lock, 0);
                                            if (ins != null)
                                            {
                                                if(ins.live_orders.ContainsKey(ord.internal_order_id))
                                                {
                                                    while (Interlocked.CompareExchange(ref ins.order_lock, 1, 0) != 0)
                                                    {
                                                    }
                                                    ins.live_orders.Remove(ord.internal_order_id);
                                                    Volatile.Write(ref ins.order_lock, 0);
                                                }
                                                decimal filled_quantity = ord.order_quantity - prevord.filled_quantity;//cancelled quantity + unprocessed filled quantity
                                                if (filled_quantity > 0 && ord.order_type != orderType.Market)
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
                                                this.placeNewSpotOrder(mod.ins, mod.side, mod.order_type, mod.newQuantity, mod.newPrice, mod.time_in_force,true);
                                                this.modifingOrders.Remove(ord.internal_order_id);
                                                mod.init();
                                                this.modifingOrdStack.Push(mod);
                                            }
                                            else if (ord.status == orderStatus.Filled)
                                            {
                                                this.modifingOrders.Remove(ord.internal_order_id);
                                                mod.init();
                                                this.modifingOrdStack.Push(mod);
                                            }
                                        }

                                        prevord.update_time = DateTime.UtcNow;
                                        this.order_pool.Enqueue(prevord);
                                        //prevord.init();
                                        //this.ord_client.ordUpdateStack.Push(prevord);
                                    }

                                }
                                else
                                {
                                    this.orders[ord.internal_order_id] = ord;
                                    if (ord.status == orderStatus.Open)
                                    {
                                        if (this.live_orders.ContainsKey(ord.internal_order_id))
                                        {
                                            this.live_orders[ord.internal_order_id] = ord;
                                        }
                                        else
                                        {
                                            while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
                                            {
                                            }
                                            this.live_orders[ord.internal_order_id] = ord;
                                            Volatile.Write(ref this.order_lock, 0);
                                        }
                                        if (ins != null)
                                        {
                                            //ins.live_orders[ord.client_order_id] = ord;
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
                                            decimal filled_quantity = ord.filled_quantity;
                                            if (filled_quantity > 0 && ord.order_type != orderType.Market)
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
                                        if (ins != null)
                                        {
                                            decimal filled_quantity = ord.order_quantity;
                                            if (filled_quantity > 0)
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
                                }
                                this.ordLogQueue.Enqueue(ord.ToString());
                            }
                            else
                            {//If the mapping doesn't exist, which means the order from the exchange reaches here before the new order processing.
                                if (!this.Instruments.ContainsKey(ord.symbol_market))
                                {
                                    ord.init();
                                    this.ord_client.ordUpdateStack.Push(ord);
                                }
                                else if (ord.queued_count % 200001 == 200000)
                                {
                                    addLog("Unknown Order", Enums.logType.WARNING);
                                    addLog(ord.ToString());
                                    if (ord.queued_count > 1_000_000)
                                    {
                                        if (ord.status == orderStatus.Open)
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
                                                    switch (ord.side)
                                                    {
                                                        case orderSide.Buy:
                                                            stg.live_buyorder_id = "";
                                                            break;
                                                        case orderSide.Sell:
                                                            stg.live_sellorder_id = "";
                                                            break;
                                                    }
                                                }
                                            }
                                            this.placeCancelSpotOrder(ins, ord.market + ord.order_id, true, false);

                                            ++(ord.queued_count);
                                            this.ord_client.ordUpdateQueue.Enqueue(ord);                                            
                                        }
                                        else
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
                                break;
                            }
                        }

                        DateTime currentTime = DateTime.UtcNow;
                        while(this.order_pool.Count > 0)
                        {
                            ord = this.order_pool.Peek();
                            if(currentTime - ord.update_time > TimeSpan.FromSeconds(this.orderLifeTime))
                            {
                                ord = this.order_pool.Dequeue();
                                ord.init();
                                this.ord_client.ordUpdateStack.Push(ord);
                            }
                            else
                            {
                                break;
                            }
                        }
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


        public void checkVirtualOrders(Instrument ins,DataTrade? last_trade = null)
        {
            List<string> removing = new List<string>();
            if(this.virtualMode)
            {
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
                    }
                    if (ord.symbol_market == ins.symbol_market)
                    {
                        switch (ord.side)
                        {
                            case orderSide.Buy:
                                if (ins.bestask.Item1 < ord.order_price || (last_trade != null && last_trade.price < ord.order_price))
                                {
                                    DataSpotOrderUpdate output;
                                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                                    {

                                    }
                                    output.isVirtual = true;
                                    output.order_id = ord.order_id;
                                    output.symbol = ins.symbol;
                                    output.market = ins.market;
                                    output.symbol_market = ins.symbol_market;
                                    output.internal_order_id = ord.internal_order_id;
                                    output.status = orderStatus.Filled;
                                    output.side = ord.side;
                                    output.order_type = ord.order_type;
                                    output.order_quantity = ord.order_quantity;
                                    output.filled_quantity = ord.order_quantity;
                                    output.order_price = ord.order_price;
                                    output.average_price = ord.order_price;
                                    output.create_time = ord.create_time;
                                    output.fee = ins.maker_fee * output.filled_quantity * output.average_price;
                                    output.fee_asset = ins.quoteCcy;
                                    output.is_trigger_order = ord.is_trigger_order;
                                    output.last_trade = ord.last_trade;
                                    output.time_in_force = ord.time_in_force;
                                    output.timestamp = DateTime.UtcNow;
                                    output.trigger_price = ord.trigger_price;
                                    output.update_time = DateTime.UtcNow;
                                    DataFill fill;
                                    while (!this.ord_client.fillStack.TryPop(out fill))
                                    {
                                        
                                    }
                                    fill.order_id = ord.order_id;
                                    fill.symbol = ins.symbol;
                                    fill.market = ins.market;
                                    fill.symbol_market = ins.symbol_market;
                                    fill.internal_order_id = output.internal_order_id;
                                    fill.side = ord.side;
                                    fill.quantity = output.filled_quantity;
                                    fill.price = output.average_price;
                                    fill.fee_quote = output.fee;
                                    fill.fee_base = 0;
                                    fill.fee_unknown = 0;
                                    fill.timestamp = output.timestamp;
                                    fill.filled_time = fill.timestamp;
                                    fill.order_type = ord.order_type;
                                    this.ord_client.ordUpdateQueue.Enqueue(output);
                                    removing.Add(key);
                                    this.ord_client.fillQueue.Enqueue(fill);
                                }
                                break;
                            case orderSide.Sell:
                                if (ins.bestbid.Item1 > ord.order_price || (last_trade != null && last_trade.price > ord.order_price))
                                {
                                    DataSpotOrderUpdate output;
                                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                                    {

                                    }
                                    output.isVirtual = true;
                                    output.order_id = ord.order_id;
                                    output.symbol = ins.symbol;
                                    output.market = ins.market;
                                    output.symbol_market = ins.symbol_market;
                                    output.internal_order_id = ord.internal_order_id;
                                    output.status = orderStatus.Filled;
                                    output.side = ord.side;
                                    output.order_type = ord.order_type;
                                    output.order_quantity = ord.order_quantity;
                                    output.filled_quantity = ord.order_quantity;
                                    output.order_price = ord.order_price;
                                    output.average_price = ord.order_price;
                                    output.create_time = ord.create_time;
                                    output.fee = ins.maker_fee * output.filled_quantity;
                                    output.fee_asset = ins.baseCcy;
                                    output.is_trigger_order = ord.is_trigger_order;
                                    output.last_trade = ord.last_trade;
                                    output.time_in_force = ord.time_in_force;
                                    output.timestamp = DateTime.UtcNow;
                                    output.trigger_price = ord.trigger_price;
                                    output.update_time = DateTime.UtcNow;
                                    DataFill fill;
                                    while (!this.ord_client.fillStack.TryPop(out fill))
                                    {

                                    }
                                    fill.order_id = ord.order_id;
                                    fill.symbol = ins.symbol;
                                    fill.market = ins.market;
                                    fill.symbol_market = ins.symbol_market;
                                    fill.internal_order_id = output.internal_order_id;
                                    fill.side = ord.side;
                                    fill.quantity = output.filled_quantity;
                                    fill.price = output.average_price;
                                    fill.fee_quote = 0;
                                    fill.fee_base = output.fee;
                                    fill.fee_unknown = 0;
                                    fill.timestamp = output.timestamp;
                                    fill.filled_time = fill.timestamp;
                                    fill.order_type = ord.order_type;
                                    this.ord_client.ordUpdateQueue.Enqueue(output);
                                    removing.Add(key);
                                    this.ord_client.fillQueue.Enqueue(fill);
                                }
                                break;
                        }
                    }
                }
                foreach (string key in removing)
                {
                    this.virtual_liveorders.Remove(key);
                }
                Volatile.Write(ref this.virtual_order_lock, 0);
            }
        }

        public void setOrdLogPath(string logPath)
        {
            this.outputPath = logPath;
            string filename = this.outputPath + "/orderlog_" + DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss") + ".csv";
            this.f = new FileStream(filename, FileMode.Create, FileAccess.Write);
            this.sw = new StreamWriter(f);
        }

        public void ordLogging(string logPath = "")
        {
            this.ord_logged = false;
            if(logPath != "")
            {
                this.outputPath = logPath;
            }
            string filename = this.outputPath + "/orderlog_" + DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss") + ".csv";
            using (FileStream f = new FileStream(filename, FileMode.Create, FileAccess.Write))
            {
                using (StreamWriter s = new StreamWriter(f))
                {
                    int i = 0;
                    string line;
                    while (true)
                    {
                        if (this.ordLogQueue.TryDequeue(out line))
                        {
                            s.WriteLine(line);
                            this.ord_logged = true;
                            ++i;
                        }
                        else
                        {
                            s.Flush();
                            Thread.Sleep(1000);
                        }
                        if (this.aborting && this.updateOrderStopped)
                        {
                            while (this.ordLogQueue.Count > 0)
                            {
                                if (this.ordLogQueue.TryDequeue(out line))
                                {
                                    s.WriteLine(line);
                                    this.ord_logged = true;
                                }
                            }
                            s.Flush();
                            s.Close();
                            if(!this.ord_logged)
                            {
                                File.Delete(filename);
                            }
                            this.aborting = false;
                            break;
                        }
                    }
                }
            }
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
                    while (this.ordLogQueue.TryDequeue(out line))
                    {
                        start();
                        this.sw.WriteLine(line);
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
                this.addLog(ex.Message, Enums.logType.WARNING);
                ret = false;
            }
            return ret;

        }
        public async Task<(bool,double)> _orderLogging()
        {
            string line;
            if (this.ordLogQueue.TryDequeue(out line))
            {
                this.sw.WriteLine(line);
                //this.sw.Flush();
                this.ord_logged = true;
            }
            else
            {
                this.sw.Flush();
                Thread.Sleep(1000);
            }
            return (true, 0);
        }
        public void ordLoggingOnClosing()
        {
            string line;
            while (this.ordLogQueue.Count > 0)
            {
                if (this.ordLogQueue.TryDequeue(out line))
                {
                    this.sw.WriteLine(line);
                    this.ord_logged = true;
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
                if (this.ordLogQueue.TryDequeue(out line))
                {
                    this.sw.WriteLine(line);
                    this.ord_logged = true;
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

