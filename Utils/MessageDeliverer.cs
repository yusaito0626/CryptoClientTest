using Discord;
using Discord.WebSocket;
using Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Utils
{
    public class MessageDeliverer
    {
        private string discord_token;
        private UInt64 discord_channel;
        private UInt64 discord_channel_error;
        private UInt64 discord_channel_test;
        private DiscordSocketClient discord_client;

        private MessageDeliverer()
        {
            this.discord_token = "";
            this.discord_channel = 0;
        }

        public async Task setDiscordToken(string token, UInt64 channel)
        {

            this.discord_client = new DiscordSocketClient();
            this.discord_token = token;
            this.discord_channel = channel;
            await this.discord_client.LoginAsync(TokenType.Bot, this.discord_token);
            await this.discord_client.StartAsync();
        }
        public async Task<bool> setDiscordToken(string jsonfile)
        {
            if(File.Exists(jsonfile))
            {
                string fileContent = File.ReadAllText(jsonfile);

                using JsonDocument doc = JsonDocument.Parse(fileContent);
                this.discord_client = new DiscordSocketClient();
                this.discord_token = doc.RootElement.GetProperty("token").GetString();
                this.discord_channel = doc.RootElement.GetProperty("channel").GetUInt64();
                JsonElement temp;
                if(doc.RootElement.TryGetProperty("channel_error",out temp))
                {
                    this.discord_channel_error = temp.GetUInt64();
                }
                if(doc.RootElement.TryGetProperty("channel_test",out temp))
                {
                    this.discord_channel_test = temp.GetUInt64();
                }

                await this.discord_client.LoginAsync(TokenType.Bot, this.discord_token);
                await this.discord_client.StartAsync();
                return true;
            }
            else
            {
                return false;
            }
        }

        public async Task sendMessage(string body,msgType msg_type = msgType.NOTIFICATION)
        {
            if(this.discord_client != null)
            {
                switch (msg_type)
                {
                    case msgType.NOTIFICATION:
                        if(this.discord_channel > 0)
                        {
                            var channel = this.discord_client.GetChannel(this.discord_channel) as IMessageChannel;
                            if (channel != null)
                            {
                                await channel.SendMessageAsync(body);
                            }
                        }
                        break;
                    case msgType.ERROR:
                        if (this.discord_channel_error > 0)
                        {
                            var channel = this.discord_client.GetChannel(this.discord_channel_error) as IMessageChannel;
                            if (channel != null)
                            {
                                await channel.SendMessageAsync(body);
                            }
                        }
                        else if(this.discord_channel > 0)
                        {
                            var channel = this.discord_client.GetChannel(this.discord_channel) as IMessageChannel;
                            if (channel != null)
                            {
                                await channel.SendMessageAsync(body);
                            }
                        }
                        break;
                    case msgType.TEST:
                        if (this.discord_channel_test > 0)
                        {
                            var channel = this.discord_client.GetChannel(this.discord_channel_test) as IMessageChannel;
                            if (channel != null)
                            {
                                await channel.SendMessageAsync(body);
                            }
                        }
                        else if (this.discord_channel > 0)
                        {
                            var channel = this.discord_client.GetChannel(this.discord_channel) as IMessageChannel;
                            if (channel != null)
                            {
                                await channel.SendMessageAsync(body);
                            }
                        }
                        break;
                }
            }
            
            //if(this.discord_client != null && this.discord_channel > 0)
            //{
            //    var channel = this.discord_client.GetChannel(this.discord_channel) as IMessageChannel;
            //    if(channel != null)
            //    {
            //        await channel.SendMessageAsync(body);
            //    }
            //}
        }

        private static MessageDeliverer _instance;
        private static readonly object _lockObject = new object();

        public static MessageDeliverer GetInstance()
        {
            lock (_lockObject)
            {
                if (_instance == null)
                {
                    _instance = new MessageDeliverer();
                }
                return _instance;
            }
        }
    }
}
