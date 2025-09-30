using ProtoBuf.WellKnownTypes;
using PubnubApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO.Compression;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Crypto_Clients
{
    public class bittrade_connection
    {
        private string apiName;
        private string secretKey;

        private const string URL = "https://api-cloud.bittrade.co.jp";
        private const string ws_URL = "wss://api-cloud.bittrade.co.jp/ws";
        private const string private_URL = "wss://api-cloud.bittrade.co.jp";
        private const string private_Path = "/ws/v2";

        public ConcurrentQueue<JsonElement> orderQueue;
        public ConcurrentQueue<JsonElement> fillQueue;

        ClientWebSocket websocket_client;
        ClientWebSocket private_client;

        public Action<string> onMessage;
        public Action<string> onPrivateMessage;
        public Action<string> _addLog;


        byte[] ws_buffer = new byte[16384];
        MemoryStream ws_memory = new MemoryStream();
        MemoryStream result_memory = new MemoryStream();

        byte[] pv_buffer = new byte[16384];
        MemoryStream pv_memory = new MemoryStream();
        MemoryStream pv_result_memory = new MemoryStream();

        public bool autoReconnecting;
        private List<string> subscribingChannels;

        private bittrade_connection()
        {
            this.apiName = "";
            this.secretKey = "";

            this.websocket_client = new ClientWebSocket();
            this.private_client = new ClientWebSocket();

            this.orderQueue = new ConcurrentQueue<JsonElement>();
            this.fillQueue = new ConcurrentQueue<JsonElement>();

            this.autoReconnecting = true;
            this.subscribingChannels = new List<string>();

            this._addLog = Console.WriteLine;
        }
        public void SetApiCredentials(string name, string key)
        {
            this.apiName = name;
            this.secretKey = key;
        }
        public async Task connectPublicAsync()
        {
            this.addLog("INFO", "Connecting to bitTrade");
            var uri = new Uri(bittrade_connection.ws_URL);
            try
            {
                await this.websocket_client.ConnectAsync(uri, CancellationToken.None);
                this.addLog("INFO", "Connected to bitTrade.");
            }
            catch (WebSocketException wse)
            {
                this.addLog("ERROR", $"WebSocketException: {wse.Message}");
            }
            catch (Exception ex)
            {
                this.addLog("ERROR", $"Connection failed: {ex.Message}");
            }
        }
        public async Task connectPrivateAsync()
        {
            this.addLog("INFO", "Connecting to private channel of bitTrade");
            var _timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            var uri = new Uri(bittrade_connection.private_URL + bittrade_connection.private_Path);

            var authParams = new NameValueCollection();
            authParams["accessKey"] = this.apiName;
            authParams["signatureMethod"] = "HmacSHA256";
            authParams["signatureVersion"] = "2.1";
            authParams["timestamp"] = _timestamp;

            string query = string.Join("&", Array.ConvertAll(authParams.AllKeys, key =>
                $"{key}={Uri.EscapeDataString(authParams[key])}"
            ));

            string s = $"GET\n{bittrade_connection.private_URL.Replace("wss://","")}\n{bittrade_connection.private_Path}\n{query}";

            string signature = this.ToHmacSha256Base64(s, this.secretKey);

            var Params = new AuthParams
            {
                authType = "api",
                accessKey = this.apiName,
                signatureMethod = "HmacSHA256",
                signatureVersion = "2.1",
                timestamp = _timestamp,
                signature = signature
            };

            string jsonBody = "{\"action\":\"req\",\"ch\":\"auth\",\"params\":" + JsonSerializer.Serialize(Params) + "}";

            //var jsonBody = JsonSerializer.Serialize(auth);
            
            var bytes = Encoding.UTF8.GetBytes(jsonBody);
            try
            {
                await this.private_client.ConnectAsync(uri, CancellationToken.None);
                await this.private_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                WebSocketReceiveResult res;
                var buffer = new byte[16384];
                using var ms = new MemoryStream();
                do
                {
                    res = await this.private_client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    ms.Write(buffer, 0, res.Count);

                } while (!res.EndOfMessage);
                //var res = await this.private_client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (res.MessageType == WebSocketMessageType.Text)
                {
                    var msg_body = Encoding.UTF8.GetString(buffer, 0, res.Count);
                    JsonElement js = JsonDocument.Parse(msg_body).RootElement;
                    if (js.GetProperty("code").GetInt32() != 200)
                    {
                        this.addLog("ERROR", "Failed to login to the private channel.");
                        this.addLog("ERROR", msg_body);
                        await this.private_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    }
                }
                else if (res.MessageType == WebSocketMessageType.Binary)
                {
                    ms.Position = 0;
                    using var gzipStream = new GZipStream(ms, CompressionMode.Decompress);
                    using var resultStream = new MemoryStream();
                    gzipStream.CopyTo(resultStream);
                    var msg = Encoding.UTF8.GetString(resultStream.ToArray());
                    this.addLog("ERROR", "Failed to login to the private channel.");
                    this.addLog("ERROR", msg);
                }
            }
            catch (WebSocketException wse)
            {
                this.addLog("ERROR", $"WebSocketException: {wse.Message}");
            }
            catch (Exception ex)
            {
                this.addLog("ERROR", $"Connection failed: {ex.Message}");
            }
        }
        public async Task sendPing(bool isPrivate = false)
        {
            if(isPrivate)
            {
                if (this.private_client.State == WebSocketState.Open)
                {
                    var subscribeJson = "{\"action\":\"ping\",\"data\":{\"ts\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "}}";
                    var bytes = Encoding.UTF8.GetBytes(subscribeJson);
                    await this.private_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            else
            {
                var subscribeJson = "{\"ping\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "}";
                var bytes = Encoding.UTF8.GetBytes(subscribeJson);
                if (this.websocket_client.State == WebSocketState.Open)
                {
                    await this.websocket_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            
        }
        public async Task sendPong(Int64 num,bool isPrivate = false)
        {
            if(isPrivate)
            {
                var subscribeJson = "{\"action\":\"pong\",\"data\":{\"ts\":" + num.ToString() + "}}";
                var bytes = Encoding.UTF8.GetBytes(subscribeJson);
                if (this.private_client.State == WebSocketState.Open)
                {
                    await this.private_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            else
            {
                var subscribeJson = "{\"pong\":" + num.ToString() + "}";
                var bytes = Encoding.UTF8.GetBytes(subscribeJson);
                if (this.websocket_client.State == WebSocketState.Open)
                {
                    await this.websocket_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }

        }
        public async Task subscribeTrades(string baseCcy, string quoteCcy)
        {
            string event_name = "market." + baseCcy.ToLower() + quoteCcy.ToLower() + ".trade.detail";
            var subscribeJson = "{\"sub\":\"" + event_name + "\", \"id\":\"idtrade\"}";
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
            string event_name = "market." + baseCcy.ToLower() + quoteCcy.ToLower() + ".depth.step0";
            var subscribeJson = "{\"sub\":\"" + event_name + "\", \"id\":\"idorderbook\"}";
            this.addLog("INFO",subscribeJson);
            var bytes = Encoding.UTF8.GetBytes(subscribeJson);
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
            while (true)
            {
                if(this.websocket_client.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result;
                    using var ms = new MemoryStream();
                    do
                    {
                        result = await this.websocket_client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        ms.Write(buffer, 0, result.Count);

                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {

                        var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        this.onMessage(msg);
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        ms.Position = 0;
                        using var gzipStream = new GZipStream(ms, CompressionMode.Decompress);
                        using var resultStream = new MemoryStream();
                        gzipStream.CopyTo(resultStream);
                        var msg = Encoding.UTF8.GetString(resultStream.ToArray());
                        this.onMessage(msg);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        this.addLog("INFO", "Closed by server");
                        await this.websocket_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    }
                }
                else
                {
                    this.addLog("ERROR", "Public channel is closed. Check the status. State:" + this.websocket_client.State.ToString());
                    Thread.Sleep(60000);
                }
            }
        }

        public async Task onListen(Action<string> onMsg)
        {
            if (this.websocket_client.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await this.websocket_client.ReceiveAsync(new ArraySegment<byte>(this.ws_buffer), CancellationToken.None);
                    this.ws_memory.Write(this.ws_buffer, 0, result.Count);

                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    this.ws_memory.Position = 0;
                    var msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
                    onMsg(msg);

                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    this.ws_memory.Position = 0;
                    using var gzipStream = new GZipStream(this.ws_memory, CompressionMode.Decompress, leaveOpen: true);
                    gzipStream.CopyTo(this.result_memory);
                    var msg = Encoding.UTF8.GetString(this.result_memory.ToArray());
                    onMsg(msg);
                    this.result_memory.SetLength(0);
                    this.result_memory.Position = 0;
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    this.addLog("INFO", "Closed by server");
                    await this.websocket_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    if (this.autoReconnecting)
                    {
                        this.addLog("INFO", "Reconnecting...");
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
                }
                this.ws_memory.SetLength(0);
                this.ws_memory.Position = 0;
            }
            else
            {
                this.addLog("ERROR", "Public channel is closed. Check the status. State:" + this.websocket_client.State.ToString());
                if (this.autoReconnecting)
                {
                    this.addLog("INFO", "Reconnecting...");
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
                else
                {
                    Thread.Sleep(10000);
                }
            }

        }

        public async Task subscribeOrderEvent(string baseCcy, string quoteCcy)
        {
            var subscribeJson = "{\"action\":\"sub\", \"ch\":\"orders#" + baseCcy + quoteCcy + "\"}";
            var bytes = Encoding.UTF8.GetBytes(subscribeJson);
            if(this.private_client.State == WebSocketState.Open)
            {
                await this.private_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        public async Task subscribeExecutionEvent()
        {
            var subscribeJson = "{\"type\":\"subscribe\", \"channels\":[\"execution-events\"]}";
            var bytes = Encoding.UTF8.GetBytes(subscribeJson);
            if (this.private_client.State == WebSocketState.Open)
            {
                await this.private_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        public async void startListenPrivate(Action<string> onMsg)
        {
            this.onPrivateMessage = onMsg;
            var buffer = new byte[16384];
            while (this.private_client.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                using var ms = new MemoryStream();
                do
                {
                    result = await this.private_client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    ms.Write(buffer, 0, result.Count);

                } while (!result.EndOfMessage);
                if (result.MessageType == WebSocketMessageType.Text)
                {

                    var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    this.onPrivateMessage(msg);
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    ms.Position = 0;
                    using var gzipStream = new GZipStream(ms, CompressionMode.Decompress);
                    using var resultStream = new MemoryStream();
                    gzipStream.CopyTo(resultStream);
                    var msg = Encoding.UTF8.GetString(resultStream.ToArray());
                    this.onMessage(msg);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    this.addLog("INFO", "Closed by server");
                    await this.private_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
            }
            this.addLog("INFO", "Check websocket state. State:" + this.private_client.State.ToString());
        }

        public async void onListenPrivate(Action<string> onMsg)
        {
            WebSocketReceiveResult result;
            if(this.private_client.State == WebSocketState.Open)
            {
                do
                {
                    result = await this.private_client.ReceiveAsync(new ArraySegment<byte>(this.pv_buffer), CancellationToken.None);
                    this.pv_memory.Write(this.pv_buffer, 0, result.Count);

                } while (!result.EndOfMessage);
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    this.pv_memory.Position = 0;
                    var msg = Encoding.UTF8.GetString(this.pv_memory.ToArray());
                    onMsg(msg);
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    this.pv_memory.Position = 0;
                    using var gzipStream = new GZipStream(this.pv_memory, CompressionMode.Decompress, leaveOpen: true);
                    gzipStream.CopyTo(this.pv_result_memory);
                    var msg = Encoding.UTF8.GetString(this.pv_result_memory.ToArray());
                    onMsg(msg);
                    this.pv_result_memory.SetLength(0);
                    this.pv_result_memory.Position = 0;
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    this.addLog("INFO", "Closed by server");
                    await this.private_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
                this.pv_memory.SetLength(0);
                this.pv_memory.Position = 0;
            }
            else
            {
                this.addLog("ERROR", "Private channel is closed. Check the status. State:" + this.private_client.State.ToString());
                Thread.Sleep(60000);
            }           
        }

        private async Task<string> getAsync(string endpoint)
        {
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var message = $"{nonce}{bittrade_connection.URL}{endpoint}";

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, bittrade_connection.URL + endpoint);

            request.Headers.Add("ACCESS-KEY", this.apiName);
            request.Headers.Add("ACCESS-NONCE", nonce.ToString());
            request.Headers.Add("ACCESS-SIGNATURE", ToSha256(this.secretKey, message));

            request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var resString = await response.Content.ReadAsStringAsync();

            return resString;
        }

        private async Task<string> postAsync(string endpoint, string body)
        {
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var message = $"{nonce}{bittrade_connection.URL}{endpoint}{body}";

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, bittrade_connection.URL + endpoint);

            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            request.Headers.Add("ACCESS-KEY", this.apiName);
            request.Headers.Add("ACCESS-NONCE", nonce.ToString());
            request.Headers.Add("ACCESS-SIGNATURE", ToSha256(this.secretKey, message));

            var response = await client.SendAsync(request);
            var resString = await response.Content.ReadAsStringAsync();

            return resString;
        }

        private async Task<string> deleteAsync(string endpoint, string body)
        {
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var message = $"{nonce}{bittrade_connection.URL}{endpoint}{body}";

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Delete, bittrade_connection.URL + endpoint);

            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            request.Headers.Add("ACCESS-KEY", this.apiName);
            request.Headers.Add("ACCESS-NONCE", nonce.ToString());
            request.Headers.Add("ACCESS-SIGNATURE", ToSha256(this.secretKey, message));

            var response = await client.SendAsync(request);
            var resString = await response.Content.ReadAsStringAsync();

            return resString;
        }
        public async Task<JsonDocument> getBalance()
        {
            var resString = await this.getAsync("/api/accounts/balance");
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> placeNewOrder(string symbol, string side, decimal price = 0, decimal quantity = 0, string tif = "good_til_cancelled")
        {
            var body = new
            {
                pair = symbol,
                amount = quantity.ToString(),
                rate = price.ToString(),
                order_type = side,
                time_in_force = tif
            };


            var jsonBody = JsonSerializer.Serialize(body);
            var resString = await this.postAsync("/api/exchange/orders", jsonBody);

            return JsonDocument.Parse(resString);
        }
        public async Task<JsonDocument> placeCanOrder(string order_id)
        {
            var resString = await this.deleteAsync("/api/exchange/orders/" + order_id, "");

            return JsonDocument.Parse(resString);
        }



        private string ToSha256(string key, string value)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
        private string ToHmacSha256Base64(string message, string secret)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
                return Convert.ToBase64String(hash); 
            }
        }
        public void addLog(string logtype, string line)
        {
            this._addLog("[" + logtype + ":bittrade_connection]" + line);
        }

        private static bittrade_connection _instance;
        private static readonly object _lockObject = new object();

        public static bittrade_connection GetInstance()
        {
            lock (_lockObject)
            {
                if (_instance == null)
                {
                    _instance = new bittrade_connection();
                }
                return _instance;
            }
        }
    }
    public class AuthJson
    {
        public string action { get; set; }
        public string ch { get; set; }
        public AuthParams Params { get; set; }
    }

    public class AuthParams
    {
        public string authType { get; set; }
        public string accessKey { get; set; }
        public string signatureMethod { get; set; }
        public string signatureVersion { get; set; }
        public string timestamp { get; set; }
        public string signature { get; set; }
    }
}
