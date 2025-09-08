using CryptoClients.Net;
using CryptoClients.Net.Enums;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Requests;
using CryptoExchange.Net.SharedApis;
using HTX.Net.Enums;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Net;
using System.Text;
using System.ComponentModel.Design;

namespace Crypto_Clients
{
    public class DataOrderBook
    {
        public DateTime? timestamp;
        public string market;
        public string? streamId;
        public string? symbol;
        public SocketUpdateType? updateType;

        public Dictionary<decimal, decimal> asks;
        public Dictionary<decimal, decimal> bids;

        public DataOrderBook()
        {
            this.timestamp = null;
            this.market = "";
            this.streamId = "";
            this.symbol = "";
            this.updateType = null;
            this.asks = new Dictionary<decimal, decimal>();
            this.bids = new Dictionary<decimal, decimal>();
        }

        public void setSharedOrderBook(ExchangeEvent<SharedOrderBook> update)
        {
            this.timestamp = update.DataTime;
            this.updateType = update.UpdateType;
            this.market = update.Exchange;
            this.streamId = update.StreamId;
            this.symbol = update.Symbol;

            int i = 0;
            int dataLength = update.Data.Asks.Length;
            while (i < dataLength)
            {
                this.asks[update.Data.Asks[i].Price] = update.Data.Asks[i].Quantity;
                ++i;
            }

            i = 0;
            dataLength = update.Data.Bids.Length;
            while (i < dataLength)
            {
                this.bids[update.Data.Bids[i].Price] = update.Data.Bids[i].Quantity;
                ++i;
            }
        }

        public void setBybitOrderBook(DataEvent<Bybit.Net.Objects.Models.V5.BybitOrderbook> update)
        {
            this.timestamp = update.DataTime;
            this.updateType = update.UpdateType;
            this.market = "Bybit";
            this.streamId = update.StreamId;
            this.symbol = update.Symbol;

            int i = 0;
            int dataLength = update.Data.Asks.Length;
            while (i < dataLength)
            {
                this.asks[update.Data.Asks[i].Price] = update.Data.Asks[i].Quantity;
                ++i;
            }

            i = 0;
            dataLength = update.Data.Bids.Length;
            while (i < dataLength)
            {
                this.bids[update.Data.Bids[i].Price] = update.Data.Bids[i].Quantity;
                ++i;
            }
        }

        public void setCoinbaseOrderBook(DataEvent<Coinbase.Net.Objects.Models.CoinbaseOrderBookUpdate> update)
        {
            this.timestamp = update.DataTime;
            this.updateType = update.UpdateType;
            this.market = "Coinbase";
            this.streamId = update.StreamId;
            this.symbol = update.Symbol;

            int i = 0;
            int dataLength = update.Data.Asks.Length;
            while (i < dataLength)
            {
                this.asks[update.Data.Asks[i].Price] = update.Data.Asks[i].Quantity;
                ++i;
            }

            i = 0;
            dataLength = update.Data.Bids.Length;
            while (i < dataLength)
            {
                this.bids[update.Data.Bids[i].Price] = update.Data.Bids[i].Quantity;
                ++i;
            }
        }

        public string ToString()
        {
            string output = this.timestamp.ToString() + ",OrderBook," + this.streamId + "," + this.market + "," + this.updateType + "," + this.symbol;

            foreach (var item in this.asks)
            {
                output += ",Ask," + item.ToString();
            }
            foreach (var item in this.bids)
            {
                output += ",Bid," + item.ToString();
            }

            return output;

        }

        public void init()
        {
            this.timestamp = null;
            this.market = "";
            this.streamId = "";
            this.symbol = "";
            this.updateType = null;
            this.asks.Clear();
            this.bids.Clear();
        }
    }
    public class DataSpotOrderUpdate
    {
        public DateTime? timestamp;
        public string market;
        public string order_id;
        public string symbol;
        public SharedOrderType order_type;

        public SharedOrderSide side;
        public SharedOrderStatus status;
        public SharedTimeInForce? time_in_force;

        public decimal order_quantity;
        public decimal filled_quantity;
        public decimal order_price;
        public decimal average_price;

        public string? client_order_id;

        public string? fee_asset;
        public decimal fee;

        public DateTime? create_time;
        public DateTime? update_time;

        public string last_trade;
        public decimal trigger_price;
        public bool is_trigger_order;

        public DataSpotOrderUpdate()
        {
            this.timestamp = null;
            this.market = "";
            this.order_id = "";
            this.symbol = "";
            this.order_type = SharedOrderType.Other;
            this.side = SharedOrderSide.Buy;
            this.status = SharedOrderStatus.Canceled;
            this.time_in_force = null;
            this.order_quantity = 0;
            this.filled_quantity = 0;
            this.order_price = -1;
            this.average_price = -1;
            this.client_order_id = "";
            this.fee_asset = "";
            this.fee = 0;
            this.create_time = null;
            this.update_time = null;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
        }

        public void setSharedSpotOrder(SharedSpotOrder update, string market,DateTime? timestamp)
        {
            this.timestamp = timestamp;
            this.market = market;
            this.order_id = update.OrderId;
            this.symbol = update.Symbol;
            this.order_type = update.OrderType;
            this.side = update.Side;
            this.status = update.Status;
            this.time_in_force = update.TimeInForce;
            if(update.OrderQuantity != null)
            {
                if (update.OrderQuantity.QuantityInBaseAsset != null)
                {
                    this.order_quantity = (decimal)update.OrderQuantity.QuantityInBaseAsset;
                }
                else if (update.OrderQuantity.QuantityInQuoteAsset != null)
                {
                    if (update.AveragePrice != null)
                    {
                        this.order_quantity = (decimal)update.OrderQuantity.QuantityInQuoteAsset / (decimal)update.AveragePrice;
                    }
                    else if (update.OrderPrice != null)
                    {
                        this.order_quantity = (decimal)update.OrderQuantity.QuantityInQuoteAsset / (decimal)update.OrderPrice;
                    }
                    else
                    {
                        this.order_quantity = 0;
                    }
                }
                else if (update.OrderQuantity.QuantityInContracts != null)
                {
                    this.order_quantity = (decimal)update.OrderQuantity.QuantityInContracts;
                }
                else
                {
                    this.order_quantity = 0;
                }
            }
            else
            {
                this.order_quantity = 0;
            }
            if (update.QuantityFilled != null)
            {
                if (update.QuantityFilled.QuantityInBaseAsset != null)
                {
                    this.filled_quantity = (decimal)update.QuantityFilled.QuantityInBaseAsset;
                }
                else if (update.QuantityFilled.QuantityInQuoteAsset != null)
                {
                    if (update.AveragePrice != null)
                    {
                        this.filled_quantity = (decimal)update.QuantityFilled.QuantityInQuoteAsset / (decimal)update.AveragePrice;
                    }
                    else if (update.OrderPrice != null)
                    {
                        this.filled_quantity = (decimal)update.QuantityFilled.QuantityInQuoteAsset / (decimal)update.OrderPrice;
                    }
                    else
                    {
                        this.filled_quantity = 0;
                    }
                }
                else if (update.QuantityFilled.QuantityInContracts != null)
                {
                    this.filled_quantity = (decimal)update.QuantityFilled.QuantityInContracts;
                }
                else
                {
                    this.filled_quantity = 0;
                }
            }
            else
            {
                this.filled_quantity = 0;
            }

            if(update.OrderPrice != null)
            {
                this.order_price = (decimal)update.OrderPrice;
            }
            else
            {
                this.order_price = 0;
            }
            if(update.AveragePrice!= null)
            {
                this.average_price = (decimal)update.AveragePrice;
            }
            else
            {
                this.average_price = 0;
            }
            this.client_order_id = update.ClientOrderId;
            this.fee_asset = update.FeeAsset;
            if(update.Fee != null)
            {
                this.fee = (decimal)update.Fee;
            }
            else
            {

                this.fee = 0;
            }
            this.create_time = update.CreateTime;
            this.update_time = update.UpdateTime;
            if(update.TriggerPrice != null)
            {
                this.trigger_price = (decimal)update.TriggerPrice;
            }
            else
            {
                this.trigger_price= 0;
            }
            this.is_trigger_order = update.IsTriggerOrder;
        }

        public void init()
        {
            this.timestamp = null;
            this.market = "";
            this.order_id = "";
            this.symbol = "";
            this.order_type = SharedOrderType.Other;
            this.side = SharedOrderSide.Buy;
            this.status = SharedOrderStatus.Canceled;
            this.time_in_force = null;
            this.order_quantity = 0;
            this.filled_quantity = 0;
            this.order_price = -1;
            this.average_price = -1;
            this.client_order_id = "";
            this.fee_asset = "";
            this.fee = 0;
            this.create_time = null;
            this.update_time = null;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
        }

    }
    public class DataTrade
    {
        public string? market;
        public string? symbol;
        public DateTime? timestamp;
        public DateTime? filled_time;
        public SharedOrderSide? side;
        public decimal price;
        public decimal quantity;

        public DataTrade()
        {
            this.market = "";
            this.symbol = "";
            this.timestamp = null;
            this.filled_time = null;
            this.side = null;
            this.price = 0;
            this.quantity = 0;
        }
        public void setSharedTrade(SharedTrade trd,string? market,string? symbol,DateTime? time)
        {
            this.market = market;
            this.symbol = symbol;
            this.timestamp = time;
            this.filled_time = trd.Timestamp;
            this.side = trd.Side;
            this.price = trd.Price;
            this.quantity = trd.Quantity;
        }
        public void init()
        {
            this.market = "";
            this.symbol = "";
            this.timestamp = null;
            this.filled_time = null;
            this.side = null;
            this.price = 0;
            this.quantity = 0;
        }
    }
    public class Crypto_Clients
    {
        ExchangeSocketClient _client;
        ExchangeRestClient _rest_client;
        Bybit.Net.Clients.BybitSocketClient BybitSocketClient;
        Coinbase.Net.Clients.CoinbaseSocketClient CoinbaseSocketClient;

        CryptoClients.Net.Models.ExchangeCredentials creds;

        const int STACK_SIZE = 100000;

        public ConcurrentQueue<DataOrderBook> ordBookQueue;
        public ConcurrentStack<DataOrderBook> ordBookStack;

        public ConcurrentQueue<DataSpotOrderUpdate> ordUpdateQueue;
        public ConcurrentStack<DataSpotOrderUpdate> ordUpdateStack;

        public ConcurrentQueue<DataTrade> tradeQueue;
        public ConcurrentStack<DataTrade> tradeStack;

        public ConcurrentQueue<string> strQueue;

        public Action<string> addLog;
        public Crypto_Clients()
        {
            this._client = new ExchangeSocketClient();
            this.BybitSocketClient = new Bybit.Net.Clients.BybitSocketClient();
            this.CoinbaseSocketClient = new Coinbase.Net.Clients.CoinbaseSocketClient();
            this._rest_client = new ExchangeRestClient();

            this.creds = new CryptoClients.Net.Models.ExchangeCredentials();

            this.ordBookQueue = new ConcurrentQueue<DataOrderBook>();
            this.ordBookStack = new ConcurrentStack<DataOrderBook>();

            this.ordUpdateQueue = new ConcurrentQueue<DataSpotOrderUpdate>();
            this.ordUpdateStack = new ConcurrentStack<DataSpotOrderUpdate>();

            this.tradeQueue = new ConcurrentQueue<DataTrade>();
            this.tradeStack = new ConcurrentStack<DataTrade>();

            this.strQueue = new ConcurrentQueue<string> ();

            int i = 0;

            while(i < STACK_SIZE)
            {
                this.ordBookStack.Push(new DataOrderBook());
                this.ordUpdateStack.Push(new DataSpotOrderUpdate());
                this.tradeStack.Push(new DataTrade());
                ++i;
            }
        }

        public void pushToOrderBookStack(DataOrderBook msg)
        {
            msg.init();
            this.ordBookStack.Push(msg);
        }
        public void pushToTradeStack(DataTrade msg)
        {
            msg.init();
            this.tradeStack.Push(msg);
        }
        public void pushToOrderUpdateStack(DataSpotOrderUpdate msg)
        {
            msg.init();
            this.ordUpdateStack.Push(msg);
        }

        public void readCredentials(string market, string jsonfilename)
        {
            string fileContent = File.ReadAllText(jsonfilename);

            using JsonDocument doc = JsonDocument.Parse(fileContent);
            var root = doc.RootElement;

            switch(market)
            {
                case string value when value == Exchange.Bybit:
                    this.creds.Bybit = new CryptoExchange.Net.Authentication.ApiCredentials(root.GetProperty("name").ToString(), root.GetProperty("privateKey").ToString());
                    break;
                case string value when value == Exchange.Coinbase:
                    this.creds.Coinbase = new CryptoExchange.Net.Authentication.ApiCredentials(root.GetProperty("name").ToString(), root.GetProperty("privateKey").ToString());
                    break;
            }
            this._rest_client.SetApiCredentials(this.creds);
            this._client.SetApiCredentials(this.creds);
        }

//REST API
        async public Task getBalance(IEnumerable<string>? markets)
        {
            GetBalancesRequest req = new GetBalancesRequest(TradingMode.Spot);
            foreach (var subResult in await this._rest_client.GetBalancesAsync(req, markets))
            {
                if(subResult.Success)
                {
                    foreach(var data in subResult.Data)
                    {
                        this.strQueue.Enqueue(subResult.Exchange + " " + data.ToString()); 
                    }
                    
                }
                else
                {
                    this.strQueue.Enqueue("ERROR");
                }
            }
        }
        async public Task<string> placeNewSpotOrder(string market,string baseCcy,string quoteCcy,SharedOrderSide side,SharedOrderType ordtype,decimal quantity,decimal price,SharedTimeInForce? timeinforce = null,string? clordId = null,ExchangeParameters? param = null)
        {
            SharedSymbol symbol = new SharedSymbol(TradingMode.Spot, baseCcy, quoteCcy);
            SharedQuantity qty = new SharedQuantity();
            qty.QuantityInBaseAsset = quantity;
            PlaceSpotOrderRequest req = new PlaceSpotOrderRequest(symbol,side,ordtype,qty,price,timeinforce,clordId,param);

            var result = await this._rest_client.PlaceSpotOrderAsync(market, req);
            if(result.Success)
            {
                return result.Data.Id;
            }
            else
            {
                
                return result.Error.ToString();
            }
        }
        async public Task<string> placeCancelSpotOrder(string market,string baseCcy,string quoteCcy,string orderId,ExchangeParameters? param = null)
        {
            SharedSymbol symbol = new SharedSymbol(TradingMode.Spot, baseCcy, quoteCcy);
            CancelOrderRequest req = new CancelOrderRequest(symbol, orderId, param);
            var result = await this._rest_client.CancelSpotOrderAsync(market, req);
            if (result.Success)
            {
                return result.Data.Id;
            }
            else
            {
                return result.Error.ToString();
            }
        }
//Websocket
        async public Task subscribeSpotOrderUpdates(IEnumerable<string>? markets)
        {
            SubscribeSpotOrderRequest request = new SubscribeSpotOrderRequest();
            foreach(var subResult in await this._client.SubscribeToSpotOrderUpdatesAsync(request, LogOrderUpdates, markets))
                Console.WriteLine($"{subResult.Exchange} subscribe spot order updates result: {subResult.Success} {subResult.Error}");
        }
        void LogOrderUpdates(ExchangeEvent<SharedSpotOrder[]> update)
        {
            DataSpotOrderUpdate obj;
            foreach(var ord in update.Data)
            {
                while(!this.ordUpdateStack.TryPop(out obj))
                {

                }
                obj.setSharedSpotOrder(ord,update.Exchange,update.DataTime);
                this.ordUpdateQueue.Enqueue(obj);
            }
        }       
        async public Task subscribeTrades(IEnumerable<string>? markets,string baseCcy,string quoteCcy)
        {
            var symbol = new SharedSymbol(TradingMode.Spot, baseCcy, quoteCcy);

            // Subscribe to trade updates for the specified exchange
            foreach (var subResult in await this._client.SubscribeToTradeUpdatesAsync(new SubscribeTradeRequest(symbol), LogTrades, markets))
                Console.WriteLine($"{subResult.Exchange} subscribe trades result: {subResult.Success} {subResult.Error}");
        }
        void LogTrades(ExchangeEvent<SharedTrade[]> update)
        {
            DataTrade trd;
            foreach(var item in update.Data)
            {
                while(!this.tradeStack.TryPop(out trd))
                {

                }
                trd.setSharedTrade(item,update.Exchange,update.Symbol,update.DataTime);
                this.tradeQueue.Enqueue(trd);
            }
        }
        async public Task subscribeOrderBook(IEnumerable<string>? markets, string baseCcy, string quoteCcy)
        {
            var symbol = new SharedSymbol(TradingMode.Spot, baseCcy, quoteCcy);
            var req = new SubscribeOrderBookRequest(symbol);
            foreach (var subResult in await this._client.SubscribeToOrderBookUpdatesAsync(req, LogOrderBook, markets, default))
            {
                Console.WriteLine($"{subResult.Exchange} subscribe orderbook result: {subResult.Success} {subResult.Error}");
            }
                
        }
        void LogOrderBook(ExchangeEvent<SharedOrderBook> update)
        {
            //this.orderBookQueue.Enqueue(update);
            DataOrderBook msg;
            while (!this.ordBookStack.TryPop(out msg))
            {

            }
            msg.setSharedOrderBook(update);
            this.ordBookQueue.Enqueue(msg);
        }
        async public Task subscribeCoinbaseOrderBook(string baseCcy, string quoteCcy)
        {
            var subResult = await this.CoinbaseSocketClient.AdvancedTradeApi.SubscribeToOrderBookUpdatesAsync(baseCcy + "-" + quoteCcy, LogCoinbaseOrderBook);
            Console.WriteLine($"Coinbase subscribe orderbook result: {subResult.Success} {subResult.Error}");
        }
        void LogCoinbaseOrderBook(DataEvent<Coinbase.Net.Objects.Models.CoinbaseOrderBookUpdate> update)
        {
            DataOrderBook msg;

            while (!this.ordBookStack.TryPop(out msg))
            {

            }
            msg.setCoinbaseOrderBook(update);
            this.ordBookQueue.Enqueue(msg);

        }
        async public Task subscribeBybitOrderBook(string baseCcy, string quoteCcy)
        {
            var subResult = await this.BybitSocketClient.V5SpotApi.SubscribeToOrderbookUpdatesAsync(baseCcy + quoteCcy, 50, LogBybitOrderBook);
            Console.WriteLine($"Bybit subscribe orderbook result: {subResult.Success} {subResult.Error}");
        }
        void LogBybitOrderBook(DataEvent<Bybit.Net.Objects.Models.V5.BybitOrderbook> update)
        {
            DataOrderBook msg;
            while (!this.ordBookStack.TryPop(out msg))
            {

            }
            msg.setBybitOrderBook(update);
            this.ordBookQueue.Enqueue(msg);
        }
    }
}
