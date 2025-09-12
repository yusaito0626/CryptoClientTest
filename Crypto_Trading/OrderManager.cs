using Coinbase.Net.Objects.Models;
using Crypto_Clients;
using CryptoClients.Net;
using CryptoClients.Net.Enums;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Requests;
using CryptoExchange.Net.SharedApis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Crypto_Trading
{


    public class OrderManager
    {

        Crypto_Clients.Crypto_Clients ord_client;

        public Dictionary<string, DataSpotOrderUpdate> orders;
        public Dictionary<string, DataSpotOrderUpdate> live_orders;

        public Dictionary<string, modifingOrd> modifingOrders;
        public ConcurrentStack<modifingOrd> modifingOrdStack;

        public ConcurrentQueue<string> ordLogQueue;
        public Thread ordLoggingTh;

        const int MOD_STACK_SIZE = 100;

        Dictionary<string, Instrument> Instruments;

        public Action<string> addLog;

        public bool aborting;

        private OrderManager() 
        {
            this.aborting = false;
            this.orders = new Dictionary<string, DataSpotOrderUpdate>();
            this.live_orders = new Dictionary<string, DataSpotOrderUpdate>();

            this.modifingOrders = new Dictionary<string, modifingOrd>();
            this.modifingOrdStack = new ConcurrentStack<modifingOrd>();

            this.ordLogQueue = new ConcurrentQueue<string>();

            int i = 0;
            while (i < MOD_STACK_SIZE)
            {
                this.modifingOrdStack.Push(new modifingOrd());
                ++i;
            }

            this.addLog = Console.WriteLine;
            string ordlogpath = "c:\\users\\yusai";
            this.ordLoggingTh = new Thread(() =>
            {
                this.ordLogging(ordlogpath);
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
            DataSpotOrderUpdate? output = await this.ord_client.placeNewSpotOrder(ins.market, ins.baseCcy, ins.quoteCcy, side, ordtype, quantity, price, timeinforce);
            if(output != null)
            {
                output.symbol_market = ins.symbol_market;
                this.ord_client.ordUpdateQueue.Enqueue(output);
            }
            return output;
        }
        async public Task<DataSpotOrderUpdate?> placeCancelSpotOrder(Instrument ins, string orderId)
        {
            DataSpotOrderUpdate? output = await this.ord_client.placeCancelSpotOrder(ins.market, ins.baseCcy, ins.quoteCcy, orderId);
            if (output != null)
            {
                output.symbol_market = ins.symbol_market;
                this.ord_client.ordUpdateQueue.Enqueue(output);
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
                    this.addLog("[ERROR] Order not found. Id:" + orderId);
                    return null;
                }
            }
            else
            {
                if (this.live_orders.ContainsKey(orderId))
                {
                    ord = this.live_orders[orderId];
                    this.placeCancelSpotOrder(ins, orderId);
                    output = await this.placeNewSpotOrder(ins, ord.side, ord.order_type, quantity, price, ord.time_in_force);
                    return output;
                }
                else
                {
                    this.addLog("[ERROR] Order not found. Id:" + orderId);
                    return null;
                }
            }
        }

        public void updateOrders()
        {
            int i = 0;
            DataSpotOrderUpdate ord;
            DataSpotOrderUpdate prevord;
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
                            }
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
                            }
                        }
                    }
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

            this.addLog("updateOrders exited");
        }

        private void ordLogging(string logPath)
        {
            string filename = logPath + "\\orderlog_" + DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss") + ".csv";
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
                        if (aborting)
                        {
                            while (this.ordLogQueue.Count > 0)
                            {
                                if (this.ordLogQueue.TryDequeue(out line))
                                {
                                    s.WriteLine(line);
                                }
                            }
                            s.Flush();
                            s.Close();
                            break;
                        }
                    }
                }
            }
            this.aborting = false;
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

