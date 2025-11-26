using PubnubApi;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Utils;

namespace Crypto_Clients
{
    public class bitbank_connection
    {
        private string apiName;
        private string secretKey;

        private const string publicKey = "sub-c-ecebae8e-dd60-11e6-b6b1-02ee2ddab7fe";
        private const string URL = "https://api.bitbank.cc";
        private const string ws_URL = "wss://stream.bitbank.cc/socket.io/?EIO=4&transport=websocket";

        public ConcurrentQueue<DataSpotOrderUpdate> orderQueue;
        public ConcurrentStack<DataSpotOrderUpdate> orderStack;
        public ConcurrentQueue<DataFill> fillQueue;
        public ConcurrentStack<DataFill> fillStack;

        public ConcurrentQueue<string> msgLogQueue;
        string logPath;
        public bool logging;

        ClientWebSocket websocket_client;
        HttpClient http_client;

        private SocketsHttpHandler _handler;

        WebSocketState pubnub_state;

        public Action<string> onMessage;
        public Action<string,Enums.logType> _addLog;

        byte[] ws_buffer = new byte[16384];
        MemoryStream ws_memory = new MemoryStream();
        MemoryStream result_memory = new MemoryStream();

        //public StreamWriter msgLog;

        Stopwatch sw_POST;
        double elapsedTime_POST;
        int count;
        Stopwatch sw_Public;

        bool closeSent;
        bool pubnubReconnecting = false;
        bool pubnubReconnected = false;

        private List<string> subscribingChannels;

        volatile int refreshing = 0;

        private bitbank_connection()
        {
            this.apiName = "";
            this.secretKey = "";

            this.sw_POST = new Stopwatch();
            this.elapsedTime_POST = 0;
            this.count = 0;
            this.sw_Public = new Stopwatch();

            this.websocket_client = new ClientWebSocket();
            this._handler = this.createHandler();
            this.http_client = new HttpClient(_handler)
            {
                BaseAddress = new Uri(URL),
                Timeout = TimeSpan.FromSeconds(10)
            };

            //this.orderQueue = new ConcurrentQueue<JsonElement>();
            //this.fillQueue = new ConcurrentQueue<JsonElement>();

            this.closeSent = false;
            this.pubnub_state = WebSocketState.None;

            this.subscribingChannels = new List<string>();

            //this._addLog = Console.WriteLine;
            //this.onMessage = Console.WriteLine;

            this.msgLogQueue = new ConcurrentQueue<string>();

        }


        private SocketsHttpHandler createHandler()
        {
            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(3),
                PooledConnectionLifetime = TimeSpan.FromHours(1),

                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),

                KeepAlivePingDelay = TimeSpan.FromMinutes(1),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always
            };
            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                IPAddress[] ips = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host);

                Exception last = null;

                foreach (var ip in ips.Where(x => x.AddressFamily == AddressFamily.InterNetwork))
                {
                    try
                    {
                        var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                        {
                            NoDelay = true
                        };

                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        cts.CancelAfter(TimeSpan.FromSeconds(3)); // connect timeout

                        await socket.ConnectAsync(new IPEndPoint(ip, context.DnsEndPoint.Port), cts.Token);

                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                    }
                }

                throw last ?? new Exception("Connect failed");
            };

            return handler;
        }

        public bool refreshHttpClient()
        {
            if(Interlocked.CompareExchange(ref this.refreshing,1,0) == 0)
            {
                this.http_client.Dispose();
                this._handler.Dispose();
                Thread.Sleep(2000);
                this._handler = this.createHandler();

                this.http_client = new HttpClient(_handler)
                {
                    BaseAddress = new Uri(URL),
                    Timeout = TimeSpan.FromSeconds(10)
                };
                Volatile.Write(ref this.refreshing, 0);
                return true;
            }
            else
            {
                return false;  
            }
        }

        public void setQueues(Crypto_Clients client)
        {
            this.orderQueue = client.ordUpdateQueue;
            this.orderStack = client.ordUpdateStack;
            this.fillQueue = client.fillQueue;
            this.fillStack = client.fillStack;
        }

        public void setLogFile(string path)
        {
            this.logging = true;
            this.logPath = path;
            //FileStream fspub = new FileStream(path + "/bitbank_msglog" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".txt", FileMode.Append, FileAccess.Write, FileShare.Read);
            //this.msgLog = new StreamWriter(fspub);
        }
        public async Task<bool> msgLogging(Action start, Action end, CancellationToken ct, int spinningMax)
        {
            var spinner = new SpinWait();
            this.logging = true;
            FileStream fspub = new FileStream(this.logPath + "/bitbank_msglog" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".txt", FileMode.Append, FileAccess.Write, FileShare.Read);
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
                        this.addLog("Cancel requested. msgLogging of bitbank", Enums.logType.WARNING);
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
                this.addLog("Error occured during logging bitbank messages.", Enums.logType.WARNING);
                this.addLog($"Error: {ex.Message}");
                if (ex.StackTrace != null)
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
            this.ws_memory.SetLength(0);
            this.ws_memory.Position = 0;
            this.websocket_client = new ClientWebSocket();
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

        public async Task onClosing(Action<string> onMsg)
        {
            if(this.websocket_client.State == WebSocketState.Aborted || this.websocket_client.State == WebSocketState.Closed)
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
            string msg;
            while (this.websocket_client.State != WebSocketState.Closed)
            {
                this.ws_memory.SetLength(0);
                this.ws_memory.Position = 0;
                currentTime = DateTime.UtcNow;
                if(currentTime - time_ClosingCalled > TimeSpan.FromSeconds(10))
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
                } while ((!result.EndOfMessage) && this.websocket_client.State != WebSocketState.Aborted && this.websocket_client.State != WebSocketState.Closed);

                switch (result.MessageType)
                {
                    case WebSocketMessageType.Text:
                        msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
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
                                    await this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("40"), WebSocketMessageType.Text, true, CancellationToken.None);
                                    break;
                                case "2":
                                    await this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("3"), WebSocketMessageType.Text, true, CancellationToken.None);
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
                                    break;
                            }
                        }
                        else
                        {
                            if (msg == "2")
                            {
                                await this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("3"), WebSocketMessageType.Text, true, CancellationToken.None);
                            }
                        }
                        break;
                    case WebSocketMessageType.Binary:
                        this.addLog("Binary type is not expected", Enums.logType.WARNING);
                        this.ws_memory.Position = 0;
                        using (var gzipStream = new GZipStream(this.ws_memory, CompressionMode.Decompress, leaveOpen: true))
                        {
                            gzipStream.CopyTo(this.result_memory);
                        }
                        msg = Encoding.UTF8.GetString(this.result_memory.ToArray());
                        onMsg(msg);
                        break;
                    case WebSocketMessageType.Close:
                        this.addLog("Closed by server");
                        if (this.websocket_client.State == WebSocketState.Open || this.websocket_client.State == WebSocketState.CloseReceived)
                        {
                            await this.disconnectPublic();
                        }
                        msg = "Closing message[onClsoing]:" + Encoding.UTF8.GetString(this.ws_memory.ToArray());
                        break;
                    default:
                        msg = "";
                        break;
                }
                if(this.logging)
                {
                    this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   " + msg);
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

        public async Task<bool> Listening(Action start, Action end, CancellationToken ct,int spinningMax)//This function doesn't require to count the spinning as the receiver wait until it receives something.
        {
            WebSocketReceiveResult result;
            string msg;
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
                                                await this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("40"), WebSocketMessageType.Text, true, CancellationToken.None);
                                                break;
                                            case "2":
                                                await this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("3"), WebSocketMessageType.Text, true, CancellationToken.None);
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
                                                break;
                                        }
                                    }
                                    else
                                    {
                                        if (msg == "2")
                                        {
                                            await this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("3"), WebSocketMessageType.Text, true, CancellationToken.None);
                                        }
                                    }
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
                                    //onMsg(msg);
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
                                this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   " + msg);
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
                            this.addLog("Websocket Closed. bitbank public listening",Enums.logType.WARNING);
                            abort = true;
                            break;
                    }
                    if(abort)
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
                if(ex.Message.StartsWith("The remote party closed the WebSocket connection"))
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

        public async Task<(bool,double)> onListen()
        {
            WebSocketReceiveResult result;
            string msg;
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
                    switch(result.MessageType)
                    {
                        case WebSocketMessageType.Text:
                            msg = Encoding.UTF8.GetString(this.ws_memory.ToArray());
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
                                        await this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("40"), WebSocketMessageType.Text, true, CancellationToken.None);
                                        break;
                                    case "2":
                                        await this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("3"), WebSocketMessageType.Text, true, CancellationToken.None);
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
                                        break;
                                }
                            }
                            else
                            {
                                if (msg == "2")
                                {
                                    await this.websocket_client.SendAsync(Encoding.UTF8.GetBytes("3"), WebSocketMessageType.Text, true, CancellationToken.None);
                                }
                            }
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
                            //onMsg(msg);
                            break;
                        case WebSocketMessageType.Close:
                            this.addLog("Closed by server");
                            output =  false;
                            msg = "Closing message[onListen]:" + Encoding.UTF8.GetString(this.ws_memory.ToArray());
                            break;
                        default:
                            msg = "";
                            break;
                    }
                    if(this.logging)
                    {
                        this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   " + msg);
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
        }
       
        private async Task<string> getAsync(string endpoint,string body = "",string url = "")
        {
            
            try
            {
                var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var timeWindow = "5000";
                var message = $"{nonce}{timeWindow}{endpoint}{body}";

                HttpRequestMessage request;

                if (url != "")
                {
                    request = new HttpRequestMessage(HttpMethod.Get, url + endpoint + body);
                }
                else
                {
                    request = new HttpRequestMessage(HttpMethod.Get, bitbank_connection.URL + endpoint + body);
                }


                request.Headers.Add("ACCESS-KEY", this.apiName);
                request.Headers.Add("ACCESS-REQUEST-TIME", nonce.ToString());
                request.Headers.Add("ACCESS-TIME-WINDOW", timeWindow);
                request.Headers.Add("ACCESS-SIGNATURE", ToSha256(this.secretKey, message));

                request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var watchDogCts = new CancellationTokenSource();

                var watchDog = Task.Delay(10000,watchDogCts.Token);
                var task = this.http_client.SendAsync(request, cts.Token);
                var compleretedTask = await Task.WhenAny(task, watchDog);

                if(compleretedTask == watchDog)
                {
                    this.addLog("The request has timed out.(WatchDog)", Enums.logType.WARNING);
                    return "{\"success\":0,\"data\":{\"code\":80001}}";
                }

                watchDogCts.Cancel();
                var response = await task;

                //var response = await this.http_client.SendAsync(request,cts.Token);
                var resString = await response.Content.ReadAsStringAsync();
                return resString;
            }
            catch (TaskCanceledException tce)
            {
                this.addLog("The request has timed out." + tce.Message, Enums.logType.WARNING);
                return "{\"success\":0,\"data\":{\"code\":80001}}";
            }
            catch (TimeoutException te)
            {
                this.addLog("The request has timed out." + te.Message, Enums.logType.WARNING);
                return "{\"success\":0,\"data\":{\"code\":80001}}";
            }
            catch (Exception ex)
            {
                this.addLog("Error occured during getAsync. " + ex.Message, Enums.logType.ERROR);
                return "{\"success\":0,\"data\":{\"code\":-1}}";
            }
        }
        private async Task<string> postAsync(string endpoint, string body)
        {
            try
            {
                var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var timeWindow = "5000";
                var message = $"{nonce}{timeWindow}{body}";

                var request = new HttpRequestMessage(HttpMethod.Post, bitbank_connection.URL + endpoint);

                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                request.Headers.Add("ACCESS-KEY", this.apiName);
                request.Headers.Add("ACCESS-REQUEST-TIME", nonce.ToString());
                request.Headers.Add("ACCESS-TIME-WINDOW", timeWindow);
                request.Headers.Add("ACCESS-SIGNATURE", ToSha256(this.secretKey, message));

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var watchDogCts = new CancellationTokenSource();

                if (this.logging)
                {
                    this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   POST " + endpoint + " " + body);
                    //this.logFilePublic.Flush();
                }

                var watchDog = Task.Delay(10000, watchDogCts.Token);
                sw_POST.Restart();
                var task = this.http_client.SendAsync(request, cts.Token);

                var compleretedTask = await Task.WhenAny(task, watchDog);

                sw_POST.Stop();
                this.elapsedTime_POST = (this.elapsedTime_POST * this.count + sw_POST.Elapsed.TotalNanoseconds / 1000) / (this.count + 1);
                ++this.count;

                if (compleretedTask == watchDog)
                {
                    this.addLog("The request has timed out.(WatchDog)", Enums.logType.WARNING);
                    return "{\"success\":0,\"data\":{\"code\":80001}}";
                }
                watchDogCts.Cancel();
                
                if (sw_POST.Elapsed.TotalNanoseconds > 3_000_000_000)
                {
                    this.addLog("The roundtrip time exceeded 3 sec.    Time:" + (sw_POST.Elapsed.TotalNanoseconds / 1_000_000_000).ToString("N3") + "[sec]", Enums.logType.WARNING);
                }

                var response = await task;

                //sw_POST.Reset();
                var resString = await response.Content.ReadAsStringAsync();

                return resString;
            }
            catch (TaskCanceledException tce)
            {
                this.addLog("The request has timed out." + tce.Message, Enums.logType.WARNING);
                return "{\"success\":0,\"data\":{\"code\":80001}}";
            }
            catch (TimeoutException te)
            {
                this.addLog("The request has timed out." + te.Message, Enums.logType.WARNING);
                return "{\"success\":0,\"data\":{\"code\":80001}}";
            }
            catch (Exception ex)
            {
                this.addLog("Error occured during getAsync. " + ex.Message, Enums.logType.ERROR);
                return "{\"success\":0,\"data\":{\"code\":-1}}";
            }
            
        }
        public async Task<JsonDocument> getTicker(string symbol)
        {
            string url = "https://public.bitbank.cc";
            string request = $"/{symbol}/ticker";
            var resString = await this.getAsync(request,"",url);
            var json = JsonDocument.Parse(resString);
            return json;
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
        public async Task<JsonDocument> getCandleStick(string symbol,string candle_type,string period)
        {
            string url = "https://public.bitbank.cc";
            string request = $"/{symbol}/candlestick/{candle_type}/{period}";
            Console.WriteLine(request);
            var resString = await this.getAsync(request, "", url);
            Console.WriteLine(resString);
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getVolumeHistory(string symbol, string YYYYMMDD = "")
        {
            string url = "https://public.bitbank.cc";
            string request = $"/{symbol}/transactions";
            if(YYYYMMDD != "")
            {
                request += $"/{YYYYMMDD}";
            }
            Console.WriteLine(request);
            var resString = await this.getAsync(request, "",url);
            Console.WriteLine(resString);
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getTradeHistory(string symbol = "",DateTime? startTime = null,DateTime? endTime = null)
        {
            var js = new Dictionary<string, object>();

            if (string.IsNullOrEmpty(symbol))
            {
                js["pair"] = symbol;
            }
            if(startTime != null)
            {
                js["since"] = ((DateTimeOffset)startTime).ToUnixTimeMilliseconds();
            }
            if (endTime != null)
            {
                js["end"] = ((DateTimeOffset)endTime).ToUnixTimeMilliseconds();
            }
            js["order"] = "asc";

            string body = BuildQueryString(JsonDocument.Parse(JsonSerializer.Serialize(js)));
            var resString = await this.getAsync("/v1/user/spot/trade_history", body);
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public  string BuildQueryString(JsonDocument doc)
        {
            var sb = new StringBuilder();
            bool first = true;

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Null)
                    continue;

                if (first)
                {
                    sb.Append('?');
                    first = false;
                }
                else
                {
                    sb.Append('&');
                }

                sb.Append(Uri.EscapeDataString(property.Name));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(property.Value.ToString()));
            }

            return sb.ToString();
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
            var json = JsonDocument.Parse(resString);
            return json;
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
            var json = JsonDocument.Parse(resString);
            return json;
        }

        public async Task<List<JsonDocument>> placeCanOrders(string symbol, IEnumerable<string> order_ids)
        {
            int pageSize = 30;
            List<JsonDocument> list = new List<JsonDocument>();

            IEnumerable<Int64> orderIdInts = order_ids.Select(s => Int64.Parse(s));
            int total = orderIdInts.Count();
            int pageCount = (int)Math.Ceiling((double)total / pageSize);

            for (int page = 0; page < pageCount; page++)
            {
                var subList = orderIdInts
                    .Skip(page * pageSize)
                    .Take(pageSize)
                    .ToList();

                var body = new
                {
                    pair = symbol,
                    order_ids = subList
                };

                var json = JsonSerializer.Serialize(body);
                var resString = await this.postAsync("/v1/user/spot/cancel_orders", json);
                list.Add(JsonDocument.Parse(resString));

            }
            return list;
        }

        private async Task<(string channel, string token)> GetChannelAndToken()
        {
            var resString = await this.getAsync("/v1/user/subscribe");

            var json = JsonDocument.Parse(resString);

            if (json.RootElement.GetProperty("success").GetInt32() == 1)
            {
                var channel = json.RootElement.GetProperty("data").GetProperty("pubnub_channel").GetString();
                var token = json.RootElement.GetProperty("data").GetProperty("pubnub_token").GetString();
                return (channel!, token!);
            }
            else
            {
                var errorCode = json.RootElement.GetProperty("data").GetProperty("code").GetInt32();
                addLog("Failed to get channel and token. error code:" + errorCode.ToString(), Enums.logType.ERROR);
                return ("", "");
            }
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
                        DataFill fill;
                        DataSpotOrderUpdate ord;
                        if (json.RootElement.TryGetProperty("method", out var method))
                        {
                            var methodStr = method.GetString();
                            switch (methodStr)
                            {
                                //Orders
                                case "spot_order_new":
                                case "spot_order":
                                    var ord_msg = json.RootElement.GetProperty("params").EnumerateArray();
                                    foreach (var d in ord_msg)
                                    {
                                        while (!this.orderStack.TryPop(out ord))
                                        {

                                        }
                                        ord.setBitbankSpotOrder(d);
                                        this.orderQueue.Enqueue(ord);
                                    }
                                    break;
                                case "spot_trade":
                                    var trd = json.RootElement.GetProperty("params").EnumerateArray();
                                    foreach (var d in trd)
                                    {
                                        while(!this.fillStack.TryPop(out fill))
                                        {

                                        }
                                        fill.setBitBankFill(d);
                                        this.fillQueue.Enqueue(fill);
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
                        if(this.logging)
                        {
                            this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   " + messageResult.Message.ToString());
                        }
                    }
                },
                (pubnubObj, presenceResult) =>
                {
                    this.addLog("presence: " + presenceResult.Event);
                    if(this.logging)
                    {
                        this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   " + presenceResult.Event);
                    }
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
                        case PNStatusCategory.PNDisconnectedCategory:
                        case PNStatusCategory.PNUnexpectedDisconnectCategory:
                        case PNStatusCategory.PNAccessDeniedCategory:
                            this.pubnubReconnecting = true;
                            this.pubnub_state = WebSocketState.Closed;
                            this.addLog("pubnub reconnecting...");
                            await connectPrivateAsync();
                            this.pubnubReconnected = true;
                            this.pubnubReconnecting = false;
                            break;

                        default:
                            string? additional = status.AdditonalData.ToString();
                            this.addLog("Unexpected status:" + status.Category.ToString(),Enums.logType.ERROR);
                            if(additional != null)
                            {
                                this.addLog(additional, Enums.logType.ERROR);
                            }
                            this.pubnub_state = WebSocketState.None;
                            break;
                    }
                    if(this.logging)
                    {
                        this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   " + status.Category.ToString() + " " + status.ErrorData.ToString());
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
        public double avgLatency()
        {
            if (this.count > 0)
            {
                return this.elapsedTime_POST;
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
