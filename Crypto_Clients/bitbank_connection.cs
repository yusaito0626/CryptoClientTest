using Bitget.Net.Objects.Models.V2;
using CryptoExchange.Net.SharedApis;
using PubnubApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        public ConcurrentQueue<JsonElement> orderQueue;
        public ConcurrentQueue<JsonElement> fillQueue;

        public Action<string> addLog;

        public bitbank_connection()
        {
            this.apiName = "";
            this.secretKey = "";

            this.orderQueue = new ConcurrentQueue<JsonElement>();
            this.fillQueue = new ConcurrentQueue<JsonElement>();

            this.addLog = Console.WriteLine;
        }
        public void SetApiCredentials(string name,string key)
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

        private async Task<string> postAsync(string endpoint,string body)
        {
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timeWindow = "5000";
            var message = $"{nonce}{timeWindow}{endpoint}";

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
            var resString = await this.postAsync("/user/spot/order", jsonBody);

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
            var resString = await this.postAsync("/user/spot/cancel_order", jsonBody);

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
                        this.addLog("message received: " + messageResult.Message.ToString());
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
                    this.addLog("status: " + status.Category);

                    switch (status.Category)
                    {
                        case PNStatusCategory.PNConnectedCategory:
                            this.addLog("pubnub connection established");
                            break;

                        case PNStatusCategory.PNReconnectedCategory:
                            this.addLog("pubnub connection restored");
                            this.subscribePrivateChannels(pubnubObj, channel, token);
                            break;

                        case PNStatusCategory.PNTimeoutCategory:
                        case PNStatusCategory.PNNetworkIssuesCategory:
                        case PNStatusCategory.PNAccessDeniedCategory:
                            this.addLog("pubnub reconnecting...");
                            await connect();
                            break;

                        default:
                            this.addLog("status default");
                            break;
                    }
                }));

            return pubnub;
        }
        public async Task connect()
        {
            var (channel, token) = await this.GetChannelAndToken();
            var pubnub = this.GetPubNubAndAddListener(channel, token);
            this.subscribePrivateChannels(pubnub, channel, token);
        }
        private string ToSha256(string key, string value)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
    }
}
