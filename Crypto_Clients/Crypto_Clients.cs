using CryptoClients.Net;
using CryptoClients.Net.Enums;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Requests;
using CryptoExchange.Net.SharedApis;
using Discord;
using Discord.Audio.Streams;
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

using Enums;
using Utils;


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

        public ConcurrentQueue<DataFill> fillQueue;
        public ConcurrentStack<DataFill> fillStack;

        const int ORDUPDATE_STACK_SIZE = 300000;
        public ConcurrentQueue<DataSpotOrderUpdate> ordUpdateQueue;
        public ConcurrentStack<DataSpotOrderUpdate> ordUpdateStack;

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

            while (i < ORDUPDATE_STACK_SIZE)
            {
                this.ordUpdateStack.Push(new DataSpotOrderUpdate());
                ++i;
            }
        }

        public void checkStackCount()
        {
            if(this.ordBookStack.Count < STACK_SIZE / 10)
            {
                addLog("Pushing new objects into ordBookStack.");
                int i = 0;
                while(i < STACK_SIZE / 2)
                {
                    this.ordBookStack.Push(new DataOrderBook());
                    ++i;
                }
            }
            if (this.tradeStack.Count < STACK_SIZE / 10)
            {
                addLog("Pushing new objects into tradeStack.");
                int i = 0;
                while (i < STACK_SIZE / 2)
                {
                    this.tradeStack.Push(new DataTrade());
                    ++i;
                }
            }
            if (this.fillStack.Count < STACK_SIZE / 10)
            {
                addLog("Pushing new objects into fillStack.");
                int i = 0;
                while (i < STACK_SIZE / 2)
                {
                    this.fillStack.Push(new DataFill());
                    ++i;
                }
            }
            if (this.ordUpdateStack.Count < ORDUPDATE_STACK_SIZE / 10)
            {
                addLog("Pushing new objects into ordUpdateStack.");
                int i = 0;
                while (i < ORDUPDATE_STACK_SIZE / 2)
                {
                    this.ordUpdateStack.Push(new DataSpotOrderUpdate());
                    ++i;
                }
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
                    //this.addLog("getActiveOrders bitbank");
                    js = await this.bitbank_client.getActiveOrders();
                    //this.addLog("REST API");
                    //this.addLog(JsonSerializer.Serialize(js));
                    if (js.RootElement.GetProperty("success").GetInt16() == 1)
                    {
                        this.addLog("Order found");
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
                        this.addLog("Returning null");
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
}
