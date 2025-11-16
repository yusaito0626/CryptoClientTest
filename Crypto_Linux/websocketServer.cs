using Crypto_Clients;
using Crypto_Trading;
using Enums;
using PubnubApi.EndPoint;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Utils;
using static System.Reflection.Metadata.BlobBuilder;

namespace Crypto_Linux
{
    internal class websocketServer
    {
        private readonly HttpListener _listener = new();
        private readonly List<WebSocket> _clients = new();

        public Dictionary<string, masterInfo> masterInfos;
        public Dictionary<string, strategySetting> strategySetting;
        public List<logEntry> logList = new List<logEntry>();
        public List<fillInfo> dataFillList = new List<fillInfo>();
        
        public int sendingLogs = 0;
        public int sendingFills = 0;

        public Action<string, Enums.logType> _addLog;

        public Func<Task> onExitCommand;

        JsonSerializerOptions js_option = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private websocketServer()
        {

        }

        public async Task StartAsync(CancellationToken token)
        {
            string host = Environment.GetEnvironmentVariable("WS_HOST") ?? "http://localhost:8080/";
            this.addLog("Host: " + host);
            host = host.Trim('\"');
            _listener.Prefixes.Add(host);
            _listener.Start();
            this.addLog("WebSocket server started on " + host);

            while (!token.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                {
                    try
                    {
                        var wsContext = await context.AcceptWebSocketAsync(null);
                        var socket = wsContext.WebSocket;
                        _clients.Add(socket);
                        _ = HandleClient(socket, token);
                    }
                    catch (Exception ex)
                    {
                        this.addLog($"WebSocket Accept Error: {ex}");
                        context.Response.StatusCode = 500;
                        context.Response.Close();
                    }
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
        }

        private async Task HandleClient(WebSocket socket, CancellationToken token)
        {
            int PageSize = 10;
            int i = 0;
            string json = "";

            string msg;
            var buffer = new byte[4096];

            Dictionary<string,string> item = new Dictionary<string,string>();

            this.addLog("Client connected");

            json = JsonSerializer.Serialize(this.masterInfos);
            item["data_type"] = "master";
            item["data"] = json;
            msg = JsonSerializer.Serialize(item, this.js_option);
            await this.BroadcastAsync(msg);

            foreach (var stg in Program.strategies)
            {
                if(this.strategySetting.ContainsKey(stg.Key))
                {
                    strategySetting setting = this.strategySetting[stg.Key];
                    setting.name = stg.Value.name;
                    setting.baseCcy = stg.Value.baseCcy;
                    setting.quoteCcy = stg.Value.quoteCcy;
                    setting.taker_market = stg.Value.taker_market;
                    setting.maker_market = stg.Value.maker_market;
                    setting.markup = stg.Value.markup;
                    setting.min_markup = stg.Value.min_markup;
                    setting.max_skew = stg.Value.maxSkew;
                    setting.skew_widening = stg.Value.skewWidening;
                    setting.baseCcy_quantity = stg.Value.baseCcyQuantity;
                    setting.ToBsize = stg.Value.ToBsize;
                    setting.intervalAfterFill = stg.Value.intervalAfterFill;
                    setting.modThreshold = stg.Value.modThreshold;
                    setting.skewThreshold = stg.Value.skewThreshold;
                    setting.oneSideThreshold = stg.Value.oneSideThreshold;
                    this.strategySetting[stg.Key] = setting;
                }
                else
                {
                    strategySetting setting = new strategySetting();
                    setting.name = stg.Value.name;
                    setting.baseCcy = stg.Value.baseCcy;
                    setting.quoteCcy = stg.Value.quoteCcy;
                    setting.taker_market = stg.Value.taker_market;
                    setting.maker_market = stg.Value.maker_market;
                    setting.markup = stg.Value.markup;
                    setting.min_markup = stg.Value.min_markup;
                    setting.max_skew = stg.Value.maxSkew;
                    setting.skew_widening = stg.Value.skewWidening;
                    setting.baseCcy_quantity = stg.Value.baseCcyQuantity;
                    setting.ToBsize = stg.Value.ToBsize;
                    setting.intervalAfterFill = stg.Value.intervalAfterFill;
                    setting.modThreshold = stg.Value.modThreshold;
                    setting.skewThreshold = stg.Value.skewThreshold;
                    setting.oneSideThreshold = stg.Value.oneSideThreshold;
                    this.strategySetting[stg.Key] = setting;
                }
                    
            }
            json = JsonSerializer.Serialize(this.strategySetting);
            item["data_type"] = "strategySetting";
            item["data"] = json;
            msg = JsonSerializer.Serialize(item, this.js_option);
            await this.BroadcastAsync(msg);

            while (Interlocked.CompareExchange(ref this.sendingLogs,1,0) != 0)
            {

            }
            while (i < this.logList.Count)
            {
                List<logEntry> subList;
                if (i + PageSize >= this.logList.Count)
                {
                    subList = this.logList.GetRange(i, this.logList.Count - i);
                }
                else
                {
                    subList = this.logList.GetRange(i, PageSize);
                }
                json = JsonSerializer.Serialize(subList);
                item["data_type"] = "log";
                item["data"] = json;
                msg = JsonSerializer.Serialize(item, this.js_option);
                await this.BroadcastAsync(msg);
                i += PageSize;
            }
            Volatile.Write(ref this.sendingLogs, 0);
            while (Interlocked.CompareExchange(ref this.sendingFills, 1, 0) != 0)
            {

            }
            i = 0;
            while (i < this.dataFillList.Count)
            {
                List<fillInfo> subList;
                if (i + PageSize >= this.dataFillList.Count)
                {
                    subList = this.dataFillList.GetRange(i, this.dataFillList.Count - i);
                }
                else
                {
                    subList = this.dataFillList.GetRange(i, PageSize);
                }
                json = JsonSerializer.Serialize(subList);
                item["data_type"] = "fill";
                item["data"] = json;
                msg = JsonSerializer.Serialize(item, this.js_option);
                await this.BroadcastAsync(msg);
                i += PageSize;
            }
            Volatile.Write(ref this.sendingFills, 0);
            try
            {
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await socket.ReceiveAsync(buffer, token);
                    }
                    catch (WebSocketException ex)
                    {
                        this.addLog($"Client disconnected unexpectedly: {ex.Message}");
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        this.addLog("Client disconnected");
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token);
                        _clients.Remove(socket);
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    if (message == "ping")
                    {
                        await socket.SendAsync(
                            Encoding.UTF8.GetBytes("pong"),
                            WebSocketMessageType.Text,
                            true,
                            token
                        );
                    }
                    else if (message == "exit")
                    {
                        this.addLog("Recieved exit command from a client");
                        await onExitCommand();
                    }
                    else
                    {
                        var js = JsonDocument.Parse(message).RootElement;
                        string data_type = js.GetProperty("data_type").GetString();
                        string content = js.GetProperty("data").GetString();
                        switch (data_type)
                        {
                            case "variableUpdate":
                                {
                                    var newVar = JsonSerializer.Deserialize<variableUpdate>(content);
                                    if(Program.strategies.ContainsKey(newVar.stg_name))
                                    {
                                        decimal newvalue;
                                        Strategy stg = Program.strategies[newVar.stg_name];
                                        switch (newVar.type.ToLower())
                                        {
                                            case "markup":
                                                if(decimal.TryParse(newVar.value,out newvalue))
                                                {
                                                    addLog("The markup of " + stg.name + " has been changed from " + stg.markup.ToString("N0") + " to " + newVar.value);
                                                    stg.markup = newvalue;
                                                    await BroadcastAsync(message);
                                                }
                                                break;
                                            case "min_markup":
                                                if (decimal.TryParse(newVar.value, out newvalue))
                                                {
                                                    addLog("The minimum markup of " + stg.name + " has been changed from " + stg.min_markup.ToString("N0") + " to " + newVar.value);
                                                    stg.min_markup = newvalue;
                                                    await BroadcastAsync(message);
                                                }
                                                break;
                                            case "max_skew":
                                                if (decimal.TryParse(newVar.value, out newvalue))
                                                {
                                                    addLog("The max skew of " + stg.name + " has been changed from " + stg.maxSkew.ToString("N0") + " to " + newVar.value);
                                                    stg.maxSkew = newvalue;
                                                    await BroadcastAsync(message);
                                                }
                                                break;
                                            case "skewwidenng":
                                                if (decimal.TryParse(newVar.value, out newvalue))
                                                {
                                                    addLog("The skew widening of " + stg.name + " has been changed from " + stg.skewWidening.ToString("N0") + " to " + newVar.value);
                                                    stg.skewWidening = newvalue;
                                                    await BroadcastAsync(message);
                                                }
                                                break;
                                            case "baseccyquantity":
                                                if (decimal.TryParse(newVar.value, out newvalue))
                                                {
                                                    addLog("The total quantity of base currency of " + stg.name + " has been changed from " + stg.baseCcyQuantity.ToString("N0") + " to " + newVar.value);
                                                    stg.baseCcyQuantity = newvalue;
                                                    await BroadcastAsync(message);
                                                }
                                                break;
                                            case "tobsize":
                                                if (decimal.TryParse(newVar.value, out newvalue))
                                                {
                                                    addLog("The TOB size of " + stg.name + " has been changed from " + stg.ToBsize.ToString("N0") + " to " + newVar.value);
                                                    stg.ToBsize = newvalue;
                                                    await BroadcastAsync(message);
                                                }
                                                break;
                                            case "intervalafterfill":
                                                if (decimal.TryParse(newVar.value, out newvalue))
                                                {
                                                    addLog("The interval after a fill of " + stg.name + " has been changed from " + stg.intervalAfterFill.ToString("N0") + " to " + newVar.value);
                                                    stg.intervalAfterFill = newvalue;
                                                    await BroadcastAsync(message);
                                                }
                                                break;
                                            case "modthreashold":
                                                if (decimal.TryParse(newVar.value, out newvalue))
                                                {
                                                    addLog("The mod threshold of " + stg.name + " has been changed from " + stg.modThreshold.ToString("N0") + " to " + newVar.value);
                                                    stg.modThreshold = newvalue;
                                                    await BroadcastAsync(message);
                                                }
                                                break;
                                            case "skewthreshold":
                                                if (decimal.TryParse(newVar.value, out newvalue))
                                                {
                                                    addLog("The skew threshold of " + stg.name + " has been changed from " + stg.skewThreshold.ToString("N0") + " to " + newVar.value);
                                                    stg.skewThreshold = newvalue;
                                                    await BroadcastAsync(message);
                                                }
                                                break;
                                            case "onesidethreshold":
                                                if (decimal.TryParse(newVar.value, out newvalue))
                                                {
                                                    addLog("The one-side threshold of " + stg.name + " has been changed from " + stg.oneSideThreshold.ToString("N0") + " to " + newVar.value);
                                                    stg.oneSideThreshold = newvalue;
                                                    await BroadcastAsync(message);
                                                }
                                                break;
                                            default:
                                                break;
                                        }
                                    }
                                    
                                }
                                break;
                            default:
                                break;
                        }
                    }


                }
            }
            finally
            {
                addLog("The client removed.");
                _clients.Remove(socket);
                if (socket.State != WebSocketState.Closed)
                {
                    try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token); }
                    catch { }
                }
            }
        }

        public async Task processFill(fillInfo fill)
        {
            while (Interlocked.CompareExchange(ref this.sendingFills, 1, 0) != 0)
            {

            }
            this.dataFillList.Add(fill);
            Dictionary<string, string> sendingItem = new Dictionary<string, string>();
            string json = JsonSerializer.Serialize<List<fillInfo>>([fill]);
            string msg;
            sendingItem["data_type"] = "fill";
            sendingItem["data"] = json;
            msg = JsonSerializer.Serialize(sendingItem, this.js_option);
            this.BroadcastAsync(msg);
            Volatile.Write(ref this.sendingFills, 0);
        }
        public async Task processLog(logEntry log)
        {
            while (Interlocked.CompareExchange(ref this.sendingLogs, 1, 0) != 0)
            {

            }
            if(log.msg != null && log.msg.Contains("Error code:10009"))
            {

            }
            else
            {
                this.logList.Add(log);

                string json = JsonSerializer.Serialize<List<logEntry>>([log]);
                string msg;
                Dictionary<string, string> sendingItem = new Dictionary<string, string>();
                sendingItem["data_type"] = "log";
                sendingItem["data"] = json;
                msg = JsonSerializer.Serialize(sendingItem, this.js_option);
                this.BroadcastAsync(msg);
            }
            Volatile.Write(ref this.sendingLogs, 0);
        }
        public async Task setMasterInfo(Dictionary<string,masterInfo> msinfos)
        {
            string js = JsonSerializer.Serialize(msinfos);
            string msg;
            Dictionary<string, string> items = new Dictionary<string, string>();
            items["data_type"] = "master";
            items["data"] = js;
            msg = JsonSerializer.Serialize(items, this.js_option);
            await this.BroadcastAsync(msg);
            this.masterInfos = msinfos;
        }

        public async Task setStrategySetting(Dictionary<string, strategySetting> strategies)
        {
            string js = JsonSerializer.Serialize(strategies);
            string msg;
            Dictionary<string, string> items = new Dictionary<string, string>();
            items["data_type"] = "strategySetting";
            items["data"] = js;
            msg = JsonSerializer.Serialize(items, this.js_option);
            await this.BroadcastAsync(msg);
            this.strategySetting = strategies;
        }
        public async Task BroadcastAsync(string message)
        {
            var data = Encoding.UTF8.GetBytes(message);
            var seg = new ArraySegment<byte>(data);

            foreach (var ws in _clients.ToList())
            {
                if (ws.State != WebSocketState.Open)
                {
                    _clients.Remove(ws);
                    continue;
                }
                try
                {
                    await ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch (WebSocketException)
                {
                    this.addLog("Removing disconnected client during broadcast");
                    _clients.Remove(ws);
                }
            }
        }

        public void addLog(string line, logType logtype = logType.INFO)
        {
            this._addLog("[websocketServer]" + line, logtype);
        }

        private static websocketServer _instance;
        private static readonly object _lockObject = new object();

        public static websocketServer GetInstance()
        {
            lock (_lockObject)
            {
                if (_instance == null)
                {
                    _instance = new websocketServer();
                }
                return _instance;
            }
        }
    }
}
