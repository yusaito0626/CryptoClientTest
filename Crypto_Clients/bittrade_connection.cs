using PubnubApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net.WebSockets;
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

        private const string publicKey = "sub-c-ecebae8e-dd60-11e6-b6b1-02ee2ddab7fe";
        private const string URL = "https://api-cloud.bittrade.co.jp";
        private const string ws_URL = "wss://api-cloud.bittrade.co.jp/ws";
        private const string private_URL = "wss://api-cloud.bittrade.co.jp/ws/v2";

        public ConcurrentQueue<JsonElement> orderQueue;
        public ConcurrentQueue<JsonElement> fillQueue;

        ClientWebSocket websocket_client;
        ClientWebSocket private_client;

        public Action<string> onMessage;
        public Action<string> onPrivateMessage;
        public Action<string> _addLog;

        private bittrade_connection()
        {
            this.apiName = "";
            this.secretKey = "";

            this.websocket_client = new ClientWebSocket();
            this.private_client = new ClientWebSocket();

            this.orderQueue = new ConcurrentQueue<JsonElement>();
            this.fillQueue = new ConcurrentQueue<JsonElement>();

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
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var uri = new Uri(bittrade_connection.private_URL);
            string msg = nonce.ToString() + bittrade_connection.private_URL;
            var body = new
            {
                type = "login",
                access_key = this.apiName,
                access_nonce = nonce.ToString(),
                access_signature = this.ToSha256(this.secretKey, msg)

            };
            var jsonBody = JsonSerializer.Serialize(body);
            var bytes = Encoding.UTF8.GetBytes(jsonBody);
            try
            {
                await this.private_client.ConnectAsync(uri, CancellationToken.None);
                await this.private_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                var buffer = new byte[16384];
                var res = await this.private_client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (res.MessageType == WebSocketMessageType.Text)
                {
                    var msg_body = Encoding.UTF8.GetString(buffer, 0, res.Count);
                    JsonElement js = JsonDocument.Parse(msg_body).RootElement;
                    if (js.GetProperty("success").GetBoolean() == false)
                    {
                        this.addLog("ERROR", "Failed to login to the private channel.");
                        this.addLog("ERROR", msg_body);
                        await this.private_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    }
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
        public async Task sendPing()
        {
            var subscribeJson = "{\"ping\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + "}";
            var bytes = Encoding.UTF8.GetBytes(subscribeJson);
            if (this.websocket_client.State == WebSocketState.Open)
            {
                await this.websocket_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        public async Task sendPong(Int64 num)
        {
            var subscribeJson = "{\"pong\":" + num.ToString() + "}";
            var bytes = Encoding.UTF8.GetBytes(subscribeJson);
            if (this.websocket_client.State == WebSocketState.Open)
            {
                await this.websocket_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
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
        }

        public async void startListen(Action<string> onMsg)
        {
            this.onMessage = onMsg;
            var buffer = new byte[16384];
            while (this.websocket_client.State == WebSocketState.Open)
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
                else if(result.MessageType == WebSocketMessageType.Binary)
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
            this.addLog("INFO", "Check websocket state. State:" + this.websocket_client.State.ToString());
        }

        public async Task subscribeOrderEvent()
        {
            var subscribeJson = "{\"type\":\"subscribe\", \"channels\":[\"order-events\"]}";
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
                var result = await this.private_client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Text)
                {

                    var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    this.onPrivateMessage(msg);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    this.addLog("INFO", "Closed by server");
                    await this.private_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
            }
            this.addLog("INFO", "Check websocket state. State:" + this.private_client.State.ToString());
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
}
