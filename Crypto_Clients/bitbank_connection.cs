using Bitget.Net.Objects.Models.V2;
using CryptoExchange.Net.SharedApis;
using PubnubApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Crypto_Clients
{
    public class bitbank_connection
    {
        private string apiName;
        private string secretKey;

        private const string publicKey = "sub-c-ecebae8e-dd60-11e6-b6b1-02ee2ddab7fe";
        private const string URL = "https://api.bitbank.cc";
        private const string ws_URL = "wss://stream.bitbank.cc/socket.io/?EIO=4&transport=websocket";

        public ConcurrentQueue<JsonElement> orderQueue;
        public ConcurrentQueue<JsonElement> fillQueue;

        ClientWebSocket websocket_client;

        WebSocketState pubnub_state;

        public Action<string> onMessage;
        public Action<string,Enums.logType> _addLog;

        byte[] ws_buffer = new byte[16384];
        MemoryStream ws_memory = new MemoryStream();
        MemoryStream result_memory = new MemoryStream();

        bool closeSent;

        private List<string> subscribingChannels;

        private bitbank_connection()
        {
            this.apiName = "";
            this.secretKey = "";

            this.websocket_client = new ClientWebSocket();

            this.orderQueue = new ConcurrentQueue<JsonElement>();
            this.fillQueue = new ConcurrentQueue<JsonElement>();

            this.closeSent = false;
            this.pubnub_state = WebSocketState.None;

            this.subscribingChannels = new List<string>();

            //this._addLog = Console.WriteLine;
            this.onMessage = Console.WriteLine;
        }
        public void SetApiCredentials(string name, string key)
        {
            this.apiName = name;
            this.secretKey = key;
        }

        public void subscribePrivateChannels(Pubnub pubnub, string channel, string token)
        {
            pubnub.SetAuthToken(token);
            pubnub.Subscribe<string>()
                  .Channels(new[] { channel })
                  .Execute();
        }

        public async Task connectPublicAsync()
        {
            this.addLog("Connecting to bitbank");
            var uri = new Uri(bitbank_connection.ws_URL);
            try
            {
                this.websocket_client.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await this.websocket_client.ConnectAsync(uri, CancellationToken.None);
                this.addLog("Connected to bitbank.");
                this.closeSent = false;
            }
            catch (WebSocketException wse)
            {
                this.addLog($"WebSocketException: {wse.Message}",Enums.logType.ERROR);
            }
            catch (Exception ex)
            {
                this.addLog($"Connection failed: {ex.Message}", Enums.logType.ERROR);
            }
        }

        public async Task reconnectPublic()
        {
            this.addLog("Reconnecting...");

            Thread.Sleep(1000);

            this.websocket_client.Dispose();
            this.websocket_client = new ClientWebSocket();

            Thread.Sleep(1000);

            await this.connectPublicAsync();
            foreach (string ch in this.subscribingChannels)
            {
                string[] channel_details = ch.Split("_");

                switch (channel_details[0])
                {
                    case "trade":
                        await this.subscribeTrades(channel_details[1], channel_details[2]);
                        break;
                    case "orderbook":
                        await this.subscribeOrderBook(channel_details[1], channel_details[2]);
                        break;
                }
            }
        }

        public async Task disconnectPublic()
        {
            if(this.closeSent)
            {
                this.addLog("closeAsnyc is already called.", Enums.logType.WARNING);
            }
            else
            {
                this.closeSent = true;
                await this.websocket_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
        }

        public async Task subscribeTrades(string baseCcy, string quoteCcy)
        {
            string event_name = "transactions_" + baseCcy.ToLower() + "_" + quoteCcy.ToLower();
            var subscribeJson = @"42[""join-room"",""" + event_name + @"""]";
            var bytes = Encoding.UTF8.GetBytes(subscribeJson);
            if (this.websocket_client.State == WebSocketState.Open)
            {
                await this.websocket_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            string channel_name = "trade_" + baseCcy + "_" + quoteCcy;
            if (!this.subscribingChannels.Contains(channel_name))
            {
                this.subscribingChannels.Add(channel_name);
            }
        }

        public async Task subscribeOrderBook(string baseCcy, string quoteCcy)
        {
            string event_name = "depth_diff_" + baseCcy.ToLower() + "_" + quoteCcy.ToLower();
            var subscribeJson = @"42[""join-room"",""" + event_name + @"""]";
            var bytes = Encoding.UTF8.GetBytes(subscribeJson);
            if (this.websocket_client.State == WebSocketState.Open)
            {
                await this.websocket_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            event_name = "depth_whole_" + baseCcy.ToLower() + "_" + quoteCcy.ToLower();
            subscribeJson = @"42[""join-room"",""" + event_name + @"""]";
            bytes = Encoding.UTF8.GetBytes(subscribeJson);
            if (this.websocket_client.State == WebSocketState.Open)
            {
                await this.websocket_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            string channel_name = "orderbook_" + baseCcy + "_" + quoteCcy;
            if (!this.subscribingChannels.Contains(channel_name))
            {
                this.subscribingChannels.Add(channel_name);
            }
        }

        public async void startListen(Action<string> onMsg)
        {
            this.onMessage = onMsg;
            var buffer = new byte[16384];
            while (this.websocket_client.State == WebSocketState.Open)
            {
                var result = await this.websocket_client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Text)
                {

                    var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    int idx = msg.IndexOf("{");
                    int idx_temp = msg.IndexOf("[");
                    if (idx_temp != -1 && idx_temp < idx)
                    {
                        idx = idx_temp;
                    }
                    if (idx != -1)
                    {
                        string num = msg.Substring(0, idx);
                        switch (num)
                        {
                            case "0":
                                this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("40"), WebSocketMessageType.Text, true, CancellationToken.None);
                                break;
                            case "2":
                                this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("3"), WebSocketMessageType.Text, true, CancellationToken.None);
                                break;
                            case "40":
                                this.addLog("Hand shake completed");
                                break;
                            case "42"://Actual Message
                                if (result.EndOfMessage)
                                {
                                    string msg_body = msg.Substring(idx);
                                    this.onMessage(msg_body);
                                }
                                else
                                {
                                    this.addLog("The message is too large",Enums.logType.ERROR);
                                }
                                break;
                        }
                    }
                    else
                    {
                        if (msg == "2")
                        {
                            this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("3"), WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    this.addLog("Closed by server");
                    await this.websocket_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
            }
        }

        public async Task<bool> onListen(Action<string> onMsg)
        {
            WebSocketReceiveResult result;
            if (this.websocket_client.State == WebSocketState.Open)
            {
                
                this.ws_memory.SetLength(0);
                this.ws_memory.Position = 0;
                do
                {
                    result = await this.websocket_client.ReceiveAsync(new ArraySegment<byte>(this.ws_buffer), CancellationToken.None);
                    this.ws_memory.Write(this.ws_buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
                    //Array.Clear(this.ws_buffer, 0, this.ws_buffer.Length);
                    int idx = msg.IndexOf("{");
                    int idx_temp = msg.IndexOf("[");
                    if (idx_temp != -1 && idx_temp < idx)
                    {
                        idx = idx_temp;
                    }
                    if (idx != -1)
                    {
                        string num = msg.Substring(0, idx);
                        switch (num)
                        {
                            case "0":
                                this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("40"), WebSocketMessageType.Text, true, CancellationToken.None);
                                break;
                            case "2":
                                this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("3"), WebSocketMessageType.Text, true, CancellationToken.None);
                                break;
                            case "40":
                                this.addLog("Hand shake completed");
                                break;
                            case "42"://Actual Message
                                if (result.EndOfMessage)
                                {
                                    string msg_body = msg.Substring(idx);
                                    onMsg(msg_body);
                                }
                                else
                                {
                                    this.addLog("The message is too large", Enums.logType.ERROR);
                                    await this.disconnectPublic();
                                    return false;
                                }
                                break;
                        }
                    }
                    else
                    {
                        if (msg == "2")
                        {
                            this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("3"), WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    this.ws_memory.Position = 0;
                    using var gzipStream = new GZipStream(this.ws_memory, CompressionMode.Decompress, leaveOpen: true);
                    gzipStream.CopyTo(this.result_memory);
                    var msg = Encoding.UTF8.GetString(this.result_memory.ToArray());
                    this.result_memory.SetLength(0);
                    this.result_memory.Position = 0;
                    onMsg(msg);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    this.addLog("Closed by server");
                    if (this.websocket_client.State == WebSocketState.Open || this.websocket_client.State == WebSocketState.CloseReceived)
                    {
                        await this.disconnectPublic();
                    }
                    return false;
                }
            }
            else
            {
                this.addLog("Public channel is closed. Check the status. State:" + this.websocket_client.State.ToString(), Enums.logType.ERROR);
                if (this.websocket_client.State == WebSocketState.CloseReceived)
                {
                    await this.disconnectPublic();
                }
                return false;
            }
            return true;
        }
        private async Task<string> getAsync(string endpoint)
        {
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timeWindow = "5000";
            var message = $"{nonce}{timeWindow}{endpoint}";

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, bitbank_connection.URL + endpoint);

            request.Headers.Add("ACCESS-KEY", this.apiName);
            request.Headers.Add("ACCESS-REQUEST-TIME", nonce.ToString());
            request.Headers.Add("ACCESS-TIME-WINDOW", timeWindow);
            request.Headers.Add("ACCESS-SIGNATURE", ToSha256(this.secretKey, message));

            request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var resString = await response.Content.ReadAsStringAsync();

            return resString;
        }
        private async Task<string> postAsync(string endpoint, string body)
        {
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timeWindow = "5000";
            var message = $"{nonce}{timeWindow}{body}";

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, bitbank_connection.URL + endpoint);

            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            request.Headers.Add("ACCESS-KEY", this.apiName);
            request.Headers.Add("ACCESS-REQUEST-TIME", nonce.ToString());
            request.Headers.Add("ACCESS-TIME-WINDOW", timeWindow);
            request.Headers.Add("ACCESS-SIGNATURE", ToSha256(this.secretKey, message));

            var response = await client.SendAsync(request);
            var resString = await response.Content.ReadAsStringAsync();

            return resString;
        }

        public async Task<JsonDocument> getBalance()
        {
            var resString = await this.getAsync("/v1/user/assets");
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getActiveOrders()
        {
            var resString = await this.getAsync("/v1/user/spot/active_orders");
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getStatus()
        {
            var resString = await this.getAsync("/spot/status");
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getSpotDetails()
        {
            var resString = await this.getAsync("/spot/pairs");
            var json = JsonDocument.Parse(resString);
            return json;
        }


        public async Task<JsonDocument> placeNewOrder(string symbol,string ord_type,string side,decimal price = 0,decimal quantity = 0,bool postonly = false)
        {
            var body = new
            {
                pair = symbol,
                amount = quantity.ToString(),
                price = price.ToString(),
                side = side,
                type = ord_type,
                post_only = postonly
            };

            var jsonBody = JsonSerializer.Serialize(body);
            var resString = await this.postAsync("/v1/user/spot/order", jsonBody);

            return JsonDocument.Parse(resString);
        }
        public async Task<JsonDocument> placeCanOrder(string symbol,string order_id)
        {
            var body = new
            {
                pair = symbol,
                order_id = Int64.Parse(order_id)
            };

            var jsonBody = JsonSerializer.Serialize(body);
            var resString = await this.postAsync("/v1/user/spot/cancel_order", jsonBody);

            return JsonDocument.Parse(resString);
        }

        private async Task<(string channel, string token)> GetChannelAndToken()
        {
            var resString = await this.getAsync("/v1/user/subscribe");

            var json = JsonDocument.Parse(resString);

            var channel = json.RootElement.GetProperty("data").GetProperty("pubnub_channel").GetString();
            var token = json.RootElement.GetProperty("data").GetProperty("pubnub_token").GetString();

            return (channel!, token!);
        }

        private Pubnub GetPubNubAndAddListener(string channel, string token)
        {
            var config = new PNConfiguration(new UserId(channel))
            {
                SubscribeKey = bitbank_connection.publicKey,
                Secure = true,
            };

            var pubnub = new Pubnub(config);

            pubnub.AddListener(new SubscribeCallbackExt(
                (pubnubObj, messageResult) =>
                {
                    if (messageResult != null && messageResult.Message != null)
                    {
                        var json = JsonDocument.Parse(messageResult.Message.ToString());
                        if (json.RootElement.TryGetProperty("method", out var method))
                        {
                            var methodStr = method.GetString();
                            switch (methodStr)
                            {
                                //Orders
                                case "spot_order_new":
                                case "spot_order":
                                    var ord = json.RootElement.GetProperty("params").EnumerateArray();
                                    foreach (var d in ord)
                                    {
                                        this.orderQueue.Enqueue(d);
                                    }
                                    break;
                                case "spot_trade":
                                    var trd = json.RootElement.GetProperty("params").EnumerateArray();
                                    foreach (var d in trd)
                                    {
                                        this.fillQueue.Enqueue(d);
                                    }
                                    break;
                                case "spot_order_invalidation":
                                    break;
                                case "asset_update":
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                },
                (pubnubObj, presenceResult) =>
                {
                    this.addLog("presence: " + presenceResult.Event);
                },
                async (pubnubObj, status) =>
                {
                    switch (status.Category)
                    {
                        case PNStatusCategory.PNConnectedCategory:
                            this.addLog("pubnub connection established");
                            this.pubnub_state = WebSocketState.Open;
                            break;

                        case PNStatusCategory.PNReconnectedCategory:
                            this.addLog("pubnub connection restored");
                            this.subscribePrivateChannels(pubnubObj, channel, token);
                            this.pubnub_state = WebSocketState.Open;
                            break;

                        case PNStatusCategory.PNTimeoutCategory:
                        case PNStatusCategory.PNNetworkIssuesCategory:
                        case PNStatusCategory.PNAccessDeniedCategory:
                            this.pubnub_state = WebSocketState.Closed;
                            this.addLog("pubnub reconnecting...");
                            await connectPrivateAsync();
                            break;

                        default:
                            this.addLog("status default");
                            this.pubnub_state = WebSocketState.None;
                            break;
                    }
                }));

            return pubnub;
        }
        public async Task connectPrivateAsync()
        {
            var (channel, token) = await this.GetChannelAndToken();
            var pubnub = this.GetPubNubAndAddListener(channel, token);
            this.subscribePrivateChannels(pubnub, channel, token);
        }

        public WebSocketState GetSocketStatePublic()
        {
            return this.websocket_client.State;
        }
        public WebSocketState GetSocketStatePrivate()
        {
            return this.pubnub_state;
        }
        private string ToSha256(string key, string value)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        public void addLog(string line,Enums.logType logtype = Enums.logType.INFO)
        {
            this._addLog("[bitbank_connection]" + line, logtype);
        }

        private static bitbank_connection _instance;
        private static readonly object _lockObject = new object();

        public static bitbank_connection GetInstance()
        {
            lock (_lockObject)
            {
                if (_instance == null)
                {
                    _instance = new bitbank_connection();
                }
                return _instance;
            }
        }
    }
}
