using ProtoBuf.WellKnownTypes;
using PubnubApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace Crypto_Clients
{
    public class bittrade_connection
    {
        private string apiName;
        private string secretKey;

        private long accountId;

        private const string URL = "https://api-cloud.bittrade.co.jp";
        private const string ws_URL = "wss://api-cloud.bittrade.co.jp/ws";
        private const string private_URL = "wss://api-cloud.bittrade.co.jp";
        private const string private_Path = "/ws/v2";

        

        public ConcurrentQueue<JsonElement> orderQueue;
        public ConcurrentQueue<JsonElement> fillQueue;

        ClientWebSocket websocket_client;
        ClientWebSocket private_client;
        HttpClient http_client;

        private static readonly SocketsHttpHandler _handler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromHours(1),

            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),

            KeepAlivePingDelay = TimeSpan.FromMinutes(1),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always
        };

        public Action<string> onMessage;
        public Action<string> onPrivateMessage;
        public Action<string,Enums.logType> _addLog;


        byte[] ws_buffer = new byte[16384];
        MemoryStream ws_memory = new MemoryStream();
        MemoryStream result_memory = new MemoryStream();

        byte[] pv_buffer = new byte[16384];
        MemoryStream pv_memory = new MemoryStream();
        MemoryStream pv_result_memory = new MemoryStream();

        private bool closeSentPublic;
        private bool closeSentPrivate;
        private List<string> subscribingChannels;


        public bool logging;
        public StreamWriter logFilePublic;
        public StreamWriter logFilePrivate;

        Stopwatch sw_POST;
        double elapsedTime_POST;
        int count;
        Stopwatch sw_Private;
        Stopwatch sw_Public;

        private bittrade_connection()
        {
            this.apiName = "";
            this.secretKey = "";
            this.accountId = -1;

            this.websocket_client = new ClientWebSocket();
            this.private_client = new ClientWebSocket();
            this.http_client = new HttpClient(_handler)
            {
                BaseAddress = new Uri(URL)
            };

            this.orderQueue = new ConcurrentQueue<JsonElement>();
            this.fillQueue = new ConcurrentQueue<JsonElement>();

            this.closeSentPublic = false;
            this.closeSentPrivate = false;
            this.subscribingChannels = new List<string>();

            this.sw_POST = new Stopwatch();
            this.elapsedTime_POST = 0;
            this.count = 0;
            this.sw_Private = new Stopwatch();
            this.sw_Public = new Stopwatch();

            //this._addLog = Console.WriteLine;
        }
        public void setLogFile(string path)
        {
            this.logging = true;
            FileStream fspub = new FileStream(path + "\\bittradePublic_log" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".txt", FileMode.Append, FileAccess.Write, FileShare.Read);
            this.logFilePublic = new StreamWriter(fspub);
            FileStream fspri = new FileStream(path + "\\bittradePrivate_log" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".txt", FileMode.Append, FileAccess.Write, FileShare.Read);
            this.logFilePrivate = new StreamWriter(fspri);
        }
        public void SetApiCredentials(string name, string key)
        {
            this.apiName = name;
            this.secretKey = key;
        }
        public async Task connectPublicAsync()
        {
            this.addLog("Connecting to bitTrade");

            this.ws_memory.SetLength(0);
            this.ws_memory.Position = 0;
            this.websocket_client = new ClientWebSocket();
            var uri = new Uri(bittrade_connection.ws_URL);
            try
            {
                this.websocket_client.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await this.websocket_client.ConnectAsync(uri, CancellationToken.None);
                this.addLog("Connected to bitTrade.");
                this.closeSentPublic = false;
            }
            catch (WebSocketException wse)
            {
                this.addLog($"WebSocketException: {wse.Message}",Enums.logType.ERROR);
            }
            catch (Exception ex)
            {
                this.addLog($"Connection failed: {ex.Message}",Enums.logType.ERROR);
            }
        }
        public async Task connectPrivateAsync()
        {
            this.addLog("Connecting to private channel of bitTrade");

            this.pv_memory.SetLength(0);
            this.pv_memory.Position = 0;
            this.private_client = new ClientWebSocket();
            var acc = await this.getAccount();

            if(acc.RootElement.GetProperty("status").GetString() == "ok")
            {
                foreach(var item in acc.RootElement.GetProperty("data").EnumerateArray())
                {
                    if(item.GetProperty("type").GetString() == "spot")
                    {
                        this.accountId = item.GetProperty("id").GetInt64();
                        break;
                    }
                }
                
            }

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

            string signature = this.ToHmacSha256Base64(this.secretKey,s);

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
                this.private_client.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
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
                        this.addLog("Failed to login to the private channel.",Enums.logType.ERROR);
                        this.addLog(msg_body, Enums.logType.ERROR);
                        this.closeSentPrivate = false;
                        await this.disconnectPrivate();
                    }
                    else
                    {
                        this.closeSentPrivate = false;
                    }
                }
                else if (res.MessageType == WebSocketMessageType.Binary)
                {
                    ms.Position = 0;
                    using var gzipStream = new GZipStream(ms, CompressionMode.Decompress);
                    using var resultStream = new MemoryStream();
                    gzipStream.CopyTo(resultStream);
                    var msg = Encoding.UTF8.GetString(resultStream.ToArray());
                    JsonElement js = JsonDocument.Parse(msg).RootElement;
                    if (js.GetProperty("code").GetInt32() != 200)
                    {
                        this.addLog("Failed to login to the private channel.", Enums.logType.ERROR);
                        this.addLog(msg, Enums.logType.ERROR);
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
                this.addLog($"WebSocketException: {wse.Message}", Enums.logType.ERROR);
            }
            catch (Exception ex)
            {
                this.addLog($"Connection failed: {ex.Message}", Enums.logType.ERROR);
            }
        }

        public async Task disconnectPublic()
        {
            if (this.closeSentPublic)
            {
                this.addLog("closeAsnyc for public API is already called.",Enums.logType.WARNING);
            }
            else
            {
                this.closeSentPublic = true;
                await this.websocket_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
        }

        public async Task disconnectPrivate()
        {
            if (this.closeSentPrivate)
            {
                this.addLog("closeAsnyc for private API is already called.",Enums.logType.WARNING);
            }
            else
            {
                this.closeSentPrivate = true;
                await this.private_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
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

        //public async void startListen(Action<string> onMsg)
        //{
        //    this.onMessage = onMsg;
        //    var buffer = new byte[16384];
        //    while (true)
        //    {
        //        if(this.websocket_client.State == WebSocketState.Open)
        //        {
        //            WebSocketReceiveResult result;
        //            using var ms = new MemoryStream();
        //            do
        //            {
        //                result = await this.websocket_client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        //                ms.Write(buffer, 0, result.Count);

        //            } while (!result.EndOfMessage);

        //            if (result.MessageType == WebSocketMessageType.Text)
        //            {

        //                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
        //                this.onMessage(msg);
        //            }
        //            else if (result.MessageType == WebSocketMessageType.Binary)
        //            {
        //                ms.Position = 0;
        //                using var gzipStream = new GZipStream(ms, CompressionMode.Decompress);
        //                using var resultStream = new MemoryStream();
        //                gzipStream.CopyTo(resultStream);
        //                var msg = Encoding.UTF8.GetString(resultStream.ToArray());
        //                this.onMessage(msg);
        //            }
        //            else if (result.MessageType == WebSocketMessageType.Close)
        //            {
        //                this.addLog("Closed by server");
        //                await this.websocket_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        //            }
        //        }
        //        else
        //        {
        //            this.addLog("Public channel is closed. Check the status. State:" + this.websocket_client.State.ToString(),Enums.logType.ERROR);
        //            //Thread.Sleep(60000);
        //        }
        //    }
        //}

        public async Task onClosing(Action<string> onMsg)
        {
            if (this.websocket_client.State == WebSocketState.Aborted || this.websocket_client.State == WebSocketState.Closed)
            {
                this.websocket_client.Dispose();
                return;
            }
            this.addLog("onClosing Called.");

            await this.websocket_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            DateTime time_ClosingCalled = DateTime.UtcNow;
            DateTime currentTime = DateTime.UtcNow;
            WebSocketReceiveResult result;
            var buffer = new byte[16384];
            string msg = "";
            while (this.websocket_client.State != WebSocketState.Closed)
            {
                this.ws_memory.SetLength(0);
                this.ws_memory.Position = 0;
                currentTime = DateTime.UtcNow;
                if (currentTime - time_ClosingCalled > TimeSpan.FromSeconds(10))
                {
                    this.addLog("onClosing timeout. Aborting", Enums.logType.ERROR);
                    this.websocket_client.Abort();
                    this.websocket_client.Dispose();
                    return;
                }
                do
                {
                    result = await this.websocket_client.ReceiveAsync(new ArraySegment<byte>(this.ws_buffer), CancellationToken.None);
                    this.ws_memory.Write(this.ws_buffer, 0, result.Count);
                    currentTime = DateTime.UtcNow;
                } while ((!result.EndOfMessage) && this.websocket_client.State != WebSocketState.Aborted && this.websocket_client.State != WebSocketState.Closed);

                switch (result.MessageType)
                {
                    case WebSocketMessageType.Text:
                        this.addLog("Text message is not expected", Enums.logType.WARNING);
                        msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
                        this.addLog(msg, Enums.logType.WARNING);
                        break;
                    case WebSocketMessageType.Binary:
                        this.ws_memory.Position = 0;
                        using (var gzipStream = new GZipStream(this.ws_memory, CompressionMode.Decompress, leaveOpen: true))
                        {
                            gzipStream.CopyTo(this.result_memory);
                        }
                        msg = Encoding.UTF8.GetString(this.result_memory.ToArray());
                        onMsg(msg);
                        this.result_memory.SetLength(0);
                        this.result_memory.Position = 0;
                        break;
                    case WebSocketMessageType.Close:
                        this.addLog("Closed by server");
                        msg = "Closing message[onClosing]:" + Encoding.UTF8.GetString(this.ws_memory.ToArray());
                        break;
                }
                if(this.logging)
                {
                    this.logFilePublic.WriteLine(DateTime.UtcNow.ToString() + "   " + msg);
                    this.logFilePublic.Flush();
                }
            }

            this.ws_memory.SetLength(0);
            this.ws_memory.Position = 0;
            this.websocket_client.Dispose();
        }
        public void onListenOnError()
        {
            this.ws_memory.SetLength(0);
            this.ws_memory.Position = 0;
            this.websocket_client.Dispose();
            if (this.logging)
            {
                this.logFilePublic.Flush();
            }
        }
        public async Task<(bool,double)> onListen(Action<string> onMsg)
        {
            WebSocketReceiveResult result;
            string msg = "";
            bool output = true;
            double latency = 0;
            switch (this.websocket_client.State)
            {
                case WebSocketState.Open:
                    do
                    {
                        result = await this.websocket_client.ReceiveAsync(new ArraySegment<byte>(this.ws_buffer), CancellationToken.None);
                        this.ws_memory.Write(this.ws_buffer, 0, result.Count);

                    } while ((!result.EndOfMessage) && this.websocket_client.State != WebSocketState.Aborted && this.websocket_client.State != WebSocketState.Closed);
                    this.sw_Public.Start();
                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Text:
                            this.addLog("Text message is not expected", Enums.logType.WARNING);
                            msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
                            this.addLog(msg,Enums.logType.WARNING);
                            break;
                        case WebSocketMessageType.Binary:
                            this.ws_memory.Position = 0;
                            using (var gzipStream = new GZipStream(this.ws_memory, CompressionMode.Decompress, leaveOpen: true))
                            {
                                gzipStream.CopyTo(this.result_memory);
                            }
                            msg = Encoding.UTF8.GetString(this.result_memory.ToArray());
                            onMsg(msg);
                            this.result_memory.SetLength(0);
                            this.result_memory.Position = 0;
                            break;
                        case WebSocketMessageType.Close:
                            this.addLog("Closed by server");
                            output = false;
                            msg = "Closing message[onListen]:" + Encoding.UTF8.GetString(this.ws_memory.ToArray());
                            break;
                    }
                    if(this.logging)
                    {
                        this.logFilePublic.WriteLine(DateTime.UtcNow.ToString() + "   " + msg);
                        //this.logFilePublic.Flush();
                    }
                    this.ws_memory.SetLength(0);
                    this.ws_memory.Position = 0;
                    this.sw_Public.Stop();
                    latency = this.sw_Public.Elapsed.TotalNanoseconds / 1000;
                    this.sw_Public.Reset();
                    break;
                case WebSocketState.None:
                case WebSocketState.Connecting:
                    //Do nothing
                    break;
                case WebSocketState.CloseReceived:
                case WebSocketState.CloseSent:
                case WebSocketState.Closed:
                case WebSocketState.Aborted:
                default:
                    output = false;
                    break;
            }
            return (output,latency);

            //if (this.websocket_client.State == WebSocketState.Open)
            //{
            //    WebSocketReceiveResult result;
            //    do
            //    {
            //        result = await this.websocket_client.ReceiveAsync(new ArraySegment<byte>(this.ws_buffer), CancellationToken.None);
            //        this.ws_memory.Write(this.ws_buffer, 0, result.Count);

            //    } while (!result.EndOfMessage);

            //    if (result.MessageType == WebSocketMessageType.Text)
            //    {
            //        var msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
            //        onMsg(msg);
            //        this.logFilePublic.WriteLine(DateTime.UtcNow.ToString() + "   " + msg);
            //        this.logFilePublic.Flush();

            //    }
            //    else if (result.MessageType == WebSocketMessageType.Binary)
            //    {
            //        this.ws_memory.Position = 0;
            //        using var gzipStream = new GZipStream(this.ws_memory, CompressionMode.Decompress, leaveOpen: true);
            //        gzipStream.CopyTo(this.result_memory);
            //        var msg = Encoding.UTF8.GetString(this.result_memory.ToArray());
            //        onMsg(msg);
            //        this.logFilePublic.WriteLine(DateTime.UtcNow.ToString() + "   " + msg);
            //        this.logFilePublic.Flush();
            //        this.result_memory.SetLength(0);
            //        this.result_memory.Position = 0;
            //    }
            //    else if (result.MessageType == WebSocketMessageType.Close)
            //    {
            //        this.addLog("Closed by server");
            //        if (this.websocket_client.State == WebSocketState.Open || this.websocket_client.State == WebSocketState.CloseReceived)
            //        {
            //            await this.disconnectPublic();
            //        }
            //        var msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
            //        this.logFilePublic.WriteLine(DateTime.UtcNow.ToString() + "   " + msg);
            //        this.logFilePublic.Flush();
            //        return false;
            //    }
            //    this.ws_memory.SetLength(0);
            //    this.ws_memory.Position = 0;
            //}
            //else
            //{
            //    this.addLog("Public channel is closed. Check the status. State:" + this.websocket_client.State.ToString(),Enums.logType.ERROR);
            //    if (this.websocket_client.State == WebSocketState.CloseReceived)
            //    {
            //        await this.disconnectPublic();
            //    }
            //    return false;
            //}
            //return true;
        }

        public async Task subscribeOrderEvent()
        {
            var subscribeJson = "{\"action\":\"sub\", \"ch\":\"orders#*\"}";
            var bytes = Encoding.UTF8.GetBytes(subscribeJson);
            if(this.private_client.State == WebSocketState.Open)
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
            var subscribeJson = "{\"action\":\"sub\", \"ch\":\"trade.clearing#*#0\"}";
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
                    this.addLog("Closed by server");
                    await this.private_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
            }
            this.addLog("Check websocket state. State:" + this.private_client.State.ToString());
        }
        public async Task onClosingPrivate(Action<string> onMsg)
        {
            if (this.private_client.State == WebSocketState.Aborted || this.private_client.State == WebSocketState.Closed)
            {
                this.private_client.Dispose();
                return;
            }
            this.addLog("onClosing Called.");

            await this.private_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            DateTime time_ClosingCalled = DateTime.UtcNow;
            DateTime currentTime = DateTime.UtcNow;
            WebSocketReceiveResult result;
            var buffer = new byte[16384];
            string msg = "";
            while (this.private_client.State != WebSocketState.Closed)
            {
                this.pv_memory.SetLength(0);
                this.pv_memory.Position = 0;
                currentTime = DateTime.UtcNow;
                if (currentTime - time_ClosingCalled > TimeSpan.FromSeconds(10))
                {
                    this.addLog("onClosing timeout. Aborting", Enums.logType.ERROR);
                    this.private_client.Abort();
                    this.private_client.Dispose();
                    return;
                }
                do
                {
                    result = await this.private_client.ReceiveAsync(new ArraySegment<byte>(this.pv_buffer), CancellationToken.None);
                    this.pv_memory.Write(this.pv_buffer, 0, result.Count);

                } while (!result.EndOfMessage && this.private_client.State != WebSocketState.Aborted && this.private_client.State != WebSocketState.Closed);
                switch (result.MessageType)
                {
                    case WebSocketMessageType.Text:
                        this.addLog("Text message is not expected.", Enums.logType.WARNING);
                        msg = Encoding.UTF8.GetString(this.pv_memory.ToArray());
                        this.addLog(msg, Enums.logType.WARNING);
                        break;
                    case WebSocketMessageType.Binary:
                        this.pv_memory.Position = 0;
                        using (var gzipStream = new GZipStream(this.pv_memory, CompressionMode.Decompress, leaveOpen: true))
                        {
                            gzipStream.CopyTo(this.pv_result_memory);
                        }
                        msg = Encoding.UTF8.GetString(this.pv_result_memory.ToArray());
                        onMsg(msg);
                        this.pv_result_memory.SetLength(0);
                        this.pv_result_memory.Position = 0;
                        break;
                    case WebSocketMessageType.Close:
                        this.addLog("Closed by server");
                        msg = "Closing message[onClosing]:" + Encoding.UTF8.GetString(this.pv_memory.ToArray());
                        break;
                }
                if(this.logging)
                {
                    this.logFilePrivate.WriteLine(DateTime.UtcNow.ToString() + "   " + msg);
                    this.logFilePrivate.Flush();
                }
            }
            this.pv_memory.SetLength(0);
            this.pv_memory.Position = 0;
            this.private_client.Dispose();
        }

        public void onListenPrivateOnError()
        {
            this.pv_memory.SetLength(0);
            this.pv_memory.Position = 0;
            this.private_client.Dispose();
            if (this.logging)
            {
                this.logFilePrivate.Flush();
            }
        }

        public async Task<(bool,double)> onListenPrivate(Action<string> onMsg)
        {
            WebSocketReceiveResult result;
            string msg = "";
            bool output = true;
            double latency = 0;
            switch (this.private_client.State)
            {
                case WebSocketState.Open:
                    do
                    {
                        result = await this.private_client.ReceiveAsync(new ArraySegment<byte>(this.pv_buffer), CancellationToken.None);
                        this.pv_memory.Write(this.pv_buffer, 0, result.Count);

                    } while (!result.EndOfMessage && this.private_client.State != WebSocketState.Aborted && this.private_client.State != WebSocketState.Closed);
                    this.sw_Private.Start();
                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Text:
                            this.addLog("Text message is not expected.", Enums.logType.WARNING);
                            msg = Encoding.UTF8.GetString(this.pv_memory.ToArray());
                            this.addLog(msg, Enums.logType.WARNING);
                            break;
                        case WebSocketMessageType.Binary:
                            this.pv_memory.Position = 0;
                            using (var gzipStream = new GZipStream(this.pv_memory, CompressionMode.Decompress, leaveOpen: true))
                            {
                                gzipStream.CopyTo(this.pv_result_memory);
                            }
                            msg = Encoding.UTF8.GetString(this.pv_result_memory.ToArray());
                            onMsg(msg);
                            this.pv_result_memory.SetLength(0);
                            this.pv_result_memory.Position = 0;
                            break;
                        case WebSocketMessageType.Close:
                            this.addLog("Closed by server");
                            output = false;
                            msg = "Closing message[onListen]:" + Encoding.UTF8.GetString(this.pv_memory.ToArray());
                            break;
                    }
                    if(this.logging)
                    {
                        this.logFilePrivate.WriteLine(DateTime.UtcNow.ToString() + "   " + msg);
                        //this.logFilePrivate.Flush();
                    }
                    this.pv_memory.SetLength(0);
                    this.pv_memory.Position = 0;
                    this.sw_Private.Stop();
                    latency = this.sw_Private.Elapsed.TotalNanoseconds / 1000;
                    this.sw_Private.Reset();
                    break;
                case WebSocketState.None:
                case WebSocketState.Connecting:
                    //Do nothing
                    break;
                case WebSocketState.CloseReceived:
                case WebSocketState.CloseSent:
                case WebSocketState.Closed:
                case WebSocketState.Aborted:
                default:
                    output = false;
                    break;
            }
            return (output, latency);
        }

        private async Task<string> getAsync(string endpoint)
        {

            string url = BuildSignedUrl("GET", endpoint, null);

            var nonce = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            if (this.logging)
            {
                this.logFilePrivate.WriteLine(DateTime.UtcNow.ToString() + "   GET" + endpoint);
            }

            var response = await this.http_client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        private async Task<string> postAsync(string endpoint, string body)
        {
            string url = BuildSignedUrl("POST", endpoint, null);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Accept", "application/json");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (this.logging)
            {
                this.logFilePrivate.WriteLine(DateTime.UtcNow.ToString() + "   POST" + endpoint + body);
            }

            sw_POST.Start();
            var response = await this.http_client.SendAsync(request);
            sw_POST.Stop();
            this.elapsedTime_POST += sw_POST.Elapsed.TotalNanoseconds / 1000;
            ++this.count;
            sw_POST.Reset();
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();

        }

        private string BuildSignedUrl(string method, string endpoint, IDictionary<string, string>? extraParams)
        {
            var baseUri = new Uri(bittrade_connection.URL);
            string host = baseUri.Host;
            if (!baseUri.IsDefaultPort)
                host += ":" + baseUri.Port;

            string path = endpoint.StartsWith("/") ? endpoint : "/" + endpoint;

            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

            var parameters = new SortedDictionary<string, string>
        {
            { "AccessKeyId", this.apiName },
            { "SignatureMethod", "HmacSHA256" },
            { "SignatureVersion", "2" },
            { "Timestamp", timestamp }
        };

            if (extraParams != null)
            {
                foreach (var kv in extraParams)
                    parameters[kv.Key] = kv.Value;
            }

            string canonicalQuery = string.Join("&",
                parameters.Select(kv =>
                    $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

            string signPayload = $"{method}\n{host}\n{path}\n{canonicalQuery}";

            string signature = this.ToHmacSha256Base64(this.secretKey, signPayload);

            // RFC3986 準拠で署名をエンコード
            string encodedSignature = Uri.EscapeDataString(signature);

            return $"{bittrade_connection.URL}{path}?{canonicalQuery}&Signature={encodedSignature}";
        }

        public async Task<JsonDocument> getAccount()
        {
            var resString = await this.getAsync("/v1/account/accounts");
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getBalance()
        {
            var resString = await this.getAsync("/v1/account/accounts/" + this.accountId.ToString() + "/balance");
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getActiveOrders()
        {
            var resString = await this.getAsync("/v1/order/openOrders");
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> placeNewOrder(string symbol, string side, decimal price = 0, decimal quantity = 0, bool postonly = true)
        {
            string _type = side + "-limit";
            if (postonly)
            {
                _type += "-maker";
            }
            var jsonBody = "{\"account-id\":" + this.accountId.ToString() + ", \"amount\":\"" + quantity.ToString() + "\",\"price\":\"" + price.ToString() + "\",\"source\":\"api\",\"symbol\":\"" + symbol + "\",\"type\":\"" + _type + "\"}";
            var resString = await this.postAsync("/v1/order/orders/place", jsonBody);

            return JsonDocument.Parse(resString);
        }
        public async Task<JsonDocument> placeCanOrder(string order_id)
        {
            var resString = await this.postAsync("/v1/order/orders/" + order_id + "/submitcancel","");

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
        public double avgLatency()
        {
            if (this.count > 0)
            {
                return this.elapsedTime_POST / this.count;
            }
            else
            {
                return 0;
            }
        }

        private string ToSha256(string key, string value)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
        private string ToHmacSha256Base64(string secret, string message)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
                return Convert.ToBase64String(hash); 
            }
        }
        public void addLog(string line,Enums.logType logtype = Enums.logType.INFO)
        {
            this._addLog("[bittrade_connection]" + line,logtype);
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
