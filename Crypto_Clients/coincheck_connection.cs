using PubnubApi;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XT.Net.Objects.Models;

namespace Crypto_Clients
{
    public class coincheck_connection
    {
        private string apiName;
        private string secretKey;

        private const string publicKey = "sub-c-ecebae8e-dd60-11e6-b6b1-02ee2ddab7fe";
        private const string URL = "https://coincheck.com";
        private const string ws_URL = "wss://ws-api.coincheck.com";
        private const string private_URL = "wss://stream.coincheck.com";

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

        private bool closeSentPublic;
        private bool closeSentPrivate;
        private List<string> subscribingChannels;

        private coincheck_connection()
        {
            this.apiName = "";
            this.secretKey = "";

            this.websocket_client = new ClientWebSocket();
            this.private_client = new ClientWebSocket();

            this.orderQueue = new ConcurrentQueue<JsonElement>();
            this.fillQueue = new ConcurrentQueue<JsonElement>();

            this.closeSentPublic = false;
            this.closeSentPrivate = false;
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
            this.addLog("INFO", "Connecting to coincehck");
            var uri = new Uri(coincheck_connection.ws_URL);
            try
            {
                await this.websocket_client.ConnectAsync(uri, CancellationToken.None);
                this.addLog("INFO", "Connected to coincheck.");
                this.closeSentPublic = false;
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
            this.addLog("INFO", "Connecting to private channel of coincheck");
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
                    var msg_body = Encoding.UTF8.GetString(buffer, 0, res.Count);
                    JsonElement js = JsonDocument.Parse(msg_body).RootElement;
                    if(js.GetProperty("success").GetBoolean() == false)
                    {
                        this.addLog("ERROR", "Failed to login to the private channel.");
                        this.addLog("ERROR", msg_body);
                        this.closeSentPrivate = false;
                        await this.disconnectPrivate();
                    }
                    else
                    {
                        this.closeSentPrivate = false;
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
        public async Task disconnectPublic()
        {
            if (this.closeSentPublic)
            {
                this.addLog("WARNING", "closeAsnyc for public API is already called.");
            }
            else
            {
                await this.websocket_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                this.closeSentPublic = true;
            }
        }

        public async Task disconnectPrivate()
        {
            if (this.closeSentPrivate)
            {
                this.addLog("WARNING", "closeAsnyc for private API is already called.");
            }
            else
            {
                await this.private_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                this.closeSentPrivate = true;
            }
        }

        public async Task subscribeTrades(string baseCcy, string quoteCcy)
        {
            string event_name = baseCcy.ToLower() + "_" + quoteCcy.ToLower() + "-trades";
            var subscribeJson = "{\"type\":\"subscribe\", \"channel\":\"" + event_name + "\"}";
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
            string event_name = baseCcy.ToLower() + "_" + quoteCcy.ToLower() + "-orderbook";
            var subscribeJson = "{\"type\":\"subscribe\", \"channel\":\"" + event_name + "\"}";
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
                    this.addLog("INFO", "Closed by server");
                    await this.websocket_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
            }
            this.addLog("INFO", "Check websocket state. State:" +  this.websocket_client.State.ToString());
        }
        public async Task<bool> onListen(Action<string> onMsg)
        {
            WebSocketReceiveResult result;
            if (this.websocket_client.State == WebSocketState.Open)
            {
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
                    if (this.websocket_client.State == WebSocketState.Open || this.websocket_client.State == WebSocketState.CloseReceived)
                    {
                        await this.disconnectPublic();
                    }
                    return false;
                }
                this.ws_memory.SetLength(0);
                this.ws_memory.Position = 0;
            }
            else
            {
                this.addLog("ERROR", "Public channel is closed. Check the status. State:" + this.websocket_client.State.ToString());
                if (this.websocket_client.State == WebSocketState.CloseReceived)
                {
                    await this.disconnectPublic();
                }
                return false;
            }
            return true;
        }

        public async Task subscribeOrderEvent()
        {
            var subscribeJson = "{\"type\":\"subscribe\", \"channels\":[\"order-events\"]}";
            var bytes = Encoding.UTF8.GetBytes(subscribeJson);
            if (this.private_client.State == WebSocketState.Open)
            {
                await this.private_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            string channel_name = "orderupdate";
            if (!this.subscribingChannels.Contains(channel_name))
            {
                this.subscribingChannels.Add(channel_name);
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
            string channel_name = "execution";
            if (!this.subscribingChannels.Contains(channel_name))
            {
                this.subscribingChannels.Add(channel_name);
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
        public async Task<bool> onListenPrivate(Action<string> onMsg)
        {
            WebSocketReceiveResult result;
            if (this.private_client.State == WebSocketState.Open)
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
                    if (this.private_client.State == WebSocketState.Open || this.private_client.State == WebSocketState.CloseReceived)
                    {
                        await this.disconnectPrivate();
                    }
                    return false;
                }
                this.pv_memory.SetLength(0);
                this.pv_memory.Position = 0;
            }
            else
            {
                this.addLog("ERROR", "Private channel is closed. Check the status. State:" + this.private_client.State.ToString());
                if (this.private_client.State == WebSocketState.CloseReceived)
                {
                    await this.disconnectPrivate();
                }
                return false;
            }
            return true;
        }

        private async Task<string> getAsync(string endpoint)
        {
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var message = $"{nonce}{coincheck_connection.URL}{endpoint}";

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, coincheck_connection.URL + endpoint);

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
            var message = $"{nonce}{coincheck_connection.URL}{endpoint}{body}";

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, coincheck_connection.URL + endpoint);

            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            request.Headers.Add("ACCESS-KEY", this.apiName);
            request.Headers.Add("ACCESS-NONCE", nonce.ToString());
            request.Headers.Add("ACCESS-SIGNATURE", ToSha256(this.secretKey, message));

            var response = await client.SendAsync(request);
            var resString = await response.Content.ReadAsStringAsync();

            return resString;
        }

        private async Task<string> deleteAsync(string endpoint,string body)
        {
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var message = $"{nonce}{coincheck_connection.URL}{endpoint}{body}";

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Delete, coincheck_connection.URL + endpoint);

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

        public WebSocketState GetSocketStatePublic()
        {
            return this.websocket_client.State;
        }
        public WebSocketState GetSocketStatePrivate()
        {
            return this.private_client.State;
        }

        private string ToSha256(string key, string value)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
        public void addLog(string logtype, string line)
        {
            this._addLog("[" + logtype + ":coincheck_connection]" + line);
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
