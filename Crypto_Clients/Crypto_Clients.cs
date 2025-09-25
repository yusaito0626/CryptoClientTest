using CryptoClients.Net;
using CryptoClients.Net.Enums;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Requests;
using CryptoExchange.Net.SharedApis;
using HTX.Net.Enums;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.Design;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using PubnubApi;



namespace Crypto_Clients
{
    public class Crypto_Clients
    {
        ExchangeSocketClient _client;
        ExchangeRestClient _rest_client;
        Bybit.Net.Clients.BybitSocketClient BybitSocketClient;
        Coinbase.Net.Clients.CoinbaseSocketClient CoinbaseSocketClient;
        bitbank_connection bitbank_client;
        coincheck_connection coincheck_client;

        CryptoClients.Net.Models.ExchangeCredentials creds;

        const int STACK_SIZE = 100000;

        public ConcurrentQueue<DataOrderBook> ordBookQueue;
        public ConcurrentStack<DataOrderBook> ordBookStack;

        public ConcurrentQueue<DataTrade> tradeQueue;
        public ConcurrentStack<DataTrade> tradeStack;

        public ConcurrentQueue<DataSpotOrderUpdate> ordUpdateQueue;
        public ConcurrentStack<DataSpotOrderUpdate> ordUpdateStack;

        public ConcurrentQueue<DataFill> fillQueue;
        public ConcurrentStack<DataFill> fillStack;

        public ConcurrentQueue<string> strQueue;

        Thread bitbankOrderUpdateTh;
        Thread bitbankPublicChannelTh;
        Thread coincheckPublicChannelsTh;

        public Action<string> addLog;
        public Crypto_Clients()
        {
            this._client = new ExchangeSocketClient();
            this.BybitSocketClient = new Bybit.Net.Clients.BybitSocketClient();
            this.CoinbaseSocketClient = new Coinbase.Net.Clients.CoinbaseSocketClient();
            this._rest_client = new ExchangeRestClient();

            this.bitbank_client = bitbank_connection.GetInstance();
            this.coincheck_client = coincheck_connection.GetInstance();

            this.creds = new CryptoClients.Net.Models.ExchangeCredentials();

            this.ordBookQueue = new ConcurrentQueue<DataOrderBook>();
            this.ordBookStack = new ConcurrentStack<DataOrderBook>();

            this.ordUpdateQueue = new ConcurrentQueue<DataSpotOrderUpdate>();
            this.ordUpdateStack = new ConcurrentStack<DataSpotOrderUpdate>();

            this.tradeQueue = new ConcurrentQueue<DataTrade>();
            this.tradeStack = new ConcurrentStack<DataTrade>();

            this.fillQueue = new ConcurrentQueue<DataFill>();
            this.fillStack = new ConcurrentStack<DataFill>();

            this.strQueue = new ConcurrentQueue<string>();
            
            this.addLog = Console.WriteLine;

            int i = 0;

            while (i < STACK_SIZE)
            {
                this.ordBookStack.Push(new DataOrderBook());
                this.ordUpdateStack.Push(new DataSpotOrderUpdate());
                this.tradeStack.Push(new DataTrade());
                this.fillStack.Push(new DataFill());
                ++i;
            }
        }

        public void setAddLog(Action<string> act)
        {
            this.addLog = act;
            this.bitbank_client.addLog = act;
            this.coincheck_client.addLog = act;
        }

        public async Task connectAsync()
        {
            await this.bitbank_client.connectPublicAsync();
            this.bitbankPublicChannelTh = new Thread(() =>
            {
                this.bitbank_client.startListen(this.onBitbankMessage);
            });
            this.bitbankPublicChannelTh.Start();

            await this.coincheck_client.connectPublicAsync();
            this.coincheckPublicChannelsTh = new Thread(() =>
            {
                this.coincheck_client.startListen(this.onCoincheckMessage);
            });
            this.coincheckPublicChannelsTh.Start();
            await this.coincheck_client.connectPrivateAsync();
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

            switch (market)
            {
                case string value when value == Exchange.Bybit:
                    this.creds.Bybit = new CryptoExchange.Net.Authentication.ApiCredentials(root.GetProperty("name").ToString(), root.GetProperty("privateKey").ToString());
                    break;
                case string value when value == Exchange.Coinbase:
                    this.creds.Coinbase = new CryptoExchange.Net.Authentication.ApiCredentials(root.GetProperty("name").ToString(), root.GetProperty("privateKey").ToString());
                    break;
                case "bitbank":
                    this.bitbank_client.SetApiCredentials(root.GetProperty("name").ToString(), root.GetProperty("privateKey").ToString());
                    break;
                case "coincheck":
                    this.coincheck_client.SetApiCredentials(root.GetProperty("name").ToString(), root.GetProperty("privateKey").ToString());
                    break;
            }
            this._rest_client.SetApiCredentials(this.creds);
            this._client.SetApiCredentials(this.creds);
        }

        //REST API
        async public Task<ExchangeWebResult<SharedBalance[]>[]> getBalance(IEnumerable<string>? markets)
        {
            GetBalancesRequest req = new GetBalancesRequest(TradingMode.Spot);
            return await this._rest_client.GetBalancesAsync(req, markets);
        }
        async public Task<ExchangeWebResult<SharedFee>[]> getFees(IEnumerable<string>? markets,string baseCcy,string quoteCcy)
        {
            SharedSymbol symbol = new SharedSymbol(TradingMode.Spot, baseCcy, quoteCcy);
            GetFeeRequest req = new GetFeeRequest(symbol);
            return await this._rest_client.GetFeesAsync(req, markets);
        }
        async public Task<DataSpotOrderUpdate?> placeNewSpotOrder(string market, string baseCcy, string quoteCcy, orderSide _side,orderType _ordtype, decimal quantity, decimal price, timeInForce? _timeinforce = null, string? clordId = null, ExchangeParameters? param = null)
        {
            SharedSymbol symbol = new SharedSymbol(TradingMode.Spot, baseCcy, quoteCcy);
            SharedQuantity qty = new SharedQuantity();
            SharedOrderSide side = (SharedOrderSide)Enum.Parse(typeof(SharedOrderSide), _side.ToString());
            SharedOrderType ordtype = (SharedOrderType)Enum.Parse(typeof(SharedOrderType),_ordtype.ToString());
            SharedTimeInForce? timeinforce;
            if(_timeinforce != null)
            {
                timeinforce = (SharedTimeInForce)Enum.Parse(typeof(SharedTimeInForce), _timeinforce.ToString());
            }
            else
            {
                timeinforce = null;
            }
            qty.QuantityInBaseAsset = quantity;

            PlaceSpotOrderRequest req = new PlaceSpotOrderRequest(symbol, side, ordtype, qty, price, timeinforce, clordId, param);
            
            var result = await this._rest_client.PlaceSpotOrderAsync(market, req);
            if (result.Success)
            {
                DataSpotOrderUpdate ord;
                while (!this.ordUpdateStack.TryPop(out ord))
                {

                }
                ord.order_id = result.Data.Id;
                ord.timestamp = DateTime.UtcNow;
                ord.side = _side;
                ord.order_price = price;
                ord.order_quantity = quantity;
                ord.market = market;
                ord.symbol = symbol.SymbolName;
                ord.filled_quantity = 0;
                ord.order_type = _ordtype;
                ord.status = orderStatus.WaitOpen;
                return ord;
            }
            else
            {
                this.addLog("[ERROR] New Order Failed.");
                this.addLog(result.Error.ToString());
                return null;
            }
        }
        async public Task<DataSpotOrderUpdate?> placeCancelSpotOrder(string market, string baseCcy, string quoteCcy, string orderId, ExchangeParameters? param = null)
        {
            SharedSymbol symbol = new SharedSymbol(TradingMode.Spot, baseCcy, quoteCcy);
            CancelOrderRequest req = new CancelOrderRequest(symbol, orderId, param);
            var result = await this._rest_client.CancelSpotOrderAsync(market, req);
            if (result.Success)
            {
                DataSpotOrderUpdate ord;
                while (!this.ordUpdateStack.TryPop(out ord))
                {

                }
                ord.order_id = result.Data.Id;
                ord.timestamp = DateTime.UtcNow;
                ord.order_price = -1;
                ord.order_quantity = 0;
                ord.market = market;
                ord.symbol = symbol.SymbolName;
                ord.filled_quantity = 0;
                ord.status = orderStatus.WaitCancel;
                return ord;
            }
            else
            {
                this.addLog("[ERROR] Cancel Order Failed.");
                this.addLog(result.Error.ToString());
                return null;
            }
        }
        //Websocket
        async public Task subscribeSpotOrderUpdates(IEnumerable<string>? markets)
        {
            SubscribeSpotOrderRequest request = new SubscribeSpotOrderRequest();
            foreach(string m in markets)
            {
                switch(m)
                {
                    case "bitbank":
                        await this.bitbank_client.connectPrivateAsync();
                        this.bitbankOrderUpdateTh = new Thread(this.bitbankOrderUpdates);
                        this.bitbankOrderUpdateTh.Start();
                        break;
                    case "coincheck":
                        break;
                    default:
                        var subResult = await this._client.SubscribeToSpotOrderUpdatesAsync(m, request, LogOrderUpdates);
                        this.addLog($"{subResult.Exchange} subscribe spot order updates result: {subResult.Success} {subResult.Error}");
                        break;
                }
            }
            //foreach (var subResult in await this._client.SubscribeToSpotOrderUpdatesAsync(request, LogOrderUpdates, markets))
            //    this.addLog($"{subResult.Exchange} subscribe spot order updates result: {subResult.Success} {subResult.Error}");
        }
        void LogOrderUpdates(ExchangeEvent<SharedSpotOrder[]> update)
        {
            DataSpotOrderUpdate obj;
            foreach (var ord in update.Data)
            {
                while (!this.ordUpdateStack.TryPop(out obj))
                {

                }
                obj.setSharedSpotOrder(ord, update.Exchange, update.DataTime);
                this.ordUpdateQueue.Enqueue(obj);
            }
        }

        public void bitbankOrderUpdates()
        {
            int i = 0;
            JsonElement js;
            DataSpotOrderUpdate ord;
            DataFill fill;
            while(true)
            {
                if(this.bitbank_client.orderQueue.TryDequeue(out js))
                {
                    while(!this.ordUpdateStack.TryPop(out ord))
                    {

                    }
                    ord.setBitbankSpotOrder(js);
                    this.ordUpdateQueue.Enqueue(ord);
                    i = 0;
                }
                else if(this.bitbank_client.fillQueue.TryDequeue(out js))
                {
                    while(!this.fillStack.TryPop(out fill))
                    {

                    }
                    fill.setBitBankFill(js);
                    this.fillQueue.Enqueue(fill);
                    i = 0;
                }
                else
                {
                    ++i;
                    if (i > 100000)
                    {
                        Thread.Sleep(0);
                        i = 0;
                    }
                }
            }
        }
        async public Task subscribeTrades(IEnumerable<string>? markets, string baseCcy, string quoteCcy)
        {
            var symbol = new SharedSymbol(TradingMode.Spot, baseCcy, quoteCcy);
            
            // Subscribe to trade updates for the specified exchange
            foreach (string m in markets)
            {
                switch (m)
                {
                    case "bitbank":
                        await this.bitbank_client.subscribeTrades(baseCcy, quoteCcy);
                        break;
                    case "coincheck":
                        await this.coincheck_client.subscribeTrades(baseCcy, quoteCcy);
                        break;
                    default:
                        var subResult = await this._client.SubscribeToTradeUpdatesAsync(m, new SubscribeTradeRequest(symbol), LogTrades);
                        this.addLog($"{subResult.Exchange} subscribe trades result: {subResult.Success} {subResult.Error}");
                        break;
                }
                
            }
        }
        void LogTrades(ExchangeEvent<SharedTrade[]> update)
        {
            DataTrade trd;
            foreach (var item in update.Data)
            {
                while (!this.tradeStack.TryPop(out trd))
                {

                }
                trd.setSharedTrade(item, update.Exchange, update.Symbol, update.DataTime);
                this.tradeQueue.Enqueue(trd);
            }
        }
        async public Task subscribeOrderBook(IEnumerable<string>? markets, string baseCcy, string quoteCcy)
        {
            var symbol = new SharedSymbol(TradingMode.Spot, baseCcy, quoteCcy);
            var req = new SubscribeOrderBookRequest(symbol);
            foreach (string m in markets)
            {
                switch(m)
                {
                    case "bitbank":
                        await this.bitbank_client.subscribeOrderBook(baseCcy, quoteCcy);
                        break;
                    case "coincheck":
                        await this.coincheck_client.subscribeOrderBook(baseCcy, quoteCcy);
                        break;
                    default:
                        var subResult = await this._client.SubscribeToOrderBookUpdatesAsync(m, req, LogOrderBook);
                        this.addLog($"{subResult.Exchange} subscribe trades result: {subResult.Success} {subResult.Error}");
                        break;
                }
            }
            //foreach (var subResult in await this._client.SubscribeToOrderBookUpdatesAsync(req, LogOrderBook, markets, default))
            //{
            //    this.addLog($"{subResult.Exchange} subscribe orderbook result: {subResult.Success} {subResult.Error}");
            //}
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
            this.addLog($"Coinbase subscribe orderbook result: {subResult.Success} {subResult.Error}");
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
            this.addLog($"Bybit subscribe orderbook result: {subResult.Success} {subResult.Error}");
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

        public void onBitbankMessage(string msg_body)
        {
            JsonDocument doc = JsonDocument.Parse(msg_body);

            string eventName = doc.RootElement[0].GetString();
            JsonElement payload = doc.RootElement[1];
            string roomName = payload.GetProperty("room_name").GetString();
            if (roomName.StartsWith("ticker"))
            {

            }
            else if (roomName.StartsWith("depth_diff"))
            {
                string symbol = roomName.Substring("depth_diff_".Length);
                DataOrderBook ord;
                while (!this.ordBookStack.TryPop(out ord))
                {

                }
                ord.setBitbankOrderBook(payload.GetProperty("message").GetProperty("data"), symbol, false);
                this.ordBookQueue.Enqueue(ord);
            }
            else if (roomName.StartsWith("depth_whole"))
            {
                string symbol = roomName.Substring("depth_whole_".Length);
                DataOrderBook ord;
                while (!this.ordBookStack.TryPop(out ord))
                {

                }
                ord.setBitbankOrderBook(payload.GetProperty("message").GetProperty("data"), symbol, true);
                this.ordBookQueue.Enqueue(ord);

            }
            else if (roomName.StartsWith("transactions"))
            {
                string symbol = roomName.Substring("transactions_".Length);
                foreach (var element in payload.GetProperty("message").GetProperty("data").GetProperty("transactions").EnumerateArray())
                {
                    DataTrade trd;
                    while (!this.tradeStack.TryPop(out trd))
                    {

                    }
                    trd.setBitbankTrade(element, "bitbank", symbol);
                    this.tradeQueue.Enqueue(trd);
                }

            }
        }

        public void onCoincheckMessage(string msg_body)
        {
            //this.addLog(msg_body);
            JsonDocument doc = JsonDocument.Parse(msg_body);
            var root = doc.RootElement;
            if(root[0].ValueKind == JsonValueKind.String)//OrderBook
            {
                string symbol = root[0].GetString();
                var obj = root[1];
                DataOrderBook ord;
                while (!this.ordBookStack.TryPop(out ord))
                {

                }
                ord.setCoincheckOrderBook(obj,symbol);
                this.ordBookQueue.Enqueue(ord);
            }
            else if(root[0].ValueKind == JsonValueKind.Array)//Trade
            {
                foreach( var item in root.EnumerateArray())
                {
                    DataTrade trd;
                    while (!this.tradeStack.TryPop(out trd))
                    {

                    }
                    trd.setCoincheckTrade(item);
                    this.tradeQueue.Enqueue(trd);
                }
            }
        }

    }
    public class DataOrderBook
    {
        public DateTime? timestamp;
        public DateTime? orderbookTime;
        public string market;
        public string? streamId;
        public string? symbol;
        public SocketUpdateType? updateType;

        public Dictionary<decimal, decimal> asks;
        public Dictionary<decimal, decimal> bids;

        public long seqNo;

        public DataOrderBook()
        {
            this.timestamp = null;
            this.orderbookTime = null;
            this.market = "";
            this.streamId = "";
            this.symbol = "";
            this.updateType = null;
            this.asks = new Dictionary<decimal, decimal>();
            this.bids = new Dictionary<decimal, decimal>();
            this.seqNo = -1;
        }

        public void setSharedOrderBook(ExchangeEvent<SharedOrderBook> update)
        {
            this.timestamp = DateTime.UtcNow;
            this.orderbookTime = update.DataTime;
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
            this.timestamp = DateTime.UtcNow;
            this.orderbookTime = update.DataTime;
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
            this.timestamp = DateTime.UtcNow;
            this.orderbookTime = update.DataTime; 
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

        public void setBitbankOrderBook(JsonElement js,string symbol,bool snapshot)
        {
            this.timestamp = DateTime.UtcNow;
            this.market = "bitbank";
            this.symbol = symbol;

            if (snapshot)
            {
                this.updateType = SocketUpdateType.Snapshot;
                this.orderbookTime = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("timestamp").GetInt64()).UtcDateTime;
                this.streamId = js.GetProperty("sequenceId").GetString();
                this.seqNo = Int64.Parse(this.streamId);
                var data = js.GetProperty("asks").EnumerateArray();
                foreach (var item in data)
                {
                    this.asks[decimal.Parse(item[0].GetString())] = decimal.Parse(item[1].GetString());
                }
                data = js.GetProperty("bids").EnumerateArray();
                foreach (var item in data)
                {
                    this.bids[decimal.Parse(item[0].GetString())] = decimal.Parse(item[1].GetString());
                }
            }
            else
            {
                this.updateType = SocketUpdateType.Update;
                this.orderbookTime = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("t").GetInt64()).UtcDateTime;
                this.streamId = js.GetProperty("s").GetString();
                this.seqNo = Int64.Parse(this.streamId);
                var data = js.GetProperty("a").EnumerateArray();
                foreach (var item in data)
                {
                    this.asks[decimal.Parse(item[0].GetString())] = decimal.Parse(item[1].GetString());
                }
                data = js.GetProperty("b").EnumerateArray();
                foreach (var item in data)
                {
                    this.bids[decimal.Parse(item[0].GetString())] = decimal.Parse(item[1].GetString());
                }
            }
        }
        public void setCoincheckOrderBook(JsonElement js,string symbol)
        {
            this.timestamp = DateTime.UtcNow;
            this.market = "coincheck";
            this.symbol = symbol;
            this.updateType = SocketUpdateType.Update;
            this.orderbookTime = DateTimeOffset.FromUnixTimeSeconds(Int64.Parse(js.GetProperty("last_update_at").GetString())).UtcDateTime;
            var data = js.GetProperty("asks").EnumerateArray();
            foreach (var item in data)
            {
                this.asks[decimal.Parse(item[0].GetString())] = decimal.Parse(item[1].GetString());
            }
            data = js.GetProperty("bids").EnumerateArray();
            foreach (var item in data)
            {
                this.bids[decimal.Parse(item[0].GetString())] = decimal.Parse(item[1].GetString());
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
            this.orderbookTime = null;
            this.market = "";
            this.streamId = "";
            this.symbol = "";
            this.updateType = null;
            this.asks.Clear();
            this.bids.Clear();
            this.seqNo = -1;
        }
    }
    public class DataFill
    {
        public DateTime? timestamp;
        public string symbol_market;
        public string market;
        public decimal quantity;
        public DateTime? filled_time;
        public decimal fee_base;
        public decimal fee_quote;
        public string maker_taker;
        public string order_id;
        public string symbol;
        public decimal price;
        public orderSide side;
        public string trade_id;
        public orderType order_type;
        public decimal profit_loss;
        public decimal interest;

        public DataFill()
        {
            this.timestamp = null;
            this.symbol_market = "";
            this.market = "";
            this.quantity = 0;
            this.filled_time = null;
            this.fee_base = 0;
            this.fee_quote = 0;
            this.maker_taker = "";
            this.order_id = "";
            this.symbol = "";
            this.price = 0;
            this.side = orderSide.NONE;
            this.trade_id = "";
            this.order_type = orderType.NONE;
            this.profit_loss = 0;
            this.interest = 0;
        }

        public void setBitBankFill(JsonElement js)
        {
            this.timestamp = DateTime.UtcNow;
            this.quantity = decimal.Parse(js.GetProperty("amount").GetString());
            this.filled_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("executed_at").GetInt64()).UtcDateTime;
            this.fee_base = decimal.Parse(js.GetProperty("fee_amount_base").GetString());
            this.fee_quote = decimal.Parse(js.GetProperty("fee_amount_quote").GetString());
            this.maker_taker = js.GetProperty("maker_taker").GetString();
            this.order_id = js.GetProperty("order_id").GetInt64().ToString();
            this.symbol = js.GetProperty("pair").GetString();
            this.market = "bitbank";
            this.symbol_market = this.symbol_market + "@" + this.market;
            this.price = decimal.Parse(js.GetProperty("price").GetString());
            string side = js.GetProperty("side").GetString();
            if(side == "buy")
            {
                this.side = orderSide.Buy;
            }
            else if(side == "sell")
            {
                this.side = orderSide.Sell;
            }
            this.trade_id = js.GetProperty("trade_id").GetInt64().ToString();
            string _type = js.GetProperty("type").GetString();
            switch(_type)
            {
                case "limit":
                    this.order_type = orderType.Limit;
                    break;
                case "market":
                    this.order_type = orderType.Market;
                    break;
                default:
                    this.order_type = orderType.Other;
                    break;
            }
            //this.profit_loss = decimal.Parse(js.GetProperty("profit_loss").GetString());
            //this.interest = decimal.Parse(js.GetProperty("interest").GetString());
        }

        public string ToString()
        {
            string line;
            if (this.timestamp != null)
            {
                line = ((DateTime)this.timestamp).ToString("yyyy-MM-dd HH:mm:ss.fff");
            }
            else
            {
                line = "";
            }
            line += "," + this.trade_id + "," + this.order_id + "," + this.market + "," + this.symbol + "," + this.order_type.ToString() + "," + this.side.ToString() + "," + this.price.ToString() + "," + this.quantity.ToString() + "," + this.maker_taker + "," + this.fee_base.ToString() + "," + this.fee_quote.ToString() + "," + this.profit_loss.ToString() + "," + this.interest + ",";
            if (this.filled_time != null)
            {
                line += ((DateTime)this.filled_time).ToString("yyyy-MM-dd HH:mm:ss.fff");
            }
            else
            {
                line += "";
            }
            return line;
        }

        public void init()
        {
            this.timestamp = null;
            this.symbol_market = "";
            this.market = "";
            this.quantity = 0;
            this.filled_time = null;
            this.fee_base = 0;
            this.fee_quote = 0;
            this.maker_taker = "";
            this.order_id = "";
            this.symbol = "";
            this.price = 0;
            this.side = orderSide.NONE;
            this.trade_id = "";
            this.order_type = orderType.NONE;
            this.profit_loss = 0;
            this.interest = 0;
        }
    }
    public class DataSpotOrderUpdate
    {
        public DateTime? timestamp;
        public string symbol_market;
        public string market;
        public string order_id;
        public string symbol;
        public orderType order_type;
        public orderSide side;
        public orderStatus status;
        public timeInForce time_in_force;

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
            this.symbol_market = "";
            this.market = "";
            this.order_id = "";
            this.symbol = "";
            this.order_type = orderType.NONE;
            this.side = orderSide.NONE;
            this.status = orderStatus.NONE;
            this.time_in_force = timeInForce.NONE;
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

        public void setBitbankSpotOrder(JsonElement js)
        {
            this.timestamp = DateTime.UtcNow;
            this.symbol = js.GetProperty("pair").GetString();
            this.symbol_market = this.symbol + "@bitbank";
            this.market = "bitbank";
            this.order_id = js.GetProperty("order_id").GetInt64().ToString();
            string _type = js.GetProperty("type").GetString();
            switch (_type)
            {
                case "limit":
                    this.order_type = orderType.Limit;
                    this.order_price = decimal.Parse(js.GetProperty("price").GetString());
                    break;
                case "market":
                    this.order_type = orderType.Market;
                    this.order_price = 0;
                    break;
                default:
                    this.order_type = orderType.Other;
                    break;
            }
            string side = js.GetProperty("side").GetString();
            if (side == "buy")
            {
                this.side = orderSide.Buy;
            }
            else if (side == "sell")
            {
                this.side = orderSide.Sell;
            }
            string str_status = js.GetProperty("status").GetString();
            switch(str_status)
            {
                case "UNFILLED":
                    this.status = orderStatus.Open;
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("ordered_at").GetInt64()).UtcDateTime;
                    break;
                case "PARTIALLY_FILLED":
                    this.status = orderStatus.Open;
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("executed_at").GetInt64()).UtcDateTime;
                    break;
                case "FULLY_FILLED":
                    this.status = orderStatus.Filled;
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("executed_at").GetInt64()).UtcDateTime;
                    break;
                case "CANCELED_PARTIALLY_FILLED":
                    this.status = orderStatus.Filled;
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("canceled_at").GetInt64()).UtcDateTime;
                    break;
                case "CANCELED_UNFILLED":
                    this.status = orderStatus.Canceled;
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("canceled_at").GetInt64()).UtcDateTime;
                    break;
                default:
                    this.status = orderStatus.INVALID;
                    break;
            }
            Int64 expire_at = js.GetProperty("order_id").GetInt64();
            if(expire_at == 0)
            {
                this.time_in_force = timeInForce.GoodTillCanceled;
            }
            else
            {
                this.time_in_force = timeInForce.NONE;
            }
            this.order_quantity = decimal.Parse(js.GetProperty("start_amount").GetString());
            this.filled_quantity = decimal.Parse(js.GetProperty("executed_amount").GetString());
            this.average_price = decimal.Parse(js.GetProperty("average_price").GetString());
            this.client_order_id = "";
            this.fee_asset = "";
            this.fee = 0;
            this.create_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("ordered_at").GetInt64()).UtcDateTime;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
        }

        public void setSharedSpotOrder(SharedSpotOrder update, string market,DateTime? timestamp)
        {
            this.timestamp = DateTime.UtcNow;
            this.market = market;
            this.order_id = update.OrderId;
            this.symbol = update.Symbol;
            this.symbol_market = update.Symbol + "@" + market;
            this.order_type = (orderType)Enum.Parse(typeof(orderType), update.OrderType.ToString());
            this.side = (orderSide)Enum.Parse(typeof(orderSide), update.Side.ToString());
            this.status = (orderStatus)Enum.Parse(typeof(orderStatus), update.Status.ToString());
            this.time_in_force = (timeInForce)Enum.Parse(typeof(timeInForce), update.TimeInForce.ToString());
            
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
            this.symbol_market = "";
            this.market = "";
            this.order_id = "";
            this.symbol = "";
            this.order_type = orderType.NONE;
            this.side = orderSide.NONE;
            this.status = orderStatus.NONE;
            this.time_in_force = timeInForce.NONE;
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
        public string ToString()
        {
            string line;
            if(this.timestamp != null)
            {
                line = ((DateTime)this.timestamp).ToString("yyyy-MM-dd HH:mm:ss.fff");
            }
            else
            {
                line = "";
            }
            line += "," + this.order_id + "," + this.market + "," + this.symbol + "," + this.order_type.ToString() + "," + this.side.ToString() + "," + this.status.ToString() + "," + this.time_in_force.ToString() + "," + this.order_price.ToString() + "," + this.order_quantity.ToString() + "," + this.filled_quantity.ToString() + "," + this.average_price.ToString() + "," + this.client_order_id + "," + this.fee_asset + "," + this.fee.ToString();
            if (this.create_time != null)
            {
                line += "," + ((DateTime)this.create_time).ToString("yyyy-MM-dd HH:mm:ss.fff");
            }
            else
            {
                line += ",";
            }
            if (this.update_time != null)
            {
                line += "," + ((DateTime)this.update_time).ToString("yyyy-MM-dd HH:mm:ss.fff");
            }
            else
            {
                line += ",";
            }
            line += "," + this.last_trade + "," + this.trigger_price.ToString() + "," + this.is_trigger_order.ToString();

            return line;
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
            this.timestamp = DateTime.UtcNow;
            this.filled_time = trd.Timestamp;
            this.side = trd.Side;
            this.price = trd.Price;
            this.quantity = trd.Quantity;
        }
        public void setBitbankTrade(JsonElement js, string? market, string? symbol)
        {   
            this.market = market;
            this.symbol = symbol;
            this.timestamp = DateTime.UtcNow;
            this.filled_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("executed_at").GetInt64()).UtcDateTime;
            string str_side = js.GetProperty("side").GetString();
            if (str_side == "buy")
            {
                this.side = SharedOrderSide.Buy;
            }
            else if(str_side == "sell")
            {
                this.side = SharedOrderSide.Sell;
            }
            this.price = decimal.Parse(js.GetProperty("price").GetString());
            this.quantity = decimal.Parse(js.GetProperty("amount").GetString());
        }
        public void setCoincheckTrade(JsonElement js)
        {
            int i = 0;
            this.timestamp = DateTime.UtcNow;
            this.market = "coincheck";
            foreach (var item in js.EnumerateArray())
            {
                string str_item = item.GetString();
                switch(i)
                {
                    case 0://Time
                        this.filled_time = DateTimeOffset.FromUnixTimeSeconds(Int64.Parse(str_item)).UtcDateTime;
                        break;
                    case 1://ID
                        break;
                    case 2://symbol
                        this.symbol = str_item;
                        break;
                    case 3://price
                        this.price = decimal.Parse(str_item);
                        break;
                    case 4://quantity
                        this.quantity = decimal.Parse(str_item);
                        break;
                    case 5://side
                        if(str_item == "buy")
                        {
                            this.side = SharedOrderSide.Buy;
                        }
                        else if(str_item == "sell")
                        {
                            this.side = SharedOrderSide.Sell;
                        }
                        break;
                    case 6://Taker ID
                        break;
                    case 7://Maker ID
                        break;
                    case 8://Itayose ID
                        break;
                       
                }
                ++i;
            }
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

        public string ToString()
        {
            return this.market + "," + this.symbol + "," + ((DateTime)this.timestamp).ToString("yyyy-MM-dd HH:mm:ss.fff") + "," + ((DateTime)this.filled_time).ToString("yyyy-MM-dd HH:mm:ss.fff") + "," + this.side.ToString() + "," + this.price.ToString() + "," + this.quantity.ToString();
        }
    }

    public enum orderType
    {
        NONE = -1,
        Limit = 1,
        LimitMaker = 2,
        Market = 3,
        Other = 4
    }
    public enum orderSide
    {
        NONE = -1,
        Buy = 1,
        Sell = 2
    }
    public enum orderStatus
    {
        NONE = -1,
        Open = 1,
        Filled = 2,
        PartialFill = 3,
        Canceled = 4,
        WaitOpen = 5,
        WaitMod = 6,
        WaitCancel = 7,
        INVALID = 99
    }
    public enum timeInForce
    {
        NONE = -1,
        GoodTillCanceled = 1,
        ImmediateOrCancel = 2,
        FillOrKill = 3
    }
}
