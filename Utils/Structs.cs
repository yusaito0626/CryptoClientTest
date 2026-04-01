using CryptoClients.Net;
using CryptoClients.Net.Enums;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Requests;
using CryptoExchange.Net.SharedApis;
using Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Globalization;
using Binance.Net.Enums;
using Discord.Rest;

namespace Utils
{
    public class DataBalance
    {
        public string asset;
        public Enums.market market;
        public decimal available;
        public decimal total;
        public DataBalance()
        {
            this.asset = "";
            this.market = market.NONE;
            this.available = 0;
            this.total = 0;
        }
        public void init()
        {
            this.asset = "";
            this.market = market.NONE;
            this.available = 0;
            this.total = 0;
        }
    }
    public class DataMarginPos
    {
        public string symbol;
        public Enums.market market;
        public string symbol_market
        {
            get { return symbol + "@" + market.ToString(); }
        }
        public positionSide side;
        public decimal quantity;
        public decimal avgPrice;
        public decimal unrealizedFee;
        public decimal unrealizedInterest;
        public decimal unrealizedPnL;

        public DataMarginPos()
        {
            this.symbol = "";
            this.market = market.NONE;
            this.side = positionSide.NONE;
            this.quantity = 0;
            this.avgPrice = 0;
            this.unrealizedFee = 0;
            this.unrealizedInterest = 0;
            this.unrealizedPnL = 0;
        }
        public void setBitbankJson(JsonElement js)
        {
            this.symbol = js.GetProperty("pair").GetString();
            this.market = market.bitbank;
            string str_side = js.GetProperty("position_side").GetString();
            if (str_side == "long")
            {
                this.side = positionSide.Long;
            }
            else if (str_side == "short")
            {
                this.side = positionSide.Short;
            }
            this.quantity = decimal.Parse(js.GetProperty("open_amount").GetString());
            this.avgPrice = decimal.Parse(js.GetProperty("average_price").GetString());
            this.unrealizedFee = decimal.Parse(js.GetProperty("unrealized_fee_amount").GetString());
            this.unrealizedInterest = decimal.Parse(js.GetProperty("unrealized_interest_amount").GetString());
        }
        public void setGMOCoinJson(JsonElement js)
        {
            this.symbol = js.GetProperty("symbol").GetString();
            this.market = market.gmocoin;
            string str_side = js.GetProperty("side").GetString();
            if (str_side == "BUY")
            {
                this.side = positionSide.Long;
            }
            else if (str_side == "SELL")
            {
                this.side = positionSide.Short;
            }
            this.quantity = decimal.Parse(js.GetProperty("sumPositionQuantity").GetString());
            this.avgPrice = decimal.Parse(js.GetProperty("averagePositionRate").GetString());
            this.unrealizedPnL = decimal.Parse(js.GetProperty("positionLossGain").GetString());
        }
        public string ToString()
        {
            string res = this.symbol + "," + this.market + "," + this.side.ToString() + "," + this.quantity.ToString() + "," + this.avgPrice.ToString() + "," + this.unrealizedFee.ToString() + "," + this.unrealizedInterest.ToString();
            return res;
        }
        public void init()
        {
            this.symbol = "";
            this.market = market.NONE;
            this.side = positionSide.NONE;
            this.quantity = 0;
            this.avgPrice = 0;
            this.unrealizedFee = 0;
            this.unrealizedInterest = 0;
            this.unrealizedPnL = 0;
        }
    }
    public class DataOrderBook
    {
        public DateTime? timestamp;
        public DateTime? orderbookTime;
        public Enums.market market;
        public string? streamId;
        public string? symbol;
        public SocketUpdateType? updateType;

        public Dictionary<decimal, decimal> asks;
        public Dictionary<decimal, decimal> bids;

        public long seqNo;

        public DataOrderBook()
        {
            this.timestamp = null;
            this.orderbookTime = null;
            this.market = market.NONE;
            this.streamId = "";
            this.symbol = "";
            this.updateType = null;
            this.asks = new Dictionary<decimal, decimal>();
            this.bids = new Dictionary<decimal, decimal>();
            this.seqNo = -1;
        }

        public void setSharedOrderBook(ExchangeEvent<SharedOrderBook> update)
        {
            this.timestamp = DateTime.UtcNow;
            this.orderbookTime = update.DataTime;
            this.updateType = update.UpdateType;
            this.market = market.NONE;//Undifined
            this.streamId = update.StreamId;
            this.symbol = update.Symbol;

            int i = 0;
            int dataLength = update.Data.Asks.Length;
            while (i < dataLength)
            {
                this.asks[update.Data.Asks[i].Price] = update.Data.Asks[i].Quantity;
                ++i;
            }

            i = 0;
            dataLength = update.Data.Bids.Length;
            while (i < dataLength)
            {
                this.bids[update.Data.Bids[i].Price] = update.Data.Bids[i].Quantity;
                ++i;
            }
        }

        public void setBybitOrderBook(DataEvent<Bybit.Net.Objects.Models.V5.BybitOrderbook> update)
        {
            this.timestamp = DateTime.UtcNow;
            this.orderbookTime = update.DataTime;
            this.updateType = update.UpdateType;
            this.market = market.bybit;
            this.streamId = update.StreamId;
            this.symbol = update.Symbol;

            int i = 0;
            int dataLength = update.Data.Asks.Length;
            while (i < dataLength)
            {
                this.asks[update.Data.Asks[i].Price] = update.Data.Asks[i].Quantity;
                ++i;
            }

            i = 0;
            dataLength = update.Data.Bids.Length;
            while (i < dataLength)
            {
                this.bids[update.Data.Bids[i].Price] = update.Data.Bids[i].Quantity;
                ++i;
            }
        }

        public void setCoinbaseOrderBook(DataEvent<Coinbase.Net.Objects.Models.CoinbaseOrderBookUpdate> update)
        {
            this.timestamp = DateTime.UtcNow;
            this.orderbookTime = update.DataTime;
            this.updateType = update.UpdateType;
            this.market = market.coinbase;
            this.streamId = update.StreamId;
            this.symbol = update.Symbol;

            int i = 0;
            int dataLength = update.Data.Asks.Length;
            while (i < dataLength)
            {
                this.asks[update.Data.Asks[i].Price] = update.Data.Asks[i].Quantity;
                ++i;
            }

            i = 0;
            dataLength = update.Data.Bids.Length;
            while (i < dataLength)
            {
                this.bids[update.Data.Bids[i].Price] = update.Data.Bids[i].Quantity;
                ++i;
            }
        }
        public void setGMOCoinOrderBook(JsonDocument js)
        {
            this.timestamp = DateTime.UtcNow;
            this.market = market.gmocoin;
            this.symbol = js.RootElement.GetProperty("symbol").GetString();
            this.updateType = SocketUpdateType.Snapshot;
            this.orderbookTime = Functions.convertToDateTime(js.RootElement.GetProperty("timestamp").GetString(), this.market);
            //this.orderbookTime = DateTime.ParseExact(js.RootElement.GetProperty("timestamp").GetString(), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            var asks = js.RootElement.GetProperty("asks").EnumerateArray();
            foreach (var item in asks)
            {
                this.asks[decimal.Parse(item.GetProperty("price").GetString())] = decimal.Parse(item.GetProperty("size").GetString());
            }
            var bids  = js.RootElement.GetProperty("bids").EnumerateArray();
            foreach (var item in bids)
            {
                this.bids[decimal.Parse(item.GetProperty("price").GetString())] = decimal.Parse(item.GetProperty("size").GetString());
            }
        }

        public void setBitbankOrderBook(JsonElement js, string symbol, bool snapshot)
        {
            this.timestamp = DateTime.UtcNow;
            this.market = market.bitbank;
            this.symbol = symbol;

            if (snapshot)
            {
                this.updateType = SocketUpdateType.Snapshot;
                this.orderbookTime = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("timestamp").GetInt64()).UtcDateTime;
                this.streamId = js.GetProperty("sequenceId").GetString();
                this.seqNo = Int64.Parse(this.streamId);
                var data = js.GetProperty("asks").EnumerateArray();
                foreach (var item in data)
                {
                    this.asks[decimal.Parse(item[0].GetString())] = decimal.Parse(item[1].GetString());
                }
                data = js.GetProperty("bids").EnumerateArray();
                foreach (var item in data)
                {
                    this.bids[decimal.Parse(item[0].GetString())] = decimal.Parse(item[1].GetString());
                }
            }
            else
            {
                this.updateType = SocketUpdateType.Update;
                this.orderbookTime = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("t").GetInt64()).UtcDateTime;
                this.streamId = js.GetProperty("s").GetString();
                this.seqNo = Int64.Parse(this.streamId);
                var data = js.GetProperty("a").EnumerateArray();
                foreach (var item in data)
                {
                    this.asks[decimal.Parse(item[0].GetString())] = decimal.Parse(item[1].GetString());
                }
                data = js.GetProperty("b").EnumerateArray();
                foreach (var item in data)
                {
                    this.bids[decimal.Parse(item[0].GetString())] = decimal.Parse(item[1].GetString());
                }
            }
        }
        public void setBitTradeOrderBook(JsonElement js, string symbol, Int64 unixTime)
        {
            this.timestamp = DateTime.UtcNow;
            this.market = market.bittrade;
            this.symbol = symbol;
            this.updateType = SocketUpdateType.Snapshot;
            this.orderbookTime = DateTimeOffset.FromUnixTimeMilliseconds(unixTime).UtcDateTime;
            var data = js.GetProperty("asks").EnumerateArray();
            foreach (var item in data)
            {
                this.asks[item[0].GetDecimal()] = item[1].GetDecimal();
            }
            data = js.GetProperty("bids").EnumerateArray();
            foreach (var item in data)
            {
                this.bids[item[0].GetDecimal()] = item[1].GetDecimal();
            }
        }
        public void setCoincheckOrderBook(JsonElement js, string symbol)
        {
            this.timestamp = DateTime.UtcNow;
            this.market = market.coincheck;
            this.symbol = symbol;
            JsonElement current_time;
            if (js.TryGetProperty("last_update_at", out current_time))
            {
                this.updateType = SocketUpdateType.Update;
                this.orderbookTime = DateTimeOffset.FromUnixTimeSeconds(Int64.Parse(js.GetProperty("last_update_at").GetString())).UtcDateTime;
            }
            else
            {
                this.updateType = SocketUpdateType.Snapshot;
            }
            var data = js.GetProperty("asks").EnumerateArray();
            foreach (var item in data)
            {
                this.asks[decimal.Parse(item[0].GetString())] = decimal.Parse(item[1].GetString());
            }
            data = js.GetProperty("bids").EnumerateArray();
            foreach (var item in data)
            {
                this.bids[decimal.Parse(item[0].GetString())] = decimal.Parse(item[1].GetString());
            }
        }

        public override string ToString()
        {
            string output = this.timestamp.ToString() + ",OrderBook," + this.streamId + "," + this.market + "," + this.updateType + "," + this.symbol;

            foreach (var item in this.asks)
            {
                output += ",Ask," + item.ToString();
            }
            foreach (var item in this.bids)
            {
                output += ",Bid," + item.ToString();
            }

            return output;

        }

        public void init()
        {
            this.timestamp = null;
            this.orderbookTime = null;
            this.market = market.NONE;
            this.streamId = "";
            this.symbol = "";
            this.updateType = null;
            this.asks.Clear();
            this.bids.Clear();
            this.seqNo = -1;
        }
    }
    public class DataFill
    {
        public DateTime? timestamp;
        public string symbol_market;
        public Enums.market market;
        public decimal order_quantity;
        public decimal executed_quantity;
        public decimal quantity;
        public DateTime? filled_time;
        public decimal fee_base;
        public decimal fee_quote;
        public decimal fee_unknown;
        public string maker_taker;
        public string order_id;
        public string position_id;
        public string internal_order_id;
        public string symbol;
        public decimal price;
        public orderSide side;
        public positionSide position_side;
        public string trade_id;
        public orderType order_type;
        public decimal profit_loss;
        public decimal interest;

        public double downStreamLatency;

        public string msg;
        public int queued_count;

        public DataFill()
        {
            this.timestamp = null;
            this.symbol_market = "";
            this.market = market.NONE;
            this.quantity = 0;
            this.order_quantity = 0;
            this.executed_quantity = 0;
            this.filled_time = null;
            this.fee_base = 0;
            this.fee_quote = 0;
            this.fee_unknown = 0;
            this.maker_taker = "";
            this.order_id = "";
            this.position_id = "";
            this.internal_order_id = "";
            this.symbol = "";
            this.price = 0;
            this.side = orderSide.NONE;
            this.position_side = positionSide.NONE;
            this.trade_id = "";
            this.order_type = orderType.NONE;
            this.profit_loss = 0;
            this.interest = 0;
            this.msg = "";
            this.queued_count = 0;
            this.downStreamLatency = 0;
        }

        public void setCoincheckFill(JsonElement js)
        {
            this.timestamp = DateTime.UtcNow;
            this.order_id = js.GetProperty("order_id").GetUInt64().ToString();
            this.trade_id = js.GetProperty("id").GetUInt64().ToString();
            JsonElement time;
            if (js.TryGetProperty("event_time", out time))
            {
                this.filled_time = DateTime.Parse(time.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
            }
            else if (js.TryGetProperty("created_at", out time))
            {
                this.filled_time = DateTime.Parse(time.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
            }

            this.symbol = js.GetProperty("pair").GetString();
            this.market = market.coincheck;
            this.symbol_market = this.symbol + "@" + this.market.ToString();
            this.internal_order_id = this.market + this.order_id;
            string maker_taker = js.GetProperty("liquidity").GetString();
            if (maker_taker == "M")
            {
                this.maker_taker = "maker";
            }
            else if (maker_taker == "T")
            {
                this.maker_taker = "taker";
            }
            this.price = decimal.Parse(js.GetProperty("rate").GetString());
            string[] assets = this.symbol.Split("_");
            string fee_ccy = js.GetProperty("fee_currency").GetString();
            if (fee_ccy == assets[0])
            {
                this.fee_base = decimal.Parse(js.GetProperty("fee").GetString());
                this.fee_quote = 0;
            }
            else if (fee_ccy == assets[1])
            {
                this.fee_base = 0;
                this.fee_quote = decimal.Parse(js.GetProperty("fee").GetString());
            }
            else
            {
                this.fee_base = 0;
                this.fee_unknown = decimal.Parse(js.GetProperty("fee").GetString());
            }
            string side = js.GetProperty("side").GetString();
            decimal temp_qt = decimal.Parse(js.GetProperty("funds").GetProperty(assets[0]).GetString());
            if (side == "buy")
            {
                this.side = orderSide.Buy;
            }
            else if (side == "sell")
            {
                this.side = orderSide.Sell;
                temp_qt *= -1;
            }
            else
            {
                this.side = orderSide.NONE;
            }
            this.position_side = positionSide.NONE;

            this.quantity = temp_qt;
            this.quantity += this.fee_base;
        }

        public void setBitBankFill(JsonElement js)
        {
            this.timestamp = DateTime.UtcNow;
            this.quantity = decimal.Parse(js.GetProperty("amount").GetString());
            this.filled_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("executed_at").GetInt64()).UtcDateTime;
            this.fee_base = decimal.Parse(js.GetProperty("fee_amount_base").GetString());
            this.fee_quote = decimal.Parse(js.GetProperty("fee_amount_quote").GetString());
            this.maker_taker = js.GetProperty("maker_taker").GetString();
            this.order_id = js.GetProperty("order_id").GetInt64().ToString();
            this.symbol = js.GetProperty("pair").GetString();
            this.market = market.bitbank;
            this.symbol_market = this.symbol + "@" + this.market.ToString();
            this.internal_order_id = this.market + this.order_id;
            this.price = decimal.Parse(js.GetProperty("price").GetString());
            string side = js.GetProperty("side").GetString();
            if (side == "buy")
            {
                this.side = orderSide.Buy;
            }
            else if (side == "sell")
            {
                this.side = orderSide.Sell;
            }

            JsonElement js_posside;
            if (js.TryGetProperty("position_side", out js_posside) && js_posside.GetString() != null)
            {
                string str_posside = js_posside.GetString();
                if (str_posside == "long")
                {
                    this.position_side = positionSide.Long;
                }
                else if (str_posside == "short")
                {
                    this.position_side = positionSide.Short;
                }
                else
                {
                    this.position_side = positionSide.NONE;
                }
            }
            else
            {
                this.position_side = positionSide.NONE;
            }
            this.trade_id = js.GetProperty("trade_id").GetInt64().ToString();
            string _type = js.GetProperty("type").GetString();
            switch (_type)
            {
                case "limit":
                    this.order_type = orderType.Limit;
                    break;
                case "market":
                    this.order_type = orderType.Market;
                    break;
                default:
                    this.order_type = orderType.Other;
                    break;
            }
            JsonElement js_pnl;
            if(js.TryGetProperty("profit_loss",out js_pnl) && js_pnl.GetString() != null)
            {
                this.profit_loss = decimal.Parse(js_pnl.GetString());
            }
            JsonElement js_interest;
            if (js.TryGetProperty("interest", out js_interest) && js_interest.GetString() != null)
            {
                this.interest = decimal.Parse(js_interest.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture);
            }
            //this.profit_loss = decimal.Parse(js.GetProperty("profit_loss").GetString());
            //this.interest = decimal.Parse(js.GetProperty("interest").GetString());
        }
        public void setGMOCoinFill(JsonElement js)
        {
            this.market = market.gmocoin;
            this.timestamp = DateTime.UtcNow;
            this.quantity = decimal.Parse(js.GetProperty("executionSize").GetString());
            this.filled_time = Functions.convertToDateTime(js.GetProperty("executionTimestamp").GetString(), this.market);
            this.fee_quote = decimal.Parse(js.GetProperty("fee").GetString());
            this.order_id = js.GetProperty("orderId").GetInt64().ToString();
            this.position_id = js.GetProperty("positionId").GetInt64().ToString();
            this.symbol = js.GetProperty("symbol").GetString();
            this.symbol_market = this.symbol + "@" + this.market.ToString();
            this.internal_order_id = this.market + this.order_id;
            this.price = decimal.Parse(js.GetProperty("executionPrice").GetString());
            this.order_quantity = decimal.Parse(js.GetProperty("orderSize").GetString());
            this.executed_quantity = decimal.Parse(js.GetProperty("orderExecutedSize").GetString());
            string side = js.GetProperty("side").GetString();
            if (side == "BUY")
            {
                this.side = orderSide.Buy;
            }
            else if (side == "SELL")
            {
                this.side = orderSide.Sell;
            }

            JsonElement js_settle;
            if (js.TryGetProperty("settleType", out js_settle) && js_settle.GetString() != null)
            {
                string str_settle = js_settle.GetString();
                if (str_settle == "OPEN")
                {
                    if(this.side == orderSide.Buy)
                    {
                        this.position_side = positionSide.Long;
                    }
                    else if (this.side == orderSide.Sell)
                    {
                        this.position_side = positionSide.Short;
                    }
                    else
                    {
                        this.position_side = positionSide.NONE;
                    }
                }
                else if (str_settle == "CLOSE")
                {
                    if (this.side == orderSide.Buy)
                    {
                        this.position_side = positionSide.Short;
                    }
                    else if (this.side == orderSide.Sell)
                    {
                        this.position_side = positionSide.Long;
                    }
                    else
                    {
                        this.position_side = positionSide.NONE;
                    }
                }
                else
                {
                    this.position_side = positionSide.NONE;
                }
            }
            else
            {
                this.position_side = positionSide.NONE;
            }
            this.trade_id = js.GetProperty("executionId").GetInt64().ToString();
            string _type = js.GetProperty("executionType").GetString();
            switch (_type)
            {
                case "LIMIT":
                    this.order_type = orderType.Limit;
                    break;
                case "MARKET":
                    this.order_type = orderType.Market;
                    break;
                default:
                    this.order_type = orderType.Other;
                    break;
            }
            JsonElement js_pnl;
            if (js.TryGetProperty("lossGain", out js_pnl) && js_pnl.GetString() != null)
            {
                this.profit_loss = decimal.Parse(js_pnl.GetString());
            }
        }
        public void setGMOCoinHistFill(JsonElement js,string symbol)
        {
            this.market = market.gmocoin;
            this.timestamp = DateTime.UtcNow;
            this.quantity = decimal.Parse(js.GetProperty("size").GetString());
            //this.filled_time = DateTime.ParseExact(js.GetProperty("timestamp").GetString(), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            this.filled_time = Functions.convertToDateTime(js.GetProperty("timestamp").GetString(), this.market);
            this.fee_quote = decimal.Parse(js.GetProperty("fee").GetString());
            this.order_id = js.GetProperty("orderId").GetInt64().ToString();
            this.position_id = js.GetProperty("positionId").GetInt64().ToString();
            this.symbol = symbol;
            this.symbol_market = this.symbol + "@" + this.market.ToString();
            this.internal_order_id = this.market + this.order_id;
            this.price = decimal.Parse(js.GetProperty("price").GetString());
            this.order_quantity = decimal.Parse(js.GetProperty("size").GetString());
            this.executed_quantity = decimal.Parse(js.GetProperty("size").GetString());
            string side = js.GetProperty("side").GetString();
            if (side == "BUY")
            {
                this.side = orderSide.Buy;
            }
            else if (side == "SELL")
            {
                this.side = orderSide.Sell;
            }

            JsonElement js_settle;
            if (js.TryGetProperty("settleType", out js_settle) && js_settle.GetString() != null)
            {
                string str_settle = js_settle.GetString();
                if (str_settle == "OPEN")
                {
                    if (this.side == orderSide.Buy)
                    {
                        this.position_side = positionSide.Long;
                    }
                    else if (this.side == orderSide.Sell)
                    {
                        this.position_side = positionSide.Short;
                    }
                    else
                    {
                        this.position_side = positionSide.NONE;
                    }
                }
                else if (str_settle == "CLOSE")
                {
                    if (this.side == orderSide.Buy)
                    {
                        this.position_side = positionSide.Short;
                    }
                    else if (this.side == orderSide.Sell)
                    {
                        this.position_side = positionSide.Long;
                    }
                    else
                    {
                        this.position_side = positionSide.NONE;
                    }
                }
                else
                {
                    this.position_side = positionSide.NONE;
                }
            }
            else
            {
                this.position_side = positionSide.NONE;
            }
            this.trade_id = js.GetProperty("executionId").GetInt64().ToString();
            this.order_type = orderType.Other;
            JsonElement js_pnl;
            if (js.TryGetProperty("lossGain", out js_pnl) && js_pnl.GetString() != null)
            {
                this.profit_loss = decimal.Parse(js_pnl.GetString());
            }
        }
        public void setBitTradeFill(JsonElement js)//trade.clearing object. retrive only fees
        {
            this.timestamp = DateTime.UtcNow;
            this.quantity = 0;//Executions are reflected with order objects.
            this.price = -1;
            this.filled_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("tradeTime").GetInt64()).UtcDateTime;
            this.symbol = js.GetProperty("symbol").GetString();
            this.market = market.bittrade;
            this.symbol_market = this.symbol + "@" + this.market.ToString();
            this.internal_order_id = this.market + this.order_id;
            this.order_id = js.GetProperty("orderId").GetInt64().ToString();
            bool aggressor = js.GetProperty("aggressor").GetBoolean();
            if (aggressor)
            {
                this.maker_taker = "taker";
            }
            else
            {
                this.maker_taker = "maker";
            }
            string side = js.GetProperty("orderSide").GetString();
            if (side == "buy")
            {
                this.side = orderSide.Buy;
            }
            else if (side == "sell")
            {
                this.side = orderSide.Sell;
            }
            else
            {
                this.side = orderSide.NONE;
            }
            this.trade_id = js.GetProperty("tradeId").GetInt64().ToString();
            string feeccy = js.GetProperty("feeCurrency").GetString();
            decimal fee = decimal.Parse(js.GetProperty("transactFee").GetString()) - decimal.Parse(js.GetProperty("feeDeduct").GetString());
            if (this.symbol.Substring(this.symbol.Length - feeccy.Length) == feeccy)
            {
                this.fee_quote = fee;
            }
            else if (this.symbol.Substring(0, feeccy.Length) == feeccy)
            {
                this.fee_base = fee;
            }
            else
            {
                this.fee_unknown = fee;
            }
        }

        public override string ToString()
        {
            string line;
            if (this.timestamp != null)
            {
                line = ((DateTime)this.timestamp).ToString("yyyy-MM-dd HH:mm:ss.fff");
            }
            else
            {
                line = "";
            }
            line += "," + this.trade_id + "," + this.order_id + "," + this.position_id + "," + this.market + "," + this.symbol + "," + this.order_type.ToString() + "," + this.side.ToString() + "," + this.price.ToString() + "," + this.quantity.ToString() + "," + this.order_quantity.ToString() + "," + this.executed_quantity.ToString() + "," + this.maker_taker + "," + this.fee_base.ToString() + "," + this.fee_quote.ToString() + "," + this.profit_loss.ToString() + "," + this.interest + "," + this.internal_order_id + ",";
            if (this.filled_time != null)
            {
                line += ((DateTime)this.filled_time).ToString("yyyy-MM-dd HH:mm:ss.fff");
            }
            else
            {
                line += "";
            }
            line += "," + this.downStreamLatency.ToString() + "," + this.msg;
            return line;
        }

        public void init()
        {
            this.timestamp = null;
            this.symbol_market = "";
            this.market = market.NONE;
            this.quantity = 0;
            this.order_quantity = 0;
            this.executed_quantity = 0;
            this.filled_time = null;
            this.fee_base = 0;
            this.fee_quote = 0;
            this.fee_unknown = 0;
            this.maker_taker = "";
            this.order_id = "";
            this.position_id = "";
            this.symbol = "";
            this.internal_order_id = "";
            this.price = 0;
            this.side = orderSide.NONE;
            this.position_side = positionSide.NONE;
            this.trade_id = "";
            this.order_type = orderType.NONE;
            this.profit_loss = 0;
            this.interest = 0;
            this.downStreamLatency = 0;
            this.msg = "";
            this.queued_count = 0;
        }
    }
    public class DataSpotOrderUpdate
    {
        public DateTime? timestamp;
        public string symbol_market;
        public Enums.market market;
        public string order_id;
        public string symbol;
        public orderType order_type;
        public orderSide side;
        public positionSide position_side;
        public orderStatus status;
        public timeInForce time_in_force;

        public decimal order_quantity;
        public decimal filled_quantity;
        public decimal order_price;
        public decimal average_price;

        public decimal current_traded_quantity;
        public decimal current_traded_price;

        public string internal_order_id;

        public string? fee_asset;
        public decimal fee;

        public DateTime? create_time;
        public DateTime? update_time;

        public string last_trade;
        public decimal trigger_price;
        public bool is_trigger_order;

        public bool isVirtual;
        public string msg;

        public int err_code;

        public int queued_count;

        public DataSpotOrderUpdate()
        {
            this.timestamp = null;
            this.symbol_market = "";
            this.market = market.NONE;
            this.order_id = "";
            this.symbol = "";
            this.order_type = orderType.NONE;
            this.side = orderSide.NONE;
            this.position_side = positionSide.NONE;
            this.status = orderStatus.NONE;
            this.time_in_force = timeInForce.NONE;
            this.order_quantity = 0;
            this.filled_quantity = 0;
            this.order_price = -1;
            this.average_price = -1;
            this.current_traded_quantity = 0;
            this.current_traded_price = -1;
            this.internal_order_id = "";
            this.fee_asset = "";
            this.fee = 0;
            this.create_time = null;
            this.update_time = null;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
            this.isVirtual = false;
            this.msg = "";
            this.err_code = 0;
            this.queued_count = 0;
        }
        public void Copy(DataSpotOrderUpdate org)
        {
            this.timestamp = DateTime.UtcNow;
            this.symbol_market = org.symbol_market;
            this.market = org.market;
            this.order_id = org.order_id;
            this.symbol = org.symbol;
            this.order_type = org.order_type;
            this.side = org.side;
            this.position_side = org.position_side;
            this.status = org.status;
            this.time_in_force = org.time_in_force;
            this.order_quantity = org.order_quantity;
            this.filled_quantity = org.filled_quantity;
            this.order_price = org.order_price;
            this.average_price = org.average_price;
            this.current_traded_quantity = org.current_traded_quantity;
            this.current_traded_price = org.current_traded_price;
            this.internal_order_id = org.internal_order_id;
            this.fee_asset = org.fee_asset;
            this.fee = org.fee;
            this.create_time = org.create_time;
            this.update_time = org.update_time;
            this.last_trade = org.last_trade;
            this.trigger_price = org.trigger_price;
            this.is_trigger_order = org.is_trigger_order;
            this.isVirtual = org.isVirtual;
            this.msg = org.msg;
            this.err_code = org.err_code;
            this.queued_count = org.queued_count;
        }


        public void setCoincheckSpotOrder(JsonElement js)
        {
            this.timestamp = DateTime.UtcNow;
            this.symbol = js.GetProperty("pair").GetString();
            this.market = market.coincheck;
            this.symbol_market = this.symbol + "@" + this.market.ToString();
            this.order_id = js.GetProperty("id").GetInt64().ToString();
            string str_status = js.GetProperty("order_event").GetString();
            switch (str_status)
            {
                case "NEW":
                case "PARTIALLY_FILLED":
                    this.status = orderStatus.Open;
                    break;
                case "FILL":
                    this.status = orderStatus.Filled;
                    break;
                case "EXPIRY":
                case "PARTIALLY_FILLED_EXPIRED":
                    this.status = orderStatus.INVALID;
                    break;
                case "CANCEL":
                case "PARTIALLY_FILLED_CANCELED":
                    this.status = orderStatus.Canceled;
                    break;
                default:
                    this.status = orderStatus.INVALID;
                    break;
            }
            string side = js.GetProperty("order_type").GetString();
            if (side == "buy" || side == "market_buy")
            {
                this.side = orderSide.Buy;
            }
            else if (side == "sell" || side == "market_sell")
            {
                this.side = orderSide.Sell;
            }
            if (side.StartsWith("market_"))
            {
                this.order_type = orderType.Market;
                if (side == "market_buy")
                {
                    this.order_price = 0;
                    this.order_quantity = 0;
                }
            }
            else
            {
                this.order_type = orderType.Limit;
                this.order_price = decimal.Parse(js.GetProperty("rate").GetString());
                this.order_quantity = decimal.Parse(js.GetProperty("amount").GetString());
            }

            if (js.TryGetProperty("latest_executed_amount", out JsonElement filled_element)
    && filled_element.ValueKind != JsonValueKind.Null
    && filled_element.ValueKind != JsonValueKind.Undefined)
            {
                this.filled_quantity = decimal.Parse(js.GetProperty("latest_executed_amount").GetString());
                this.average_price = this.order_price;
            }
            else
            {
                this.filled_quantity = 0;
            }

            this.average_price = -1;
            string str_tif = js.GetProperty("time_in_force").GetString();
            switch (str_tif)
            {
                case "good_til_cancelled":
                    this.time_in_force = timeInForce.GoodTillCanceled;
                    break;
                default:
                    this.time_in_force = timeInForce.NONE;
                    break;
            }
            this.fee_asset = "";
            this.fee = 0;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
            this.update_time = DateTime.Parse(js.GetProperty("event_time").GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
        }
        public void setGMOCoinSpotOrder(JsonElement js)
        {
            this.timestamp = DateTime.UtcNow;
            this.symbol = js.GetProperty("symbol").GetString();
            this.market = market.gmocoin;
            this.symbol_market = this.symbol + "@" + this.market.ToString();
            this.order_id = js.GetProperty("orderId").GetInt64().ToString();
            string _type = js.GetProperty("executionType").GetString();
            switch (_type)
            {
                case "LIMIT":
                    this.order_type = orderType.Limit;
                    this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                    break;
                case "MARKET":
                    this.order_type = orderType.Market;
                    this.order_price = 0;
                    break;
                default:
                    this.order_type = orderType.Other;
                    break;
            }
            string side = js.GetProperty("side").GetString();
            if (side == "BUY")
            {
                this.side = orderSide.Buy;
            }
            else if (side == "SELL")
            {
                this.side = orderSide.Sell;
            }
            JsonElement js_settle;
            if (js.TryGetProperty("settleType", out js_settle) && js_settle.GetString() != null)
            {
                string str_settle = js_settle.GetString();
                if (str_settle == "OPEN")
                {
                    if(this.side == orderSide.Buy)
                    {
                        this.position_side = positionSide.Long;
                    }
                    else if(this.side == orderSide.Sell)
                    {
                        this.position_side = positionSide.Short;
                    }
                    else
                    {
                        this.position_side = positionSide.NONE;
                    }
                }
                else if (str_settle == "CLOSE")
                {
                    if (this.side == orderSide.Buy)
                    {
                        this.position_side = positionSide.Short;
                    }
                    else if (this.side == orderSide.Sell)
                    {
                        this.position_side = positionSide.Long;
                    }
                    else
                    {
                        this.position_side = positionSide.NONE;
                    }
                }
                else
                {
                    this.position_side = positionSide.NONE;
                }
            }
            else
            {
                this.position_side = positionSide.NONE;
            }
            string str_status = js.GetProperty("orderStatus").GetString();
            switch (str_status)
            {
                case "ORDERED":
                    this.status = orderStatus.Open;
                    break;
                case "CANCELED":
                    this.status = orderStatus.Canceled;
                    break;
                case "WAITING ":
                    this.status = orderStatus.WaitOpen;
                    break;
                case "EXPIRED":
                    this.status = orderStatus.Expired;
                    break;
                default:
                    this.status = orderStatus.INVALID;
                    break;
            }
            this.create_time = Functions.convertToDateTime(js.GetProperty("orderTimestamp").GetString(), this.market);
            //this.create_time = DateTime.ParseExact(js.GetProperty("orderTimestamp").GetString(), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            this.update_time = this.timestamp;
            string time_in_force = js.GetProperty("timeInForce").GetString();

            if(time_in_force == "SOK")//PostOnly
            {
                this.time_in_force = timeInForce.GoodTillCanceled;
            }
            else if(time_in_force == "FAS")
            {
                this.time_in_force = timeInForce.GoodTillCanceled;
            }
            else if(time_in_force == "FAK")
            {
                this.time_in_force = timeInForce.ImmediateOrCancel;
            }
            else if(time_in_force == "FOK")
            {
                this.time_in_force = timeInForce.FillOrKill;
            }
            else
            {
                this.time_in_force = timeInForce.NONE;
            }
            this.order_quantity = decimal.Parse(js.GetProperty("orderSize").GetString());
            this.filled_quantity = decimal.Parse(js.GetProperty("orderExecutedSize").GetString());
            this.average_price = -1;
            this.fee_asset = "";
            this.fee = 0;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
        }
        public void setGMOCoinFill(JsonElement js)
        {
            this.timestamp = DateTime.UtcNow;
            this.symbol = js.GetProperty("symbol").GetString();
            this.market = market.gmocoin;
            this.symbol_market = this.symbol + "@" + this.market.ToString();
            this.order_id = js.GetProperty("orderId").GetInt64().ToString();
            string _type = js.GetProperty("executionType").GetString();
            switch (_type)
            {
                case "LIMIT":
                    this.order_type = orderType.Limit;
                    this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                    break;
                case "MARKET":
                    this.order_type = orderType.Market;
                    this.order_price = 0;
                    break;
                default:
                    this.order_type = orderType.Other;
                    break;
            }
            string side = js.GetProperty("side").GetString();
            if (side == "BUY")
            {
                this.side = orderSide.Buy;
            }
            else if (side == "SELL")
            {
                this.side = orderSide.Sell;
            }
            JsonElement js_settle;
            if (js.TryGetProperty("settleType", out js_settle) && js_settle.GetString() != null)
            {
                string str_settle = js_settle.GetString();
                if (str_settle == "OPEN")
                {
                    if (this.side == orderSide.Buy)
                    {
                        this.position_side = positionSide.Long;
                    }
                    else if (this.side == orderSide.Sell)
                    {
                        this.position_side = positionSide.Short;
                    }
                    else
                    {
                        this.position_side = positionSide.NONE;
                    }
                }
                else if (str_settle == "CLOSE")
                {
                    if (this.side == orderSide.Buy)
                    {
                        this.position_side = positionSide.Short;
                    }
                    else if (this.side == orderSide.Sell)
                    {
                        this.position_side = positionSide.Long;
                    }
                    else
                    {
                        this.position_side = positionSide.NONE;
                    }
                }
                else
                {
                    this.position_side = positionSide.NONE;
                }
            }
            else
            {
                this.position_side = positionSide.NONE;
            }
            //string str_status = js.GetProperty("orderStatus").GetString();
            //switch (str_status)
            //{
            //    case "ORDERED":
            //        this.status = orderStatus.Open;
            //        break;
            //    case "CANCELED":
            //        this.status = orderStatus.Canceled;
            //        break;
            //    case "WAITING ":
            //        this.status = orderStatus.WaitOpen;
            //        break;
            //    case "EXPIRED":
            //        this.status = orderStatus.Expired;
            //        break;
            //    default:
            //        this.status = orderStatus.INVALID;
            //        break;
            //}
            this.create_time = Functions.convertToDateTime(js.GetProperty("orderTimestamp").GetString(), this.market);
            //this.create_time = DateTime.ParseExact(js.GetProperty("orderTimestamp").GetString(), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            this.update_time = this.timestamp;
            string time_in_force = js.GetProperty("timeInForce").GetString();

            if (time_in_force == "SOK")//PostOnly
            {
                this.time_in_force = timeInForce.GoodTillCanceled;
            }
            else if (time_in_force == "FAS")
            {
                this.time_in_force = timeInForce.GoodTillCanceled;
            }
            else if (time_in_force == "FAK")
            {
                this.time_in_force = timeInForce.ImmediateOrCancel;
            }
            else if (time_in_force == "FOK")
            {
                this.time_in_force = timeInForce.FillOrKill;
            }
            else
            {
                this.time_in_force = timeInForce.NONE;
            }
            this.order_quantity = decimal.Parse(js.GetProperty("orderSize").GetString());
            this.filled_quantity = decimal.Parse(js.GetProperty("orderExecutedSize").GetString());
            if(this.order_quantity == this.filled_quantity)
            {
                this.status = orderStatus.Filled;
            }
            else
            {
                this.status = orderStatus.Open;
            }
            this.average_price = -1;
            this.fee_asset = "";
            this.fee = 0;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
        }
        public void setBitbankSpotOrder(JsonElement js)
        {
            this.timestamp = DateTime.UtcNow;
            this.symbol = js.GetProperty("pair").GetString();
            this.market = market.bitbank;
            this.symbol_market = this.symbol + "@" + this.market.ToString();
            this.order_id = js.GetProperty("order_id").GetInt64().ToString();
            string _type = js.GetProperty("type").GetString();
            switch (_type)
            {
                case "limit":
                    this.order_type = orderType.Limit;
                    this.order_price = decimal.Parse(js.GetProperty("price").GetString());
                    break;
                case "market":
                    this.order_type = orderType.Market;
                    this.order_price = 0;
                    break;
                default:
                    this.order_type = orderType.Other;
                    break;
            }
            string side = js.GetProperty("side").GetString();
            if (side == "buy")
            {
                this.side = orderSide.Buy;
            }
            else if (side == "sell")
            {
                this.side = orderSide.Sell;
            }
            JsonElement js_posside;
            if(js.TryGetProperty("position_side",out js_posside) && js_posside.GetString() != null)
            {
                string str_posside = js_posside.GetString();
                if (str_posside == "long")
                {
                    this.position_side = positionSide.Long;
                }
                else if (str_posside == "short") 
                {
                    this.position_side= positionSide.Short;
                }
                else
                {
                    this.position_side = positionSide.NONE;
                }
            }
            else
            {
                this.position_side = positionSide.NONE;
            }
            string str_status = js.GetProperty("status").GetString();
            switch (str_status)
            {
                case "UNFILLED":
                    this.status = orderStatus.Open;
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("ordered_at").GetInt64()).UtcDateTime;
                    break;
                case "PARTIALLY_FILLED":
                    this.status = orderStatus.Open;
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("executed_at").GetInt64()).UtcDateTime;
                    break;
                case "FULLY_FILLED":
                    this.status = orderStatus.Filled;
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("executed_at").GetInt64()).UtcDateTime;
                    break;
                case "CANCELED_PARTIALLY_FILLED":
                    this.status = orderStatus.Canceled;
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("canceled_at").GetInt64()).UtcDateTime;
                    break;
                case "CANCELED_UNFILLED":
                    this.status = orderStatus.Canceled;
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("canceled_at").GetInt64()).UtcDateTime;
                    break;
                default:
                    this.status = orderStatus.INVALID;
                    break;
            }
            Int64 expire_at = js.GetProperty("expire_at").GetInt64();
            if (expire_at == 0)
            {
                this.time_in_force = timeInForce.GoodTillCanceled;
            }
            else
            {
                this.time_in_force = timeInForce.NONE;
            }
            this.order_quantity = decimal.Parse(js.GetProperty("start_amount").GetString());
            this.filled_quantity = decimal.Parse(js.GetProperty("executed_amount").GetString());
            this.average_price = decimal.Parse(js.GetProperty("average_price").GetString());
            this.fee_asset = "";
            this.fee = 0;
            this.create_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("ordered_at").GetInt64()).UtcDateTime;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
        }

        public void setBitTradeOrder(JsonElement js)
        {
            string _type;
            string str_status;
            this.timestamp = DateTime.UtcNow;
            this.symbol = js.GetProperty("symbol").GetString();
            this.market = market.bittrade;
            this.symbol_market = this.symbol + "@" + this.market.ToString();
            this.order_id = js.GetProperty("orderId").GetInt64().ToString();
            string eventType = js.GetProperty("eventType").GetString();
            switch (eventType)
            {
                case "creation":
                    _type = js.GetProperty("type").GetString();
                    switch (_type)
                    {
                        case "sell-limit":
                        case "sell-limit-maker":
                            this.order_type = orderType.Limit;
                            this.side = orderSide.Sell;
                            this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                            break;
                        case "buy-limit":
                        case "buy-limit-maker":
                            this.order_type = orderType.Limit;
                            this.side = orderSide.Buy;
                            this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                            break;
                        case "sell-market":
                            this.order_type = orderType.Market;
                            this.side = orderSide.Sell;
                            this.order_price = 0;
                            break;
                        case "buy-market":
                            this.order_type = orderType.Market;
                            this.side = orderSide.Buy;
                            this.order_price = 0;
                            break;
                        case "sell-ioc":
                        case "buy-ioc":
                        default:
                            this.order_type = orderType.Other;
                            break;
                    }
                    str_status = js.GetProperty("orderStatus").GetString();
                    switch (str_status)
                    {
                        case "created":
                            this.status = orderStatus.WaitOpen;
                            this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("orderCreateTime").GetInt64()).UtcDateTime;
                            this.create_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("orderCreateTime").GetInt64()).UtcDateTime;
                            break;
                        case "submitted":
                            this.status = orderStatus.Open;
                            this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("orderCreateTime").GetInt64()).UtcDateTime;
                            this.create_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("orderCreateTime").GetInt64()).UtcDateTime;
                            break;
                        case "partial-filled":
                        case "partial-canceled":
                            this.status = orderStatus.Open;
                            break;
                        case "filled":
                            this.status = orderStatus.Filled;
                            break;
                        case "canceling":
                            this.status = orderStatus.WaitCancel;
                            break;
                        case "canceled":
                            this.status = orderStatus.Canceled;
                            break;
                        default:
                            this.status = orderStatus.INVALID;
                            break;
                    }
                    this.order_quantity = decimal.Parse(js.GetProperty("orderSize").GetString());
                    break;
                case "cancellation":
                    _type = js.GetProperty("type").GetString();
                    switch (_type)
                    {
                        case "sell-limit":
                        case "sell-limit-maker":
                            this.order_type = orderType.Limit;
                            this.side = orderSide.Sell;
                            this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                            break;
                        case "buy-limit":
                        case "buy-limit-maker":
                            this.order_type = orderType.Limit;
                            this.side = orderSide.Buy;
                            this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                            break;
                        case "sell-market":
                            this.order_type = orderType.Market;
                            this.side = orderSide.Sell;
                            this.order_price = 0;
                            break;
                        case "buy-market":
                            this.order_type = orderType.Market;
                            this.side = orderSide.Buy;
                            this.order_price = 0;
                            break;
                        case "sell-ioc":
                        case "buy-ioc":
                        default:
                            this.order_type = orderType.Other;
                            break;
                    }
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("lastActTime").GetInt64()).UtcDateTime;
                    str_status = js.GetProperty("orderStatus").GetString();
                    switch (str_status)
                    {
                        case "created":
                            this.status = orderStatus.WaitOpen;
                            break;
                        case "submitted":
                            this.status = orderStatus.Open;
                            break;
                        case "partial-filled":
                        case "partial-canceled":
                            this.status = orderStatus.Open;
                            break;
                        case "filled":
                            this.status = orderStatus.Filled;
                            break;
                        case "canceling":
                            this.status = orderStatus.WaitCancel;
                            break;
                        case "canceled":
                            this.status = orderStatus.Canceled;
                            break;
                        default:
                            this.status = orderStatus.INVALID;
                            break;
                    }
                    break;
                case "trade":
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("tradeTime").GetInt64()).UtcDateTime;
                    this.current_traded_price = decimal.Parse(js.GetProperty("tradePrice").GetString());
                    this.current_traded_quantity = decimal.Parse(js.GetProperty("tradeVolume").GetString());
                    this.filled_quantity = decimal.Parse(js.GetProperty("execAmt").GetString());
                    _type = js.GetProperty("type").GetString();
                    switch (_type)
                    {
                        case "sell-limit":
                        case "sell-limit-maker":
                            this.order_type = orderType.Limit;
                            this.side = orderSide.Sell;
                            this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                            break;
                        case "buy-limit":
                        case "buy-limit-maker":
                            this.order_type = orderType.Limit;
                            this.side = orderSide.Buy;
                            this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                            break;
                        case "sell-market":
                            this.order_type = orderType.Market;
                            this.side = orderSide.Sell;
                            this.order_price = 0;
                            break;
                        case "buy-market":
                            this.order_type = orderType.Market;
                            this.side = orderSide.Buy;
                            this.order_price = 0;
                            break;
                        case "sell-ioc":
                        case "buy-ioc":
                        default:
                            this.order_type = orderType.Other;
                            break;
                    }
                    str_status = js.GetProperty("orderStatus").GetString();
                    switch (str_status)
                    {
                        case "created":
                            this.status = orderStatus.WaitOpen;
                            break;
                        case "submitted":
                            this.status = orderStatus.Open;
                            break;
                        case "partial-filled":
                        case "partial-canceled":
                            this.status = orderStatus.Open;
                            break;
                        case "filled":
                            this.status = orderStatus.Filled;
                            break;
                        case "canceling":
                            this.status = orderStatus.WaitCancel;
                            break;
                        case "canceled":
                            this.status = orderStatus.Canceled;
                            break;
                        default:
                            this.status = orderStatus.INVALID;
                            break;
                    }
                    break;
            }
            this.average_price = 0;
            this.fee_asset = "";
            this.fee = 0;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
        }
        public void setBitTradeTrade(JsonElement js)
        {
            this.timestamp = DateTime.UtcNow;
            this.symbol = js.GetProperty("symbol").GetString();
            this.market = market.bittrade;
            this.symbol_market = this.symbol + "@" + this.market.ToString();
            this.order_id = js.GetProperty("orderId").GetInt64().ToString();

            string side = js.GetProperty("orderSide").GetString();
            if (side == "buy")
            {
                this.side = orderSide.Buy;
            }
            else if (side == "sell")
            {
                this.side = orderSide.Sell;
            }

            string _type = js.GetProperty("orderType").GetString();
            switch (_type)
            {
                case "sell-limit":
                case "sell-limit-maker":
                    this.order_type = orderType.Limit;
                    this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                    this.order_quantity = decimal.Parse(js.GetProperty("orderSize").GetString());
                    break;
                case "buy-limit":
                case "buy-limit-maker":
                    this.order_type = orderType.Limit;
                    this.order_price = decimal.Parse(js.GetProperty("orderPrice").GetString());
                    this.order_quantity = decimal.Parse(js.GetProperty("orderSize").GetString());
                    break;
                case "sell-market":
                    this.order_type = orderType.Market;
                    this.order_price = 0;
                    this.order_quantity = decimal.Parse(js.GetProperty("orderSize").GetString());
                    break;
                case "buy-market":
                    this.order_type = orderType.Market;
                    this.order_price = 0;
                    this.order_quantity = this.filled_quantity;
                    break;
                case "sell-ioc":
                case "buy-ioc":
                default:
                    this.order_type = orderType.Other;
                    break;
            }

            string str_status = js.GetProperty("orderStatus").GetString();
            switch (str_status)
            {
                case "created":
                    this.status = orderStatus.WaitOpen;
                    break;
                case "submitted":
                case "partial-filled":
                case "partial-canceled":
                    this.status = orderStatus.Open;
                    break;
                case "filled":
                    this.status = orderStatus.Filled;
                    break;
                case "canceling":
                    this.status = orderStatus.WaitCancel;
                    break;
                case "canceled":
                    this.status = orderStatus.Canceled;
                    break;
                default:
                    this.status = orderStatus.INVALID;
                    break;
            }


            string eventType = js.GetProperty("eventType").GetString();
            switch (eventType)
            {
                case "trade":
                    //this.average_price = decimal.Parse(js.GetProperty("tradePrice").GetString());
                    //this.filled_quantity = decimal.Parse(js.GetProperty("tradeVolume").GetString());
                    this.update_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("tradeTime").GetInt64()).UtcDateTime;
                    this.fee = decimal.Parse(js.GetProperty("transactFee").GetString());
                    this.fee_asset = js.GetProperty("feeCurrency").GetString().ToUpper();
                    this.fee -= decimal.Parse(js.GetProperty("feeDeduct").GetString());
                    break;
                case "cancellation":
                    this.fee = 0;
                    this.fee_asset = "";
                    break;
            }

            //this.client_order_id = js.GetProperty("clientOrderId").GetString();
            this.create_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("orderCreateTime").GetInt64()).UtcDateTime;


            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
        }
        public void setSharedSpotOrder(SharedSpotOrder update, string market, DateTime? timestamp)
        {
            this.timestamp = DateTime.UtcNow;
            this.market = Enums.market.NONE;//Undefined
            this.order_id = update.OrderId;
            this.symbol = update.Symbol;
            this.symbol_market = update.Symbol + "@" + market.ToString();
            this.order_type = (orderType)Enum.Parse(typeof(orderType), update.OrderType.ToString());
            this.side = (orderSide)Enum.Parse(typeof(orderSide), update.Side.ToString());
            this.status = (orderStatus)Enum.Parse(typeof(orderStatus), update.Status.ToString());
            this.time_in_force = (timeInForce)Enum.Parse(typeof(timeInForce), update.TimeInForce.ToString());

            if (update.OrderQuantity != null)
            {
                if (update.OrderQuantity.QuantityInBaseAsset != null)
                {
                    this.order_quantity = (decimal)update.OrderQuantity.QuantityInBaseAsset;
                }
                else if (update.OrderQuantity.QuantityInQuoteAsset != null)
                {
                    if (update.AveragePrice != null)
                    {
                        this.order_quantity = (decimal)update.OrderQuantity.QuantityInQuoteAsset / (decimal)update.AveragePrice;
                    }
                    else if (update.OrderPrice != null)
                    {
                        this.order_quantity = (decimal)update.OrderQuantity.QuantityInQuoteAsset / (decimal)update.OrderPrice;
                    }
                    else
                    {
                        this.order_quantity = 0;
                    }
                }
                else if (update.OrderQuantity.QuantityInContracts != null)
                {
                    this.order_quantity = (decimal)update.OrderQuantity.QuantityInContracts;
                }
                else
                {
                    this.order_quantity = 0;
                }
            }
            else
            {
                this.order_quantity = 0;
            }
            if (update.QuantityFilled != null)
            {
                if (update.QuantityFilled.QuantityInBaseAsset != null)
                {
                    this.filled_quantity = (decimal)update.QuantityFilled.QuantityInBaseAsset;
                }
                else if (update.QuantityFilled.QuantityInQuoteAsset != null)
                {
                    if (update.AveragePrice != null)
                    {
                        this.filled_quantity = (decimal)update.QuantityFilled.QuantityInQuoteAsset / (decimal)update.AveragePrice;
                    }
                    else if (update.OrderPrice != null)
                    {
                        this.filled_quantity = (decimal)update.QuantityFilled.QuantityInQuoteAsset / (decimal)update.OrderPrice;
                    }
                    else
                    {
                        this.filled_quantity = 0;
                    }
                }
                else if (update.QuantityFilled.QuantityInContracts != null)
                {
                    this.filled_quantity = (decimal)update.QuantityFilled.QuantityInContracts;
                }
                else
                {
                    this.filled_quantity = 0;
                }
            }
            else
            {
                this.filled_quantity = 0;
            }

            if (update.OrderPrice != null)
            {
                this.order_price = (decimal)update.OrderPrice;
            }
            else
            {
                this.order_price = 0;
            }
            if (update.AveragePrice != null)
            {
                this.average_price = (decimal)update.AveragePrice;
            }
            else
            {
                this.average_price = 0;
            }
            this.fee_asset = update.FeeAsset.ToUpper();
            if (update.Fee != null)
            {
                this.fee = (decimal)update.Fee;
            }
            else
            {

                this.fee = 0;
            }
            this.create_time = update.CreateTime;
            this.update_time = update.UpdateTime;
            if (update.TriggerPrice != null)
            {
                this.trigger_price = (decimal)update.TriggerPrice;
            }
            else
            {
                this.trigger_price = 0;
            }
            this.is_trigger_order = update.IsTriggerOrder;
        }

        public void init()
        {
            this.timestamp = null;
            this.symbol_market = "";
            this.market = market.NONE;
            this.order_id = "";
            this.symbol = "";
            this.order_type = orderType.NONE;
            this.side = orderSide.NONE;
            this.status = orderStatus.NONE;
            this.time_in_force = timeInForce.NONE;
            this.order_quantity = 0;
            this.filled_quantity = 0;
            this.order_price = -1;
            this.average_price = -1;
            this.internal_order_id = "";
            this.fee_asset = "";
            this.fee = 0;
            this.create_time = null;
            this.update_time = null;
            this.last_trade = "";
            this.trigger_price = 0;
            this.is_trigger_order = false;
            this.isVirtual = false;
            this.msg = "";
            this.err_code = 0;
            this.queued_count = 0;
        }
        public override string ToString()
        {
            string line;
            if (this.timestamp != null)
            {
                line = ((DateTime)this.timestamp).ToString("yyyy-MM-dd HH:mm:ss.fff");
            }
            else
            {
                line = "";
            }
            line += "," + this.order_id + "," + this.market + "," + this.symbol + "," + this.order_type.ToString() + "," + this.position_side.ToString() + "," + this.side.ToString() + "," + this.status.ToString() + "," + this.time_in_force.ToString() + "," + this.order_price.ToString() + "," + this.order_quantity.ToString() + "," + this.filled_quantity.ToString() + "," + this.average_price.ToString() + "," + this.internal_order_id + "," + this.fee_asset + "," + this.fee.ToString();
            if (this.create_time != null)
            {
                line += "," + ((DateTime)this.create_time).ToString("yyyy-MM-dd HH:mm:ss.fff");
            }
            else
            {
                line += ",";
            }
            if (this.update_time != null)
            {
                line += "," + ((DateTime)this.update_time).ToString("yyyy-MM-dd HH:mm:ss.fff");
            }
            else
            {
                line += ",";
            }
            line += "," + this.last_trade + "," + this.trigger_price.ToString() + "," + this.is_trigger_order.ToString() + "," + this.err_code.ToString() + "," + this.msg;

            return line;
        }
    }
    public class DataTrade
    {
        public Enums.market market;
        public string? symbol;
        public DateTime? timestamp;
        public DateTime? filled_time;
        public orderSide side;
        public decimal price;
        public decimal quantity;

        public DataTrade()
        {
            this.market = market.NONE;
            this.symbol = "";
            this.timestamp = null;
            this.filled_time = null;
            this.side = orderSide.NONE;
            this.price = 0;
            this.quantity = 0;
        }
        public void setSharedTrade(SharedTrade trd, string? market, string? symbol, DateTime? time)
        {
            this.market = Enums.market.NONE;//Undefined
            this.symbol = symbol;
            this.timestamp = DateTime.UtcNow;
            this.filled_time = trd.Timestamp;
            switch(trd.Side)
            {
                case SharedOrderSide.Buy:
                    this.side = orderSide.Buy;
                    break;
                case SharedOrderSide.Sell:
                    this.side = orderSide.Sell;
                    break;
                default:
                    this.side = orderSide.NONE;
                    break;
            }
            this.price = trd.Price;
            this.quantity = trd.Quantity;
        }
        public void setBitbankTrade(JsonElement js, Enums.market market, string? symbol)
        {
            this.market = market;
            this.symbol = symbol;
            this.timestamp = DateTime.UtcNow;
            this.filled_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("executed_at").GetInt64()).UtcDateTime;
            string str_side = js.GetProperty("side").GetString();
            if (str_side == "buy")
            {
                this.side = orderSide.Buy;
            }
            else if (str_side == "sell")
            {
                this.side = orderSide.Sell;
            }
            this.price = decimal.Parse(js.GetProperty("price").GetString());
            this.quantity = decimal.Parse(js.GetProperty("amount").GetString());
        }
        public void setGMOCoinTrade(JsonDocument js)
        {
            this.market = market.gmocoin;
            this.symbol = js.RootElement.GetProperty("symbol").GetString();
            this.timestamp = DateTime.UtcNow;
            this.filled_time = Functions.convertToDateTime(js.RootElement.GetProperty("timestamp").GetString(), this.market);
            //this.filled_time = DateTime.ParseExact(js.RootElement.GetProperty("timestamp").GetString(), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            string str_side = js.RootElement.GetProperty("side").GetString();
            if (str_side == "BUY")
            {
                this.side = orderSide.Buy;
            }
            else if (str_side == "SELL")
            {
                this.side = orderSide.Sell;
            }
            this.price = decimal.Parse(js.RootElement.GetProperty("price").GetString());
            this.quantity = decimal.Parse(js.RootElement.GetProperty("size").GetString());
        }
        public void setBitTradeTrade(JsonElement js, string symbol)
        {
            int i = 0;
            this.timestamp = DateTime.UtcNow;
            this.market = market.bittrade;
            this.symbol = symbol;
            this.filled_time = DateTimeOffset.FromUnixTimeMilliseconds(js.GetProperty("ts").GetInt64()).UtcDateTime;
            this.price = js.GetProperty("price").GetDecimal();
            this.quantity = js.GetProperty("amount").GetDecimal();
            string str_side = js.GetProperty("direction").GetString();
            if (str_side == "buy")
            {
                this.side = orderSide.Buy;
            }
            else if (str_side == "sell")
            {
                this.side = orderSide.Sell;
            }
        }
        public void setCoincheckTrade(JsonElement js)
        {
            int i = 0;
            this.timestamp = DateTime.UtcNow;
            this.market = market.coincheck;
            foreach (var item in js.EnumerateArray())
            {
                string str_item = item.GetString();
                switch (i)
                {
                    case 0://Time
                        this.filled_time = DateTimeOffset.FromUnixTimeSeconds(Int64.Parse(str_item)).UtcDateTime;
                        break;
                    case 1://ID
                        break;
                    case 2://symbol
                        this.symbol = str_item;
                        break;
                    case 3://price
                        this.price = decimal.Parse(str_item);
                        break;
                    case 4://quantity
                        this.quantity = decimal.Parse(str_item);
                        break;
                    case 5://side
                        if (str_item == "buy")
                        {
                            this.side = orderSide.Buy;
                        }
                        else if (str_item == "sell")
                        {
                            this.side = orderSide.Sell;
                        }
                        break;
                    case 6://Taker ID
                        break;
                    case 7://Maker ID
                        break;
                    case 8://Itayose ID
                        break;

                }
                ++i;
            }
        }
        public void init()
        {
            this.market = market.NONE;
            this.symbol = "";
            this.timestamp = null;
            this.filled_time = null;
            this.side = orderSide.NONE;
            this.price = 0;
            this.quantity = 0;
        }

        public override string ToString()
        {
            return this.market + "," + this.symbol + "," + ((DateTime)this.timestamp).ToString("yyyy-MM-dd HH:mm:ss.fff") + "," + ((DateTime)this.filled_time).ToString("yyyy-MM-dd HH:mm:ss.fff") + "," + this.side.ToString() + "," + this.price.ToString() + "," + this.quantity.ToString();
        }
    }
    public class tradeSummary
    {
        public string id;
        public string strategy;
        public DateTime? timestamp;
        public bool BBook;
        public string maker_symbolmarket;
        public string taker_symbolmarket;
        public string maker_orderid;
        public string taker_orderid;

        public orderSide maker_side;
        public orderSide taker_side;
        public int layer;
        public decimal maker_avgprice;
        public decimal taker_avgprice;
        public decimal maker_quantity;
        public decimal taker_quantity;
        public double realized_volatility;
        public decimal maker_markup;
        public decimal taker_markup;
        public decimal skew;
        public decimal skew_widening;
        public decimal const_skew_widening;
        public decimal maker_priceAdjustment;
        public decimal taker_priceAdjustment;

        public double maker_avgExecutedTime;
        public double taker_avgExecutedTime;

        public decimal totalPnL;
        public decimal totalFee;
        public decimal skewPnL;
        public decimal priceAdjPnL;
        public decimal markupPnL;
        public decimal residualPnL;
        public double avg_latency;

        //Factors
        public double makerLatency;
        public decimal makerTradeImbalance;
        public double makerQuoteImbalance;
        public double makerMidRatio;
        public double takerLatency;
        public decimal takerTradeImbalance;
        public double takerQuoteImbalance;
        public double takerMidRatio;

        public tradeSummary()
        {
            this.init();   
        }
        public bool setTakerFill(DataFill fill,DateTime? timestamp = null,decimal fee_rate = 0)
        {
            if(this.taker_symbolmarket == fill.symbol_market)
            {
                if(this.taker_orderid == "")
                {
                    this.taker_orderid = fill.internal_order_id;
                    this.taker_side = fill.side;
                }
                else if(this.taker_orderid != fill.internal_order_id || this.taker_side != fill.side)
                {
                    return false;
                }
                if(!timestamp.HasValue)
                {
                    timestamp = fill.timestamp;
                }
                this.taker_avgExecutedTime = (this.taker_avgExecutedTime * (double)this.taker_quantity + timestamp.Value.Ticks * (double)fill.quantity) / (double)(this.taker_quantity + fill.quantity);
                this.taker_avgprice = (this.taker_avgprice * this.taker_quantity + fill.price * fill.quantity) / (this.taker_quantity + fill.quantity);
                this.taker_quantity += fill.quantity;
                this.totalFee += fill.quantity * fill.price * fee_rate;
            }
            else
            {
                return false;
            }
            return true;
        }
        public bool setMakerFill(DataFill fill, DateTime? _timestamp = null, decimal fee_rate = 0)
        {
            if (this.maker_symbolmarket == fill.symbol_market)
            {
                if (this.maker_orderid== "")
                {
                    this.maker_orderid = fill.internal_order_id;
                    this.timestamp = fill.filled_time;
                    this.maker_side = fill.side;
                }
                else if (this.maker_orderid != fill.internal_order_id || this.maker_side != fill.side)
                {
                    return false;
                }
                if (_timestamp.HasValue)
                {
                    this.maker_avgExecutedTime = (this.maker_avgExecutedTime * (double)this.maker_quantity + _timestamp.Value.Ticks * (double)fill.quantity) / (double)(this.maker_quantity + fill.quantity);
                }
                this.maker_avgprice = (this.maker_avgprice * this.maker_quantity + fill.price * fill.quantity) / (this.maker_quantity + fill.quantity);
                this.maker_quantity += fill.quantity;
                this.totalFee += fill.quantity * fill.price * fee_rate;
                //this.totalFee += fill.fee_quote + fill.fee_base * fill.price; 
            }
            else
            {
                return false;
            }
            return true;
        }
        public void setPricingInfo(string strategy_name,string m_symbolmarket,string t_symbolmarket, int layer = 0,decimal makerMarkup = 0,decimal takerMarkup = 0,double rv = 0,decimal skew = 0,decimal skew_widening = 0,decimal const_skew = 0,decimal makerPrAdj = 0,decimal takerPrAdj = 0)
        {
            this.strategy = strategy_name;
            this.layer = layer;
            this.maker_symbolmarket = m_symbolmarket;
            this.taker_symbolmarket = t_symbolmarket;
            this.realized_volatility = rv;
            this.maker_markup = makerMarkup;
            this.taker_markup = takerMarkup;
            this.skew = skew;
            this.skew_widening = skew_widening;
            this.const_skew_widening = const_skew;
            this.maker_priceAdjustment = makerPrAdj;
            this.taker_priceAdjustment = takerPrAdj;
        }

        public void addFactors(double mLat, decimal mtImb, double mqImb, double mMid, double tLat, decimal ttImb, double tqImb, double tMid)
        {
            this.makerLatency = mLat;
            this.makerTradeImbalance = mtImb;
            this.makerQuoteImbalance = mqImb;
            this.makerMidRatio = mMid;
            this.takerLatency = tLat;
            this.takerTradeImbalance = ttImb;
            this.takerQuoteImbalance = tqImb;
            this.takerMidRatio = tMid;
        }
        public void calcPnL()
        {
            decimal quantity = Math.Min(this.taker_quantity,this.maker_quantity);
            int sign = 1;
            decimal extra_skew = this.skew_widening;
            decimal const_skew = this.const_skew_widening;
            if(this.skew < 0)
            {
                extra_skew = 0;
                const_skew = 0;
            }
            if(this.maker_side == orderSide.Buy)
            {
                sign = -1;
                if (this.skew > 0)
                {
                    extra_skew = 0;
                    const_skew = 0;
                }
            }
            this.totalPnL = quantity * sign * (this.maker_avgprice - this.taker_avgprice) - this.totalFee;
            if(sign > 0)
            {
                this.markupPnL = quantity * (this.maker_avgprice * (1 - 1_000_000 / (1_000_000 + this.maker_markup)) + this.taker_avgprice * (1_000_000 / (1_000_000 - this.taker_markup) - 1));
            }
            else if(sign < 0)
            {
                this.markupPnL = quantity * (this.maker_avgprice * (1_000_000 / (1_000_000 - this.maker_markup) - 1) + this.taker_avgprice * (1 - 1_000_000 / (1_000_000 + this.taker_markup)));
            }
            this.skewPnL = quantity * sign * ((const_skew + (1 + extra_skew) * this.skew) / 1_000_000 * this.taker_avgprice);
            this.priceAdjPnL = quantity * sign * (this.maker_avgprice * this.maker_priceAdjustment + this.taker_avgprice * - this.taker_priceAdjustment);
            this.residualPnL = this.totalPnL + this.totalFee - this.markupPnL - this.skewPnL - this.priceAdjPnL;
            this.avg_latency = (this.taker_avgExecutedTime - this.maker_avgExecutedTime) / 10000.0;
        }
        public void init()
        {
            this.id = "";
            this.strategy = "";
            this.timestamp = new DateTime(2000, 1, 1, 0, 0, 0);
            this.BBook = false;
            this.maker_symbolmarket = "";
            this.taker_symbolmarket = "";
            this.maker_orderid = "";
            this.taker_orderid = "";
            this.maker_side = orderSide.NONE;
            this.taker_side = orderSide.NONE;
            this.layer = 0;
            this.maker_avgprice = 0;
            this.taker_avgprice = 0;
            this.maker_quantity = 0;
            this.taker_quantity = 0;
            this.realized_volatility = 0;
            this.maker_markup = 0;
            this.taker_markup = 0;
            this.maker_avgExecutedTime = 0;
            this.taker_avgExecutedTime = 0;
            this.skew = 0;
            this.skew_widening = 0;
            this.const_skew_widening = 0;
            this.maker_priceAdjustment = 0;
            this.taker_priceAdjustment = 0;
            this.totalPnL = 0;
            this.totalFee = 0;
            this.skewPnL = 0;
            this.priceAdjPnL = 0;
            this.markupPnL = 0;
            this.residualPnL = 0;
            this.avg_latency = 0;

            this.makerLatency = 0;
            this.makerTradeImbalance = 0;
            this.makerQuoteImbalance = 0;
            this.makerMidRatio = 0;
            this.takerLatency = 0;
            this.takerTradeImbalance = 0;
            this.takerQuoteImbalance = 0;
            this.takerMidRatio = 0;
        }
        public string ToString()
        {
            string strtime;
            if(this.timestamp.HasValue)
            {
                strtime = this.timestamp.Value.ToString(GlobalVariables.tmMsecFormat);
            }
            else
            {
                strtime = "";
            }
            //timestamp,id,BBook,maker_symbolmarket,taker_symbolmarket,maker_orderid,taker_orderid,maker_side,maker_avgprice,maker_quantity,maker_avgExecutedTime,taker_side,taker_avgprice,taker_quantity,taker_avgExecutedTime,maker_markup,taker_markup,skew,maker_priceAdj,taker_priceAdj,markupPnL,skewPnL,priceAdjPnL,residualPnL,totalFee,totalPnL,avg_Latency;
            return $"{strtime},{strategy},{this.id},{this.BBook},{this.maker_symbolmarket},{this.taker_symbolmarket},{this.maker_orderid},{this.taker_orderid},{this.layer},{this.maker_side},{this.maker_avgprice},{this.maker_quantity},{this.maker_avgExecutedTime},{this.taker_side},{this.taker_avgprice},{this.taker_quantity},{this.taker_avgExecutedTime},{this.realized_volatility},{this.maker_markup},{this.taker_markup},{this.skew},{this.maker_priceAdjustment},{this.taker_priceAdjustment},{this.markupPnL},{this.skewPnL},{this.priceAdjPnL},{this.residualPnL},{this.totalFee},{this.totalPnL},{this.avg_latency},{this.makerLatency},{this.makerTradeImbalance},{this.makerQuoteImbalance},{this.makerMidRatio},{this.takerLatency},{this.takerTradeImbalance},{this.takerQuoteImbalance},{this.takerMidRatio}";
        }

    }
    public class logEntry
    {
        public string? logtype { get; set; }
        public string? msg { get; set; }
    }

    public class masterInfo
    {
        public string? symbol { get; set; }
        public string? baseCcy { get; set; }
        public string? quoteCcy { get; set; }
        public string? market { get; set; }
        public decimal taker_fee { get; set; }
        public decimal maker_fee { get; set; }
        public decimal price_unit { get; set; }
        public decimal quantity_unit { get; set; }
    }
    public class strategySetting
    {
        public string? name { get; set; }
        public string? baseCcy { get; set; }
        public string? quoteCcy { get; set; }
        public string? taker_market { get; set; }
        public string? maker_market { get; set; }
        public double order_throttle { get; set; }
        public decimal markup { get; set; }
        //public decimal markup_adjustment { get; set; }
        public decimal min_markup { get; set; }
        public decimal max_skew { get; set; }
        public decimal skew_widening { get; set; }
        public decimal skew_widening_const { get; set; }
        public decimal maxMakerPosition { get; set; }
        public decimal targetMakerPosition { get; set; }
        public decimal ToBsize { get; set; }
        public decimal ToBsizeMultiplier { get; set; }
        public decimal intervalAfterFill { get; set; }
        public decimal modThreshold { get; set; }
        public decimal skewThreshold { get; set; }
        public decimal oneSideThreshold { get; set; }
        public decimal decaying_time { get; set; }
        public decimal rv_penalty_multiplier { get; set; }
        public double rv_base_param { get; set; }
        public decimal maxBaseMarkup { get; set; }
        public Boolean predictFill { get; set; }
        public string? skew_type { get; set; }
        public decimal skew_step { get; set; }
    }
    public class fillInfo
    {
        public string? timestamp { get; set; }
        public string? market { get; set; }
        public string? symbol { get; set; }
        public string? side { get; set; }
        public string? fill_price { get; set; }
        public string? quantity { get; set; }
        public string? fee { get; set; }
        public string? msg { get; set; }
    }
    public class strategyInfo
    {
        public string? name { get; set; }
        public string? baseCcy { get; set; }
        public string? quoteCcy { get; set; }
        public string? maker_market { get; set; }
        public string? taker_market { get; set; }
        public string? maker_symbol_market { get; set; }
        public string? taker_symbol_market { get; set; }

        public decimal spread { get; set; }
        public decimal markup { get; set; }
        public decimal skew { get; set; }

        public decimal ask { get; set; }
        public decimal askSize { get; set; }
        public decimal bid { get; set; }
        public decimal bidSize { get; set; }

        public decimal liquidity_ask { get; set; }
        public decimal liquidity_bid { get; set; }

        public decimal notionalVolume { get; set; }
        public decimal posPnL { get; set; }
        public decimal tradingPnL { get; set; }
        public decimal totalFee { get; set; }
        public decimal totalPnL { get; set; }
        public decimal notionalVolumeB { get; set; }
        public decimal markupPnL { get; set; }
        public decimal skewPnL { get; set; }
        public decimal priceAdjPnL { get; set; }
        public decimal residualPnL { get; set; }
        public decimal tradingPnLB { get; set; }

        public decimal mi_volume { get; set; }
        public Dictionary<double, decimal>? market_impact_curve { get; set; }
    }
    public class balanceInfo
    {
        public string ? market { get; set; }
        public string ? posType { get; set; }
        public string ? symbol { get; set; }
        public string ? side { get; set; }
        public decimal total { get; set; }
        public decimal avg_price { get; set; }
        public decimal current_price { get; set; }
        public string ? valuation_pair { get; set; }
        public decimal unrealized_fee { get; set; }
        public decimal unrealized_interest { get; set; }
        public bool isSoD { get; set; }
    }

    public class instrumentInfo
    {
        public string? symbol { get; set; }
        public string? market { get; set; }
        public string? symbol_market { get; set; }
        public string? baseCcy { get; set; }
        public string? quoteCcy { get; set; }

        public decimal last_price { get; set; }
        public decimal notional_buy { get; set; }
        public decimal notional_sell { get; set; }
        public decimal quantity_buy { get; set; }
        public decimal quantity_sell { get; set; }
        public double avg_RV { get; set; }
        public double realized_volatility { get; set; }

        //balance
        public decimal baseCcy_total { get; set; }
        public decimal baseCcy_inuse { get; set; }
        public decimal quoteCcy_total { get; set; }
        public decimal quoteCcy_inuse { get; set; }
        public decimal long_total { get; set; }
        public decimal long_inuse { get; set; }
        public decimal long_avg_price { get; set; }
        public decimal short_total { get; set; }
        public decimal short_inuse { get; set; }
        public decimal short_avg_price { get; set; }
        public decimal marginAvailable { get; set; }//Total assets - margin position + unrealized PnL
        public decimal marginInuse { get; set; }//The amount locked as existing orders
        public decimal leverage { get; set; }

        //Execution
        public decimal my_quantity_buy { get; set; }
        public decimal my_notional_buy { get; set; }
        public decimal my_quantity_sell { get; set; }
        public decimal my_notional_sell { get; set; }

        public decimal quoteFee_total { get; set; }
        public decimal baseFee_total { get; set; }
        public decimal mi_volume { get; set; }
        public Dictionary<double,decimal>? market_impact_curve { get; set; }

        //Factors
        public decimal tradeImbalance { get; set; }
        public double quoteImbalance { get; set; }
        public double weightedMid { get; set; }
    }

    public class connecitonStatus
    {
        public string? market { get; set; }
        public string? publicState { get; set; }
        public string? privateState { get; set; }
        public double avgRTT { get; set; }
    }

    public class threadStatus
    {
        public string? name { get; set; }
        public bool isRunning { get; set; }
        public double avgProcessingTime { get; set; }
    }
    public class queueInfo
    {
        public string? name { get; set; }
        public int count { get; set; }
    }
    public class variableUpdate
    {
        public string stg_name { get; set; }
        public string? type { get; set; }
        public string? value { get; set; }
    }
    public class intradayPnL
    {
        public string? strategy_name { get; set; }
        public double OADatetime { get; set; }
        public double PnL { get; set; }
        public double notionalVolume { get; set; }
    }
    public class latency
    {
        public string? name { get; private set; }
        public int count { get; private set; }
        public double totalElapsedTime { get; private set; }
        public double maxElapsedTime { get; private set; }
        public double avgLatency { get { return (count == 0) ? 0 : totalElapsedTime / count; } }
        public Stopwatch sw = new Stopwatch();
        public latency(string _name = "")
        {
            this.name = _name;
            this.count = 0;
            this.totalElapsedTime = 0;
            this.maxElapsedTime = 0;
            int i = 0;
            while (i < 10)
            {
                sw.Start();
                sw.Stop();
                sw.Reset();
                ++i;
            }
        }
        public void start()
        {
            this.sw.Start();
        }
        public Action MeasureLatency()
        {
            this.sw.Start();
            return this.stop;
        }
        public void stop()
        {
            this.sw.Stop();
            this.addLatency(sw.Elapsed.TotalMicroseconds);
            this.sw.Reset();
        }
        public void init(string _name = "")
        {
            this.name = _name;
            this.count = 0;
            this.totalElapsedTime = 0;
            this.maxElapsedTime = 0;
            int i = 0;
            while (i < 10)
            {
                sw.Start();
                sw.Stop();
                sw.Reset();
                ++i;
            }
        }
        public void addLatency(double elapsedTime)
        {
            ++(this.count);
            this.totalElapsedTime += elapsedTime;
            if (elapsedTime > this.maxElapsedTime)
            {
                this.maxElapsedTime = elapsedTime;
            }
        }
        public override string ToString()
        {
            return this.name + "," + this.count.ToString() + "," + this.avgLatency.ToString() + "," + this.maxElapsedTime.ToString();
        }
    }

    public class PnLBreakDown
    {
        public string strategy;
        public string symbol;
        public decimal totalAmount;
        public decimal notionalVolume;
        public decimal SoDBalance;
        public decimal EoDBalance;
        public decimal markupPnL;
        public decimal skewPnL;
        public decimal priceAdjPnL;
        public decimal residualPnL;
        public decimal totalFee;
        public decimal totalPnL;
        public double avg_Latency;
        public int count;

        public PnLBreakDown()
        {
            strategy = "";
            symbol = "";
            totalAmount = 0;
            notionalVolume = 0;
            SoDBalance = 0;
            EoDBalance = 0;
            markupPnL = 0;
            skewPnL = 0;
            priceAdjPnL = 0;
            residualPnL = 0;
            totalFee = 0;
            totalPnL = 0;
            avg_Latency = 0;
            count = 0;
        }
        public void addData(string[] str_data)
        {
            //"0timestamp,1strategy,2id,3BBook,4maker_symbolmarket,5taker_symbolmarket,6maker_orderid,7taker_orderid,8layer,9maker_side,10maker_avgprice,11maker_quantity,
            //12maker_avgExecutedTime,13taker_side,14taker_avgprice,15taker_quantity,16taker_avgExecutedTime,17realized_volatility,18maker_markup,19taker_markup,20skew,21maker_priceAdj,
            //22taker_priceAdj,23markupPnL,24skewPnL,25priceAdjPnL,26residualPnL,27totalFee,28totalPnL,29avg_Latency"
            ++this.count;
            this.markupPnL += Decimal.Parse(str_data[23]);
            this.skewPnL += Decimal.Parse(str_data[24]);
            this.priceAdjPnL += Decimal.Parse(str_data[25]);
            this.residualPnL += Decimal.Parse(str_data[26]);
            this.totalFee += Decimal.Parse(str_data[27]);
            this.totalPnL += Decimal.Parse(str_data[28]);
            decimal min_quantity = Math.Min(Decimal.Parse(str_data[11]), Decimal.Parse(str_data[15]));
            this.avg_Latency = (this.avg_Latency * (double)this.totalAmount + Double.Parse(str_data[29]) * (double)min_quantity) / (double)(this.totalAmount + min_quantity);
            this.totalAmount += min_quantity;
            this.notionalVolume += Decimal.Parse(str_data[11]) * Decimal.Parse(str_data[10]);
        }
        public void addData(tradeSummary tpt_data)
        {
            ++this.count;
            this.markupPnL += tpt_data.markupPnL;
            this.skewPnL += tpt_data.skewPnL;
            this.priceAdjPnL += tpt_data.priceAdjPnL;
            this.residualPnL += tpt_data.residualPnL;
            this.totalFee += tpt_data.totalFee;
            this.totalPnL += tpt_data.totalPnL;
            decimal min_quantity = Math.Min(tpt_data.maker_quantity, tpt_data.taker_quantity);
            this.avg_Latency = (this.avg_Latency * (double)this.totalAmount + tpt_data.avg_latency * (double)min_quantity) / (double)(this.totalAmount + min_quantity);
            this.totalAmount += min_quantity;
            this.notionalVolume += tpt_data.maker_quantity * tpt_data.maker_avgprice;
        }

        public string ToString()
        {
            return $"{this.strategy},{this.symbol},{this.SoDBalance},{this.EoDBalance},{this.count},{this.totalAmount},{this.notionalVolume},{this.markupPnL},{this.skewPnL},{this.priceAdjPnL},{this.residualPnL},{this.totalFee},{this.totalPnL},{this.avg_Latency}";
        }
    }
    public class orderCondition
    {
        public string order_id;
        public orderSide side;
        public positionSide pos_side;
        public decimal price;
        public decimal quantity;

        public orderCondition()
        {
            this.init();
        }
        public void init()
        {
            this.order_id = "";
            this.side = orderSide.NONE;
            this.pos_side = positionSide.NONE;
            this.price = 0;
            this.quantity = 0;
        }
    }
}
