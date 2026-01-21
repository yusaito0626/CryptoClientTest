using Binance.Net.Objects.Models.Spot.Staking;
using CryptoExchange.Net;
using Enums;
using LockFreeQueue;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Diagnostics.SymbolStore;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XT.Net.Objects.Models;

namespace Crypto_Clients
{
    internal class gmo_marginPos
    {
        public string positionid;
        public string symbol;
        public orderSide side;
        public decimal size;
        public decimal price;
        public DateTime updated_time;
    }
    public class gmocoin_connection
    {
        private const string CONN_NAME = "GMOCoin";
        private string apiName;
        private string secretKey;

        private const string public_URL = "https://api.coin.z.com/public";
        private const string private_URL = "https://api.coin.z.com/private";
        private const string public_wss = "wss://api.coin.z.com/ws/public/v1";
        private const string private_wss = "wss://api.coin.z.com/ws/private";

        public string token = "";
        private DateTime lastAccTokenObtained;

        public MISOQueue<string> msgLogQueue;
        string logPath;
        public bool logging;

        ClientWebSocket websocket_client;
        ClientWebSocket private_client;
        HttpClient http_client;

        private SocketsHttpHandler _handler;

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


        volatile int refreshing = 0;
        volatile int httpReady = 0;

        private gmocoin_connection()
        {
            this.apiName = "";
            this.secretKey = "";

            this.websocket_client = new ClientWebSocket();
            this.private_client = new ClientWebSocket();
            this._handler = this.createHandler();
            this.http_client = new HttpClient(_handler)
            {
                BaseAddress = new Uri(private_URL),
                Timeout = TimeSpan.FromSeconds(10)
            };

            this.msgLogQueue = new MISOQueue<string>();

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
            if (Interlocked.CompareExchange(ref this.refreshing, 1, 0) == 0)
            {
                Thread.Sleep(2000);
                this.http_client.Dispose();
                this._handler.Dispose();
                Thread.Sleep(1000);
                this._handler = this.createHandler();

                this.http_client = new HttpClient(_handler)
                {
                    BaseAddress = new Uri(private_URL),
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
        public void setLogFile(string path)
        {
            this.logging = true;
            this.logPath = path;
        }

        public async Task<bool> msgLogging(Action start,Action end,CancellationToken ct,int spinningMax)
        {
            var spinner = new SpinWait();
            this.logging = true;
            FileStream fspub = new FileStream(this.logPath + "/gmocoin_msglog" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".txt", FileMode.Append, FileAccess.Write, FileShare.Read);
            StreamWriter msgLog = new StreamWriter(fspub);
            bool ret = true;
            int i = 0;
            string msg;
            try
            {
                while (true)
                {
                    i = 0;
                    msg = this.msgLogQueue.Dequeue();
                    //while (this.msgLogQueue.TryDequeue(out msg))
                    while(msg != null)
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
                        else
                        {
                            msg = this.msgLogQueue.Dequeue();
                        }
                    }
                    if (ct.IsCancellationRequested)
                    {
                        this.addLog("Cancel requested. msgLogging of gmocoin", Enums.logType.WARNING);
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
                this.addLog("Error occured during logging gmocoin messages.", Enums.logType.WARNING);
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
        public async Task<bool> connectPublicAsync()
        {
            bool ret = true;
            this.addLog("Connecting to " + gmocoin_connection.CONN_NAME);
            this.ws_memory.SetLength(0);
            this.ws_memory.Position = 0;
            this.websocket_client = new ClientWebSocket();
            var uri = new Uri(gmocoin_connection.public_wss);
            try
            {
                this.websocket_client.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await this.websocket_client.ConnectAsync(uri, CancellationToken.None);
                this.addLog("Connected to " + gmocoin_connection.CONN_NAME);
                this.closeSentPublic = false;
            }
            catch (WebSocketException wse)
            {
                this.addLog($"WebSocketException: {wse.Message}",Enums.logType.WARNING);
                ret = false;
            }
            catch (Exception ex)
            {
                this.addLog($"Connection failed: {ex.Message}", Enums.logType.WARNING);
                ret = false;
            }
            return ret;
        }
        public async Task<bool> connectPrivateAsync()
        {
            bool ret = true;
            this.addLog("Connecting to private channel of " + gmocoin_connection.CONN_NAME);
            this.pv_memory.SetLength(0);
            this.pv_memory.Position = 0;
            this.private_client = new ClientWebSocket();

            JsonDocument accToken = await this.getToken();

            if(accToken.RootElement.GetProperty("status").GetInt32() != 0)
            {
                addLog("Failed to obtain the access token", logType.ERROR);
                return false;
            }

            this.token = accToken.RootElement.GetProperty("data").GetString();
            this.lastAccTokenObtained = DateTime.ParseExact(accToken.RootElement.GetProperty("responsetime").GetString(), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            var uri = new Uri(gmocoin_connection.private_wss + "/v1/" + this.token);

            try
            {
                this.private_client.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await this.private_client.ConnectAsync(uri, CancellationToken.None);
                this.addLog("Connected to " + gmocoin_connection.CONN_NAME);
                this.closeSentPublic = false;
            }
            catch (WebSocketException wse)
            {
                this.addLog($"WebSocketException: {wse.Message}", Enums.logType.WARNING);
                ret = false;
            }
            catch (Exception ex)
            {
                this.addLog($"Connection failed: {ex.Message}", Enums.logType.WARNING);
                ret = false;
            }
            return ret;
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
            string symbol;
            if (quoteCcy == "")
            {
                symbol = baseCcy.ToUpper();
            }
            else
            {
                symbol = baseCcy.ToUpper() + "_" + quoteCcy.ToUpper();
            }
            string subscribeJson = "{ \"command\" : \"subscribe\", \"channel\": \"trades\", \"symbol\": \"" + symbol + "\" }";
            var bytes = Encoding.UTF8.GetBytes(subscribeJson);
            if (this.websocket_client.State == WebSocketState.Open)
            {
                await this.websocket_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            string channel_name = "trade_" + symbol;
            if (!this.subscribingChannels.Contains(channel_name))
            {
                this.subscribingChannels.Add(channel_name);
            }
        }

        public async Task subscribeOrderBook(string baseCcy, string quoteCcy)
        {
            string symbol;
            if(quoteCcy == "")
            {
                symbol = baseCcy.ToUpper();
            }
            else
            {
                symbol = baseCcy.ToUpper() + "_" + quoteCcy.ToUpper();
            }
            string subscribeJson = "{ \"command\" : \"subscribe\", \"channel\": \"orderbooks\", \"symbol\": \"" + symbol + "\" }";
            var bytes = Encoding.UTF8.GetBytes(subscribeJson);
            if (this.websocket_client.State == WebSocketState.Open)
            {
                await this.websocket_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            string channel_name = "orderbook_" + symbol;
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
                this.addLog("Cancel Requested. public listening", Enums.logType.WARNING);
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

        public async Task subscribeOrderEvent()
        {
            var subscribeJson = "{\"command\":\"subscribe\", \"channel\":\"orderEvents\"}";
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
            var subscribeJson = "{\"command\":\"subscribe\", \"channel\":\"executionEvents\"}";
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
                this.addLog("Cancel Requested. private listening", Enums.logType.WARNING);
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
        private string buildCanonicalQuery(IDictionary<string, string> extraParams)
        {
            string canonicalQuery = "";
            if (extraParams != null)
            {
                canonicalQuery = "?" + string.Join("&",
                extraParams.Select(kv =>
                    $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            }

            return canonicalQuery;
        }

        private async Task<string> publicGetAsync(string endpoint,string body = "")
        {
            try
            {
                if (this.refreshing == 0)
                {


                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var watchDogCts = new CancellationTokenSource();

                    if (this.logging)
                    {
                        this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   GET " + endpoint + " " + body);
                        //this.logFilePublic.Flush();
                    }

                    var watchDog = Task.Delay(10000, watchDogCts.Token);
                    var task = this.http_client.GetStringAsync(gmocoin_connection.public_URL + endpoint, cts.Token);

                    var compleretedTask = await Task.WhenAny(task, watchDog);

                    if (compleretedTask == watchDog)
                    {
                        this.addLog("The request has timed out.(WatchDog)", Enums.logType.WARNING);
                        return "{\"status\":80001}";
                    }
                    watchDogCts.Cancel();

                    if (sw_POST.Elapsed.TotalNanoseconds > 3_000_000_000)
                    {
                        this.addLog("The roundtrip time exceeded 3 sec.    Time:" + (sw_POST.Elapsed.TotalNanoseconds / 1_000_000_000).ToString("N3") + "[sec]", Enums.logType.WARNING);
                    }

                    var response = await task;

                    if (this.logging)
                    {
                        this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   POST ack " + response);
                    }

                    return response;
                }
                else
                {
                    this.addLog("The http client is being refreshed.", Enums.logType.WARNING);
                    return "{\"status\":80002}";
                }
            }
            catch (TaskCanceledException tce)
            {
                this.addLog("The request has timed out." + tce.Message, Enums.logType.WARNING);
                return "{\"status\":80001}";
            }
            catch (TimeoutException te)
            {
                this.addLog("The request has timed out." + te.Message, Enums.logType.WARNING);
                return "{\"status\":80001}";
            }
            catch (Exception ex)
            {
                this.addLog("Error occured during getAsync. " + ex.Message, Enums.logType.ERROR);
                return "{\"status\":-1}";
            }
        }
        private async Task<string> privateGetAsync(string endpoint,string parameter = "", string body = "")
        {
            try
            {
                if (this.refreshing == 0)
                {
                    var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    string method = "GET";
                    var message = $"{nonce}{method}{endpoint}";

                    var request = new HttpRequestMessage(HttpMethod.Get, gmocoin_connection.private_URL + endpoint + parameter);


                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    request.Headers.Add("API-KEY", this.apiName);
                    request.Headers.Add("API-TIMESTAMP", nonce.ToString());
                    request.Headers.Add("API-SIGN", ToSha256(this.secretKey, message));

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var watchDogCts = new CancellationTokenSource();

                    if (this.logging)
                    {
                        this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   GET " + endpoint + " " + body);
                        //this.logFilePublic.Flush();
                    }

                    var watchDog = Task.Delay(10000, watchDogCts.Token);
                    var task = this.http_client.SendAsync(request, cts.Token);

                    var compleretedTask = await Task.WhenAny(task, watchDog);

                    if (compleretedTask == watchDog)
                    {
                        this.addLog("The request has timed out.(WatchDog)", Enums.logType.WARNING);
                        return "{\"status\":80001}";
                    }
                    watchDogCts.Cancel();

                    if (sw_POST.Elapsed.TotalNanoseconds > 3_000_000_000)
                    {
                        this.addLog("The roundtrip time exceeded 3 sec.    Time:" + (sw_POST.Elapsed.TotalNanoseconds / 1_000_000_000).ToString("N3") + "[sec]", Enums.logType.WARNING);
                    }

                    var response = await task;

                    var resString = await response.Content.ReadAsStringAsync();


                    if (this.logging)
                    {
                        this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   GET ack " + resString);
                    }

                    return resString;
                }
                else
                {
                    this.addLog("The http client is being refreshed.", Enums.logType.WARNING);
                    return "{\"status\":80002}";
                }
            }
            catch (TaskCanceledException tce)
            {
                this.addLog("The request has timed out." + tce.Message, Enums.logType.WARNING);
                return "{\"status\":80001}";
            }
            catch (TimeoutException te)
            {
                this.addLog("The request has timed out." + te.Message, Enums.logType.WARNING);
                return "{\"status\":80001}";
            }
            catch (Exception ex)
            {
                this.addLog("Error occured during getAsync. " + ex.Message, Enums.logType.ERROR);
                return "{\"status\":-1}";
            }
        }

        private async Task<string> privatePostAsync(string endpoint, string body = "")
        {
            try
            {
                if (this.refreshing == 0)
                {
                    var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    string method = "POST";
                    var message = $"{nonce}{method}{endpoint}";

                    var request = new HttpRequestMessage(HttpMethod.Post, gmocoin_connection.private_URL + endpoint);

                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    request.Headers.Add("API-KEY", this.apiName);
                    request.Headers.Add("API-TIMESTAMP", nonce.ToString());
                    request.Headers.Add("API-SIGN", ToSha256(this.secretKey, message));

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
                        return "{\"status\":80001}";
                    }
                    watchDogCts.Cancel();

                    if (sw_POST.Elapsed.TotalNanoseconds > 3_000_000_000)
                    {
                        this.addLog("The roundtrip time exceeded 3 sec.    Time:" + (sw_POST.Elapsed.TotalNanoseconds / 1_000_000_000).ToString("N3") + "[sec]", Enums.logType.WARNING);
                    }

                    var response = await task;

                    //sw_POST.Reset();
                    var resString = await response.Content.ReadAsStringAsync();


                    if (this.logging)
                    {
                        this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   POST ack " + resString);
                        //this.logFilePublic.Flush();
                    }

                    return resString;
                }
                else
                {
                    this.addLog("The http client is being refreshed.", Enums.logType.WARNING);
                    return "{\"status\":80002}";
                }
            }
            catch (TaskCanceledException tce)
            {
                this.addLog("The request has timed out." + tce.Message, Enums.logType.WARNING);
                return "{\"status\":80001}";
            }
            catch (TimeoutException te)
            {
                this.addLog("The request has timed out." + te.Message, Enums.logType.WARNING);
                return "{\"status\":80001}";
            }
            catch (Exception ex)
            {
                this.addLog("Error occured during postAsync. " + ex.Message, Enums.logType.ERROR);
                return "{\"status\":-1}";
            }
        }
        private async Task<string> privatePutAsync(string endpoint, string body = "")
        {
            try
            {
                if (this.refreshing == 0)
                {
                    var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    string method = "PUT";
                    var message = $"{nonce}{method}{endpoint}";

                    var request = new HttpRequestMessage(HttpMethod.Put, gmocoin_connection.private_URL + endpoint);

                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    request.Headers.Add("API-KEY", this.apiName);
                    request.Headers.Add("API-TIMESTAMP", nonce.ToString());
                    request.Headers.Add("API-SIGN", ToSha256(this.secretKey, message));

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var watchDogCts = new CancellationTokenSource();

                    if (this.logging)
                    {
                        this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   PUT " + endpoint + " " + body);
                        //this.logFilePublic.Flush();
                    }

                    var watchDog = Task.Delay(10000, watchDogCts.Token);
                    var task = this.http_client.SendAsync(request, cts.Token);

                    var compleretedTask = await Task.WhenAny(task, watchDog);

                    if (compleretedTask == watchDog)
                    {
                        this.addLog("The request has timed out.(WatchDog)", Enums.logType.WARNING);
                        return "{\"status\":80001}";
                    }
                    watchDogCts.Cancel();

                    var response = await task;

                    var resString = await response.Content.ReadAsStringAsync();


                    if (this.logging)
                    {
                        this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   PUT ack " + resString);
                    }

                    return resString;
                }
                else
                {
                    this.addLog("The http client is being refreshed.", Enums.logType.WARNING);
                    return "{\"status\":80002}";
                }
            }
            catch (TaskCanceledException tce)
            {
                this.addLog("The request has timed out." + tce.Message, Enums.logType.WARNING);
                return "{\"status\":80001}";
            }
            catch (TimeoutException te)
            {
                this.addLog("The request has timed out." + te.Message, Enums.logType.WARNING);
                return "{\"status\":80001}";
            }
            catch (Exception ex)
            {
                this.addLog("Error occured during postAsync. " + ex.Message, Enums.logType.ERROR);
                return "{\"status\":-1}";
            }
        }
        private async Task<string> privateDeleteAsync(string endpoint, string body = "")
        {
            try
            {
                if (this.refreshing == 0)
                {
                    var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    string method = "DELETE";
                    var message = $"{nonce}{method}{endpoint}";

                    var request = new HttpRequestMessage(HttpMethod.Delete, gmocoin_connection.private_URL + endpoint);

                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    request.Headers.Add("API-KEY", this.apiName);
                    request.Headers.Add("API-TIMESTAMP", nonce.ToString());
                    request.Headers.Add("API-SIGN", ToSha256(this.secretKey, message));

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var watchDogCts = new CancellationTokenSource();

                    if (this.logging)
                    {
                        this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   DELETE " + endpoint + " " + body);
                        //this.logFilePublic.Flush();
                    }

                    var watchDog = Task.Delay(10000, watchDogCts.Token);
                    var task = this.http_client.SendAsync(request, cts.Token);

                    var compleretedTask = await Task.WhenAny(task, watchDog);

                    if (compleretedTask == watchDog)
                    {
                        this.addLog("The request has timed out.(WatchDog)", Enums.logType.WARNING);
                        return "{\"status\":80001}";
                    }
                    watchDogCts.Cancel();

                    var response = await task;

                    var resString = await response.Content.ReadAsStringAsync();


                    if (this.logging)
                    {
                        this.msgLogQueue.Enqueue(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") + "   DELETE ack " + resString);
                    }

                    return resString;
                }
                else
                {
                    this.addLog("The http client is being refreshed.", Enums.logType.WARNING);
                    return "{\"status\":80002}";
                }
            }
            catch (TaskCanceledException tce)
            {
                this.addLog("The request has timed out." + tce.Message, Enums.logType.WARNING);
                return "{\"status\":80001}";
            }
            catch (TimeoutException te)
            {
                this.addLog("The request has timed out." + te.Message, Enums.logType.WARNING);
                return "{\"status\":80001}";
            }
            catch (Exception ex)
            {
                this.addLog("Error occured during postAsync. " + ex.Message, Enums.logType.ERROR);
                return "{\"status\":-1}";
            }
        }

        public async Task<JsonDocument> getToken()
        {

            var resString = await this.privatePostAsync("/v1/ws-auth");
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> extendToken(string token)
        {
            string reqBody = "{ \"token\": \"" + token + "\"}";
            var resString = await this.privatePutAsync("/v1/ws-auth",reqBody);
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> deleteToken(string token)
        {
            string reqBody = "{ \"token\": \"" + token + "\"}";
            var resString = await this.privateDeleteAsync("/v1/ws-auth", reqBody);
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getTicker(string symbol)
        {
            var resString = await this.privateGetAsync("/api/ticker?pair=" + symbol);
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
                var resString = await this.privateGetAsync("/api/trades" + query);
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
        public async Task<List<JsonElement>> getTradeHistory(string symbol = "", int page = 1, int count = 100)
        {
            addLog("getTradeHistory Called");
            List<JsonElement> res = new List<JsonElement>();
            if (page < 1)
            {
                page = 1;
            }

            if (count > 100 || count <= 0)
            {
                count = 100;
            }
            Dictionary<string, string> js = new Dictionary<string, string>();

            int i = 1;
            while(i <= page)
            {
                if (!string.IsNullOrEmpty(symbol))
                {
                    js["symbol"] = symbol;
                }
                js["page"] = i.ToString();
                js["count"] = count.ToString();
                string str_param = "?" + this.getCanonicalQuery(js);
                var resString = await this.privateGetAsync("/v1/latestExecutions", str_param);
                var json = JsonDocument.Parse(resString);
                JsonElement result;
                if(json.RootElement.TryGetProperty("status",out result) && result.GetUInt16() == 0)
                {
                    JsonElement data;
                    if(json.RootElement.TryGetProperty("data",out data))
                    {
                        //JsonElement pagenation;
                        JsonElement l;
                        //if(data.TryGetProperty("pagenation",out pagenation))
                        //{
                        //    addLog("Check Page:" + data.GetProperty("currentPage").GetInt32().ToString());
                        //    addLog("Check Count:" + data.GetProperty("count").GetInt32().ToString());
                        //}
                        //else
                        //{
                        //    break;
                        //}
                        if (data.TryGetProperty("list", out l))
                        {
                            foreach (var item in l.EnumerateArray())
                            {
                                res.Add(item);
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                js.Clear();
                ++i;
            }
            return res;
        }
        //public async Task<List<JsonElement>> getTradeHistoryPagenation(DateTime? startTime = null, DateTime? endTime = null)
        //{
        //    List<JsonElement> allTransactions = new List<JsonElement>();
        //    string starting_after = null;
        //    int limit = 100;
        //    DateTime currentTime = DateTime.UtcNow;
        //    while (true)
        //    {
        //        string query = $"?limit={limit}&order=desc";
        //        if (!string.IsNullOrEmpty(starting_after))
        //            query += $"&starting_after={starting_after}";
        //        var resString = await this.privateGetAsync("/api/exchange/orders/transactions_pagination" + query);
        //        var json = JsonDocument.Parse(resString);

        //        if (!json.RootElement.TryGetProperty("data", out JsonElement transactions) || transactions.GetArrayLength() == 0)
        //            break;

        //        foreach (var tx in transactions.EnumerateArray())
        //        {
        //            currentTime = DateTime.Parse(tx.GetProperty("created_at").GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
        //            if (endTime != null && currentTime > endTime)
        //            {

        //            }
        //            else if (startTime != null && currentTime < startTime)
        //            {
        //                break;
        //            }
        //            else
        //            {
        //                allTransactions.Add(tx);
        //            }
        //        }
        //        if (startTime != null && currentTime < startTime)
        //        {
        //            break;
        //        }


        //        starting_after = transactions[transactions.GetArrayLength() - 1].GetProperty("id").GetInt64().ToString();
        //    }
        //    return allTransactions;
        //}

        public string getCanonicalQuery(Dictionary<string,string> parameters)
        {
            return  string.Join("&",
                parameters.Select(kv =>
                    $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        }
        public async Task<JsonDocument> getMargin()
        {
            var resString = await this.privateGetAsync("/v1/account/margin");
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getBalance()
        {
            var resString = await this.privateGetAsync("/v1/account/assets");
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getActiveOrders(string symbol)
        {
            Dictionary<string,string> param = new Dictionary<string,string>();
            param["symbol"] = symbol;
            string str_param = "?" + this.getCanonicalQuery(param);
            var resString = await this.privateGetAsync("/v1/activeOrders",str_param);
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getOpenPositions(string symbol)
        {
            Dictionary<string, string> param = new Dictionary<string, string>();
            param["symbol"] = symbol;
            string str_param = "?" + this.getCanonicalQuery(param);
            var resString = await this.privateGetAsync("/v1/openPositions", str_param);
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> getMarginPosition(string symbol = "")
        {
            string str_param = "";
            if (symbol != "")
            {
                Dictionary<string, string> param = new Dictionary<string, string>();
                param["symbol"] = symbol;
                str_param = "?" + this.getCanonicalQuery(param);
            }
            var resString = await this.privateGetAsync("/v1/positionSummary", str_param);
            var json = JsonDocument.Parse(resString);
            return json;
        }
        
        public async Task<JsonDocument> placeNewOrder(string symbol, string side, decimal price = 0, decimal quantity = 0, string tif = "FOK")//If postonly set tif as "SOK"
        {
            var body = new
            {
                symbol = symbol,
                executionType = "LIMIT",
                size = quantity.ToString(),
                price = price.ToString(),
                side = side.ToUpper(),
                time_in_force = tif
            };
            

            var jsonBody = JsonSerializer.Serialize(body);
            //var sw = Stopwatch.StartNew();
            var resString = await this.privatePostAsync("/v1/order", jsonBody);
            var json = JsonDocument.Parse(resString);
            return json;
        }
        public async Task<JsonDocument> placeCloseOrder(string symbol, string side, decimal price = 0, decimal quantity = 0, string tif = "FOK")//If postonly set tif as "SOK"
        {
            var body = new
            {
                symbol = symbol,
                executionType = "LIMIT",
                size = quantity.ToString(),
                price = price.ToString(),
                side = side.ToUpper(),
                time_in_force = tif
            };


            var jsonBody = JsonSerializer.Serialize(body);
            //var sw = Stopwatch.StartNew();
            var resString = await this.privatePostAsync("/v1/closeBulkOrder", jsonBody);
            var json = JsonDocument.Parse(resString);
            return json;
        }

        public async Task<JsonDocument> placeMarketNewOrder(string symbol, string side, decimal price = 0, decimal quantity = 0)
        {
            var body = new
            {
                symbol = symbol,
                executionType = "MARKET",
                size = quantity.ToString(),
                side = side.ToUpper(),
            };


            var jsonBody = JsonSerializer.Serialize(body);
            var resString = await this.privatePostAsync("/v1/order", jsonBody);
            var json = JsonDocument.Parse(resString);
            return json;
            
        }
        public async Task<JsonDocument> placeModOrder(string ordid, decimal price = 0)
        {
            var body = new
            {
                orderid = ordid,
                price = price.ToString()
            };


            var jsonBody = JsonSerializer.Serialize(body);
            //var sw = Stopwatch.StartNew();
            var resString = await this.privatePostAsync("/v1/changeOrder", jsonBody);
            var json = JsonDocument.Parse(resString);
            return json;
        }

        public async Task<JsonDocument> placeCanOrder(string order_id)
        {
            var body = new
            {
                orderid = order_id
            };
            var jsonBody = JsonSerializer.Serialize(body);
            var resString = await this.privatePostAsync("/v1/cancelOrder", jsonBody);
            var json = JsonDocument.Parse(resString);
            return json;
        }

        public async Task<JsonDocument> placeCanOrders(IEnumerable<string> order_ids)
        {
            IEnumerable<Int64> orderIdInts = order_ids.Select(s => Int64.Parse(s));
            var body = new
            {
                orderids = orderIdInts
            };
            var jsonBody = JsonSerializer.Serialize(body);
            var resString = await this.privatePostAsync("/v1/cancelOrder", jsonBody);
            var json = JsonDocument.Parse(resString);
            return json;

        }

        public async Task sendPing(bool isPrivate)
        {
            var bytes = Encoding.UTF8.GetBytes("{\"command\":\"ping\"}");
            if(isPrivate)
            {
                if (this.private_client.State == WebSocketState.Open)
                {
                    await this.private_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            else
            {
                if (this.websocket_client.State == WebSocketState.Open)
                {
                    await this.websocket_client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
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
            this._addLog("[gmocoin_connection]" + line,logtype);
        }

        private static gmocoin_connection _instance;
        private static readonly object _lockObject = new object();

        public static gmocoin_connection GetInstance()
        {
            lock (_lockObject)
            {
                if (_instance == null)
                {
                    _instance = new gmocoin_connection();
                }
                return _instance;
            }
        }
    }
}
