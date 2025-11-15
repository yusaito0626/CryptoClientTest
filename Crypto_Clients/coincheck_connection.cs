using PubnubApi;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
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

        public ConcurrentQueue<string> msgLogQueue;
        string logPath;
        public bool logging;

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

        private Int64 lastnonce;
        private volatile int nonceChecking;


        Stopwatch sw_POST;
        double elapsedTime_POST;
        int count;

        Stopwatch sw_Private;
        Stopwatch sw_Public;

        private coincheck_connection()
        {
            this.apiName = "";
            this.secretKey = "";

            this.websocket_client = new ClientWebSocket();
            this.private_client = new ClientWebSocket();
            this.http_client = new HttpClient(_handler)
            {
                BaseAddress = new Uri(URL)
            };

            this.orderQueue = new ConcurrentQueue<JsonElement>();
            this.fillQueue = new ConcurrentQueue<JsonElement>();
            this.msgLogQueue = new ConcurrentQueue<string>();

            this.closeSentPublic = false;
            this.closeSentPrivate = false;
            this.subscribingChannels = new List<string>();

            this.sw_POST = new Stopwatch();
            this.elapsedTime_POST = 0;
            this.count = 0;
            this.sw_Private = new Stopwatch();
            this.sw_Public = new Stopwatch();

            this.lastnonce = 0;
            this.nonceChecking = 0;
            
        }
        public void setLogFile(string path)
        {
            this.logging = true;
            this.logPath = path;
            //FileStream fspub = new FileStream(path + "/coincheck_msglog" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".txt", FileMode.Append, FileAccess.Write, FileShare.Read);
            //this.msgLog = new StreamWriter(fspub);
        }

        public async Task<bool> msgLogging(Action start,Action end,CancellationToken ct,int spinningMax)
        {
            var spinner = new SpinWait();
            this.logging = true;
            FileStream fspub = new FileStream(this.logPath + "/coincheck_msglog" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".txt", FileMode.Append, FileAccess.Write, FileShare.Read);
            StreamWriter msgLog = new StreamWriter(fspub);
            bool ret = true;
            int i = 0;
            string msg;
            try
            {
                while (true)
                {
                    i = 0;
                    while (this.msgLogQueue.TryDequeue(out msg))
                    {
                        start();
                        msgLog.WriteLine(msg);
                        ++i;
                        spinner.Reset();
                        end();
                        if (i == 10000)
                        {
                            break;
                        }
                    }
                    if (ct.IsCancellationRequested)
                    {
                        this.addLog("Cancel requested. msgLogging of coincheck", Enums.logType.WARNING);
                        break;
                    }
                    spinner.SpinOnce();
                    if (spinningMax > 0 && spinner.Count >= spinningMax)
                    {
                        Thread.Sleep(500);
                        spinner.Reset();
                    }
                }
            }
            catch (Exception ex)
            {
                this.addLog("Error occured during logging coincheck messages.", Enums.logType.WARNING);
                this.addLog($"Error: {ex.Message}");
                if(ex.StackTrace != null)
                {
                    this.addLog($"{ex.StackTrace}");
                }
                ret = false;
            }
            finally
            {
                msgLog.Flush();
                msgLog.Dispose();
            }
            return ret;
        }
        public void SetApiCredentials(string name, string key)
        {
            this.apiName = name;
            this.secretKey = key;
        }
        public async Task connectPublicAsync()
        {
            this.addLog("Connecting to coincehck");
            this.ws_memory.SetLength(0);
            this.ws_memory.Position = 0;
            this.websocket_client = new ClientWebSocket();
            var uri = new Uri(coincheck_connection.ws_URL);
            try
            {
                this.websocket_client.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await this.websocket_client.ConnectAsync(uri, CancellationToken.None);
                this.addLog("Connected to coincheck.");
                this.closeSentPublic = false;
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
        public async Task connectPrivateAsync()
        {
            this.addLog("Connecting to private channel of coincheck");
            this.pv_memory.SetLength(0);
            this.pv_memory.Position = 0;
            this.private_client = new ClientWebSocket();
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if(nonce <= this.lastnonce)
            {
                ++nonce;
            }
            this.lastnonce = nonce;
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
                this.private_client.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
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
                    this.addLog("Closed by server");
                    await this.websocket_client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
            }
            this.addLog("Check websocket state. State:" +  this.websocket_client.State.ToString());
        }

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
                        msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
                        onMsg(msg);
                        break;
                    case WebSocketMessageType.Binary:
                        this.addLog("Binary type is not expected", Enums.logType.WARNING);
                        this.ws_memory.Position = 0;
                        using (var gzipStream = new GZipStream(this.ws_memory, CompressionMode.Decompress, leaveOpen: true))
                        {
                            gzipStream.CopyTo(this.result_memory);
                        }
                        msg = Encoding.UTF8.GetString(this.result_memory.ToArray());

                        this.addLog(msg, Enums.logType.WARNING);
                        break;
                    case WebSocketMessageType.Close:
                        this.addLog("Closed by server");
                        msg = "Closing message[onClosing]:" + Encoding.UTF8.GetString(this.ws_memory.ToArray());
                        break;
                }
                if(this.logging)
                {
                    this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString() + "   " + msg);
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
        }
        public async Task<bool> Listening(Action start, Action end, CancellationToken ct, int spinningMax)//This function doesn't require to count the spinning as the receiver wait until it receives something.
        {
            WebSocketReceiveResult result;
            string msg = "";
            bool abort = false;
            try
            {
                while (true)
                {
                    switch (this.websocket_client.State)
                    {
                        case WebSocketState.Open:
                            do
                            {
                                result = await this.websocket_client.ReceiveAsync(new ArraySegment<byte>(this.ws_buffer), ct);
                                this.ws_memory.Write(this.ws_buffer, 0, result.Count);

                            } while ((!result.EndOfMessage) && this.websocket_client.State != WebSocketState.Aborted && this.websocket_client.State != WebSocketState.Closed);
                            start();
                            switch (result.MessageType)
                            {
                                case WebSocketMessageType.Text:
                                    msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
                                    this.onMessage(msg);
                                    break;
                                case WebSocketMessageType.Binary:
                                    this.addLog("Binary type is not expected", Enums.logType.WARNING);
                                    this.ws_memory.Position = 0;
                                    using (var gzipStream = new GZipStream(this.ws_memory, CompressionMode.Decompress, leaveOpen: true))
                                    {
                                        gzipStream.CopyTo(this.result_memory);
                                    }
                                    msg = Encoding.UTF8.GetString(this.result_memory.ToArray());

                                    this.addLog(msg, Enums.logType.WARNING);
                                    break;
                                case WebSocketMessageType.Close:
                                    this.addLog("Closed by server");
                                    abort = true;
                                    msg = "Closing message[onListen]:" + Encoding.UTF8.GetString(this.ws_memory.ToArray());
                                    break;
                                default:
                                    msg = "";
                                    break;
                            }
                            if (this.logging)
                            {
                                this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString() + "   " + msg);
                                //this.logFilePublic.Flush();
                            }
                            this.ws_memory.SetLength(0);
                            this.ws_memory.Position = 0;
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
                            abort = true;
                            break;
                    }
                    if (abort)
                    {
                        return true;
                    }
                    end();
                }
            }
            catch (OperationCanceledException)
            {
                this.addLog("Cancel Requested. bitbank public listening", Enums.logType.WARNING);
                return true;
            }
            catch (WebSocketException ex)
            {
                if (ex.Message.StartsWith("The remote party closed the WebSocket connection"))
                {
                    this.addLog($"WebSocket Error: {ex.Message}", Enums.logType.WARNING);
                    return false;
                }
                else
                {
                    this.addLog($"WebSocket Error: {ex.Message}", Enums.logType.ERROR);
                }
            }
            catch (Exception ex)
            {
                this.addLog($"Unexpacted Error: {ex.Message}", Enums.logType.ERROR);
            }
            return false;
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
                            msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
                            onMsg(msg);
                            break;
                        case WebSocketMessageType.Binary:
                            this.addLog("Binary type is not expected", Enums.logType.WARNING);
                            this.ws_memory.Position = 0;
                            using (var gzipStream = new GZipStream(this.ws_memory, CompressionMode.Decompress, leaveOpen: true))
                            {
                                gzipStream.CopyTo(this.result_memory);
                            }
                            msg = Encoding.UTF8.GetString(this.result_memory.ToArray());

                            this.addLog(msg, Enums.logType.WARNING);
                            break;
                        case WebSocketMessageType.Close:
                            this.addLog("Closed by server");
                            output = false;
                            msg = "Closing message[onListen]:" + Encoding.UTF8.GetString(this.ws_memory.ToArray());
                            break;
                    }
                    if(this.logging)
                    {
                        this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString() + "   " + msg);
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
            return (output, latency);
            //if (this.websocket_client.State == WebSocketState.Open)
            //{
                //do
                //{
                //    result = await this.websocket_client.ReceiveAsync(new ArraySegment<byte>(this.ws_buffer), CancellationToken.None);
                //    this.ws_memory.Write(this.ws_buffer, 0, result.Count);

                //} while ((!result.EndOfMessage) && this.websocket_client.State != WebSocketState.Aborted && this.websocket_client.State != WebSocketState.Closed);

                //switch (result.MessageType)
                //{
                //    case WebSocketMessageType.Text:
                //        msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
                //        onMsg(msg);
                //        break;
                //    case WebSocketMessageType.Binary:
                //        this.addLog("Binary type is not expected", Enums.logType.WARNING);
                //        this.ws_memory.Position = 0;
                //        using (var gzipStream = new GZipStream(this.ws_memory, CompressionMode.Decompress, leaveOpen: true))
                //        {
                //            gzipStream.CopyTo(this.result_memory);
                //        }
                //        msg = Encoding.UTF8.GetString(this.result_memory.ToArray());
                        
                //        this.addLog(msg,Enums.logType.WARNING);
                //        break;
                //    case WebSocketMessageType.Close:
                //        this.addLog("Closed by server");
                //        output = false;
                //        msg = "Closing message[onListen]:" + Encoding.UTF8.GetString(this.ws_memory.ToArray());
                //        break;
                //}
                //this.logFilePublic.WriteLine(DateTime.UtcNow.ToString() + "   " + msg);
                //this.logFilePublic.Flush();

                //if (result.MessageType == WebSocketMessageType.Text)
                //{
                //    var msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
                //    onMsg(msg);

                //    this.logFilePublic.WriteLine(DateTime.UtcNow.ToString() + "   " + msg);
                //    this.logFilePublic.Flush();
                //}
                //else if (result.MessageType == WebSocketMessageType.Binary)
                //{
                //    this.ws_memory.Position = 0;
                //    using var gzipStream = new GZipStream(this.ws_memory, CompressionMode.Decompress, leaveOpen: true);
                //    gzipStream.CopyTo(this.result_memory);
                //    var msg = Encoding.UTF8.GetString(this.result_memory.ToArray());
                //    onMsg(msg);
                //    this.logFilePublic.WriteLine(DateTime.UtcNow.ToString() + "   " + msg);
                //    this.logFilePublic.Flush();
                //    this.result_memory.SetLength(0);
                //    this.result_memory.Position = 0;
                //}
                //else if (result.MessageType == WebSocketMessageType.Close)
                //{
                //    this.addLog("Closed by server");
                //    if (this.websocket_client.State == WebSocketState.Open || this.websocket_client.State == WebSocketState.CloseReceived)
                //    {
                //        await this.disconnectPublic();
                //    }
                //    var msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
                //    this.logFilePublic.WriteLine(DateTime.UtcNow.ToString() + "   " + msg);
                //    this.logFilePublic.Flush();
                //    return false;
                //}
                //this.ws_memory.SetLength(0);
                //this.ws_memory.Position = 0;
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
                        this.pv_memory.Position = 0;
                        msg = Encoding.UTF8.GetString(this.pv_memory.ToArray());
                        onMsg(msg);
                        break;
                    case WebSocketMessageType.Binary:
                        this.addLog("Binary message is not expected.", Enums.logType.WARNING);
                        this.pv_memory.Position = 0;
                        using (var gzipStream = new GZipStream(this.pv_memory, CompressionMode.Decompress, leaveOpen: true))
                        {
                            gzipStream.CopyTo(this.pv_result_memory);
                        }
                        msg = Encoding.UTF8.GetString(this.pv_result_memory.ToArray());
                        this.addLog(msg, Enums.logType.WARNING);
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
                    this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString() + "   " + msg);
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
        }
        public async Task<bool> ListeningPrivate(Action start, Action end, CancellationToken ct, int spinningMax)//This function doesn't require to count the spinning as the receiver wait until it receives something.
        {
            WebSocketReceiveResult result;
            string msg = "";
            bool abort = false;
            try
            {
                while (true)
                {
                    switch (this.private_client.State)
                    {
                        case WebSocketState.Open:

                            do
                            {
                                result = await this.private_client.ReceiveAsync(new ArraySegment<byte>(this.pv_buffer), ct);
                                this.pv_memory.Write(this.pv_buffer, 0, result.Count);

                            } while (!result.EndOfMessage && this.private_client.State != WebSocketState.Aborted && this.private_client.State != WebSocketState.Closed);
                            start();
                            switch (result.MessageType)
                            {
                                case WebSocketMessageType.Text:
                                    msg = Encoding.UTF8.GetString(this.pv_memory.ToArray());
                                    this.onPrivateMessage(msg);
                                    break;
                                case WebSocketMessageType.Binary:
                                    this.addLog("Binary message is not expected.", Enums.logType.WARNING);
                                    this.pv_memory.Position = 0;
                                    using (var gzipStream = new GZipStream(this.pv_memory, CompressionMode.Decompress, leaveOpen: true))
                                    {
                                        gzipStream.CopyTo(this.pv_result_memory);
                                    }
                                    msg = Encoding.UTF8.GetString(this.pv_result_memory.ToArray());
                                    this.addLog(msg, Enums.logType.WARNING);
                                    this.pv_result_memory.SetLength(0);
                                    this.pv_result_memory.Position = 0;
                                    break;
                                case WebSocketMessageType.Close:
                                    this.addLog("Closed by server");
                                    abort = true;
                                    msg = "Closing message[onListen]:" + Encoding.UTF8.GetString(this.pv_memory.ToArray());
                                    break;
                                default:
                                    msg = "";
                                    break;
                            }
                            if (this.logging)
                            {
                                this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString() + "   " + msg);
                                //this.logFilePrivate.Flush();
                            }
                            this.pv_memory.SetLength(0);
                            this.pv_memory.Position = 0;
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
                            abort = true;
                            break;
                    }
                    if (abort)
                    {
                        return true;
                    }
                    end();
                }
            }
            catch (OperationCanceledException)
            {
                this.addLog("Cancel Requested. bitbank public listening", Enums.logType.WARNING);
                return true;
            }
            catch (WebSocketException ex)
            {
                if (ex.Message.StartsWith("The remote party closed the WebSocket connection"))
                {
                    this.addLog($"WebSocket Error: {ex.Message}", Enums.logType.WARNING);
                    return false;
                }
                else
                {
                    this.addLog($"WebSocket Error: {ex.Message}", Enums.logType.ERROR);
                }
            }
            catch (Exception ex)
            {
                this.addLog($"Unexpacted Error: {ex.Message}", Enums.logType.ERROR);
            }
            return false;
        }
        public async Task<(bool,double)> onListenPrivate(Action<string> onMsg)
        {
            WebSocketReceiveResult result;
            string msg = "";
            bool output = true;
            double latency = 0;
            switch(this.private_client.State)
            {
                case WebSocketState.Open:

                    do
                    {
                        result = await this.private_client.ReceiveAsync(new ArraySegment<byte>(this.pv_buffer), CancellationToken.None);
                        this.pv_memory.Write(this.pv_buffer, 0, result.Count);

                    } while (!result.EndOfMessage && this.private_client.State != WebSocketState.Aborted && this.private_client.State != WebSocketState.Closed);
                    this.sw_Private.Start();
                    switch(result.MessageType)
                    {
                        case WebSocketMessageType.Text:
                            msg = Encoding.UTF8.GetString(this.pv_memory.ToArray());
                            onMsg(msg);
                            break;
                        case WebSocketMessageType.Binary:
                            this.addLog("Binary message is not expected.", Enums.logType.WARNING);
                            this.pv_memory.Position = 0;
                            using (var gzipStream = new GZipStream(this.pv_memory, CompressionMode.Decompress, leaveOpen: true))
                            {
                                gzipStream.CopyTo(this.pv_result_memory);
                            }
                            msg = Encoding.UTF8.GetString(this.pv_result_memory.ToArray());
                            this.addLog(msg,Enums.logType.WARNING);
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
                        this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString() + "   " + msg);
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
            return (output,latency);
        }

        private async Task<string> getAsync(string endpoint,string body = "")
        {
            while (Interlocked.CompareExchange(ref this.nonceChecking, 1, 0) != 0)
            {

            }
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (nonce <= this.lastnonce)
            {
                nonce = this.lastnonce + 1;
            }
            this.lastnonce = nonce;
            var message = $"{nonce}{coincheck_connection.URL}{endpoint}";

            var request = new HttpRequestMessage(HttpMethod.Get, coincheck_connection.URL + endpoint);

            request.Headers.Add("ACCESS-KEY", this.apiName);
            request.Headers.Add("ACCESS-NONCE", nonce.ToString());
            request.Headers.Add("ACCESS-SIGNATURE", ToSha256(this.secretKey, message));

            if(body == "")
            {
                request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
            }
            else
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
            if (this.logging)
            {
                this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString() + "   GET" + endpoint + body);
            }


            Volatile.Write(ref this.nonceChecking, 0);
            var response = await this.http_client.SendAsync(request);
            var resString = await response.Content.ReadAsStringAsync();

            return resString;
        }

        private async Task<string> postAsync(string endpoint, string body)
        {
            while(Interlocked.CompareExchange(ref this.nonceChecking,1,0) != 0)
            {

            }
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (nonce <= this.lastnonce)
            {
                nonce = this.lastnonce + 1;
            }
            this.lastnonce = nonce;
            var message = $"{nonce}{coincheck_connection.URL}{endpoint}{body}";

            var request = new HttpRequestMessage(HttpMethod.Post, coincheck_connection.URL + endpoint);

            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            request.Headers.Add("ACCESS-KEY", this.apiName);
            request.Headers.Add("ACCESS-NONCE", nonce.ToString());
            request.Headers.Add("ACCESS-SIGNATURE", ToSha256(this.secretKey, message));

            sw_POST = Stopwatch.StartNew();
            Volatile.Write(ref this.nonceChecking, 0);
            var response = await this.http_client.SendAsync(request);
            sw_POST.Stop();
            this.elapsedTime_POST += sw_POST.Elapsed.TotalNanoseconds / 1000;
            ++this.count;
            sw_POST.Reset();
            if (this.logging)
            {
                this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString() + "   POST" + endpoint + body);
            }
            var resString = await response.Content.ReadAsStringAsync();
            return resString;
        }

        private async Task<string> deleteAsync(string endpoint,string body)
        {
            while (Interlocked.CompareExchange(ref this.nonceChecking, 1, 0) != 0)
            {

            }
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (nonce <= this.lastnonce)
            {
                nonce = this.lastnonce + 1;
            }
            this.lastnonce = nonce;
            var message = $"{nonce}{coincheck_connection.URL}{endpoint}{body}";

            var request = new HttpRequestMessage(HttpMethod.Delete, coincheck_connection.URL + endpoint);

            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            request.Headers.Add("ACCESS-KEY", this.apiName);
            request.Headers.Add("ACCESS-NONCE", nonce.ToString());
            request.Headers.Add("ACCESS-SIGNATURE", ToSha256(this.secretKey, message));

            Volatile.Write(ref this.nonceChecking, 0);
            var response = await this.http_client.SendAsync(request);
            var resString = await response.Content.ReadAsStringAsync();
            if (this.logging)
            {
                this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString() + "   DELETE" + endpoint + body);
            }
            return resString;
        }
        public async Task<JsonDocument> getTicker(string symbol)
        {
            var resString = await this.getAsync("/api/ticker?pair=" + symbol);
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<List<JsonElement>> getVolumeHistory(string symbol, DateTime? startTime = null, DateTime? endTime = null)
        {
            if(startTime == null)
            {
                startTime = DateTime.UtcNow.Date;
            }
            if(endTime == null)
            {
                endTime = DateTime.UtcNow;
            }
            List<JsonElement> allTransactions = new List<JsonElement>();
            string starting_after = null;
            int limit = 100;
            DateTime currentTime = DateTime.UtcNow;
            while (true)
            {
                string query = $"?limit={limit}&order=desc&pair={symbol}";
                if (!string.IsNullOrEmpty(starting_after))
                    query += $"&starting_after={starting_after}";
                Console.WriteLine(query);
                var resString = await this.getAsync("/api/trades" + query);
                Console.WriteLine(resString);
                var json = JsonDocument.Parse(resString);

                if (!json.RootElement.TryGetProperty("data", out JsonElement transactions) || transactions.GetArrayLength() == 0)
                    break;

                foreach (var tx in transactions.EnumerateArray())
                {
                    currentTime = DateTime.Parse(tx.GetProperty("created_at").GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
                    if (endTime != null && currentTime > endTime)
                    {

                    }
                    else if (startTime != null && currentTime < startTime)
                    {
                        break;
                    }
                    else
                    {
                        allTransactions.Add(tx);
                    }
                }
                if (startTime != null && currentTime < startTime)
                {
                    break;
                }


                starting_after = transactions[transactions.GetArrayLength() - 1].GetProperty("id").GetInt64().ToString();
            }
            return allTransactions;
        }
        public async Task<JsonDocument> getTradeHistory()
        {
            var resString = await this.getAsync("/api/exchange/orders/transactions");
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<List<JsonElement>> getTradeHistoryPagenation(DateTime? startTime = null, DateTime? endTime = null)
        {
            List<JsonElement> allTransactions = new List<JsonElement>();
            string starting_after = null;
            int limit = 100;
            DateTime currentTime = DateTime.UtcNow;
            while (true)
            {
                string query = $"?limit={limit}&order=desc";
                if (!string.IsNullOrEmpty(starting_after))
                    query += $"&starting_after={starting_after}";
                var resString = await this.getAsync("/api/exchange/orders/transactions_pagination" + query);
                var json = JsonDocument.Parse(resString);

                if (!json.RootElement.TryGetProperty("data", out JsonElement transactions) || transactions.GetArrayLength() == 0)
                    break;

                foreach (var tx in transactions.EnumerateArray())
                {
                    currentTime = DateTime.Parse(tx.GetProperty("created_at").GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
                    if(endTime != null && currentTime > endTime)
                    {

                    }
                    else if(startTime != null && currentTime < startTime)
                    {
                        break;
                    }
                    else
                    {
                        allTransactions.Add(tx);
                    }
                }
                if(startTime != null && currentTime < startTime)
                {
                    break;
                }

                
                starting_after = transactions[transactions.GetArrayLength() - 1].GetProperty("id").GetInt64().ToString();
            }
            return allTransactions;
        }
        public async Task<JsonDocument> getBalance()
        {
            var resString = await this.getAsync("/api/accounts/balance");
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getActiveOrders()
        {
            var resString = await this.getAsync("/api/exchange/orders/opens");
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getStatus()
        {
            var resString = await this.getAsync("/api/exchange_status");
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getOrderBooks(string symbol)
        {
            var body = new
            {
                pair = symbol
            };
            var jsonBody = JsonSerializer.Serialize(body);
            var resString = await this.getAsync("/api/order_books?pair=" + Uri.EscapeDataString(symbol));
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
            //var sw = Stopwatch.StartNew();
            var resString = await this.postAsync("/api/exchange/orders", jsonBody);
            //sw.Stop();

            //double latency = sw.Elapsed.TotalMilliseconds;
            //this.addLog("Coincheck postAsync latency:" + latency.ToString());

            return JsonDocument.Parse(resString);
        }

        public async Task<JsonDocument> placeMarketNewOrder(string symbol, string side, decimal price = 0, decimal quantity = 0, string tif = "good_til_cancelled")
        {
            if(side == "sell")
            {
                var body = new
                {
                    pair = symbol,
                    amount = quantity.ToString(),
                    order_type = "market_" + side,
                };


                var jsonBody = JsonSerializer.Serialize(body);
                var resString = await this.postAsync("/api/exchange/orders", jsonBody);

                return JsonDocument.Parse(resString);
            }
            else
            {
                var body = new
                {
                    pair = symbol,
                    market_buy_amount = quantity.ToString(),
                    order_type = "market_" + side,
                };


                var jsonBody = JsonSerializer.Serialize(body);
                var resString = await this.postAsync("/api/exchange/orders", jsonBody);

                return JsonDocument.Parse(resString);
            }
            
        }

        public async Task<JsonDocument> placeCanOrder(string order_id)
        {
            var resString = await this.deleteAsync("/api/exchange/orders/" + order_id, "");

            return JsonDocument.Parse(resString);
        }

        public async Task<List<JsonDocument>> placeCanOrders(IEnumerable<string> order_ids)
        {
            List<JsonDocument> list = new List<JsonDocument>();
            foreach (var order_id in order_ids)
            {
                var res = await this.placeCanOrder(order_id);
                list.Add(res);
            }
            return list;
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
            if(this.count > 0)
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
        public void addLog(string line,Enums.logType logtype = Enums.logType.INFO)
        {
            this._addLog("[coincheck_connection]" + line,logtype);
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
