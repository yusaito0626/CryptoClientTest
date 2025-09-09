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

        const int MOD_STACK_SIZE = 100;

        Dictionary<string, Instrument> Instruments;

        public Action<string> addLog;

        private OrderManager() 
        {
            this.orders = new Dictionary<string, DataSpotOrderUpdate>();
            this.live_orders = new Dictionary<string, DataSpotOrderUpdate>();

            this.modifingOrders = new Dictionary<string, modifingOrd>();
            this.modifingOrdStack = new ConcurrentStack<modifingOrd>();

            int i = 0;
            while (i < MOD_STACK_SIZE)
            {
                this.modifingOrdStack.Push(new modifingOrd());
                ++i;
            }

            this.addLog = Console.WriteLine;
        }
        public void setOrderClient(Crypto_Clients.Crypto_Clients cl)
        {
            this.ord_client = cl;
        }
        public void setInstruments(Dictionary<string, Instrument> dic)
        {
            this.Instruments = dic;
        }

        async public Task<string> placeNewSpotOrder(Instrument ins, SharedOrderSide side, SharedOrderType ordtype, decimal quantity, decimal price, SharedTimeInForce? timeinforce = null)
        {
            string output = await this.ord_client.placeNewSpotOrder(ins.market, ins.baseCcy, ins.quoteCcy, side, ordtype, quantity, price, timeinforce);
            return output;
        }
        async public Task<string> placeCancelSpotOrder(Instrument ins, string orderId)
        {
            string output = await this.ord_client.placeCancelSpotOrder(ins.market, ins.baseCcy, ins.quoteCcy, orderId);
            return output;
        }
        async public Task<string> placeModSpotOrder(Instrument ins, string orderId, decimal quantity, decimal price,bool waitCancel)
        {
            DataSpotOrderUpdate ord;
            SharedOrderSide side;
            SharedOrderType ordtype;
            SharedTimeInForce? timeinforce;
            modifingOrd mod;
            string ordId;
            if (waitCancel)
            {
                if (this.live_orders.ContainsKey(orderId))
                {
                    ord = this.live_orders[orderId];
                    side = ord.side;
                    ordtype = ord.order_type;
                    timeinforce = ord.time_in_force;
                    while (!this.modifingOrdStack.TryPop(out mod))
                    {

                    }
                    mod.ordId = orderId;
                    mod.newPrice = price;
                    mod.newQuantity = quantity;
                    mod.ins = ins;
                    this.modifingOrders[orderId] = mod;
                    ordId = await this.placeCancelSpotOrder(ins, orderId);

                    return ordId;
                }
                else
                {
                    return "";
                }
            }
            else
            {
                if (this.live_orders.ContainsKey(orderId))
                {
                    ord = this.live_orders[orderId];
                    side = ord.side;
                    ordtype = ord.order_type;
                    timeinforce = ord.time_in_force;
                    this.placeCancelSpotOrder(ins, orderId);
                    ordId = await this.placeNewSpotOrder(ins, side, ordtype, quantity, price, timeinforce);
                    return ordId;
                }
                else
                {
                    return "";
                }
            }
        }

        public void updateOrders()
        {
            this.addLog("updateOrders called");
            int i = 0;
            DataSpotOrderUpdate ord;
            DataSpotOrderUpdate prevord;
            Instrument ins;
            modifingOrd mod;
            while (true)
            {
                if(this.ord_client.ordUpdateQueue.TryDequeue(out ord))
                {


                    if (this.orders.ContainsKey(ord.order_id))
                    {
                        prevord = this.orders[ord.order_id];
                        this.orders[ord.order_id] = ord;
                        if(ord.status == SharedOrderStatus.Open)
                        {
                            this.live_orders[ord.order_id] = ord;
                        }
                        else if(this.live_orders.ContainsKey(ord.order_id))
                        {
                            this.live_orders.Remove(ord.order_id);
                        }
                        if(this.modifingOrders.ContainsKey(ord.order_id))
                        {
                            mod = this.modifingOrders[ord.order_id];
                            
                            if(ord.status == SharedOrderStatus.Canceled)
                            {
                                this.placeNewSpotOrder(mod.ins, prevord.side, prevord.order_type, mod.newQuantity, mod.newPrice);
                            }
                            this.modifingOrders.Remove(ord.order_id);
                            mod.init();
                            this.modifingOrdStack.Push(mod);
                        }
                        prevord.init();
                        this.ord_client.ordUpdateStack.Push(prevord);
                    }
                    else
                    {
                        this.orders[ord.order_id] = ord;
                        if (ord.status == SharedOrderStatus.Open)
                        {
                            this.live_orders[ord.order_id] = ord;
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
        public decimal newPrice;
        public decimal newQuantity;
        public Instrument? ins;

        public modifingOrd() { }
        public void init()
        {
            this.ordId = "";
            this.newPrice = 0;
            this.newQuantity = 0;
            this.ins = null;
        }
    }
}

