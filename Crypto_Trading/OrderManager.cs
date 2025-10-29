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
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using XT.Net.Objects.Models;

namespace Crypto_Trading
{


    public class OrderManager
    {

        Crypto_Clients.Crypto_Clients ord_client = Crypto_Clients.Crypto_Clients.GetInstance();

        public volatile int order_lock;
        public Dictionary<string, DataSpotOrderUpdate> orders;
        public Dictionary<string, DataSpotOrderUpdate> live_orders;

        public volatile int virtual_order_lock;
        public Dictionary<string, DataSpotOrderUpdate> virtual_liveorders;

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

            this.strategies = new Dictionary<string, Strategy>();

            this.connections = new Dictionary<string, WebSocketState>();

            this.modifingOrders = new Dictionary<string, modifingOrd>();
            this.modifingOrdStack = new ConcurrentStack<modifingOrd>();

            this.ordLogQueue = new ConcurrentQueue<string>();

            this.ord_logged = false;

            this.id_number = 0;

            int i = 0;
            while (i < MOD_STACK_SIZE)
            {
                this.modifingOrdStack.Push(new modifingOrd());
                ++i;
            }

            //this._addLog = Console.WriteLine;
            this.sw_updateOrders = new Stopwatch();
            this.sw_updateFills = new Stopwatch();

            this.ready = false;
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
                    onMsg = async () =>
                    {
                        return await this.ord_client.coincheck_client.onListenPrivate(this.ord_client.onConcheckPrivateMessage);
                    };
                    onClosing = async () =>
                    {
                        await this.ord_client.coincheck_client.onClosingPrivate(this.ord_client.onConcheckPrivateMessage);
                    };
                    thManager.addThread(market + "Private", onMsg,onClosing,this.ord_client.coincheck_client.onListenPrivateOnError);
                    this.connections[market] = this.ord_client.coincheck_client.GetSocketStatePrivate();
                    break;
                case "bittrade":
                    await this.ord_client.bittrade_client.connectPrivateAsync();
                    onMsg = async () =>
                    {
                        return await this.ord_client.bittrade_client.onListenPrivate(this.ord_client.onBitTradePrivateMessage);
                    };
                    onClosing = async () =>
                    {
                        await this.ord_client.bittrade_client.onClosingPrivate(this.ord_client.onBitTradePrivateMessage);
                    };
                    thManager.addThread(market + "Private", onMsg,onClosing,this.ord_client.bittrade_client.onListenPrivateOnError);
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

        async public Task<DataSpotOrderUpdate?> placeNewSpotOrder(Instrument ins, orderSide side, orderType ordtype, decimal quantity, decimal price, timeInForce? timeinforce = null)
        {
            DataSpotOrderUpdate? output = null;
            JsonDocument js;
            if(this.virtualMode)
            {
                quantity = Math.Round(quantity / ins.quantity_unit) * ins.quantity_unit;
                while (!this.ord_client.ordUpdateStack.TryPop(out output))
                {

                }
                output.isVirtual = true;
                output.order_id = this.getVirtualOrdId();
                output.symbol = ins.symbol;
                output.market = ins.market;
                output.symbol_market = ins.symbol_market;
                output.client_order_id = output.market + output.order_id;
                output.side = side;
                output.order_type = ordtype;
                output.order_quantity = quantity;
                output.order_price = price;
                output.create_time = DateTime.UtcNow;
                output.is_trigger_order = true;
                output.last_trade = "";
                if(timeinforce == null)
                {
                    output.time_in_force = timeInForce.GoodTillCanceled;
                }
                else
                {
                    output.time_in_force = (timeInForce)timeinforce;
                }
                output.timestamp = DateTime.UtcNow;
                output.trigger_price = 0;
                output.update_time = DateTime.UtcNow;
                Thread.Sleep(this.latency);
                if (ordtype == orderType.Market || (side == orderSide.Buy && price > ins.bestask.Item1) || (side == orderSide.Sell && price < ins.bestbid.Item1))
                {
                    DataFill fill;
                    while(!this.ord_client.fillStack.TryPop(out fill))
                    {

                    }
                    output.timestamp = DateTime.UtcNow;
                    output.update_time = DateTime.UtcNow;
                    output.status = orderStatus.Filled;
                    output.filled_quantity = quantity;
                    decimal feetype = 0;
                    if(ordtype == orderType.Market)
                    {
                        feetype = ins.taker_fee;
                    }
                    else
                    {
                        feetype = ins.maker_fee;
                    }
                        switch (side)
                        {
                            case orderSide.Buy:
                                output.average_price = ins.adjusted_bestask.Item1;
                                output.fee = feetype * output.filled_quantity * output.average_price;
                                output.fee_asset = ins.quoteCcy;
                                fill.fee_quote = output.fee;
                                fill.fee_base = 0;
                                fill.fee_unknown = 0;
                                break;
                            case orderSide.Sell:
                                output.average_price = ins.adjusted_bestbid.Item1;
                                output.fee = feetype * output.filled_quantity;
                                output.fee_asset = ins.baseCcy;
                                fill.fee_quote = 0;
                                fill.fee_base = output.fee;
                                fill.fee_unknown = 0;
                                break;
                        }
                    fill.order_id = output.order_id;
                    fill.client_order_id = output.client_order_id;
                    fill.symbol = ins.symbol;
                    fill.market = ins.market;
                    fill.symbol_market = ins.symbol_market;
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
                    this.orders[output.client_order_id] = output;
                    string ordId = output.order_id;
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

                    }
                    output.isVirtual = true;
                    output.order_id = ordId;
                    output.symbol = ins.symbol;
                    output.market = ins.market;
                    output.client_order_id = output.market + output.order_id;
                    output.symbol_market = ins.symbol_market;
                    output.side = side;
                    output.order_type = ordtype;
                    output.order_quantity = quantity;
                    output.order_price = price;
                    output.create_time = DateTime.UtcNow;
                    output.is_trigger_order = true;
                    output.last_trade = "";
                    if (timeinforce == null)
                    {
                        output.time_in_force = timeInForce.GoodTillCanceled;
                    }
                    else
                    {
                        output.time_in_force = (timeInForce)timeinforce;
                    }
                    output.timestamp = DateTime.UtcNow;
                    output.trigger_price = 0;
                    output.update_time = DateTime.UtcNow;
                    output.status = orderStatus.Open;
                    output.filled_quantity = 0;
                    output.average_price = 0;
                    output.fee = 0;
                    output.fee_asset = "";
                    switch(side)
                    {
                        case orderSide.Buy:
                            ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                            break;
                        case orderSide.Sell:
                            ins.baseBalance.AddBalance(0, output.order_quantity);
                            break;
                    }
                    if(this.virtual_liveorders.ContainsKey(output.client_order_id))
                    {
                        this.virtual_liveorders[output.client_order_id] = output;
                    }
                    else
                    {
                        while(Interlocked.CompareExchange(ref this.virtual_order_lock, 1, 0) != 0)
                        {
                        }
                        this.virtual_liveorders[output.client_order_id] = output;
                        Volatile.Write(ref this.virtual_order_lock, 0);
                    }
                        this.ord_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else if(ins.market == "bitbank")
            {
                quantity = Math.Round(quantity / ins.quantity_unit) * ins.quantity_unit;
                DateTime sendTime = DateTime.UtcNow;
                if (ordtype == orderType.Limit)
                {
                    js = await this.ord_client.bitbank_client.placeNewOrder(ins.symbol, ordtype.ToString().ToLower(), side.ToString().ToLower(), price, quantity, true);
                }
                else
                {
                    js = await this.ord_client.bitbank_client.placeNewOrder(ins.symbol, ordtype.ToString().ToLower(), side.ToString().ToLower(), price, quantity, false);
                }

                if(js.RootElement.GetProperty("success").GetUInt16() == 1)
                {
                    var ord_obj = js.RootElement.GetProperty("data");
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

                    }
                    output.timestamp = sendTime;
                    output.order_id = ord_obj.GetProperty("order_id").GetInt64().ToString();
                    output.symbol = ord_obj.GetProperty("pair").GetString();
                    output.market = ins.market;
                    output.client_order_id = output.market + output.order_id;
                    output.symbol_market = ins.symbol_market;
                    string str_side = ord_obj.GetProperty("side").GetString();
                    if(str_side == "buy")
                    {
                        output.side = orderSide.Buy;
                    }
                    else if(str_side == "sell")
                    {
                        output.side = orderSide.Sell;
                    }
                    string str_type = ord_obj.GetProperty("type").GetString();
                    switch(str_type)
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
                    switch (side)
                    {
                        case orderSide.Buy:
                            ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                            break;
                        case orderSide.Sell:
                            ins.baseBalance.AddBalance(0, output.order_quantity);
                            break;
                    }
                    //this.orders.TryAdd(output.order_id, output);
                    //if (!this.orders.TryAdd(output.order_id,output))
                    //{
                    //    string ord_id = output.order_id;
                    //    output.init();
                    //    this.ord_client.ordUpdateStack.Push(output);
                    //    output = this.orders[ord_id];
                    //}
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    int code = js.RootElement.GetProperty("data").GetProperty("code").GetInt32();
                    switch (code)
                    {
                        case 10009:
                            this.addLog("New order failed. Too many request.", Enums.logType.WARNING);
                            break;
                        default:
                            this.addLog("New Order Failed", Enums.logType.ERROR);
                            this.addLog(js.RootElement.GetRawText(), Enums.logType.ERROR);
                            break;
                    }
                    output = null;
                }
            }
            else if(ins.market == "coincheck")
            {
                DateTime sendTime = DateTime.UtcNow;
                if (ordtype == orderType.Limit)
                {
                    quantity = Math.Round(quantity / ins.quantity_unit) * ins.quantity_unit;
                    js = await this.ord_client.coincheck_client.placeNewOrder(ins.symbol, side.ToString().ToLower(), price, quantity, "post_only");
                }
                else
                {
                    //decimal order_price;
                    if(side == orderSide.Buy)
                    {
                        if(quantity < ins.quantity_unit * (decimal)1.1)
                        {
                            quantity = ins.quantity_unit * (decimal)1.1;
                        }
                        quantity = quantity * ins.adjusted_bestask.Item1;
                        js = await this.ord_client.coincheck_client.placeMarketNewOrder(ins.symbol, side.ToString().ToLower(), 0, quantity);
                    }
                    else
                    {
                        quantity = Math.Round(quantity / ins.quantity_unit) * ins.quantity_unit;
                        js = await this.ord_client.coincheck_client.placeMarketNewOrder(ins.symbol, side.ToString().ToLower(), 0, quantity);
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
                    output.timestamp = sendTime;
                    output.order_id = ord_obj.GetProperty("id").GetInt64().ToString();
                    output.symbol = ord_obj.GetProperty("pair").GetString();
                    output.market = ins.market;
                    output.symbol_market = ins.symbol_market;
                    output.client_order_id = output.market + output.order_id;
                    string str_side = ord_obj.GetProperty("order_type").GetString();
                    if (str_side == "buy" || str_side == "market_buy")
                    {
                        output.side = orderSide.Buy;
                    }
                    else if (str_side == "sell" || str_side == "market_sell")
                    {
                        output.side = orderSide.Sell;
                    }
                    if(str_side.StartsWith("market_"))//market order
                    {
                        output.order_type = orderType.Market;
                        if(str_side == "market_buy")
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
                    //this.orders.TryAdd(output.order_id, output);
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

                    }
                    output.timestamp = DateTime.UtcNow;
                    output.order_id = ord_obj.GetProperty("id").GetInt64().ToString();
                    output.symbol = ord_obj.GetProperty("pair").GetString();
                    output.market = ins.market;
                    output.symbol_market = ins.symbol_market;
                    output.client_order_id = output.market + output.order_id;
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
                        else
                        {
                            bool stop = true;
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
                    switch (side)
                    {
                        case orderSide.Buy:
                            ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                            break;
                        case orderSide.Sell:
                            ins.baseBalance.AddBalance(0, output.order_quantity);
                            break;
                    }

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
                }
            }
            else if (ins.market == "bittrade")
            {
                quantity = Math.Round(quantity / ins.quantity_unit) * ins.quantity_unit;
                decimal order_price = price;
                DateTime sendTime = DateTime.UtcNow;
                if (ordtype == orderType.Limit)
                {
                    js = await this.ord_client.bittrade_client.placeNewOrder(ins.symbol, side.ToString().ToLower(), price, quantity, true);
                }
                else
                {
                    if (side == orderSide.Buy)
                    {
                        order_price = Math.Round(ins.bestask.Item1 * (decimal)1.05 / ins.price_unit) * ins.price_unit;
                    }
                    else
                    {
                        order_price = Math.Round(ins.bestbid.Item1 * (decimal)0.95 / ins.price_unit) * ins.price_unit;
                    }
                    //Market Order
                    js = await this.ord_client.bittrade_client.placeNewOrder(ins.symbol, side.ToString().ToLower(), order_price, quantity,false);
                }

                if(js.RootElement.GetProperty("status").GetString() == "ok")
                {
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

                    }
                    output.timestamp = sendTime;
                    output.order_id = js.RootElement.GetProperty("data").GetString();
                    output.symbol = ins.symbol;
                    output.market = ins.market;
                    output.symbol_market = ins.symbol_market;
                    output.client_order_id = output.market + output.order_id;
                    output.side = side;
                    output.order_type = ordtype;
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
                    switch (side)
                    {
                        case orderSide.Buy:
                            ins.quoteBalance.AddBalance(0, output.order_price * output.order_quantity);
                            break;
                        case orderSide.Sell:
                            ins.baseBalance.AddBalance(0, output.order_quantity);
                            break;
                    }
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    string msg = JsonSerializer.Serialize(js);
                    this.addLog(msg, Enums.logType.ERROR);
                }
            }
            else
            {
                quantity = Math.Round(quantity / ins.quantity_unit) * ins.quantity_unit;
                DateTime sendTime = DateTime.UtcNow;
                output = await this.ord_client.placeNewSpotOrder(ins.market, ins.baseCcy, ins.quoteCcy, side, ordtype, quantity, price, timeinforce);
                if (output != null)
                {
                    output.timestamp = sendTime;
                    output.symbol_market = ins.symbol_market;
                    output.client_order_id = output.market + output.order_id;
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
            }
            return output;
        }
        async public Task<DataSpotOrderUpdate?> placeCancelSpotOrder(Instrument ins, string orderId)
        {
            DataSpotOrderUpdate? output = null;
            JsonDocument js;
            if(this.virtualMode)
            {
                while (!this.ord_client.ordUpdateStack.TryPop(out output))
                {

                }
                DataSpotOrderUpdate prev = this.orders[ins.market + orderId];
                output.isVirtual = true;
                output.order_id = orderId;
                output.symbol = ins.symbol;
                output.market = ins.market;
                output.symbol_market = ins.symbol_market;
                output.client_order_id = output.market + output.order_id;
                if (prev.filled_quantity > 0)
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
                Thread.Sleep(this.latency);
                while (Interlocked.CompareExchange(ref this.virtual_order_lock, 1, 0) != 0)
                {

                }
                if (this.virtual_liveorders.ContainsKey(prev.client_order_id))
                {
                    this.virtual_liveorders.Remove(prev.client_order_id);
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
            else if(ins.market == "bitbank")
            {
                DateTime sendTime = DateTime.UtcNow;
                js = await this.ord_client.bitbank_client.placeCanOrder(ins.symbol, orderId);
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
                    output.market = ins.market;
                    output.symbol = ins.symbol;
                    output.client_order_id = output.market + output.order_id;
                    output.filled_quantity = 0;
                    output.status = orderStatus.WaitCancel;
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else if (ins.market == "coincheck")
            {
                DateTime sendTime = DateTime.UtcNow;
                js = await this.ord_client.coincheck_client.placeCanOrder(orderId);
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
                    output.market = ins.market;
                    output.symbol = ins.symbol;
                    output.client_order_id = output.market + output.order_id;
                    output.filled_quantity = 0;
                    output.status = orderStatus.WaitCancel;
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else if (ins.market == "bittrade")
            {
                DateTime sendTime = DateTime.UtcNow;
                js = await this.ord_client.bittrade_client.placeCanOrder(orderId);
                if (js.RootElement.GetProperty("status").GetString() == "ok")
                {
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {
                    }
                    output.order_id = js.RootElement.GetProperty("data").GetString();
                    output.timestamp = sendTime;
                    output.order_price = -1;
                    output.order_quantity = 0;
                    output.market = ins.market;
                    output.symbol = ins.symbol;
                    output.client_order_id = output.market + output.order_id;
                    output.filled_quantity = 0;
                    output.status = orderStatus.WaitCancel;
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else
            {
                output = await this.ord_client.placeCancelSpotOrder(ins.market, ins.baseCcy, ins.quoteCcy, orderId);
                if (output != null)
                {
                    output.symbol_market = ins.symbol_market;
                    output.client_order_id = output.market + output.order_id;
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
            }  
 
            return output;
        }
        async public Task<DataSpotOrderUpdate?> placeModSpotOrder(Instrument ins, string orderId, decimal quantity, decimal price,bool waitCancel)
        {
            DataSpotOrderUpdate ord;
            modifingOrd mod;
            DataSpotOrderUpdate? output = null;
            
            if (waitCancel)
            {
                if (this.orders.ContainsKey(ins.market + orderId))
                {
                    ord = this.orders[ins.market + orderId];
                    while (!this.modifingOrdStack.TryPop(out mod))
                    {

                    }
                    mod.ordId = orderId;
                    mod.newPrice = price;
                    mod.newQuantity = quantity;
                    mod.side = ord.side;
                    mod.order_type = ord.order_type;
                    mod.time_in_force = ord.time_in_force;
                    mod.ins = ins;
                    this.modifingOrders[ins.market + orderId] = mod;
                    output = await this.placeCancelSpotOrder(ins, orderId);

                    return output;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                if (this.orders.ContainsKey(ins.market + orderId))
                {
                    ord = this.orders[ins.market + orderId];
                    orderSide side = ord.side;
                    orderType type = ord.order_type;
                    timeInForce tif = ord.time_in_force;
                    this.placeCancelSpotOrder(ins, orderId);
                    Thread.Sleep(1);
                    output = await this.placeNewSpotOrder(ins, side, type, quantity, price, tif);
                    if (output != null)
                    {
                        output.msg += "ModOrder from " + ins.market + orderId + " ";
                    }
                    
                    return output;
                }
                else
                {
                    return null;
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
                ins = this.Instruments[ord.symbol_market];
                await this.placeCancelSpotOrder(ins, ord.order_id);
            }
            Volatile.Write(ref this.order_lock, 0);
        }


        public void pushbackFill(DataFill fill)
        {
            fill.init();
            this.ord_client.fillStack.Push(fill);
        }

        public async Task<(bool,double)> _updateFill()
        {
            DataFill fill;
            Instrument ins = null;
            double latency = 0;
            if (this.ord_client.fillQueue.TryDequeue(out fill))
            {
                this.sw_updateFills.Start();
                foreach(var stg in this.strategies)
                {
                    if(stg.Value.maker.symbol_market == fill.symbol_market)
                    {
                        await stg.Value.on_Message(fill);
                    }
                }
                if (this.Instruments.ContainsKey(fill.symbol_market))
                {
                    if (fill.market == "coincheck")
                    {
                        if (this.orders.ContainsKey(fill.client_order_id))
                        {
                            DataSpotOrderUpdate filled = this.orders[fill.client_order_id];
                            filled.average_price = fill.price;//For viewing purpose
                        }
                    }
                    ins = this.Instruments[fill.symbol_market];
                    ins.updateFills(fill);
                }
                this.ordLogQueue.Enqueue(fill.ToString());
                this.filledOrderQueue.Enqueue(fill);
                this.sw_updateFills.Stop();
                latency = this.sw_updateFills.Elapsed.TotalNanoseconds / 1000;
                this.sw_updateFills.Reset();
            }
            return (true, latency);
        }

        public async void updateFillOnClosing()
        {
            while (this.ord_client.fillQueue.Count() > 0)
            {
                await this._updateFill();
            }
        }

        public async Task<(bool,double)> _updateOrders()
        {
            DataSpotOrderUpdate ord;
            DataSpotOrderUpdate prevord;
            //DataFill fill;
            Instrument ins = null;
            modifingOrd mod;
            double latency = 0;
            if (this.ord_client.ordUpdateQueue.TryDequeue(out ord))
            {
                this.sw_updateOrders.Start();

                this.ordLogQueue.Enqueue(ord.ToString());

                if (this.Instruments.ContainsKey(ord.symbol_market))
                {
                    ins = this.Instruments[ord.symbol_market];
                }
                if (ord.status == orderStatus.WaitOpen)
                {
                    //if (!this.orders.ContainsKey(ord.order_id))
                    //{

                    //    this.orders[ord.order_id] = ord;
                    //}
                    //else
                    //{
                    //    ord.init();
                    //    this.ord_client.ordUpdateStack.Push(ord);
                    //}
                }
                else if (ord.status == orderStatus.WaitMod)
                {
                    //Undefined
                }
                else if (ord.status == orderStatus.WaitCancel)
                {
                    if (this.orders.ContainsKey(ord.client_order_id))
                    {
                        prevord = this.orders[ord.client_order_id];
                        if (prevord.status != orderStatus.Canceled)
                        {
                            this.orders[ord.client_order_id] = ord;
                            if (this.live_orders.ContainsKey(ord.client_order_id))
                            {
                                this.live_orders[ord.client_order_id] = ord;
                            }
                        }
                        prevord.init();
                        this.ord_client.ordUpdateStack.Push(prevord);
                    }
                    else
                    {
                        ord.init();
                        this.ord_client.ordUpdateStack.Push(ord);
                    }
                }
                else
                {
                    if (this.orders.ContainsKey(ord.client_order_id))
                    {
                        prevord = this.orders[ord.client_order_id];
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
                            ord.init();
                            this.ord_client.ordUpdateStack.Push(ord);
                        }
                        else
                        {
                            this.orders[ord.client_order_id] = ord;
                            if (ins != null)
                            {
                                ins.orders[ord.client_order_id] = ord;
                            }
                            if (ord.status == orderStatus.Open)
                            {
                                if(this.live_orders.ContainsKey(ord.client_order_id))
                                {
                                    this.live_orders[ord.client_order_id] = ord;
                                }
                                else
                                {
                                    while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
                                    {
                                    }
                                    this.live_orders[ord.client_order_id] = ord;
                                    Volatile.Write(ref this.order_lock, 0);
                                }
                                if (ins != null)
                                {
                                    //ins.live_orders[ord.client_order_id] = ord;
                                    decimal filled_quantity = ord.filled_quantity - prevord.filled_quantity;
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
                            else if (this.live_orders.ContainsKey(ord.client_order_id))
                            {
                                while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
                                {
                                }
                                this.live_orders.Remove(ord.client_order_id);
                                Volatile.Write(ref this.order_lock, 0);
                                if (ins != null)
                                {
                                    //while (Interlocked.CompareExchange(ref ins.orders_lock, 1, 0) != 0)
                                    //{
                                    //}
                                    //if (ins.live_orders.ContainsKey(ord.client_order_id))
                                    //{
                                    //    ins.live_orders.Remove(ord.client_order_id);
                                    //}
                                    //Volatile.Write(ref ins.orders_lock, 0);
                                    decimal filled_quantity = ord.order_quantity - prevord.filled_quantity;//cancelled quantity + unprocessed filled quantity
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
                            if (this.modifingOrders.ContainsKey(ord.client_order_id))
                            {
                                mod = this.modifingOrders[ord.client_order_id];

                                if (ord.status == orderStatus.Canceled)
                                {
                                    this.placeNewSpotOrder(mod.ins, mod.side, mod.order_type, mod.newQuantity, mod.newPrice, mod.time_in_force);
                                    this.modifingOrders.Remove(ord.client_order_id);
                                    mod.init();
                                    this.modifingOrdStack.Push(mod);
                                }
                                else if (ord.status == orderStatus.Filled)
                                {
                                    this.modifingOrders.Remove(ord.client_order_id);
                                    mod.init();
                                    this.modifingOrdStack.Push(mod);
                                }
                            }
                            prevord.init();
                            this.ord_client.ordUpdateStack.Push(prevord);
                        }
                            
                    }
                    else
                    {
                        this.orders[ord.client_order_id] = ord;
                        if (ord.status == orderStatus.Open)
                        {
                            if (this.live_orders.ContainsKey(ord.client_order_id))
                            {
                                this.live_orders[ord.client_order_id] = ord;
                            }
                            else
                            {
                                while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
                                {
                                }
                                this.live_orders[ord.client_order_id] = ord;
                                Volatile.Write(ref this.order_lock, 0);
                            }
                            if (ins != null)
                            {
                                //ins.live_orders[ord.client_order_id] = ord;
                                decimal filled_quantity = ord.filled_quantity;
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
                }
                if(ins != null)
                {
                    Volatile.Write(ref ins.orders_lock, 0);
                }
                this.sw_updateOrders.Stop();
                latency = this.sw_updateOrders.Elapsed.TotalNanoseconds / 1000;
                this.sw_updateOrders.Reset();
            }
            return (true, latency);
        }

        public async void updateOrdersOnClosing()
        {
            while(this.ord_client.ordUpdateQueue.Count() > 0)
            {
                await this._updateOrders();
            }
        }

        public void updateOrdersOnError()
        {
            this.order_lock = 0;
            foreach(var ins in this.Instruments.Values)
            {
                ins.orders_lock = 0;
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
                    if (key != ord.client_order_id)
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
                                    output.client_order_id = output.market + output.order_id;
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
                                    fill.client_order_id = output.client_order_id;
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
                                    output.client_order_id = output.market + output.order_id;
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
                                    fill.client_order_id = output.client_order_id;
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

