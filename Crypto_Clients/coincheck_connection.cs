using PubnubApi;
using System.Collections.Concurrent;
using System.Drawing;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XT.Net.Objects.Models;

namespace Crypto_Clients
{
    internal class coincheck_connection
    {
        private string apiName;
        private string secretKey;

        private const string publicKey = "sub-c-ecebae8e-dd60-11e6-b6b1-02ee2ddab7fe";
        private const string URL = "";
        private const string ws_URL = "wss://ws-api.coincheck.com";
        private const string private_URL = "wss://stream.coincheck.com";

        public ConcurrentQueue<JsonElement> orderQueue;
        public ConcurrentQueue<JsonElement> fillQueue;

        ClientWebSocket websocket_client;
        ClientWebSocket private_client;

        public Action<string> onMessage;
        public Action<string> onPrivateMessage;
        public Action<string> addLog;

        private coincheck_connection()
        {
            this.apiName = "";
            this.secretKey = "";

            this.websocket_client = new ClientWebSocket();
            this.private_client = new ClientWebSocket();

            this.orderQueue = new ConcurrentQueue<JsonElement>();
            this.fillQueue = new ConcurrentQueue<JsonElement>();

            this.addLog = Console.WriteLine;
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
            this.addLog("Connecting to coincehck");
            var uri = new Uri(coincheck_connection.ws_URL);
            try
            {
                await this.websocket_client.ConnectAsync(uri, CancellationToken.None);
                this.addLog("[INFO] Connected to coincheck.");
            }
            catch (WebSocketException wse)
            {
                this.addLog($"WebSocketException: {wse.Message}");
            }
            catch (Exception ex)
            {
                this.addLog($"Connection failed: {ex.Message}");
            }
        }
        public async Task connectPrivateAsync()
        {
            this.addLog("Connecting to private channel of coincheck");
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var uri = new Uri(coincheck_connection.private_URL);
            string msg = nonce.ToString() + coincheck_connection.private_URL + "/private"; 
            var body = new
            {
                type = "login",
                access_key = this.apiName,
                access_nonce = nonce.ToString(),
                access_signature = this.ToSha256(this.secretKey,msg)

            };
            var jsonBody = JsonSerializer.Serialize(body);
            var bytes = Encoding.UTF8.GetBytes(jsonBody);
            try
            {
                await this.private_client.ConnectAsync(uri, CancellationToken.None);
                await this.private_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                var buffer = new byte[16384];
                var res = await this.private_client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if(res.MessageType == WebSocketMessageType.Text)
                {
                    this.addLog(Encoding.UTF8.GetString(buffer, 0, res.Count));
                }
            }
            catch (WebSocketException wse)
            {
                this.addLog($"WebSocketException: {wse.Message}");
            }
            catch (Exception ex)
            {
                this.addLog($"Connection failed: {ex.Message}");
            }
            //var (channel, token) = await this.GetChannelAndToken();
            //var pubnub = this.GetPubNubAndAddListener(channel, token);
            //this.subscribePrivateChannels(pubnub, channel, token);
        }

        public async Task subscribeTrades(string baseCcy, string quoteCcy)
        {
            string event_name = baseCcy.ToLower() + "_" + quoteCcy.ToLower() + "-trades";
            var subscribeJson = "{\"type\":\"subscribe\", \"channel\":\"" + event_name + "\"}";
            this.addLog(subscribeJson);
            var bytes = Encoding.UTF8.GetBytes(subscribeJson);
            await this.websocket_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public async Task subscribeOrderBook(string baseCcy, string quoteCcy)
        {
            string event_name = baseCcy.ToLower() + "_" + quoteCcy.ToLower() + "-orderbook";
            var subscribeJson = "{\"type\":\"subscribe\", \"channel\":\"" + event_name + "\"}";
            this.addLog(subscribeJson);
            var bytes = Encoding.UTF8.GetBytes(subscribeJson);
            await this.websocket_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public async void startListen(Action<string> onMsg)
        {
            this.addLog("[coincheck_connection]startListen Called");
            this.onMessage = onMsg;
            var buffer = new byte[16384];
            while (this.websocket_client.State == WebSocketState.Open)
            {
                var result = await this.websocket_client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Text)
                {

                    var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    this.onMessage(msg);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    this.addLog("Closed by server");
                    await this.websocket_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
            }
            this.addLog("Check websocket state. State:" +  this.websocket_client.State.ToString());
        }

        public async void startListenPrivate(Action<string> onMsg)
        {
            this.addLog("[coincheck_connection]startListenPrivate Called");
            this.onPrivateMessage = onMsg;
            var buffer = new byte[16384];
            while (this.private_client.State == WebSocketState.Open)
            {
                var result = await this.private_client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Text)
                {

                    var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    this.onMessage(msg);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    this.addLog("Closed by server");
                    await this.private_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
            }
            this.addLog("Check websocket state. State:" + this.private_client.State.ToString());
        }

        private async Task<string> getAsync(string endpoint)
        {
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timeWindow = "5000";
            var message = $"{nonce}{timeWindow}{endpoint}";

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, coincheck_connection.URL + endpoint);

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
            var request = new HttpRequestMessage(HttpMethod.Post, coincheck_connection.URL + endpoint);

            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            request.Headers.Add("ACCESS-KEY", this.apiName);
            request.Headers.Add("ACCESS-REQUEST-TIME", nonce.ToString());
            request.Headers.Add("ACCESS-TIME-WINDOW", timeWindow);
            request.Headers.Add("ACCESS-SIGNATURE", ToSha256(this.secretKey, message));

            var response = await client.SendAsync(request);
            var resString = await response.Content.ReadAsStringAsync();

            return resString;
        }

        public async Task<JsonDocument> placeNewOrder(string symbol, string ord_type, string side, decimal price = 0, decimal quantity = 0, bool postonly = false)
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
        public async Task<JsonDocument> placeCanOrder(string symbol, string order_id)
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

        //private async Task<(string channel, string token)> GetChannelAndToken()
        //{
        //    var resString = await this.getAsync("/v1/user/subscribe");

        //    var json = JsonDocument.Parse(resString);

        //    var channel = json.RootElement.GetProperty("data").GetProperty("pubnub_channel").GetString();
        //    var token = json.RootElement.GetProperty("data").GetProperty("pubnub_token").GetString();

        //    return (channel!, token!);
        //}

        //private Pubnub GetPubNubAndAddListener(string channel, string token)
        //{
        //    var config = new PNConfiguration(new UserId(channel))
        //    {
        //        SubscribeKey = bitbank_connection.publicKey,
        //        Secure = true,
        //    };

        //    var pubnub = new Pubnub(config);

        //    pubnub.AddListener(new SubscribeCallbackExt(
        //        (pubnubObj, messageResult) =>
        //        {
        //            if (messageResult != null && messageResult.Message != null)
        //            {
        //                this.addLog("message received: " + messageResult.Message.ToString());
        //                var json = JsonDocument.Parse(messageResult.Message.ToString());
        //                if (json.RootElement.TryGetProperty("method", out var method))
        //                {
        //                    var methodStr = method.GetString();
        //                    switch (methodStr)
        //                    {
        //                        //Orders
        //                        case "spot_order_new":
        //                        case "spot_order":
        //                            var ord = json.RootElement.GetProperty("params").EnumerateArray();
        //                            foreach (var d in ord)
        //                            {
        //                                this.orderQueue.Enqueue(d);
        //                            }
        //                            break;
        //                        case "spot_trade":
        //                            var trd = json.RootElement.GetProperty("params").EnumerateArray();
        //                            foreach (var d in trd)
        //                            {
        //                                this.fillQueue.Enqueue(d);
        //                            }
        //                            break;
        //                        case "spot_order_invalidation":
        //                            break;
        //                        case "asset_update":
        //                            break;
        //                        default:
        //                            break;
        //                    }
        //                }
        //            }
        //        },
        //        (pubnubObj, presenceResult) =>
        //        {
        //            this.addLog("presence: " + presenceResult.Event);
        //        },
        //        async (pubnubObj, status) =>
        //        {
        //            this.addLog("status: " + status.Category);

        //            switch (status.Category)
        //            {
        //                case PNStatusCategory.PNConnectedCategory:
        //                    this.addLog("pubnub connection established");
        //                    break;

        //                case PNStatusCategory.PNReconnectedCategory:
        //                    this.addLog("pubnub connection restored");
        //                    this.subscribePrivateChannels(pubnubObj, channel, token);
        //                    break;

        //                case PNStatusCategory.PNTimeoutCategory:
        //                case PNStatusCategory.PNNetworkIssuesCategory:
        //                case PNStatusCategory.PNAccessDeniedCategory:
        //                    this.addLog("pubnub reconnecting...");
        //                    await connectPrivateAsync();
        //                    break;

        //                default:
        //                    this.addLog("status default");
        //                    break;
        //            }
        //        }));

        //    return pubnub;
        //}
 
        private string ToSha256(string key, string value)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        private static coincheck_connection _instance;
        private static readonly object _lockObject = new object();

        public static coincheck_connection GetInstance()
        {
            lock (_lockObject)
            {
                if (_instance == null)
                {
                    _instance = new coincheck_connection();
                }
                return _instance;
            }
        }
    }
}
