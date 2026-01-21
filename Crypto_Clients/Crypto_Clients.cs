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
using LockFreeQueue;
using LockFreeStack;
using ProtoBuf.WellKnownTypes;
using PubnubApi;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Utils;
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
        public gmocoin_connection gmocoin_client;

        CryptoClients.Net.Models.ExchangeCredentials creds;

        const int STACK_SIZE = 100000;

        public MIMOQueue<DataOrderBook> ordBookQueue;
        public LockFreeStack<DataOrderBook> ordBookStack;

        public MIMOQueue<DataTrade> tradeQueue;
        public LockFreeStack<DataTrade> tradeStack;

        public MIMOQueue<DataFill> fillQueue;
        public LockFreeStack<DataFill> fillStack;

        const int ORDUPDATE_STACK_SIZE = 300000;
        public MIMOQueue<DataSpotOrderUpdate> ordUpdateQueue;
        public LockFreeStack<DataSpotOrderUpdate> ordUpdateStack;

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
            this.gmocoin_client = gmocoin_connection.GetInstance();

            this.creds = new CryptoClients.Net.Models.ExchangeCredentials();

            this.ordBookQueue = new MIMOQueue<DataOrderBook>();
            this.ordBookStack = new LockFreeStack<DataOrderBook>();

            this.ordUpdateQueue = new MIMOQueue<DataSpotOrderUpdate>();
            this.ordUpdateStack = new LockFreeStack<DataSpotOrderUpdate>();

            this.tradeQueue = new MIMOQueue<DataTrade>();
            this.tradeStack = new LockFreeStack<DataTrade>();

            this.fillQueue = new MIMOQueue<DataFill>();
            this.fillStack = new LockFreeStack<DataFill>();

            this.bitbank_client.setQueues(this);

            this.megLogging = false;

            //this._addLog = Console.WriteLine;

            int i = 0;

            while (i < STACK_SIZE)
            {
                this.ordBookStack.push(new DataOrderBook());
                this.ordUpdateStack.push(new DataSpotOrderUpdate());
                this.tradeStack.push(new DataTrade());
                this.fillStack.push(new DataFill());
                ++i;
            }

            while (i < ORDUPDATE_STACK_SIZE)
            {
                this.ordUpdateStack.push(new DataSpotOrderUpdate());
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
                    this.ordBookStack.push(new DataOrderBook());
                    ++i;
                }
            }
            if (this.tradeStack.Count < STACK_SIZE / 10)
            {
                addLog("Pushing new objects into tradeStack.");
                int i = 0;
                while (i < STACK_SIZE / 2)
                {
                    this.tradeStack.push(new DataTrade());
                    ++i;
                }
            }
            if (this.fillStack.Count < STACK_SIZE / 10)
            {
                addLog("Pushing new objects into fillStack.");
                int i = 0;
                while (i < STACK_SIZE / 2)
                {
                    this.fillStack.push(new DataFill());
                    ++i;
                }
            }
            if (this.ordUpdateStack.Count < ORDUPDATE_STACK_SIZE / 10)
            {
                addLog("Pushing new objects into ordUpdateStack.");
                int i = 0;
                while (i < ORDUPDATE_STACK_SIZE / 2)
                {
                    this.ordUpdateStack.push(new DataSpotOrderUpdate());
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
                case "gmocoin":
                    this.gmocoin_client.setLogFile(outputPath);
                    ret = this.gmocoin_client.msgLogging;
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
            this.gmocoin_client._addLog = act;
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
            this.ordBookStack.push(msg);
        }
        public void pushToTradeStack(DataTrade msg)
        {
            msg.init();
            this.tradeStack.push(msg);
        }
        public void pushToOrderUpdateStack(DataSpotOrderUpdate msg)
        {
            msg.init();
            this.ordUpdateStack.push(msg);
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
                case "gmocoin":
                    this.gmocoin_client.SetApiCredentials(name, key);
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
                    case "gmocoin":
                        js = await this.gmocoin_client.getBalance();
                        JsonElement res;
                        if (js.RootElement.TryGetProperty("status",out res) && res.GetUInt16() == 0)
                        {
                            var assets = js.RootElement.GetProperty("data").EnumerateArray();
                            foreach (var asset in assets)
                            {
                                DataBalance balance = new DataBalance();
                                balance.market = m;
                                balance.asset = asset.GetProperty("symbol").GetString();
                                balance.available = Decimal.Parse(asset.GetProperty("available").GetString());
                                balance.total = Decimal.Parse(asset.GetProperty("amount").GetString());
                                temp.Add(balance);
                            }
                        }
                        else
                        {
                            this.addLog("Failed to get the balance information. Exchange:" + m, Enums.logType.WARNING);
                            this.addLog(JsonSerializer.Serialize(js), Enums.logType.WARNING);
                        }
                        Thread.Sleep(1000);
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
        async public Task<DataMarginPos[]> getMarginPos(IEnumerable<string>? markets)
        {
            List<DataMarginPos> temp = new List<DataMarginPos>();
            JsonDocument js;
            foreach(string market in markets)
            {
                switch (market)
                {
                    case "bitbank":
                        js = await this.bitbank_client.getMarginPosition();
                        if (js.RootElement.GetProperty("success").GetInt16() == 1)
                        {
                            JsonElement data = js.RootElement.GetProperty("data");
                            JsonElement notice = data.GetProperty("notice");
                            JsonElement payable = data.GetProperty("payables");
                            JsonElement positions = data.GetProperty("positions");
                            if (notice.GetProperty("what").GetString() != null)
                            {
                                addLog("Received a margin message. Please check details", logType.WARNING);
                                addLog(notice.ToString(), logType.WARNING);
                            }
                            decimal dc_payable = decimal.Parse(payable.GetProperty("amount").GetString());
                            if (dc_payable > 0)
                            {
                                addLog("The payable amount is non zero amount:" + dc_payable.ToString(), logType.WARNING);
                            }
                            foreach (var elem in positions.EnumerateArray())
                            {
                                DataMarginPos pos = new DataMarginPos();
                                pos.setBitbankJson(elem);
                                temp.Add(pos);
                            }
                        }
                        else
                        {
                            this.addLog($"Failed to get the margin position of {market}. message:{js.RootElement.ToString()}", logType.WARNING);
                        }
                        break;
                    case "gmocoin":
                        js = await this.gmocoin_client.getMarginPosition();
                        JsonElement res;
                        if (js.RootElement.TryGetProperty("status", out res) && res.GetUInt16() == 0)
                        {
                            JsonElement data = js.RootElement.GetProperty("data");
                            JsonElement list_data;
                            if(data.TryGetProperty("list",out list_data))
                            {
                                foreach (var elem in list_data.EnumerateArray())
                                {
                                    DataMarginPos pos = new DataMarginPos();
                                    pos.setGMOCoinJson(elem);
                                    temp.Add(pos);
                                }
                            }
                        }
                        else
                        {
                            this.addLog($"Failed to get the margin position of {market}. message:{js.RootElement.GetRawText()}", logType.WARNING);
                        }
                        Thread.Sleep(1000);
                        break;
                    default:
                        addLog($"The getMarginPos is not defined for {market}", logType.WARNING);
                        break;
                }
            }            
            return temp.ToArray();
        }
        public async Task<List<DataSpotOrderUpdate>> getActiveOrders(string market,List<string> symList = null)
        {
            DataSpotOrderUpdate ord;
            List<DataSpotOrderUpdate> l = new List<DataSpotOrderUpdate>();
            JsonDocument js;
            switch (market)
            {
                case "bitbank":
                    js = await this.bitbank_client.getActiveOrders();
                    if (js.RootElement.GetProperty("success").GetInt16() == 1)
                    {
                        var data = js.RootElement.GetProperty("data").GetProperty("orders");
                        foreach(var item in data.EnumerateArray())
                        {
                            ord = this.ordUpdateStack.pop();
                            if(ord == null)
                            {
                                ord = new DataSpotOrderUpdate();
                            }
                            ord.setBitbankSpotOrder(item);
                            l.Add(ord);
                            this.ordUpdateQueue.Enqueue(ord);
                        }
                    }
                    else
                    {
                        this.addLog("Failed to get active orders from bitbank",logType.WARNING);
                        l = null;
                    }
                    break;
                case "coincheck":
                    js = await this.coincheck_client.getActiveOrders();
                    if (js.RootElement.GetProperty("success").GetBoolean())
                    {
                        var data = js.RootElement.GetProperty("orders");
                        foreach (var item in data.EnumerateArray())
                        {
                            //while (!this.ordUpdateStack.TryPop(out ord))
                            //{

                            //}
                            ord = this.ordUpdateStack.pop();
                            if(ord == null)
                            {
                                ord = new DataSpotOrderUpdate();
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
                        this.addLog("Failed to get active orders from coincheck", logType.WARNING);
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
                            //while (!this.ordUpdateStack.TryPop(out ord))
                            //{

                            //}
                            ord = this.ordUpdateStack.pop();
                            if (ord == null)
                            {
                                ord = new DataSpotOrderUpdate();
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
                case "gmocoin":
                    if(symList != null)
                    {
                        foreach(string symbol in symList)
                        {
                            js = await this.gmocoin_client.getActiveOrders(symbol);
                            JsonElement res;
                            if (js.RootElement.TryGetProperty("status", out res) && res.GetUInt16() == 0)
                            {
                                JsonElement data = js.RootElement.GetProperty("data");
                                JsonElement list_data;
                                if(data.TryGetProperty("list",out list_data))
                                {
                                    foreach (var elem in list_data.EnumerateArray())
                                    {
                                        ord = this.ordUpdateStack.pop();
                                        if (ord == null)
                                        {
                                            ord = new DataSpotOrderUpdate();
                                        }
                                        ord.symbol = elem.GetProperty("symbol").GetString();
                                        ord.create_time = DateTime.ParseExact(elem.GetProperty("timestamp").GetString(), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                                        ord.update_time = ord.create_time;
                                        ord.order_quantity = decimal.Parse(elem.GetProperty("size").GetString());
                                        ord.filled_quantity = decimal.Parse(elem.GetProperty("executedSize").GetString());
                                        ord.order_id = elem.GetProperty("orderId").GetInt64().ToString();
                                        string str_status = elem.GetProperty("status").GetString();
                                        switch (str_status)
                                        {
                                            case "WAITING":
                                                ord.status = orderStatus.WaitOpen;
                                                break;
                                            case "ORDERED":
                                                ord.status = orderStatus.Open;
                                                break;
                                            case "MODIFYING":
                                                ord.status = orderStatus.WaitMod;
                                                break;
                                            case "CANCELLING":
                                                ord.status = orderStatus.WaitCancel;
                                                break;
                                            default:
                                                ord.status = orderStatus.INVALID;
                                                break;
                                        }
                                        string _type = elem.GetProperty("side").GetString();
                                        string settleType = elem.GetProperty("settleType").GetString();
                                        switch (_type)
                                        {
                                            case "BUY":
                                                ord.side = orderSide.Buy;
                                                if (settleType == "OPEN")
                                                {
                                                    ord.position_side = positionSide.Long;
                                                }
                                                else if (settleType == "CLOSE")
                                                {
                                                    ord.position_side = positionSide.Short;
                                                }
                                                break;
                                            case "SELL":
                                                ord.side = orderSide.Sell;
                                                if (settleType == "OPEN")
                                                {
                                                    ord.position_side = positionSide.Short;
                                                }
                                                else if (settleType == "CLOSE")
                                                {
                                                    ord.position_side = positionSide.Long;
                                                }
                                                break;
                                            default:
                                                ord.side = orderSide.NONE;
                                                break;
                                        }

                                        l.Add(ord);
                                        this.ordUpdateQueue.Enqueue(ord);
                                    }
                                }
                            }
                            else
                            {
                                addLog("Failed to get active orders from gmocoin. symbol:" + symbol, logType.WARNING);
                            }
                            Thread.Sleep(1000);
                        }
                    }
                    else
                    {
                        addLog("Symbol list is required to get active orders from gmocoin.", logType.WARNING);
                    }
                    break;
                default:
                    addLog($"The getActiveOrders is not defined for {market}", logType.WARNING);
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
                                //while (!this.tradeStack.TryPop(out trade))
                                //{
                                //}
                                trade = this.tradeStack.pop();
                                if(trade == null)
                                {
                                    trade = new DataTrade();
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
                                    //while (!this.tradeStack.TryPop(out trade))
                                    //{
                                    //}
                                    trade = this.tradeStack.pop();
                                    if (trade == null)
                                    {
                                        trade = new DataTrade();
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
                                        this.tradeStack.push(trade);
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
                        //while (!this.tradeStack.TryPop(out trade))
                        //{
                        //}
                        trade = this.tradeStack.pop();
                        if (trade == null)
                        {
                            trade = new DataTrade();
                        }
                        trade.market = "coincheck";
                        trade.symbol = js_elem.GetProperty("pair").GetString();
                        trade.quantity = decimal.Parse(js_elem.GetProperty("amount").GetString());
                        trade.price = decimal.Parse(js_elem.GetProperty("rate").GetString());
                        string ord_side = js_elem.GetProperty("order_type").GetString();
                        if(ord_side == "buy")
                        {
                            trade.side = orderSide.Buy;
                        }
                        else if (ord_side == "sell")
                        {
                            trade.side = orderSide.Sell;
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

        public async Task<List<DataFill>> getTradeHistory(string market,string symbol = "",DateTime? startTime = null,DateTime? endTime = null)
        {
            List<DataFill> output = new List<DataFill>();
            JsonDocument js;
            List<JsonElement> js_list;
            DataFill fill;
            string test;
            switch(market)
            {
                case "bitbank":
                    js = await this.bitbank_client.getTradeHistory(symbol, startTime, endTime);
                    if(js.RootElement.GetProperty("success").GetInt32() == 1)
                    {
                        var data = js.RootElement.GetProperty("data").GetProperty("trades");
                        foreach(var item in data.EnumerateArray())
                        {
                            fill = this.fillStack.pop();
                            if(fill == null)
                            {
                                fill = new DataFill();
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
                        fill = this.fillStack.pop();
                        if (fill == null)
                        {
                            fill = new DataFill();
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
                case "gmocoin":
                    if(symbol == "")
                    {
                        addLog("Symbol is required to get trade history from gmocoin.", logType.WARNING);
                        return output;
                    }
                    else
                    {
                        js_list = await this.gmocoin_client.getTradeHistory(symbol);
                        foreach (var js_elem in js_list)
                        {
                            fill = this.fillStack.pop();
                            if (fill == null)
                            {
                                fill = new DataFill();
                            }
                            fill.setGMOCoinHistFill(js_elem, symbol);
                            fill.timestamp = fill.filled_time;
                            if(startTime != null && fill.filled_time < startTime)
                            {
                                fill.init();
                                this.fillStack.push(fill);
                            }
                            else if(endTime != null && fill.filled_time > endTime)
                            {
                                fill.init();
                                this.fillStack.push(fill);
                            }
                            else
                            {
                                output.Add(fill);
                            }
                        }
                        if (js_list.Count == 0)
                        {
                            addLog("Failed to get trade history from gmocoin.", logType.WARNING);
                        }
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
                        addLog(js.RootElement.GetRawText(), logType.WARNING);
                    }
                    break;
                case "coincheck":
                    js = await this.coincheck_client.getTicker(symbol);
                    JsonElement success;
                    if(js.RootElement.TryGetProperty("success",out success))
                    {
                        if(success.GetBoolean() == true)
                        {
                            JsonElement js_bid;
                            JsonElement js_ask;
                            decimal bid = 0;
                            decimal ask = 0;
                            if (js.RootElement.TryGetProperty("bid", out js_bid))
                            {
                                bid = js_bid.GetDecimal();
                            }
                            if (js.RootElement.TryGetProperty("ask", out js_ask))
                            {
                                ask = js_ask.GetDecimal();
                            }
                            if (ask > 0 && bid > 0)
                            {
                                mid = (ask + bid) / 2;
                            }
                            else if (ask > 0)
                            {
                                mid = ask;
                            }
                            else if (bid > 0)
                            {
                                mid = bid;
                            }
                        }
                        else
                        {
                            addLog("Failed to get the ticker from coincheck.", logType.WARNING);
                        }
                    }
                    else
                    {
                        JsonElement js_bid;
                        JsonElement js_ask;
                        decimal bid = 0;
                        decimal ask = 0;
                        if (js.RootElement.TryGetProperty("bid", out js_bid))
                        {
                            bid = js_bid.GetDecimal();
                        }
                        if (js.RootElement.TryGetProperty("ask", out js_ask))
                        {
                            ask = js_ask.GetDecimal();
                        }
                        if (ask > 0 && bid > 0)
                        {
                            mid = (ask + bid) / 2;
                        }
                        else if (ask > 0)
                        {
                            mid = ask;
                        }
                        else if (bid > 0)
                        {
                            mid = bid;
                        }
                    }
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
                //while (!this.ordUpdateStack.TryPop(out ord))
                //{

                //}
                ord = this.ordUpdateStack.pop();
                if (ord == null)
                {
                    ord = new DataSpotOrderUpdate();
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
                //while (!this.ordUpdateStack.TryPop(out ord))
                //{

                //}
                ord = this.ordUpdateStack.pop();
                if (ord == null)
                {
                    ord = new DataSpotOrderUpdate();
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
                    case "gmocoin":
                        await this.gmocoin_client.subscribeOrderEvent();
                        Thread.Sleep(1100);//GMO Coin doesn't allow more than 1 request per a second.
                        await this.gmocoin_client.subscribeExecutionEvent();
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
                obj = this.ordUpdateStack.pop();
                if (obj== null)
                {
                    obj = new DataSpotOrderUpdate();
                }
                obj.setSharedSpotOrder(ord, update.Exchange, update.DataTime);
                this.ordUpdateQueue.Enqueue(obj);
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
                    case "bittrade":
                        await this.bittrade_client.subscribeTrades(baseCcy, quoteCcy);
                        break;
                    case "gmocoin":
                        await this.gmocoin_client.subscribeTrades(baseCcy, quoteCcy);
                        Thread.Sleep(1100);//GMO Coin doesn't allow more than 1 request per a second.
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
                trd = this.tradeStack.pop();
                if (trd == null)
                {
                    trd = new DataTrade();
                }
                trd.setSharedTrade(item, update.Exchange, update.Symbol, update.DataTime);
                this.tradeQueue.Enqueue(trd);
            }
        }
        async public Task 
            subscribeOrderBook(IEnumerable<string>? markets, string baseCcy, string quoteCcy)
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
                        DataOrderBook ord;
                        ord = this.ordBookStack.pop();
                        if (ord == null)
                        {
                            ord = new DataOrderBook();
                        }
                        ord.setCoincheckOrderBook(js.RootElement, coincheck_symbol);
                        this.ordBookQueue.Enqueue(ord);
                        await this.coincheck_client.subscribeOrderBook(baseCcy, quoteCcy);
                        break;
                    case "bittrade":
                        await this.bittrade_client.subscribeOrderBook(baseCcy, quoteCcy);
                        break;
                    case "gmocoin":
                        await this.gmocoin_client.subscribeOrderBook(baseCcy,quoteCcy);
                        Thread.Sleep(1100);//GMO Coin doesn't allow more than 1 request per a second.
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
            //while (!this.ordBookStack.TryPop(out msg))
            //{

            //}
            msg = this.ordBookStack.pop();
            if (msg == null)
            {
                msg = new DataOrderBook();
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

            //while (!this.ordBookStack.TryPop(out msg))
            //{

            //}
            msg = this.ordBookStack.pop();
            if (msg == null)
            {
                msg = new DataOrderBook();
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
            //while (!this.ordBookStack.TryPop(out msg))
            //{

            //}
            msg = this.ordBookStack.pop();
            if (msg == null)
            {
                msg = new DataOrderBook();
            }
            msg.setBybitOrderBook(update);
            this.ordBookQueue.Enqueue(msg);
        }

        public void onGMOCoinMessage(string msg_body)
        {
            try
            {
                JsonDocument doc = JsonDocument.Parse(msg_body);
                string channel = doc.RootElement.GetProperty("channel").GetString();
                switch(channel)
                {
                    case "orderbooks":
                        DataOrderBook ord;
                        ord = this.ordBookStack.pop();
                        if (ord == null)
                        {
                            ord = new DataOrderBook();
                        }
                        ord.setGMOCoinOrderBook(doc);
                        this.ordBookQueue.Enqueue(ord);
                        break;
                    case "trades":
                        DataTrade trd;
                        //while (!this.tradeStack.TryPop(out trd))
                        //{

                        //}
                        trd = this.tradeStack.pop();
                        if (trd == null)
                        {
                            trd = new DataTrade();
                        }
                        trd.setGMOCoinTrade(doc);
                        this.tradeQueue.Enqueue(trd);
                        break;
                    default:
                        addLog(msg_body);
                        break;
                }
            }
            catch (Exception e)
            {
                this.addLog("[onGMOCoinMessage]" + e.Message, Enums.logType.ERROR);
                this.addLog("[onGMOCoinMessage]" + msg_body, Enums.logType.ERROR);
            }
        }
        public void onGMOCoinPrivateMessage(string msg_body)
        {
            try
            {
                JsonDocument doc = JsonDocument.Parse(msg_body);
                string channel = doc.RootElement.GetProperty("channel").GetString();
                DataSpotOrderUpdate ord;
                DataFill exe;
                switch(channel)
                {
                    case "orderEvents":
                        ord = this.ordUpdateStack.pop();
                        if(ord == null)
                        {
                            ord = new DataSpotOrderUpdate();
                        }
                        ord.setGMOCoinSpotOrder(doc.RootElement);
                        this.ordUpdateQueue.Enqueue(ord);
                        break;
                    case "executionEvents":
                        exe = this.fillStack.pop();
                        if(exe == null)
                        {
                            exe = new DataFill();
                        }
                        exe.setGMOCoinFill(doc.RootElement);
                        //if(exe.order_quantity == exe.executed_quantity)//Consider creating order objects for every fill
                        {
                            ord = this.ordUpdateStack.pop();
                            if (ord == null)
                            {
                                ord = new DataSpotOrderUpdate();
                            }
                            ord.setGMOCoinFill(doc.RootElement);
                            this.ordUpdateQueue.Enqueue(ord);
                        }
                        this.fillQueue.Enqueue(exe);
                        break;
                }
            }
            catch (Exception e)
            {
                this.addLog("[onGMOCoinPrivateMessage]" + e.Message, Enums.logType.ERROR);
                this.addLog("[onGMOCoinPrivateMessage]" + msg_body, Enums.logType.ERROR);
            }
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
                    //while (!this.ordBookStack.TryPop(out ord))
                    //{

                    //}
                    ord = this.ordBookStack.pop();
                    if (ord == null)
                    {
                        ord = new DataOrderBook();
                    }
                    ord.setBitbankOrderBook(payload.GetProperty("message").GetProperty("data"), symbol, false);
                    this.ordBookQueue.Enqueue(ord);
                }
                else if (roomName.StartsWith("depth_whole"))
                {
                    string symbol = roomName.Substring("depth_whole_".Length);
                    DataOrderBook ord;
                    //while (!this.ordBookStack.TryPop(out ord))
                    //{

                    //}
                    ord = this.ordBookStack.pop();
                    if (ord == null)
                    {
                        ord = new DataOrderBook();
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
                        //while (!this.tradeStack.TryPop(out trd))
                        //{

                        //}
                        trd = this.tradeStack.pop();
                        if (trd == null)
                        {
                            trd = new DataTrade();
                        }
                        trd.setBitbankTrade(element, "bitbank", symbol);
                        this.tradeQueue.Enqueue(trd);
                    }
                }
            }
            catch (Exception e)
            {
                this.addLog("[onBitbankMessage]" + e.Message,Enums.logType.ERROR);
                this.addLog("[onBitbankMessage]" + msg_body, Enums.logType.ERROR);
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
                //while (!this.ordBookStack.TryPop(out ord))
                //{

                //}
                ord = this.ordBookStack.pop();
                if (ord == null)
                {
                    ord = new DataOrderBook();
                }
                ord.setCoincheckOrderBook(obj,symbol);
                this.ordBookQueue.Enqueue(ord);
            }
            else if(root[0].ValueKind == JsonValueKind.Array)//Trade
            {
                foreach( var item in root.EnumerateArray())
                {
                    DataTrade trd;

                    trd = this.tradeStack.pop();
                    if (trd == null)
                    {
                        trd = new DataTrade();
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
                    //while (!this.ordUpdateStack.TryPop(out ord))
                    //{

                    //}
                    ord = this.ordUpdateStack.pop();
                    if (ord == null)
                    {
                        ord = new DataSpotOrderUpdate();
                    }
                    ord.setCoincheckSpotOrder(js);
                    this.ordUpdateQueue.Enqueue(ord);
                    break;
                case "execution-events":
                    //while (!this.fillStack.TryPop(out fill))
                    //{

                    //}
                    fill = this.fillStack.pop();
                    if (fill == null)
                    {
                        fill = new DataFill();
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
                            //while (!this.ordBookStack.TryPop(out ord))
                            //{

                            //}
                            ord = this.ordBookStack.pop();
                            if (ord == null)
                            {
                                ord = new DataOrderBook();
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
                                //while (!this.tradeStack.TryPop(out trd))
                                //{

                                //}
                                trd = this.tradeStack.pop();
                                if (trd == null)
                                {
                                    trd = new DataTrade();
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
                                //while (!this.ordUpdateStack.TryPop(out ord))
                                //{

                                //}
                                ord = this.ordUpdateStack.pop();
                                if (ord == null)
                                {
                                    ord = new DataSpotOrderUpdate();
                                }
                                ord.setBitTradeOrder(obj);
                                this.ordUpdateQueue.Enqueue(ord);
                            }
                            else if(ch.GetString().StartsWith("trade.clearing"))
                            {
                                //while (!this.fillStack.TryPop(out fill))
                                //{

                                //}
                                fill = this.fillStack.pop();
                                if (fill == null)
                                {
                                    fill = new DataFill();
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
