using System.Runtime.CompilerServices;
using System.Threading;

using Crypto_Clients;
using CryptoExchange.Net.Objects.Options;

namespace Crypto_Trading
{
    public class Instrument
    {
        public string symbol;
        public string baseCcy;
        public string quoteCcy;
        public string market;
        public string symbol_market;
        public string master_symbol;

        public DateTime? last_quote_updated_time;
        public DateTime? last_traded_time;

        public volatile int quotes_lock;
        public SortedDictionary<decimal, decimal> asks;
        public SortedDictionary<decimal, decimal> bids;

        public ValueTuple<decimal, decimal> bestask;
        public ValueTuple<decimal, decimal> bestbid;
        //Amount Weighted Best Ask/Bid +/- fee
        public ValueTuple<decimal, decimal> adjusted_bestask;
        public ValueTuple<decimal, decimal> adjusted_bestbid;

        public decimal last_price;
        public decimal mid;
        public decimal prev_mid;

        public decimal sell_quantity;
        public decimal buy_quantity;
        public decimal sell_notional;
        public decimal buy_notional;

        public decimal my_sell_quantity;
        public decimal my_buy_quantity;
        public decimal my_sell_notional;
        public decimal my_buy_notional;

        public decimal total_fee;
        public decimal unknown_fee;

        public Balance baseBalance;
        public Balance quoteBalance;

        public decimal taker_fee;
        public decimal maker_fee;

        public decimal price_unit;
        public decimal quantity_unit;
        public string price_scale;
        public string quantity_scale;

        public decimal ToBsize;

        public Instrument()
        {
            this.symbol = "";
            this.baseCcy = "";
            this.quoteCcy = "";
            this.market = "";
            this.symbol_market = "";
            this.master_symbol = "";

            this.last_quote_updated_time = null;
            this.quotes_lock = 0;
            this.asks = new SortedDictionary<decimal, decimal>();
            this.bids = new SortedDictionary<decimal, decimal>();

            this.bestask = new ValueTuple<decimal, decimal>(0, 0);
            this.bestbid = new ValueTuple<decimal, decimal>(0, 0);
            //Amount Weighted Best Ask/Bid +/- fee
            this.adjusted_bestask = new ValueTuple<decimal, decimal>(0,0);
            this.adjusted_bestbid = new ValueTuple<decimal, decimal>(0,0);

            this.last_price = 0;
            this.sell_quantity = 0;
            this.buy_quantity = 0;
            this.sell_notional = 0;
            this.buy_notional = 0;

            this.my_sell_quantity = 0;
            this.my_buy_quantity = 0;
            this.my_sell_notional = 0;
            this.my_buy_notional = 0;

            this.total_fee = 0;
            this.unknown_fee = 0;

            this.baseBalance = new Balance();
            this.quoteBalance = new Balance();

            this.taker_fee = 0;
            this.maker_fee = 0;

            this.price_unit = 0;
            this.quantity_unit = 0;

            this.ToBsize = 0;
        }

        public void initialize(string line)
        {
            string[] items = line.Split(",");

            int i = 0;
            foreach (string item in items)
            {
                switch (i)
                {
                    case 0: this.symbol = item; break;
                    case 1: this.baseCcy = item; break;
                    case 2: this.quoteCcy = item; break;
                    case 3: this.market = item; break;
                    case 4: this.taker_fee = decimal.Parse(item); break;
                    case 5: this.maker_fee = decimal.Parse(item); break;
                    case 6: this.price_unit = decimal.Parse(item); break;
                    case 7: this.quantity_unit = decimal.Parse(item); break;
                    default: break;
                }
                i++;
            }

            this.price_scale = Instrument.GetDecimalScale(this.price_unit).ToString();
            this.quantity_scale = Instrument.GetDecimalScale(this.quantity_unit).ToString();

            this.master_symbol = this.baseCcy + this.quoteCcy;
            this.baseBalance.market = this.market;
            this.baseBalance.ccy = this.baseCcy;
            this.quoteBalance.market = this.market;
            this.quoteBalance.ccy = this.quoteCcy;
            this.setSymbolMarket(symbol, market);
        }
        static int GetDecimalScale(decimal value)
        {
            int[] bits = decimal.GetBits(value);
            byte scale = (byte)((bits[3] >> 16) & 0x7F);
            return scale;
        }

        public void setSymbolMarket(string symbol, string market)
        {
            this.symbol = symbol;
            this.market = market;
            this.symbol_market = symbol + "@" + market;
        }

        public void updateQuotes(DataOrderBook update)
        {
            int desired = 1;
            int expected = 0;
            this.last_quote_updated_time = update.timestamp;
            switch (update.updateType)
            {
                case CryptoExchange.Net.Objects.SocketUpdateType.Snapshot:

                    while(Interlocked.CompareExchange(ref this.quotes_lock,1,0) != 0)
                    {

                    }
                    this.asks.Clear();
                    foreach(var item in update.asks)
                    {
                        this.asks[item.Key] = item.Value;
                    }
                    this.bids.Clear();
                    foreach (var item in update.bids)
                    {
                        this.bids[item.Key] = item.Value;
                    }
                    Volatile.Write(ref this.quotes_lock, 0);
                    break;
                case CryptoExchange.Net.Objects.SocketUpdateType.Update:

                    while (Interlocked.CompareExchange(ref this.quotes_lock, 1, 0) != 0)
                    {

                    }
                    foreach (var item in update.asks)
                    {
                        if (item.Value == 0)
                        {
                            this.asks.Remove(item.Key);
                        }
                        else
                        {
                            this.asks[item.Key] = item.Value;
                        }
                    }
                    foreach (var item in update.bids)
                    {
                        if (item.Value == 0)
                        {
                            this.bids.Remove(item.Key);
                        }
                        else
                        {
                            this.bids[item.Key] = item.Value;
                        }
                    }
                    Volatile.Write(ref this.quotes_lock, 0);
                    break;
            }
            this.updateBeskAskBid(this.ToBsize);
        }
        public void updateTrade(DataTrade update)
        {
            this.last_traded_time = update.timestamp;
            this.last_price = update.price;
            switch (update.side)
            {
                case CryptoExchange.Net.SharedApis.SharedOrderSide.Buy:
                    this.buy_quantity += update.quantity;
                    this.buy_notional += update.quantity * update.price;
                    break;
                case CryptoExchange.Net.SharedApis.SharedOrderSide.Sell:
                    this.sell_quantity += update.quantity;
                    this.sell_notional += update.quantity * update.price;
                    break;
            }
        }

        public void updateBeskAskBid(decimal quantity)
        {
            if(quantity > 0)
            {
                decimal cumQuantity = 0;
                decimal weightedPrice = 0;
                foreach(var item in this.asks)
                {
                    if(cumQuantity + item.Value < quantity)
                    {
                        cumQuantity += item.Value;
                        weightedPrice += item.Value * item.Key;
                    }
                    else
                    {
                        cumQuantity += (quantity - cumQuantity);
                        weightedPrice += (quantity - cumQuantity) * item.Key;
                        break;
                    }
                }
                weightedPrice /= cumQuantity;
                this.adjusted_bestask.Item1 = weightedPrice;
                this.adjusted_bestask.Item2 = cumQuantity;

                cumQuantity = 0;
                weightedPrice = 0;
                foreach (var item in this.bids.Reverse())
                {
                    if (cumQuantity + item.Value < quantity)
                    {
                        cumQuantity += item.Value;
                        weightedPrice += item.Value * item.Key;
                    }
                    else
                    {
                        cumQuantity += (quantity - cumQuantity);
                        weightedPrice += (quantity - cumQuantity) * item.Key;
                        break;
                    }
                }
                weightedPrice /= cumQuantity;
                this.adjusted_bestbid.Item1 = weightedPrice;
                this.adjusted_bestbid.Item2 = cumQuantity;
            }
            else
            {
                if(this.asks.Count > 0)
                {
                    this.adjusted_bestask.Item1 = this.asks.First().Key * (1 + this.taker_fee);
                    this.adjusted_bestask.Item2 = this.asks.First().Value;
                }
                if(this.bids.Count > 0)
                {
                    this.adjusted_bestbid.Item1 = this.bids.Last().Key * (1 - this.taker_fee);
                    this.adjusted_bestbid.Item2 = this.bids.Last().Value;
                }
            }
            if (this.asks.Count > 0)
            {
                this.bestask.Item1 = this.asks.First().Key;
                this.bestask.Item2 = this.asks.First().Value;
            }
            if (this.bids.Count > 0)
            {
                this.bestbid.Item1 = this.bids.Last().Key;
                this.bestbid.Item2 = this.bids.Last().Value;
            }

            if(this.bestask.Item1 > 0 && this.bestbid.Item1 > 0)
            {
                this.prev_mid = this.mid;
                this.mid = (this.bestask.Item1 + this.bestbid.Item1) / 2;
            }
            else if(this.bestask.Item1 > 0)
            {
                this.prev_mid = this.mid;
                this.mid = this.bestask.Item1;
            }
            else if (this.bestbid.Item1 > 0)
            {
                this.prev_mid = this.mid;
                this.mid = this.bestbid.Item1;
            }
            else
            {
                this.mid = 0;
            }

        }
        public void updateFills(DataSpotOrderUpdate prev_ord,DataSpotOrderUpdate new_ord)
        {
            decimal filledQuantity;
            decimal filledPrice;
            decimal fee;

            filledQuantity = new_ord.filled_quantity - prev_ord.filled_quantity;

            if(new_ord.isVirtual == false && new_ord.market == "bitbank")//Update fill with fill object.
            {
                filledQuantity = 0;
                fee = 0;
                filledPrice = 0;
            }
            else if (filledQuantity == 0)
            {
                filledQuantity = new_ord.filled_quantity;
                filledPrice = new_ord.average_price;
                fee = new_ord.fee;
            }
            else
            {
                filledPrice = (new_ord.filled_quantity * new_ord.average_price - prev_ord.filled_quantity * prev_ord.average_price) / filledQuantity;
                fee = new_ord.fee - prev_ord.fee;
            }

            if (new_ord.side == orderSide.Sell)
            {
                this.my_sell_quantity += filledQuantity;
                this.my_sell_notional += filledQuantity * filledPrice;
                filledQuantity *= -1;
            }
            else
            {
                this.my_buy_quantity += filledQuantity;
                this.my_buy_notional += filledQuantity * filledPrice;
            }
            this.baseBalance.balance += filledQuantity;
            this.quoteBalance.balance -= filledQuantity * filledPrice;

            if(new_ord.fee_asset == this.baseCcy)
            {
                this.baseBalance.balance -= fee;
                this.total_fee += fee * filledPrice;
            }
            else if(new_ord.fee_asset == this.quoteCcy)
            {
                this.quoteBalance.balance -= fee;
                this.total_fee += fee;
            }
            else
            {
                this.unknown_fee += fee;
            }
        }

        public void updateFills(DataFill fill)
        {
            if(fill.side == orderSide.Buy)
            {
                this.my_buy_quantity += fill.quantity;
                this.my_buy_notional += fill.quantity * fill.price;
                this.baseBalance.balance += fill.quantity;
                this.quoteBalance.balance -= fill.quantity * fill.price;
            }
            else if(fill.side == orderSide.Sell)
            {

                this.my_sell_quantity += fill.quantity;
                this.my_sell_notional += fill.quantity * fill.price;
                this.baseBalance.balance -= fill.quantity;
                this.quoteBalance.balance += fill.quantity * fill.price;
            }
            this.baseBalance.balance -= fill.fee_base;
            this.quoteBalance.balance -= fill.fee_quote;
            this.total_fee += fill.fee_quote;
            this.total_fee += fill.fee_base * fill.price;
        }
        public string ToString(string content = "")
        {
            int desired = 1;
            int expected = 0;
            string output = this.symbol_market + " " + content + "\n";
            if(content == "" || content == "General")
            {
                output = this.symbol + "," + this.baseCcy + "," + this.quoteCcy + "," + this.market + "," + this.taker_fee.ToString() + "," + this.maker_fee + "," + this.price_unit + "," + this.quantity_unit;
            }
            else if(content == "Quote5")
            {
                int i = 4;
                int priceStrLength = 0;
                while (Interlocked.CompareExchange(ref this.quotes_lock, 1, 0) != 0)
                {

                }
                if (i >= this.asks.Count)
                {
                    i = this.asks.Count - 1;
                }
                while (i >= 0)
                {
                    string temp = "";
                    temp += this.asks.ElementAt(i).Value.ToString("N5").PadLeft(10) + " | ";
                    string strPrice = this.asks.ElementAt(i).Key.ToString("N2");
                    if (i == 4)
                    {
                        priceStrLength = strPrice.Length;
                        if(priceStrLength < 7)
                        {
                            priceStrLength = 7;
                        }
                    }
                    else
                    {
                        strPrice = strPrice.PadLeft(priceStrLength);
                    }
                    temp += strPrice + " | ";
                    temp = temp.PadRight(temp.Length + 10);
                    output += temp + "\n";
                    --i;
                }
                i = 0;
                foreach(var item in this.bids.Reverse())
                {
                    if(i > 4)
                    {
                        break;
                    }
                    else
                    {
                        string temp = "".PadLeft(10) + " | ";
                        string strPrice = item.Key.ToString("N2").PadLeft(priceStrLength);
                        temp += strPrice + " | ";
                        temp += item.Value.ToString("N5").PadRight(10);
                        output += temp + "\n";
                    }
                        ++i;
                }
                Volatile.Write(ref this.quotes_lock, 0);
            }
            return output;
        }
    }
}
