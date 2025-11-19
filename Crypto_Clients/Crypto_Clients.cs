using CryptoClients.Net;
using CryptoClients.Net.Enums;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Requests;
using CryptoExchange.Net.SharedApis;
using Discord;
using Discord.Audio.Streams;
using Enums;
using HTX.Net.Enums;
using PubnubApi;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.Design;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using XT.Net.Objects.Models;



namespace Crypto_Clients
{
    public class Crypto_Clients
    {
        ExchangeSocketClient _client;
        ExchangeRestClient _rest_client;
        Bybit.Net.Clients.BybitSocketClient BybitSocketClient;
        Coinbase.Net.Clients.CoinbaseSocketClient CoinbaseSocketClient;
        public bitbank_connection bitbank_client;
        public coincheck_connection coincheck_client;
        public bittrade_connection bittrade_client;

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

        public Thread bitbankOrderUpdateTh;

        public Action<string,Enums.logType> _addLog;

        private bool megLogging;
        private Crypto_Clients()
        {
            this._client = new ExchangeSocketClient();
            this.BybitSocketClient = new Bybit.Net.Clients.BybitSocketClient();
            this.CoinbaseSocketClient = new Coinbase.Net.Clients.CoinbaseSocketClient();
            this._rest_client = new ExchangeRestClient();

            this.bitbank_client = bitbank_connection.GetInstance();
            this.coincheck_client = coincheck_connection.GetInstance();
            this.bittrade_client = bittrade_connection.GetInstance();

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

            this.bitbank_client.setQueues(this);

            this.megLogging = false;

            //this._addLog = Console.WriteLine;

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

        public Func<Action, Action, CancellationToken, int, Task<bool>>? setMsgLogging(string market, string outputPath)
        {
            this.megLogging = true;
            Func<Action, Action, CancellationToken, int, Task<bool>>? ret = null;
            switch (market)
            {
                case "bitbank":
                    this.bitbank_client.setLogFile(outputPath);
                    ret = this.bitbank_client.msgLogging;
                    break;
                case "coincheck":
                    this.coincheck_client.setLogFile(outputPath);
                    ret = this.coincheck_client.msgLogging;
                    break;
                case "bittrade":
                    this.addLog("Message logging is not defined for " + market, logType.WARNING);
                    //this.bittrade_client.setLogFile(outputPath);
                    break;
                default:
                    this.addLog("Message logging is not defined for " + market, logType.WARNING);
                    break;
            }
            return ret;
        }

        public void setAddLog(Action<string, Enums.logType> act)
        {
            this._addLog = act;
            this.bitbank_client._addLog = act;
            this.coincheck_client._addLog = act;
            this.bittrade_client._addLog = act;
        }

        //public async Task connectAsync(IEnumerable<string> markets)
        //{
        //    foreach (string market in markets)
        //    {
        //        switch(market)
        //        {
        //            case "bitbank":
        //                await this.bitbank_client.connectPublicAsync();
        //                this.bitbankPublicChannelTh = new Thread(() =>
        //                {
        //                    this.bitbank_client.startListen(this.onBitbankMessage);
        //                });
        //                this.bitbankPublicChannelTh.Start();
        //                break;
        //            case "coincheck":
        //                await this.coincheck_client.connectPublicAsync();
        //                this.coincheckPublicChannelsTh = new Thread(() =>
        //                {
        //                    this.coincheck_client.startListen(this.onCoincheckMessage);
        //                });
        //                this.coincheckPublicChannelsTh.Start();
        //                //await this.coincheck_client.connectPrivateAsync();
        //                //this.coincheckPrivateChannelsTh = new Thread(() =>
        //                //{
        //                //    this.coincheck_client.startListenPrivate(this.onConcheckPrivateMessage);
        //                //});
        //                //this.coincheckPrivateChannelsTh.Start();
        //                break;
        //            case "bittrade":
        //                await this.bittrade_client.connectPublicAsync();
        //                this.bittradePublicChannelTh = new Thread(() =>
        //                {
        //                    this.bittrade_client.startListen(this.onBitTradeMessage);
        //                });
        //                this.bittradePublicChannelTh.Start();

        //                //await this.bittrade_client.connectPrivateAsync();
        //                //this.bittradePrivateChannelTh = new Thread(() =>
        //                //{
        //                //    this.bittrade_client.startListenPrivate(this.onBitTradePrivateMessage);
        //                //});
        //                //this.bittradePrivateChannelTh.Start();
        //                break;
        //        }
        //    }
        //}

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

        public void setCredentials(string market,string name, string key)
        {
            switch (market)
            {
                case string value when value == Exchange.Bybit:
                    this.creds.Bybit = new CryptoExchange.Net.Authentication.ApiCredentials(name, key);
                    break;
                case string value when value == Exchange.Coinbase:
                    this.creds.Coinbase = new CryptoExchange.Net.Authentication.ApiCredentials(name, key);
                    break;
                case "bitbank":
                    this.bitbank_client.SetApiCredentials(name, key);
                    break;
                case "coincheck":
                    this.coincheck_client.SetApiCredentials(name, key);
                    break;
                case "bittrade":
                    this.bittrade_client.SetApiCredentials(name, key);
                    break;
            }
            this._rest_client.SetApiCredentials(this.creds);
            this._client.SetApiCredentials(this.creds);
        }
        public void readCredentials(string jsonfilename)
        {
            string fileContent = File.ReadAllText(jsonfilename);

            using JsonDocument doc = JsonDocument.Parse(fileContent);
            var root = doc.RootElement;

            string market = root.GetProperty("market").GetString();
            string name = root.GetProperty("name").GetString();
            string key = root.GetProperty("privateKey").GetString();

            this.setCredentials(market, name, key);

            //switch (market)
            //{
            //    case string value when value == Exchange.Bybit:
            //        this.creds.Bybit = new CryptoExchange.Net.Authentication.ApiCredentials(root.GetProperty("name").ToString(), root.GetProperty("privateKey").ToString());
            //        break;
            //    case string value when value == Exchange.Coinbase:
            //        this.creds.Coinbase = new CryptoExchange.Net.Authentication.ApiCredentials(root.GetProperty("name").ToString(), root.GetProperty("privateKey").ToString());
            //        break;
            //    case "bitbank":
            //        this.bitbank_client.SetApiCredentials(root.GetProperty("name").ToString(), root.GetProperty("privateKey").ToString());
            //        break;
            //    case "coincheck":
            //        this.coincheck_client.SetApiCredentials(root.GetProperty("name").ToString(), root.GetProperty("privateKey").ToString());
            //        break;
            //    case "bittrade":
            //        this.bittrade_client.SetApiCredentials(root.GetProperty("name").ToString(), root.GetProperty("privateKey").ToString());
            //        break;
            //}
            //this._rest_client.SetApiCredentials(this.creds);
            //this._client.SetApiCredentials(this.creds);
        }

        //REST API
        async public Task<DataBalance[]> getBalance(IEnumerable<string>? markets)
        {
            GetBalancesRequest req = new GetBalancesRequest(TradingMode.Spot);
            List<DataBalance> temp = new List<DataBalance>();
            JsonDocument js;
            foreach (var m in markets)
            {
                switch(m)
                {
                    case "bitbank":
                        js = await this.bitbank_client.getBalance();
                        if(js.RootElement.GetProperty("success").GetUInt16() == 1)
                        {
                            var assets = js.RootElement.GetProperty("data").GetProperty("assets").EnumerateArray();
                            foreach(var asset in assets)
                            {
                                DataBalance balance = new DataBalance();
                                balance.market = m;
                                balance.asset = asset.GetProperty("asset").GetString();
                                balance.available = Decimal.Parse(asset.GetProperty("free_amount").GetString());
                                balance.total = Decimal.Parse(asset.GetProperty("onhand_amount").GetString());
                                temp.Add(balance);
                            }
                        }
                        else
                        {
                            this.addLog("Failed to get the balance information. Exchange:" + m,Enums.logType.WARNING);
                            this.addLog(JsonSerializer.Serialize(js), Enums.logType.WARNING);
                        }
                        break;
                    case "coincheck":
                        js = await this.coincheck_client.getBalance();
                        if (js.RootElement.GetProperty("success").GetBoolean())
                        {
                            Dictionary<string,DataBalance> cc_balance = new Dictionary<string, DataBalance>();
                            JsonElement js_elem = js.RootElement;
                            foreach(var elem in js_elem.EnumerateObject())
                            {
                                if(elem.Name != "success")
                                {
                                    if(elem.Name.Contains("_"))
                                    {
                                        if (elem.Name.Split("_")[1] == "reserved")
                                        {
                                            string asset_name = elem.Name.Split("_")[0];
                                            if (cc_balance.ContainsKey(asset_name))
                                            {
                                                cc_balance[asset_name].available = cc_balance[asset_name].total - decimal.Parse(elem.Value.GetString());
                                            }
                                            else
                                            {
                                                DataBalance balance = new DataBalance();
                                                balance.market = m;
                                                balance.asset = elem.Name;
                                                balance.available = - decimal.Parse(elem.Value.GetString());
                                                cc_balance[balance.asset] = balance;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if(cc_balance.ContainsKey(elem.Name))
                                        {
                                            cc_balance[elem.Name].total = decimal.Parse(elem.Value.GetString());
                                            cc_balance[elem.Name].available += cc_balance[elem.Name].total;
                                        }
                                        else
                                        {
                                            DataBalance balance = new DataBalance();
                                            balance.market = m;
                                            balance.asset = elem.Name;
                                            balance.total = decimal.Parse(elem.Value.GetString());
                                            cc_balance[balance.asset] = balance;
                                        }
                                    }
                                }
                               
                            }
                            temp.AddRange(cc_balance.Values);
                        }
                        else
                        {
                            this.addLog("Failed to get the balance information. Exchange:" + m, Enums.logType.WARNING);
                            this.addLog(JsonSerializer.Serialize(js), Enums.logType.WARNING);
                        }
                        break;
                    case "bittrade":
                        js = await this.bittrade_client.getBalance();
                        if(js.RootElement.GetProperty("status").GetString()=="ok")
                        {
                            Dictionary<string, DataBalance> cc_balance = new Dictionary<string, DataBalance>();
                            var data = js.RootElement.GetProperty("data").GetProperty("list");
                            foreach(var item in data.EnumerateArray())
                            {
                                string _type = item.GetProperty("type").GetString();
                                string ccy = item.GetProperty("currency").GetString();
                                decimal amount = decimal.Parse(item.GetProperty("balance").GetString());
                                if (_type == "trade")
                                {
                                    if (cc_balance.ContainsKey(ccy))
                                    {
                                        cc_balance[ccy].total += amount;
                                        cc_balance[ccy].available = amount;
                                    }
                                    else
                                    {
                                        DataBalance balance = new DataBalance();
                                        balance.market = m;
                                        balance.asset = ccy;
                                        balance.total = amount;
                                        balance.available = amount;
                                        cc_balance[balance.asset] = balance;
                                    }
                                }
                                else if(_type == "frozen")
                                {
                                    if (cc_balance.ContainsKey(ccy))
                                    {
                                        cc_balance[ccy].total += amount;
                                    }
                                    else
                                    {
                                        DataBalance balance = new DataBalance();
                                        balance.market = m;
                                        balance.asset = ccy;
                                        balance.total = amount;
                                        balance.available = 0;
                                        cc_balance[balance.asset] = balance;
                                    }
                                }
                            }
                            temp.AddRange(cc_balance.Values);
                        }
                        else
                        {
                            this.addLog("Failed to get the balance information. Exchange:" + m, Enums.logType.WARNING);
                            this.addLog(JsonSerializer.Serialize(js), Enums.logType.WARNING);
                        }
                        break;
                    default:
                        var result = await this._rest_client.GetBalancesAsync(m, req);
                        if(result.Success)
                        {
                            foreach(var d in result.Data)
                            {
                                DataBalance balance = new DataBalance();
                                balance.market = m;
                                balance.asset = d.Asset;
                                balance.total = d.Total;
                                balance.available = d.Available;
                                temp.Add(balance);
                            }
                        }
                        else
                        {
                            this.addLog("Failed to get the balance information. Exchange:" + m, Enums.logType.WARNING);
                        }
                        break;
                }
            }
            return temp.ToArray();
        }

        public async Task<List<DataSpotOrderUpdate>> getActiveOrders(string market)
        {
            DataSpotOrderUpdate ord;
            List<DataSpotOrderUpdate> l = new List<DataSpotOrderUpdate>();
            JsonDocument js;
            switch (market)
            {
                case "bitbank":
                    js = await this.bitbank_client.getActiveOrders();
                    //this.addLog(JsonSerializer.Serialize(js));
                    if(js.RootElement.GetProperty("success").GetInt16() == 1)
                    {
                        var data = js.RootElement.GetProperty("data").GetProperty("orders");
                        foreach(var item in data.EnumerateArray())
                        {
                            while (!this.ordUpdateStack.TryPop(out ord))
                            {

                            }
                            ord.setBitbankSpotOrder(item);
                            l.Add(ord);
                            this.ordUpdateQueue.Enqueue(ord);
                        }
                    }
                    else
                    {
                        l = null;
                    }
                    break;
                case "coincheck":
                    js = await this.coincheck_client.getActiveOrders();
                    //this.addLog(JsonSerializer.Serialize(js));
                    if (js.RootElement.GetProperty("success").GetBoolean())
                    {
                        var data = js.RootElement.GetProperty("orders");
                        foreach (var item in data.EnumerateArray())
                        {
                            while (!this.ordUpdateStack.TryPop(out ord))
                            {

                            }
                            ord.symbol = item.GetProperty("pair").GetString();
                            ord.market = "coincheck";
                            string ord_type = item.GetProperty("order_type").GetString();
                            switch(ord_type)
                            {
                                case "buy":
                                    ord.side = orderSide.Buy;
                                    break;
                                case "sell":
                                    ord.side = orderSide.Sell;
                                    break;
                                default:
                                    ord.side = orderSide.NONE;
                                    break;
                            }
                            ord.order_type = orderType.Limit;
                            ord.status = orderStatus.Open;
                            ord.symbol_market = ord.symbol + "@" + ord.market;
                            ord.order_id = item.GetProperty("id").GetInt64().ToString();
                            ord.order_price = decimal.Parse(item.GetProperty("rate").GetString());
                            ord.order_quantity = decimal.Parse(item.GetProperty("pending_amount").GetString());
                            ord.create_time = DateTime.Parse(item.GetProperty("created_at").GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind); 
                            l.Add(ord);
                            this.ordUpdateQueue.Enqueue(ord);
                        }
                    }
                    else
                    {
                        l = null;
                    }
                    break;
                case "bittrade":
                    js = await this.bittrade_client.getActiveOrders();
                    //this.addLog(JsonSerializer.Serialize(js));
                    if (js.RootElement.GetProperty("status").GetString() == "ok")
                    {
                        var data = js.RootElement.GetProperty("data");
                        foreach (var item in data.EnumerateArray())
                        {
                            while (!this.ordUpdateStack.TryPop(out ord))
                            {

                            }
                            ord.symbol = item.GetProperty("symbol").GetString();
                            ord.create_time = DateTimeOffset.FromUnixTimeMilliseconds(item.GetProperty("created-at").GetInt64()).UtcDateTime;
                            ord.order_quantity = decimal.Parse(item.GetProperty("amount").GetString());
                            ord.filled_quantity = decimal.Parse(item.GetProperty("filled-amount").GetString());
                            ord.fee = decimal.Parse(item.GetProperty("filled-fees").GetString());
                            ord.order_id = item.GetProperty("id").GetInt64().ToString();
                            string str_status = item.GetProperty("state").GetString();
                            switch (str_status)
                            {
                                case "created":
                                    ord.status = orderStatus.WaitOpen;
                                    break;
                                case "submitted":
                                    ord.status = orderStatus.Open;
                                    break;
                                case "partial-filled":
                                case "partial-canceled":
                                    ord.status = orderStatus.Open;
                                    break;
                                case "filled":
                                    ord.status = orderStatus.Filled;
                                    break;
                                case "canceling":
                                    ord.status = orderStatus.WaitCancel;
                                    break;
                                case "canceled":
                                    ord.status = orderStatus.Canceled;
                                    break;
                                default:
                                    ord.status = orderStatus.INVALID;
                                    break;
                            }
                            string _type = item.GetProperty("type").GetString();
                            switch (_type)
                            {
                                case "sell-limit":
                                case "sell-limit_maker":
                                    ord.order_type = orderType.Limit;
                                    ord.side = orderSide.Sell;
                                    ord.order_price = decimal.Parse(item.GetProperty("price").GetString());
                                    break;
                                case "buy-limit":
                                case "buy-limit_maker":
                                    ord.order_type = orderType.Limit;
                                    ord.side = orderSide.Buy;
                                    ord.order_price = decimal.Parse(item.GetProperty("price").GetString());
                                    break;
                                case "sell-market":
                                    ord.order_type = orderType.Market;
                                    ord.side = orderSide.Sell;
                                    ord.order_price = 0;
                                    break;
                                case "buy-market":
                                    ord.order_type = orderType.Market;
                                    ord.side = orderSide.Buy;
                                    ord.order_price = 0;
                                    break;
                                case "sell-ioc":
                                case "buy-ioc":
                                default:
                                    ord.order_type = orderType.Other;
                                    break;
                            }
                            l.Add(ord);
                            this.ordUpdateQueue.Enqueue(ord);
                        }
                    }
                    else
                    {
                        l = null;
                    }
                    break;
            }
            return l;
        }

        public async Task<List<DataTrade>> getVolumeHistory(string market,string symbol, DateTime? startTime = null, DateTime? endTime = null)
        {
            List<DataTrade> output = new List<DataTrade>();
            JsonDocument js;
            List<JsonElement> js_list;
            DataTrade trade;
            string test;
            switch (market)
            {
                case "bitbank":
                    if(startTime == null && endTime == null)
                    {
                        js = await this.bitbank_client.getVolumeHistory(symbol);
                        if (js.RootElement.GetProperty("success").GetInt32() == 1)
                        {
                            var data = js.RootElement.GetProperty("data").GetProperty("transactions");
                            foreach (var item in data.EnumerateArray())
                            {
                                while (!this.tradeStack.TryPop(out trade))
                                {
                                }
                                trade.setBitbankTrade(item, market, symbol);
                                trade.timestamp = trade.filled_time;
                                output.Add(trade);
                            }
                        }
                        else
                        {
                            addLog("Failed to get trade history from bitbank.", logType.WARNING);
                        }
                    }
                    else
                    {
                        if (endTime == null)
                        {
                            endTime = DateTime.UtcNow;
                        }
                        DateTime d = ((DateTime)startTime).Date;
                        while (d < (DateTime)endTime)
                        {
                            string YYYYMMDD = d.ToString("yyyyMMdd");
                            js = await this.bitbank_client.getVolumeHistory(symbol, YYYYMMDD);
                            if (js.RootElement.GetProperty("success").GetInt32() == 1)
                            {
                                var data = js.RootElement.GetProperty("data").GetProperty("transactions");
                                foreach (var item in data.EnumerateArray())
                                {
                                    while (!this.tradeStack.TryPop(out trade))
                                    {
                                    }
                                    trade.setBitbankTrade(item, market, symbol);
                                    if (trade.filled_time >= (DateTime)startTime && trade.filled_time <= (DateTime)endTime)
                                    {
                                        trade.timestamp = trade.filled_time;
                                        output.Add(trade);
                                    }
                                    else
                                    {
                                        trade.init();
                                        this.tradeStack.Push(trade);
                                    }
                                }
                            }
                            else
                            {
                                addLog("Failed to get trade history from bitbank.", logType.WARNING);
                            }
                            d += TimeSpan.FromDays(1);
                        }
                    }
                    
                    
                    break;
                case "coincheck":
                    js_list = await this.coincheck_client.getVolumeHistory(symbol,startTime, endTime);
                    foreach (var js_elem in js_list)
                    {
                        while (!this.tradeStack.TryPop(out trade))
                        {
                        }
                        trade.market = "coincheck";
                        trade.symbol = js_elem.GetProperty("pair").GetString();
                        trade.quantity = decimal.Parse(js_elem.GetProperty("amount").GetString());
                        trade.price = decimal.Parse(js_elem.GetProperty("rate").GetString());
                        string ord_side = js_elem.GetProperty("order_type").GetString();
                        if(ord_side == "buy")
                        {
                            trade.side = SharedOrderSide.Buy;
                        }
                        else if (ord_side == "sell")
                        {
                            trade.side = SharedOrderSide.Sell;
                        }
                        trade.filled_time = DateTime.Parse(js_elem.GetProperty("created_at").GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
                        trade.timestamp = trade.filled_time;
                        output.Add(trade);
                    }
                    if (js_list.Count == 0)
                    {
                        addLog("Failed to get trade history from coincheck.", logType.WARNING);
                    }
                    break;
            }
            return output;
        }

        public async Task<List<DataFill>> getTradeHistory(string market,DateTime? startTime = null,DateTime? endTime = null)
        {
            List<DataFill> output = new List<DataFill>();
            JsonDocument js;
            List<JsonElement> js_list;
            DataFill fill;
            string test;
            switch(market)
            {
                case "bitbank":
                    js = await this.bitbank_client.getTradeHistory("", startTime, endTime);
                    if(js.RootElement.GetProperty("success").GetInt32() == 1)
                    {
                        var data = js.RootElement.GetProperty("data").GetProperty("trades");
                        foreach(var item in data.EnumerateArray())
                        {
                            while (!this.fillStack.TryPop(out fill))
                            {
                            }
                            fill.setBitBankFill(item);
                            fill.timestamp = fill.filled_time;
                            output.Add(fill);
                        }
                    }
                    else
                    {
                        addLog("Failed to get trade history from bitbank.", logType.WARNING);
                    }
                    break;
                case "coincheck":
                    js_list = await this.coincheck_client.getTradeHistoryPagenation(startTime, endTime);
                    foreach(var js_elem in js_list)
                    {
                        while (!this.fillStack.TryPop(out fill))
                        {
                        }
                        fill.setCoincheckFill(js_elem);
                        fill.timestamp = fill.filled_time;
                        output.Add(fill);
                    }
                    if(js_list.Count == 0)
                    {
                        addLog("Failed to get trade history from coincheck.", logType.WARNING);
                    }
                    break;
            }
            return output;
        }

        public async Task<decimal> getCurrentMid(string market, string symbol)
        {
            decimal mid = -1;
            JsonDocument js;

            switch(market)
            {
                case "bitbank":
                    js = await this.bitbank_client.getTicker(symbol);
                    if(js.RootElement.GetProperty("success").GetInt32() == 1)
                    {
                        var data = js.RootElement.GetProperty("data");
                        mid = (decimal.Parse(data.GetProperty("sell").GetString()) + decimal.Parse(data.GetProperty("buy").GetString())) / 2;
                    }
                    else
                    {
                        addLog("Failed to get the ticker from bitbank.", logType.WARNING);
                    }
                    break;
                case "coincheck":
                    js = await this.coincheck_client.getTicker(symbol);
                    JsonElement js_bid;
                    JsonElement js_ask;
                    decimal bid = 0;
                    decimal ask = 0;    
                    if (js.RootElement.TryGetProperty("bid",out js_bid))
                    {
                        bid = js_bid.GetDecimal();
                    }
                    if (js.RootElement.TryGetProperty("ask", out js_ask))
                    {
                        ask = js_ask.GetDecimal();
                    }
                    if(ask > 0 && bid > 0)
                    {
                        mid = (ask + bid) / 2;
                    }
                    else if(ask > 0)
                    {
                        mid = ask;
                    }
                    else if(bid > 0)
                    {
                        mid = bid;
                    }
                        //if(js.RootElement.GetProperty("success").GetBoolean())
                        //{
                        //    mid = (decimal.Parse(js.RootElement.GetProperty("ask").GetString()) + decimal.Parse(js.RootElement.GetProperty("bid").GetString())) / 2;
                        //}
                        //else
                        //{
                        //    addLog("Failed to get the ticker from coincheck.", logType.WARNING);
                        //}
                    break;
            }
            return mid;
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
                //ord.timestamp = DateTime.UtcNow;
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
                this.addLog("New Order Failed.", Enums.logType.ERROR);
                this.addLog(result.Error.ToString(), Enums.logType.ERROR);
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
                        //await this.bitbank_client.connectPrivateAsync();
                        //this.addLog("For bitbank spot order updates, please subscribe separately and register it on the thread manager.", Enums.logType.WARNING);
                        //this.bitbankOrderUpdateTh = new Thread(this.bitbankOrderUpdates);
                        //this.bitbankOrderUpdateTh.Start();
                        break;
                    case "coincheck":
                        await this.coincheck_client.subscribeOrderEvent();
                        await this.coincheck_client.subscribeExecutionEvent();
                        break;
                    case "bittrade":
                        await this.bittrade_client.subscribeOrderEvent();
                        await this.bittrade_client.subscribeExecutionEvent();
                        break;
                    default:
                        var subResult = await this._client.SubscribeToSpotOrderUpdatesAsync(m, request, LogOrderUpdates);
                        this.addLog($"{subResult.Exchange} subscribe spot order updates result: {subResult.Success} {subResult.Error}");
                        break;
                }
            }
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

        //public async Task<bool> onBitbankOrderUpdates()
        //{
        //    JsonElement js;
        //    DataSpotOrderUpdate ord;
        //    DataFill fill;
        //    if (this.bitbank_client.orderQueue.TryDequeue(out js))
        //    {
        //        while (!this.ordUpdateStack.TryPop(out ord))
        //        {

        //        }
        //        ord.setBitbankSpotOrder(js);
        //        this.ordUpdateQueue.Enqueue(ord);
        //    }
        //    else if (this.bitbank_client.fillQueue.TryDequeue(out js))
        //    {
        //        while (!this.fillStack.TryPop(out fill))
        //        {

        //        }
        //        fill.setBitBankFill(js);
        //        this.fillQueue.Enqueue(fill);
        //    }
        //    return true;
        //}

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
                    case "bittrade":
                        await this.bittrade_client.subscribeTrades(baseCcy, quoteCcy);
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
                        string coincheck_symbol = baseCcy.ToLower() + "_" + quoteCcy.ToLower();
                        var js = await this.coincheck_client.getOrderBooks(coincheck_symbol);
                        //this.addLog("INFO", JsonSerializer.Serialize(js));
                        DataOrderBook ord;
                        while(!this.ordBookStack.TryPop(out ord))
                        {

                        }
                        ord.setCoincheckOrderBook(js.RootElement, coincheck_symbol);
                        this.ordBookQueue.Enqueue(ord);
                        await this.coincheck_client.subscribeOrderBook(baseCcy, quoteCcy);
                        break;
                    case "bittrade":
                        await this.bittrade_client.subscribeOrderBook(baseCcy, quoteCcy);
                        break;
                    default:
                        var subResult = await this._client.SubscribeToOrderBookUpdatesAsync(m, req, LogOrderBook);
                        this.addLog($"{subResult.Exchange} subscribe trades result: {subResult.Success} {subResult.Error}");
                        break;
                }
            }
        }
        void LogOrderBook(ExchangeEvent<SharedOrderBook> update)
        {
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

            try
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
            catch (Exception e)
            {
                this.addLog(e.Message,Enums.logType.ERROR);
                this.addLog(msg_body, Enums.logType.ERROR);
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
        public void onConcheckPrivateMessage(string msg_body)
        {
            DataSpotOrderUpdate ord;
            DataFill fill;
            JsonElement js = JsonDocument.Parse(msg_body).RootElement;
            string msg_type = js.GetProperty("channel").GetString();
            switch(msg_type)
            {
                case "order-events":
                    while (!this.ordUpdateStack.TryPop(out ord))
                    {

                    }
                    ord.setCoincheckSpotOrder(js);
                    this.ordUpdateQueue.Enqueue(ord);
                    break;
                case "execution-events":
                    while (!this.fillStack.TryPop(out fill))
                    {

                    }
                    fill.setCoincheckFill(js);
                    this.fillQueue.Enqueue(fill);
                    break;
            }
        }
        public void onBitTradeMessage(string msg_body)
        {
            JsonElement js = JsonDocument.Parse(msg_body).RootElement;
            JsonElement subElement;
            Int64 pong_no;
            if (js.TryGetProperty("ch", out subElement))
            {
                string[] channel = subElement.GetString().Split(".");
                //0:market,1:symbol,2:channel,3:additional info
                if (channel.Length > 2)
                {
                    switch (channel[2])
                    {
                        case "depth":
                            var data = js.GetProperty("tick");
                            DataOrderBook ord;
                            while (!this.ordBookStack.TryPop(out ord))
                            {

                            }
                            ord.setBitTradeOrderBook(data, channel[1], js.GetProperty("ts").GetInt64());
                            this.ordBookQueue.Enqueue(ord);
                            break;
                        case "trade":
                            var tick = js.GetProperty("tick");
                            var root = tick.GetProperty("data");
                            foreach (var item in root.EnumerateArray())
                            {
                                DataTrade trd;
                                while (!this.tradeStack.TryPop(out trd))
                                {

                                }
                                trd.setBitTradeTrade(item, channel[1]);
                                this.tradeQueue.Enqueue(trd);
                            }
                            break;
                        case "kline":
                        case "bbo":
                        case "detail":
                        default:
                            break;
                    }
                }

            }
            else if (js.TryGetProperty("ping", out subElement))
            {
                //this.addLog("INFO", msg_body);
                this.bittrade_client.sendPong(subElement.GetInt64());
            }
        }

        public void onBitTradePrivateMessage(string msg_body)
        {
            JsonElement js = JsonDocument.Parse(msg_body).RootElement;
            JsonElement subElement;
            DataSpotOrderUpdate ord;
            DataFill fill;
            if (js.TryGetProperty("action", out subElement))
            {
                string act = subElement.GetString();
                JsonElement ch;
                switch (act)
                {
                    case "ping":
                        var data = js.GetProperty("data");
                        this.bittrade_client.sendPong(data.GetProperty("ts").GetInt64(),true);
                        break;
                    case "push":
                        if(js.TryGetProperty("ch",out ch))
                        {
                            var obj = js.GetProperty("data");

                            if(ch.GetString().StartsWith("orders"))
                            {
                                while (!this.ordUpdateStack.TryPop(out ord))
                                {

                                }
                                ord.setBitTradeOrder(obj);
                                this.ordUpdateQueue.Enqueue(ord);
                            }
                            else if(ch.GetString().StartsWith("trade.clearing"))
                            {
                                while (!this.fillStack.TryPop(out fill))
                                {

                                }
                                fill.setBitTradeFill(obj);
                                this.fillQueue.Enqueue(fill);
                            }
                        }
                        break;
                    case "sub":
                        //Do nothing
                        break;
                    default:
                        this.addLog("Unknown message from bittrade private connection");
                        this.addLog(msg_body, Enums.logType.WARNING);
                        break;

                }
                
            }
            else
            {
                this.addLog("Unknown message from bittrade private connection");
                this.addLog(msg_body, Enums.logType.WARNING);
            }
        }

        public void addLog(string line, Enums.logType logtype = Enums.logType.INFO)
        {
            this._addLog("[Crypto_Client]" + line,logtype);
        }

        private static Crypto_Clients _instance;
        private static readonly object _lockObject = new object();

        public static Crypto_Clients GetInstance()
        {
            lock (_lockObject)
            {
                if (_instance == null)
                {
                    _instance = new Crypto_Clients();
                }
                return _instance;
            }
        }
    }
    public class DataBalance
    {
        public string asset;
        public string market;
        public decimal available;
        public decimal total;
        public DataBalance()
        {
            this.asset = "";
            this.market = "";
            this.available = 0;
            this.total = 0;
        }
        public void init()
        {
            this.asset = "";
            this.market = "";
            this.available = 0;
            this.total = 0;
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
        public void setBitTradeOrderBook(JsonElement js, string symbol, Int64 unixTime)
        {
            this.timestamp = DateTime.UtcNow;
            this.market = "bittrade";
            this.symbol = symbol;
            this.updateType = SocketUpdateType.Snapshot;
            this.orderbookTime = DateTimeOffset.FromUnixTimeMilliseconds(unixTime).UtcDateTime;
            var data = js.GetProperty("asks").EnumerateArray();
            foreach (var item in data)
            {
                this.asks[item[0].GetDecimal()] = item[1].GetDecimal();
            }
            data = js.GetProperty("bids").EnumerateArray();
            foreach (var item in data)
            {
                this.bids[item[0].GetDecimal()] = item[1].GetDecimal();
            }
        }
        public void setCoincheckOrderBook(JsonElement js,string symbol)
        {
            this.timestamp = DateTime.UtcNow;
            this.market = "coincheck";
            this.symbol = symbol;
            JsonElement current_time;
            if(js.TryGetProperty("last_update_at",out current_time))
            {
                this.updateType = SocketUpdateType.Update;
                this.orderbookTime = DateTimeOffset.FromUnixTimeSeconds(Int64.Parse(js.GetProperty("last_update_at").GetString())).UtcDateTime;
            }
            else
            {
                this.updateType = SocketUpdateType.Snapshot;
            }
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
        public decimal fee_unknown;
        public string maker_taker;
        public string order_id;
        public string internal_order_id;
        public string symbol;
        public decimal price;
        public orderSide side;
        public string trade_id;
        public orderType order_type;
        public decimal profit_loss;
        public decimal interest;

        public string msg;
        public int queued_count;

        public DataFill()
        {
            this.timestamp = null;
            this.symbol_market = "";
            this.market = "";
            this.quantity = 0;
            this.filled_time = null;
            this.fee_base = 0;
            this.fee_quote = 0;
            this.fee_unknown = 0;
            this.maker_taker = "";
            this.order_id = "";
            this.internal_order_id = "";
            this.symbol = "";
            this.price = 0;
            this.side = orderSide.NONE;
            this.trade_id = "";
            this.order_type = orderType.NONE;
            this.profit_loss = 0;
            this.interest = 0;
            this.msg = "";
            this.queued_count = 0;
        }

        public void setCoincheckFill(JsonElement js)
        {
            this.timestamp = DateTime.UtcNow;
            this.order_id = js.GetProperty("order_id").GetUInt64().ToString();
            this.trade_id = js.GetProperty("id").GetUInt64().ToString();
            JsonElement time;
            if(js.TryGetProperty("event_time",out time))
            {
                this.filled_time = DateTime.Parse(time.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
            }
            else if(js.TryGetProperty("created_at", out time))
            {
                this.filled_time = DateTime.Parse(time.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
            }

            this.symbol = js.GetProperty("pair").GetString();
            this.market = "coincheck";
            this.symbol_market = this.symbol + "@" + this.market;
            this.internal_order_id = this.market + this.order_id;
            string maker_taker = js.GetProperty("liquidity").GetString();
            if (maker_taker == "M")
            {
                this.maker_taker = "maker";
            }
            else if(maker_taker == "T")
            {
                this.maker_taker = "taker";
            }
            this.price = decimal.Parse(js.GetProperty("rate").GetString());
            string[] assets = this.symbol.Split("_");
            string fee_ccy = js.GetProperty("fee_currency").GetString();
            if(fee_ccy == assets[0])
            {
                this.fee_base = decimal.Parse(js.GetProperty("fee").GetString());
                this.fee_quote = 0;
            }
            else if(fee_ccy == assets[1])
            {
                this.fee_base = 0;
                this.fee_quote = decimal.Parse(js.GetProperty("fee").GetString());
            }
            else
            {
                this.fee_base = 0;
                this.fee_unknown = decimal.Parse(js.GetProperty("fee").GetString());
            }
            string side = js.GetProperty("side").GetString();
            decimal temp_qt = decimal.Parse(js.GetProperty("funds").GetProperty(assets[0]).GetString());
            if (side == "buy")
            {
                this.side = orderSide.Buy;
            }
            else if(side == "sell")
            {
                this.side= orderSide.Sell;
                temp_qt *= -1;
            }
            else
            {
                this.side = orderSide.NONE;
            }

            this.quantity = temp_qt;
            this.quantity += this.fee_base;
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
            this.symbol_market = this.symbol + "@" + this.market;
            this.internal_order_id = this.market + this.order_id;
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
        public void setBitTradeFill(JsonElement js)//trade.clearing object. retrive only fees
        {
            this.timestamp = DateTime.UtcNow;
            this.quantity = 0;//Executions are reflected with order objects.
            this.price = -1;
            this.filled_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("tradeTime").GetInt64()).UtcDateTime;
            this.symbol = js.GetProperty("symbol").GetString();
            this.market = "bittrade";
            this.symbol_market = this.symbol + "@" + this.market;
            this.internal_order_id = this.market + this.order_id;
            this.order_id = js.GetProperty("orderId").GetInt64().ToString();
            bool aggressor = js.GetProperty("aggressor").GetBoolean();
            if(aggressor)
            {
                this.maker_taker = "taker";
            }
            else
            {
                this.maker_taker = "maker";
            }
            string side = js.GetProperty("orderSide").GetString();
            if (side == "buy")
            {
                this.side = orderSide.Buy;
            }
            else if (side == "sell")
            {
                this.side = orderSide.Sell;
            }
            else
            {
                this.side = orderSide.NONE;
            }
            this.trade_id = js.GetProperty("tradeId").GetInt64().ToString();
            string feeccy = js.GetProperty("feeCurrency").GetString();
            decimal fee = decimal.Parse(js.GetProperty("transactFee").GetString()) - decimal.Parse(js.GetProperty("feeDeduct").GetString());
            if (this.symbol.Substring(this.symbol.Length - feeccy.Length) == feeccy)
            {
                this.fee_quote= fee;
            }
            else if (this.symbol.Substring(0, feeccy.Length) == feeccy)
            {
                this.fee_base = fee;
            }
            else
            {
                this.fee_unknown = fee;
            }
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
            line += "," + this.trade_id + "," + this.order_id + "," + this.market + "," + this.symbol + "," + this.order_type.ToString() + "," + this.side.ToString() + "," + this.price.ToString() + "," + this.quantity.ToString() + "," + this.maker_taker + "," + this.fee_base.ToString() + "," + this.fee_quote.ToString() + "," + this.profit_loss.ToString() + "," + this.interest + "," + this.internal_order_id + ",";
            if (this.filled_time != null)
            {
                line += ((DateTime)this.filled_time).ToString("yyyy-MM-dd HH:mm:ss.fff");
            }
            else
            {
                line += "";
            }
            line += "," + this.msg;
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
            this.fee_unknown = 0;
            this.maker_taker = "";
            this.order_id = "";
            this.symbol = "";
            this.internal_order_id = "";
            this.price = 0;
            this.side = orderSide.NONE;
            this.trade_id = "";
            this.order_type = orderType.NONE;
            this.profit_loss = 0;
            this.interest = 0;
            this.msg = "";
            this.queued_count = 0;
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

        public decimal current_traded_quantity;
        public decimal current_traded_price;

        public string internal_order_id;

        public string? fee_asset;
        public decimal fee;

        public DateTime? create_time;
        public DateTime? update_time;

        public string last_trade;
        public decimal trigger_price;
        public bool is_trigger_order;

        public bool isVirtual;
        public string msg;

        public int err_code;

        public int queued_count;

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
            this.current_traded_quantity = 0;
            this.current_traded_price = -1;
            this.internal_order_id = "";
            this.fee_asset = "";
            this.fee = 0;
            this.create_time = null;
            this.update_time = null;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
            this.isVirtual = false;
            this.msg = "";
            this.err_code = 0;
            this.queued_count = 0;
        }

        public void setCoincheckSpotOrder(JsonElement js)
        {
            this.timestamp = DateTime.UtcNow;
            this.symbol = js.GetProperty("pair").GetString();
            this.symbol_market = this.symbol + "@coincheck";
            this.market = "coincheck";
            this.order_id = js.GetProperty("id").GetInt64().ToString();
            string str_status = js.GetProperty("order_event").GetString();
            switch (str_status)
            {
                case "NEW":
                case "PARTIALLY_FILLED":
                    this.status = orderStatus.Open;
                    break;
                case "FILL":
                    this.status = orderStatus.Filled;
                    break;
                case "EXPIRY":
                case "PARTIALLY_FILLED_EXPIRED":
                    this.status = orderStatus.INVALID;
                    break;
                case "CANCEL":
                case "PARTIALLY_FILLED_CANCELED":
                    this.status = orderStatus.Canceled;
                    break;
                default:
                    this.status = orderStatus.INVALID;
                    break;
            }
            string side = js.GetProperty("order_type").GetString();
            if (side == "buy" || side == "market_buy")
            {
                this.side = orderSide.Buy;
            }
            else if (side == "sell" || side == "market_sell")
            {
                this.side = orderSide.Sell;
            }
            if(side.StartsWith("market_"))
            {
                this.order_type = orderType.Market;
                if(side == "market_buy")
                {
                    this.order_price = 0;
                    this.order_quantity = 0;
                }
            }
            else
            {
                this.order_type = orderType.Limit;
                this.order_price = decimal.Parse(js.GetProperty("rate").GetString());
                this.order_quantity = decimal.Parse(js.GetProperty("amount").GetString());
            }

            if (js.TryGetProperty("latest_executed_amount", out JsonElement filled_element)
    && filled_element.ValueKind != JsonValueKind.Null
    && filled_element.ValueKind != JsonValueKind.Undefined)
            {
                this.filled_quantity = decimal.Parse(js.GetProperty("latest_executed_amount").GetString());
                this.average_price = this.order_price;
            }
            else
            {
                this.filled_quantity = 0;
            }

                this.average_price = -1;
            string str_tif = js.GetProperty("time_in_force").GetString();
            switch (str_tif)
            {
                case "good_til_cancelled":
                    this.time_in_force = timeInForce.GoodTillCanceled;
                    break;
                default :
                    this.time_in_force = timeInForce.NONE;
                    break;
            }
            this.fee_asset = "";
            this.fee = 0;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
            this.update_time = DateTime.Parse(js.GetProperty("event_time").GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
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
                    this.status = orderStatus.Canceled;
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
            Int64 expire_at = js.GetProperty("expire_at").GetInt64();
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
            this.fee_asset = "";
            this.fee = 0;
            this.create_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("ordered_at").GetInt64()).UtcDateTime;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
        }

        public void setBitTradeOrder(JsonElement js)
        {
            string _type;
            string str_status;
            this.timestamp = DateTime.UtcNow;
            this.symbol = js.GetProperty("symbol").GetString();
            this.symbol_market = this.symbol + "@bittrade";
            this.market = "bittrade";
            this.order_id = js.GetProperty("orderId").GetInt64().ToString();
            string eventType = js.GetProperty("eventType").GetString();
            switch (eventType)
            {
                case "creation":
                    _type = js.GetProperty("type").GetString();
                    switch (_type)
                    {
                        case "sell-limit":
                        case "sell-limit-maker":
                            this.order_type = orderType.Limit;
                            this.side = orderSide.Sell;
                            this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                            break;
                        case "buy-limit":
                        case "buy-limit-maker":
                            this.order_type = orderType.Limit;
                            this.side = orderSide.Buy;
                            this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                            break;
                        case "sell-market":
                            this.order_type = orderType.Market;
                            this.side = orderSide.Sell;
                            this.order_price = 0;
                            break;
                        case "buy-market":
                            this.order_type = orderType.Market;
                            this.side = orderSide.Buy;
                            this.order_price = 0;
                            break;
                        case "sell-ioc":
                        case "buy-ioc":
                        default:
                            this.order_type = orderType.Other;
                            break;
                    }
                    str_status = js.GetProperty("orderStatus").GetString();
                    switch (str_status)
                    {
                        case "created":
                            this.status = orderStatus.WaitOpen;
                            this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("orderCreateTime").GetInt64()).UtcDateTime;
                            this.create_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("orderCreateTime").GetInt64()).UtcDateTime;
                            break;
                        case "submitted":
                            this.status = orderStatus.Open;
                            this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("orderCreateTime").GetInt64()).UtcDateTime;
                            this.create_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("orderCreateTime").GetInt64()).UtcDateTime;
                            break;
                        case "partial-filled":
                        case "partial-canceled":
                            this.status = orderStatus.Open;
                            break;
                        case "filled":
                            this.status = orderStatus.Filled;
                            break;
                        case "canceling":
                            this.status = orderStatus.WaitCancel;
                            break;
                        case "canceled":
                            this.status = orderStatus.Canceled;
                            break;
                        default:
                            this.status = orderStatus.INVALID;
                            break;
                    }
                    this.order_quantity = decimal.Parse(js.GetProperty("orderSize").GetString());
                    break;
                case "cancellation":
                    _type = js.GetProperty("type").GetString();
                    switch (_type)
                    {
                        case "sell-limit":
                        case "sell-limit-maker":
                            this.order_type = orderType.Limit;
                            this.side = orderSide.Sell;
                            this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                            break;
                        case "buy-limit":
                        case "buy-limit-maker":
                            this.order_type = orderType.Limit;
                            this.side = orderSide.Buy;
                            this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                            break;
                        case "sell-market":
                            this.order_type = orderType.Market;
                            this.side = orderSide.Sell;
                            this.order_price = 0;
                            break;
                        case "buy-market":
                            this.order_type = orderType.Market;
                            this.side = orderSide.Buy;
                            this.order_price = 0;
                            break;
                        case "sell-ioc":
                        case "buy-ioc":
                        default:
                            this.order_type = orderType.Other;
                            break;
                    }
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("lastActTime").GetInt64()).UtcDateTime;
                    str_status = js.GetProperty("orderStatus").GetString();
                    switch (str_status)
                    {
                        case "created":
                            this.status = orderStatus.WaitOpen;
                            break;
                        case "submitted":
                            this.status = orderStatus.Open;
                            break;
                        case "partial-filled":
                        case "partial-canceled":
                            this.status = orderStatus.Open;
                            break;
                        case "filled":
                            this.status = orderStatus.Filled;
                            break;
                        case "canceling":
                            this.status = orderStatus.WaitCancel;
                            break;
                        case "canceled":
                            this.status = orderStatus.Canceled;
                            break;
                        default:
                            this.status = orderStatus.INVALID;
                            break;
                    }
                    break;
                case "trade":
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("tradeTime").GetInt64()).UtcDateTime;
                    this.current_traded_price = decimal.Parse(js.GetProperty("tradePrice").GetString());
                    this.current_traded_quantity = decimal.Parse(js.GetProperty("tradeVolume").GetString());
                    this.filled_quantity = decimal.Parse(js.GetProperty("execAmt").GetString());
                    _type = js.GetProperty("type").GetString();
                    switch (_type)
                    {
                        case "sell-limit":
                        case "sell-limit-maker":
                            this.order_type = orderType.Limit;
                            this.side = orderSide.Sell;
                            this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                            break;
                        case "buy-limit":
                        case "buy-limit-maker":
                            this.order_type = orderType.Limit;
                            this.side = orderSide.Buy;
                            this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                            break;
                        case "sell-market":
                            this.order_type = orderType.Market;
                            this.side = orderSide.Sell;
                            this.order_price = 0;
                            break;
                        case "buy-market":
                            this.order_type = orderType.Market;
                            this.side = orderSide.Buy;
                            this.order_price = 0;
                            break;
                        case "sell-ioc":
                        case "buy-ioc":
                        default:
                            this.order_type = orderType.Other;
                            break;
                    }
                    str_status = js.GetProperty("orderStatus").GetString();
                    switch (str_status)
                    {
                        case "created":
                            this.status = orderStatus.WaitOpen;
                            break;
                        case "submitted":
                            this.status = orderStatus.Open;
                            break;
                        case "partial-filled":
                        case "partial-canceled":
                            this.status = orderStatus.Open;
                            break;
                        case "filled":
                            this.status = orderStatus.Filled;
                            break;
                        case "canceling":
                            this.status = orderStatus.WaitCancel;
                            break;
                        case "canceled":
                            this.status = orderStatus.Canceled;
                            break;
                        default:
                            this.status = orderStatus.INVALID;
                            break;
                    }
                    break;
            }
            this.average_price = 0;
            this.fee_asset = "";
            this.fee = 0;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
        }
        public void setBitTradeTrade(JsonElement js)
        {
            this.timestamp = DateTime.UtcNow;
            this.symbol = js.GetProperty("symbol").GetString();
            this.symbol_market = this.symbol + "@bittrade";
            this.market = "bittrade";
            this.order_id = js.GetProperty("orderId").GetInt64().ToString();

            string side = js.GetProperty("orderSide").GetString();
            if (side == "buy")
            {
                this.side = orderSide.Buy;
            }
            else if (side == "sell")
            {
                this.side = orderSide.Sell;
            }

            string _type = js.GetProperty("orderType").GetString();
            switch (_type)
            {
                case "sell-limit":
                case "sell-limit-maker":
                    this.order_type = orderType.Limit;
                    this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                    this.order_quantity = decimal.Parse(js.GetProperty("orderSize").GetString());
                    break;
                case "buy-limit":
                case "buy-limit-maker":
                    this.order_type = orderType.Limit;
                    this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                    this.order_quantity = decimal.Parse(js.GetProperty("orderSize").GetString());
                    break;
                case "sell-market":
                    this.order_type = orderType.Market;
                    this.order_price = 0;
                    this.order_quantity = decimal.Parse(js.GetProperty("orderSize").GetString());
                    break;
                case "buy-market":
                    this.order_type = orderType.Market;
                    this.order_price = 0;
                    this.order_quantity = this.filled_quantity;
                    break;
                case "sell-ioc":
                case "buy-ioc":
                default:
                    this.order_type = orderType.Other;
                    break;
            }

            string str_status = js.GetProperty("orderStatus").GetString();
            switch (str_status)
            {
                case "created":
                    this.status = orderStatus.WaitOpen;
                    break;
                case "submitted":
                case "partial-filled":
                case "partial-canceled":
                    this.status = orderStatus.Open;
                    break;
                case "filled":
                    this.status = orderStatus.Filled;
                    break;
                case "canceling":
                    this.status = orderStatus.WaitCancel;
                    break;
                case "canceled":
                    this.status = orderStatus.Canceled;
                    break;
                default:
                    this.status = orderStatus.INVALID;
                    break;
            }


            string eventType = js.GetProperty("eventType").GetString();
            switch (eventType)
            {
                case "trade":
                    //this.average_price = decimal.Parse(js.GetProperty("tradePrice").GetString());
                    //this.filled_quantity = decimal.Parse(js.GetProperty("tradeVolume").GetString());
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("tradeTime").GetInt64()).UtcDateTime;
                    this.fee = decimal.Parse(js.GetProperty("transactFee").GetString());
                    this.fee_asset = js.GetProperty("feeCurrency").GetString().ToUpper();
                    this.fee -= decimal.Parse(js.GetProperty("feeDeduct").GetString());
                    break;
                case "cancellation":
                    this.fee = 0;
                    this.fee_asset = "";
                    break;
            }

            //this.client_order_id = js.GetProperty("clientOrderId").GetString();
            this.create_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("orderCreateTime").GetInt64()).UtcDateTime;

           
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
            this.fee_asset = update.FeeAsset.ToUpper();
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
            this.internal_order_id = "";
            this.fee_asset = "";
            this.fee = 0;
            this.create_time = null;
            this.update_time = null;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
            this.isVirtual = false;
            this.msg = "";
            this.err_code = 0;
            this.queued_count = 0;
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
            line += "," + this.order_id + "," + this.market + "," + this.symbol + "," + this.order_type.ToString() + "," + this.side.ToString() + "," + this.status.ToString() + "," + this.time_in_force.ToString() + "," + this.order_price.ToString() + "," + this.order_quantity.ToString() + "," + this.filled_quantity.ToString() + "," + this.average_price.ToString() + "," + this.internal_order_id + "," + this.fee_asset + "," + this.fee.ToString();
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
            line += "," + this.last_trade + "," + this.trigger_price.ToString() + "," + this.is_trigger_order.ToString() + "," + this.msg;

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
        public void setBitTradeTrade(JsonElement js,string symbol)
        {
            int i = 0;
            this.timestamp = DateTime.UtcNow;
            this.market = "bittrade";
            this.symbol = symbol;
            this.filled_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("ts").GetInt64()).UtcDateTime;
            this.price = js.GetProperty("price").GetDecimal();
            this.quantity = js.GetProperty("amount").GetDecimal();
            string str_side = js.GetProperty("direction").GetString();
            if (str_side == "buy")
            {
                this.side = SharedOrderSide.Buy;
            }
            else if (str_side == "sell")
            {
                this.side = SharedOrderSide.Sell;
            }
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
}
