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
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using XT.Net.Objects.Models;

namespace Crypto_Trading
{


    public class OrderManager
    {

        Crypto_Clients.Crypto_Clients ord_client;
        bitbank_connection bitbank_client = bitbank_connection.GetInstance();
        coincheck_connection coincheck_client = coincheck_connection.GetInstance();

        public Dictionary<string, DataSpotOrderUpdate> orders;
        public Dictionary<string, DataSpotOrderUpdate> live_orders;

        public Dictionary<string, DataSpotOrderUpdate> virtual_liveorders;

        public Dictionary<string, modifingOrd> modifingOrders;
        public ConcurrentStack<modifingOrd> modifingOrdStack;

        public string outputPath;
        public ConcurrentQueue<string> ordLogQueue;
        public Thread ordLoggingTh;

        public ConcurrentQueue<string> filledOrderQueue;

        public Strategy stg;

        const int MOD_STACK_SIZE = 100;

        Dictionary<string, Instrument> Instruments;

        private bool virtualMode;
        public volatile int id_number;

        public Action<string> _addLog;

        public bool aborting;
        public bool updateOrderStopped;

        private OrderManager() 
        {
            this.aborting = false;
            this.updateOrderStopped = false;
            this.virtualMode = true;
            this.orders = new Dictionary<string, DataSpotOrderUpdate>();
            this.live_orders = new Dictionary<string, DataSpotOrderUpdate>();
            this.virtual_liveorders = new Dictionary<string, DataSpotOrderUpdate>();

            this.modifingOrders = new Dictionary<string, modifingOrd>();
            this.modifingOrdStack = new ConcurrentStack<modifingOrd>();

            this.ordLogQueue = new ConcurrentQueue<string>();

            this.id_number = 0;

            int i = 0;
            while (i < MOD_STACK_SIZE)
            {
                this.modifingOrdStack.Push(new modifingOrd());
                ++i;
            }

            this._addLog = Console.WriteLine;
            
        }

        public void startOrderLogging()
        {
            this.ordLoggingTh = new Thread(() =>
            {
                this.ordLogging();
            });
            this.ordLoggingTh.Start();
        }
        public void setOrderClient(Crypto_Clients.Crypto_Clients cl)
        {
            this.ord_client = cl;
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
                while(!this.ord_client.ordUpdateStack.TryPop(out output))
                {

                }
                output.order_id = this.getVirtualOrdId();
                output.symbol = ins.symbol;
                output.market = ins.market;
                output.symbol_market = ins.symbol_market;
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
                if(ordtype == orderType.Market || (side == orderSide.Buy && price > ins.bestask.Item1) || (side == orderSide.Sell && price < ins.bestbid.Item1))
                {
                    Thread.Sleep(100);
                    output.timestamp = DateTime.UtcNow;
                    output.update_time = DateTime.UtcNow;
                    output.status = orderStatus.Filled;
                    output.filled_quantity = quantity;
                    switch (side)
                    {
                        case orderSide.Buy:
                            output.average_price = ins.bestask.Item1;
                            output.fee = ins.taker_fee * output.filled_quantity * output.average_price;
                            output.fee_asset = ins.quoteCcy;
                            break;
                        case orderSide.Sell:
                            output.average_price = ins.bestbid.Item1;
                            output.fee = ins.taker_fee * output.filled_quantity;
                            output.fee_asset = ins.baseCcy;
                            break;
                    }
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    output.status = orderStatus.Open;
                    output.filled_quantity = 0;
                    output.average_price = 0;
                    output.fee = 0;
                    output.fee_asset = "";
                    this.virtual_liveorders[output.order_id] = output;
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                    Thread.Sleep(100);
                }
            }
            else if(ins.market == "bitbank")
            {
                if(ordtype == orderType.Limit)
                {
                    js = await this.bitbank_client.placeNewOrder(ins.symbol, ordtype.ToString().ToLower(), side.ToString().ToLower(), price, quantity, true);
                }
                else
                {
                    js = await this.bitbank_client.placeNewOrder(ins.symbol, ordtype.ToString().ToLower(), side.ToString().ToLower(), price, quantity, false);
                }

                if(js.RootElement.GetProperty("success").GetUInt16() == 1)
                {
                    var ord_obj = js.RootElement.GetProperty("data");
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

                    }
                    output.timestamp = DateTime.UtcNow;
                    output.order_id = ord_obj.GetProperty("order_id").GetInt64().ToString();
                    output.symbol = ord_obj.GetProperty("pair").GetString();
                    output.market = ins.market;
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
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    this.addLog("ERROR","New Order Failed");
                    this.addLog("ERROR",js.RootElement.GetRawText());
                    output = null;
                }
            }
            else if(ins.market == "coincheck")
            {
                if(ordtype == orderType.Limit)
                {
                    js = await this.coincheck_client.placeNewOrder(ins.symbol, side.ToString().ToLower(), price, quantity, "post_only");
                }
                else
                {
                    decimal order_price;
                    if(side == orderSide.Buy)
                    {
                        order_price = Math.Round(ins.bestask.Item1 * (decimal)1.1 / ins.price_unit) * ins.price_unit;
                    }
                    else
                    {
                        order_price = ins.price_unit;
                    }
                    //Market Order
                    js = await this.coincheck_client.placeNewOrder(ins.symbol, side.ToString().ToLower(), order_price, quantity);
                }
                if (js.RootElement.GetProperty("success").GetBoolean())
                {
                    JsonElement ord_obj = js.RootElement;
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {

                    }
                    output.timestamp = DateTime.UtcNow;
                    output.order_id = ord_obj.GetProperty("id").GetInt64().ToString();
                    output.symbol = ord_obj.GetProperty("pair").GetString();
                    output.market = ins.market;
                    output.symbol_market = ins.symbol_market;
                    string str_side = ord_obj.GetProperty("order_type").GetString();
                    if (str_side == "buy" || str_side == "market_buy")
                    {
                        output.side = orderSide.Buy;
                    }
                    else if (str_side == "sell" || str_side == "market_sell")
                    {
                        output.side = orderSide.Sell;
                    }
                    output.order_type = orderType.Limit;
                    output.order_price = decimal.Parse(ord_obj.GetProperty("rate").GetString());
                    output.order_quantity = decimal.Parse(ord_obj.GetProperty("amount").GetString());
                    output.filled_quantity = 0;//Even if an executed order is passed, output 0 executed quantity as the execution will be streamed anyway.
                    output.average_price = 0;
                    output.create_time = DateTime.Parse(ord_obj.GetProperty("created_at").GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
                    output.update_time = output.create_time;

                    output.status = orderStatus.Open;
                    output.fee = 0;
                    output.fee_asset = "";
                    output.is_trigger_order = true;
                    output.last_trade = "";
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
                else
                {
                    string msg = JsonSerializer.Serialize(js);
                    this.addLog("ERROR", msg);
                }
            }
            else
            {
                output = await this.ord_client.placeNewSpotOrder(ins.market, ins.baseCcy, ins.quoteCcy, side, ordtype, quantity, price, timeinforce);
                if (output != null)
                {
                    output.symbol_market = ins.symbol_market;
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
                DataSpotOrderUpdate prev = this.orders[orderId];
                output.order_id = orderId;
                output.symbol = ins.symbol;
                output.market = ins.market;
                output.symbol_market = ins.symbol_market;
                if(prev.filled_quantity > 0)
                {
                    output.status = orderStatus.Filled;
                }
                else
                {
                    output.status = orderStatus.Canceled;
                }
                output.side = prev.side;
                output.order_type = prev.order_type;
                output.order_quantity = prev.filled_quantity;
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
                if(this.virtual_liveorders.ContainsKey(prev.order_id))
                {
                    this.virtual_liveorders.Remove(prev.order_id);
                }
                this.ord_client.ordUpdateQueue.Enqueue(output);
                Thread.Sleep(100);
            }
            else if(ins.market == "bitbank")
            {
                js = await this.bitbank_client.placeCanOrder(ins.symbol, orderId);
                if (js.RootElement.GetProperty("success").GetUInt16() == 1)
                {
                    var ord_obj = js.RootElement.GetProperty("data");
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {
                    }
                    output.order_id = ord_obj.GetProperty("order_id").GetInt64().ToString();
                    output.timestamp = DateTime.UtcNow;
                    output.order_price = -1;
                    output.order_quantity = 0;
                    output.market = ins.market;
                    output.symbol = ins.symbol;
                    output.filled_quantity = 0;
                    output.status = orderStatus.WaitCancel;
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
            }
            else if (ins.market == "coincheck")
            {
                js = await this.coincheck_client.placeCanOrder(orderId);
                if (js.RootElement.GetProperty("success").GetBoolean())
                {
                    var ord_obj = js.RootElement;
                    while (!this.ord_client.ordUpdateStack.TryPop(out output))
                    {
                    }
                    output.order_id = ord_obj.GetProperty("id").GetInt64().ToString();
                    output.timestamp = DateTime.UtcNow;
                    output.order_price = -1;
                    output.order_quantity = 0;
                    output.market = ins.market;
                    output.symbol = ins.symbol;
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
                    this.ord_client.ordUpdateQueue.Enqueue(output);
                }
            }  
 
            return output;
        }
        async public Task<DataSpotOrderUpdate?> placeModSpotOrder(Instrument ins, string orderId, decimal quantity, decimal price,bool waitCancel)
        {
            DataSpotOrderUpdate ord;
            modifingOrd mod;
            DataSpotOrderUpdate? output;
            if (waitCancel)
            {
                if (this.live_orders.ContainsKey(orderId))
                {
                    ord = this.live_orders[orderId];
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
                    this.modifingOrders[orderId] = mod;
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
                if (this.live_orders.ContainsKey(orderId))
                {
                    ord = this.live_orders[orderId];
                    orderSide side = ord.side;
                    orderType type = ord.order_type;
                    timeInForce tif = ord.time_in_force;
                    this.placeCancelSpotOrder(ins, orderId);
                    output = await this.placeNewSpotOrder(ins, side, type, quantity, price, tif);
                    return output;
                }
                else
                {
                    return null;
                }
            }
        }

        public void cancelAllOrders()
        {
            Instrument ins;
            foreach(var ord in this.live_orders.Values)
            {
                ins = this.Instruments[ord.symbol_market];
                this.placeCancelSpotOrder(ins, ord.order_id);
            }
        }

        public void updateOrders()
        {
            this.startOrderLogging();
            int i = 0;
            DataSpotOrderUpdate ord;
            DataSpotOrderUpdate prevord;
            DataFill fill;
            Instrument ins;
            modifingOrd mod;
            while (true)
            {
                if(this.ord_client.ordUpdateQueue.TryDequeue(out ord))
                {
                    this.ordLogQueue.Enqueue(ord.ToString());
                    if (ord.status == orderStatus.WaitOpen)
                    {
                        if(!this.orders.ContainsKey(ord.order_id))
                        {

                            this.orders[ord.order_id] = ord;
                        }
                        else
                        {
                            ord.init();
                            this.ord_client.ordUpdateStack.Push(ord);
                        }
                    }
                    else if(ord.status == orderStatus.WaitMod)
                    {
                        //Undefined
                    }
                    else if (ord.status == orderStatus.WaitCancel)
                    {
                        if (this.orders.ContainsKey(ord.order_id))
                        {
                            prevord = this.orders[ord.order_id];
                            if (prevord.status != orderStatus.Canceled)
                            {
                                this.orders[ord.order_id] = ord;
                                if (this.live_orders.ContainsKey(ord.order_id))
                                {
                                    this.live_orders[ord.order_id] = ord;
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
                        if (this.orders.ContainsKey(ord.order_id))
                        {
                            prevord = this.orders[ord.order_id];
                            this.orders[ord.order_id] = ord;
                            if (ord.status == orderStatus.Open)
                            {
                                this.live_orders[ord.order_id] = ord;
                            }
                            else if (this.live_orders.ContainsKey(ord.order_id))
                            {
                                this.live_orders.Remove(ord.order_id);
                            }
                            if (this.modifingOrders.ContainsKey(ord.order_id))
                            {
                                mod = this.modifingOrders[ord.order_id];

                                if (ord.status == orderStatus.Canceled)
                                {
                                    this.placeNewSpotOrder(mod.ins, mod.side, mod.order_type, mod.newQuantity, mod.newPrice, mod.time_in_force);
                                    this.modifingOrders.Remove(ord.order_id);
                                    mod.init();
                                    this.modifingOrdStack.Push(mod);
                                }
                                else if (ord.status == orderStatus.Filled)
                                {
                                    this.modifingOrders.Remove(ord.order_id);
                                    mod.init();
                                    this.modifingOrdStack.Push(mod);
                                }
                            }
                            decimal filledQuantity = ord.filled_quantity - prevord.filled_quantity;
                            if(filledQuantity > 0)
                            {
                                ins = this.Instruments[ord.symbol_market];
                                ins.updateFills(prevord, ord);
                                this.filledOrderQueue.Enqueue(ord.order_id);
                            }
                            this.stg.on_Message(prevord, ord);
                            
                            prevord.init();
                            this.ord_client.ordUpdateStack.Push(prevord);
                        }
                        else
                        {
                            this.orders[ord.order_id] = ord;
                            if (ord.status == orderStatus.Open)
                            {
                                this.live_orders[ord.order_id] = ord;
                            }
                            if(ord.filled_quantity > 0)
                            {
                                ins = this.Instruments[ord.symbol_market];
                                ins.updateFills(ord, ord);
                                this.filledOrderQueue.Enqueue(ord.order_id);
                            }
                            this.stg.on_Message(ord, ord);
                        }
                    }
                    i = 0;
                }
                else if(this.ord_client.fillQueue.TryDequeue(out fill))
                {
                    this.ordLogQueue.Enqueue(fill.ToString());
                    if(this.Instruments.ContainsKey(fill.symbol_market))
                    {
                        ins = this.Instruments[fill.symbol_market];
                        ins.updateFills(fill);
                        fill.init();
                        this.ord_client.fillStack.Push(fill);
                    }
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
                if(this.aborting && this.ord_client.ordUpdateQueue.Count == 0 && this.ord_client.fillQueue.Count == 0)
                {
                    this.updateOrderStopped = true;
                    break;
                }
            }
        }

        public void checkVirtualOrders(Instrument ins,DataTrade? last_trade = null)
        {
            List<string> removing = new List<string>();
            foreach (DataSpotOrderUpdate ord in this.virtual_liveorders.Values)
            {
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
                                output.order_id = ord.order_id;
                                output.symbol = ins.symbol;
                                output.market = ins.market;
                                output.symbol_market = ins.symbol_market;
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
                                this.ord_client.ordUpdateQueue.Enqueue(output);
                                removing.Add(output.order_id);
                            }
                            break;
                        case orderSide.Sell:
                            if (ins.bestbid.Item1 > ord.order_price || (last_trade != null && last_trade.price > ord.order_price))
                            {
                                DataSpotOrderUpdate output;
                                while (!this.ord_client.ordUpdateStack.TryPop(out output))
                                {

                                }
                                output.order_id = ord.order_id;
                                output.symbol = ins.symbol;
                                output.market = ins.market;
                                output.symbol_market = ins.symbol_market;
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
                                this.ord_client.ordUpdateQueue.Enqueue(output);
                                removing.Add(output.order_id);
                            }
                            break;
                    }
                }
            }
            foreach(string key in removing)
            {
                this.virtual_liveorders.Remove(key);
            }
        }

        private void ordLogging(string logPath = "")
        {
            bool logged = false;
            if(logPath != "")
            {
                this.outputPath = logPath;
            }
            string filename = this.outputPath + "\\orderlog_" + DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss") + ".csv";
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
                            logged = true;
                            ++i;
                        }
                        else
                        {
                            ++i;
                            if (i > 100000)
                            {
                                s.Flush();
                                i = 0;
                            }
                        }
                        if (this.aborting && this.updateOrderStopped)
                        {
                            while (this.ordLogQueue.Count > 0)
                            {
                                if (this.ordLogQueue.TryDequeue(out line))
                                {
                                    s.WriteLine(line);
                                    logged = true;
                                }
                            }
                            s.Flush();
                            s.Close();
                            if(!logged)
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

        public bool setVirtualMode(bool newValue)
        {
            if(newValue)
            {
                this.addLog("INFO","The virtual mode turned on.");
                this.virtualMode = newValue;
            }
            else
            {
                this.addLog("INFO","The virtual mode turned off. Orders will go to real markets");
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

        public void addLog(string logtype,string line)
        {
            this._addLog("[" + logtype + ":OrderManager]" + line);
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

