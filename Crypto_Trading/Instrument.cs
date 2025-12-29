using Crypto_Clients;
using CryptoExchange.Net.Objects.Options;
using DeepCoin.Net.Objects.Models;
using Discord;
using Discord.Audio.Streams;
using Enums;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using Utils;

namespace Crypto_Trading
{
    public class Instrument
    {
        public string symbol;
        public string baseCcy;
        public string quoteCcy;
        public string market;
        public string master_symbol;
        public string symbol_market
        {
            get { return symbol + "@" + market; }
        }

        public DateTime? last_quote_updated_time;
        public DateTime? last_traded_time;

        public volatile int quotes_lock;
        public long quoteSeqNo;
        public SortedDictionary<decimal, decimal> asks;
        public SortedDictionary<decimal, decimal> bids;

        public DateTime quoteTime;

        DateTime sample1Time;
        double sample_latency1;
        DateTime intercept_time1;
        public double intercept_latency1;
        DateTime intercept_time2;
        public double intercept_latency2;
        double sample_gap = 60;
        double sample_gap2 = 3600;
        int count = 0;
        int currentSampling = 0;
        int NofSample = 100;
        public double coef;
        public double intercept;
        DateTime intercept_time;
        public double base_latency = 20;

        public int count_Allquotes;
        public int count_Latentquotes;
        public int count_AllTrade;
        public int count_LatentTrade;
        public int count_AllOrderUpdates;
        public int count_LatentOrderUpdates;
        public int count_AllFill;
        public int count_LatentFill;

        public int cum_Allquotes;
        public int cum_Latentquotes;
        public int cum_AllTrade;
        public int cum_LatentTrade;
        public int cum_AllOrderUpdates;
        public int cum_LatentOrderUpdates;
        public int cum_AllFill;
        public int cum_LatentFill;
        public double latencyTh = 200;

        public ValueTuple<decimal, decimal> bestask;
        public ValueTuple<decimal, decimal> bestbid;

        public ValueTuple<decimal, decimal> prev_bestask;
        public ValueTuple<decimal, decimal> prev_bestbid;
        public DateTime bestask_time;
        public DateTime bestbid_time;
        //Amount Weighted Best Ask/Bid +/- fee
        public ValueTuple<decimal, decimal> adjusted_bestask;
        public ValueTuple<decimal, decimal> adjusted_bestbid;

        public volatile int order_lock;
        public Dictionary<string, DataSpotOrderUpdate> orders;
        public Dictionary<string, DataSpotOrderUpdate> live_orders;

        public decimal last_price;
        public decimal mid;
        public decimal prev_mid;
        public decimal adj_mid;
        public decimal adj_prev;

        public decimal open_mid;

        public decimal sell_quantity;
        public decimal buy_quantity;
        public decimal sell_notional;
        public decimal buy_notional;

        public decimal my_sell_quantity;
        public decimal my_buy_quantity;
        public decimal my_sell_notional;
        public decimal my_buy_notional;

        public double RV_minute;
        public double cumlative_RV;
        public double prev_cumRV;
        public double avg_RV;
        public double realized_volatility;
        public double prev_RV;
        DateTime? RV_startTime;
        DateTime? RV_currentPeriodStart;


        public double avg_RV_display;
        public double realized_volatility_display;

        public decimal mi_volume;
        public Dictionary<double, decimal> market_impact_curve;


        public decimal base_fee;
        public decimal quote_fee;
        public decimal unknown_fee;

        public Balance baseBalance;
        public Balance quoteBalance;

        public BalanceMargin longPostion;
        public BalanceMargin shortPosition;

        public decimal net_pos
        {
            get {return this.baseBalance.total + this.longPostion.total - this.shortPosition.total;}
        }

        public Balance SoD_baseBalance;
        public Balance SoD_quoteBalance;

        public decimal taker_fee;
        public decimal maker_fee;

        public decimal price_unit;
        public decimal quantity_unit;
        public string price_scale;
        public string quantity_scale;

        public decimal ToBsize;

        public bool readyToTrade = false;

        public Instrument()
        {
            this.symbol = "";
            this.baseCcy = "";
            this.quoteCcy = "";
            this.market = "";
            this.master_symbol = "";

            this.last_quote_updated_time = null;
            this.quotes_lock = 0;
            this.quoteSeqNo = 0;
            this.asks = new SortedDictionary<decimal, decimal>();
            this.bids = new SortedDictionary<decimal, decimal>();

            this.bestask = new ValueTuple<decimal, decimal>(0, 0);
            this.bestbid = new ValueTuple<decimal, decimal>(0, 0);
            this.prev_bestask = new ValueTuple<decimal, decimal>(0, 0);
            this.prev_bestbid = new ValueTuple<decimal, decimal>(0, 0);
            DateTime current = DateTime.UtcNow;
            this.bestask_time = current;
            this.bestbid_time = current;
            //Amount Weighted Best Ask/Bid +/- fee
            this.adjusted_bestask = new ValueTuple<decimal, decimal>(0,0);
            this.adjusted_bestbid = new ValueTuple<decimal, decimal>(0,0);

            this.orders = new Dictionary<string, DataSpotOrderUpdate>();
            this.live_orders = new Dictionary<string, DataSpotOrderUpdate>();

            this.mid = -1;
            this.prev_mid = -1;
            this.adj_mid = -1;
            this.adj_prev = -1;

            this.open_mid = -1;

            this.last_price = 0;
            this.sell_quantity = 0;
            this.buy_quantity = 0;
            this.sell_notional = 0;
            this.buy_notional = 0;

            this.my_sell_quantity = 0;
            this.my_buy_quantity = 0;
            this.my_sell_notional = 0;
            this.my_buy_notional = 0;

            this.RV_minute = 0.2;
            this.cumlative_RV = 0;
            this.avg_RV = 0;
            this.realized_volatility = 0;
            this.prev_cumRV = 0;
            this.RV_startTime = null;
            this.RV_currentPeriodStart = null;

            this.mi_volume = 0;
            this.market_impact_curve = new Dictionary<double, decimal>();
            foreach(double d in GlobalVariables.MI_period)
            {
                this.market_impact_curve[d] = 0;
            }

            this.base_fee = 0;
            this.quote_fee = 0;
            this.unknown_fee = 0;

            this.baseBalance = new Balance();
            this.quoteBalance = new Balance();

            this.longPostion = new BalanceMargin();
            this.shortPosition = new BalanceMargin();

            this.longPostion.side = positionSide.Long;
            this.shortPosition.side = positionSide.Short;

            this.SoD_baseBalance = new Balance();
            this.SoD_quoteBalance = new Balance();

            this.taker_fee = 0;
            this.maker_fee = 0;

            this.price_unit = 0;
            this.quantity_unit = 0;

            this.ToBsize = 0;

            this.count_Allquotes = 0;
            this.count_Latentquotes = 0;
            this.count_AllTrade = 0;
            this.count_LatentTrade = 0;
            this.count_AllOrderUpdates = 0;
            this.count_LatentOrderUpdates = 0;
            this.count_AllFill = 0;
            this.count_LatentFill = 0;

            this.cum_Allquotes = 0;
            this.cum_Latentquotes = 0;
            this.cum_AllTrade = 0;
            this.cum_LatentTrade = 0;
            this.cum_AllOrderUpdates = 0;
            this.cum_LatentOrderUpdates = 0;
            this.cum_AllFill = 0;
            this.cum_LatentFill = 0;
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
        }
        public void initialize(masterInfo msinfo)
        {
            this.symbol = msinfo.symbol;
            this.baseCcy = msinfo.baseCcy;
            this.quoteCcy = msinfo.quoteCcy;
            this.market = msinfo.market;
            this.taker_fee = msinfo.taker_fee;
            this.maker_fee = msinfo.maker_fee;
            this.price_unit = msinfo.price_unit;
            this.quantity_unit = msinfo.quantity_unit;

            this.price_scale = Instrument.GetDecimalScale(this.price_unit).ToString();
            this.quantity_scale = Instrument.GetDecimalScale(this.quantity_unit).ToString();

            this.master_symbol = this.baseCcy + this.quoteCcy;
            this.baseBalance.market = this.market;
            this.baseBalance.ccy = this.baseCcy;
            this.quoteBalance.market = this.market;
            this.quoteBalance.ccy = this.quoteCcy;
        }
        static int GetDecimalScale(decimal value)
        {
            int[] bits = decimal.GetBits(value);
            byte scale = (byte)((bits[3] >> 16) & 0x7F);
            return scale;
        }

        public double getTheoLatency(DateTime currentTime)
        {
            if (this.readyToTrade)
            {
                return this.coef * (currentTime - this.intercept_time).TotalSeconds + this.intercept;
            }
            else
            {
                return -60000;
            }
        }

        public void updateQuotes(DataOrderBook update)
        {
            this.last_quote_updated_time = update.timestamp;

            if(update.orderbookTime.HasValue)
            {
                this.quoteTime = update.orderbookTime.Value;

                if(this.count < this.NofSample)
                {
                    if(this.count == 0)
                    {
                        if(this.currentSampling == 0)
                        {
                            this.sample_latency1 = (this.last_quote_updated_time.Value - this.quoteTime).TotalMilliseconds;
                            this.sample1Time = this.last_quote_updated_time.Value;
                            ++(this.count);
                        }
                        else if(currentSampling == 1 && (this.last_quote_updated_time.Value - this.sample1Time).TotalSeconds > this.sample_gap)
                        {
                            this.intercept_latency1 = (this.last_quote_updated_time.Value - this.quoteTime).TotalMilliseconds;
                            this.intercept_time1 = this.last_quote_updated_time.Value;
                            ++(this.count);
                        }
                        else if(currentSampling == 2 && (this.last_quote_updated_time.Value - this.sample1Time).TotalSeconds > this.sample_gap2)
                        {
                            this.intercept_latency2 = (this.last_quote_updated_time.Value - this.quoteTime).TotalMilliseconds;
                            this.intercept_time2 = this.last_quote_updated_time.Value;
                            ++(this.count);
                        }
                    }
                    else
                    {
                        if(this.currentSampling == 0)
                        {
                            double currentValue = (this.last_quote_updated_time.Value - this.quoteTime).TotalMilliseconds;
                            if(currentValue < this.sample_latency1)
                            {
                                this.sample_latency1 = currentValue;
                                this.sample1Time = this.last_quote_updated_time.Value;
                            }
                            ++(this.count);
                            if(count == this.NofSample)
                            {
                                count = 0;
                                ++(this.currentSampling);
                            }
                        }
                        else if(currentSampling == 1)
                        {
                            double currentValue = (this.last_quote_updated_time.Value - this.quoteTime).TotalMilliseconds;
                            if (currentValue < this.intercept_latency1)
                            {
                                this.intercept_latency1 = currentValue;
                                this.intercept_time1 = this.last_quote_updated_time.Value;
                            }
                            ++(this.count);
                            if (count == this.NofSample)
                            {
                                this.coef = (this.intercept_latency1 - this.sample_latency1) / (this.intercept_time1 - this.sample1Time).TotalSeconds;
                                this.intercept = this.intercept_latency1;
                                this.intercept_time = this.intercept_time1;
                                this.readyToTrade = true;
                                count = 0;
                                ++(this.currentSampling);
                            }
                        }
                        else if (currentSampling == 2)
                        {
                            double currentValue = (this.last_quote_updated_time.Value - this.quoteTime).TotalMilliseconds;
                            if (currentValue < this.intercept_latency2)
                            {
                                this.intercept_latency2 = currentValue;
                                this.intercept_time2 = this.last_quote_updated_time.Value;
                            }
                            ++(this.count);
                            if (count == this.NofSample)
                            {
                                this.coef = (this.intercept_latency2 - this.sample_latency1) / (this.intercept_time2 - this.sample1Time).TotalSeconds;
                                this.intercept = this.intercept_latency2;
                                this.intercept_time = this.intercept_time2;
                                ++(this.currentSampling);
                            }
                        }
                    }
                }
                else
                {
                    double currentValue = (this.last_quote_updated_time.Value - this.quoteTime).TotalMilliseconds;
                    if(currentValue < this.intercept)
                    {
                        this.coef = (currentValue - this.sample_latency1) / (this.last_quote_updated_time.Value - this.sample1Time).TotalSeconds;
                        this.intercept = currentValue;
                        this.intercept_time = this.last_quote_updated_time.Value;
                    }
                }
            }

            switch (update.updateType)
            {
                case CryptoExchange.Net.Objects.SocketUpdateType.Snapshot:

                    while(Interlocked.CompareExchange(ref this.quotes_lock,1,0) != 0)
                    {

                    }
                    this.quoteSeqNo = update.seqNo;
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

                    this.updateBeskAskBid(this.ToBsize);
                    Volatile.Write(ref this.quotes_lock, 0);
                    break;
                case CryptoExchange.Net.Objects.SocketUpdateType.Update:
                    while (Interlocked.CompareExchange(ref this.quotes_lock, 1, 0) != 0)
                    {

                    }

                    if (this.quoteSeqNo > 0 && update.seqNo <= this.quoteSeqNo)
                    {
                        
                    }
                    else
                    {
                        this.quoteSeqNo = update.seqNo;
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
                        this.updateBeskAskBid(this.ToBsize);
                    }
                        
                    Volatile.Write(ref this.quotes_lock, 0);
                    break;
            }

            if (this.readyToTrade && update.timestamp.HasValue && update.orderbookTime.HasValue)
            {
                ++(this.count_Allquotes);
                ++(this.cum_Allquotes);
                double theo = this.getTheoLatency(update.timestamp.Value);
                double latency = (update.timestamp.Value - update.orderbookTime.Value).TotalMilliseconds;

                if (latency - theo > this.latencyTh)
                {
                    ++(this.count_Latentquotes);
                    ++(this.cum_Latentquotes);
                }
            }
        }
        public void updateTrade(DataTrade update)
        {
            this.last_traded_time = update.timestamp;
            this.last_price = update.price;
            switch (update.side)
            {
                case orderSide.Buy:
                    this.buy_quantity += update.quantity;
                    this.buy_notional += update.quantity * update.price;
                    break;
                case orderSide.Sell:
                    this.sell_quantity += update.quantity;
                    this.sell_notional += update.quantity * update.price;
                    break;
            }

            if (this.readyToTrade && update.timestamp.HasValue && update.filled_time.HasValue)
            {
                ++(this.count_AllTrade);
                ++(this.cum_AllTrade);
                double theo = this.getTheoLatency(update.timestamp.Value);
                double latency = (update.timestamp.Value - update.filled_time.Value).TotalMilliseconds;

                if (latency - theo > this.latencyTh)
                {
                    ++(this.count_LatentTrade);
                    ++(this.cum_LatentTrade);
                }
            }
        }

        private bool updateBeskAskBid(decimal quantity)
        {
            bool ret = true;
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
                        weightedPrice += (quantity - cumQuantity) * item.Key;
                        cumQuantity += (quantity - cumQuantity);
                        break;
                    }
                }
                if (cumQuantity > 0)
                {
                    weightedPrice /= cumQuantity;
                    this.adjusted_bestask.Item1 = weightedPrice * (1 + this.taker_fee);
                    this.adjusted_bestask.Item2 = cumQuantity;
                }
                else
                {
                    this.adjusted_bestask.Item1 = 0;
                    this.adjusted_bestask.Item2 = 0;
                }

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
                        weightedPrice += (quantity - cumQuantity) * item.Key;
                        cumQuantity += (quantity - cumQuantity);
                        break;
                    }
                }
                if(cumQuantity > 0)
                {
                    weightedPrice /= cumQuantity;
                    this.adjusted_bestbid.Item1 = weightedPrice * (1 - this.taker_fee);
                    this.adjusted_bestbid.Item2 = cumQuantity;
                }
                else
                {
                    this.adjusted_bestbid.Item1 = 0;
                    this.adjusted_bestbid.Item2 = 0;
                }
                
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
            this.prev_bestask.Item1 = this.bestask.Item1;
            this.prev_bestask.Item2 = this.bestask.Item2;
            this.prev_bestbid.Item1 = this.bestbid.Item1;
            this.prev_bestbid.Item2 = this.bestbid.Item2;
            DateTime current = DateTime.UtcNow;
            if (this.asks.Count > 0)
            {
                this.bestask.Item1 = this.asks.First().Key;
                this.bestask.Item2 = this.asks.First().Value;
                if(this.bestask.Item1 != this.prev_bestask.Item1 || this.bestask.Item2 != this.prev_bestask.Item2)
                {
                    this.bestask_time = current;
                }
            }
            if (this.bids.Count > 0)
            {
                this.bestbid.Item1 = this.bids.Last().Key;
                this.bestbid.Item2 = this.bids.Last().Value;

                if (this.bestbid.Item1 != this.prev_bestbid.Item1 || this.bestbid.Item2 != this.prev_bestbid.Item2)
                {
                    this.bestbid_time = current;
                }
            }
            if(this.bestask.Item1 > 0 && this.bestbid.Item1 > 0 && this.bestbid.Item1 >= this.bestask.Item1)
            {
                ret = false;
                if(this.bestask_time > this.bestbid_time)
                {
                    this.bids.Remove(this.bestbid.Item1);
                }
                else if(this.bestask_time < this.bestbid_time)
                {
                    this.asks.Remove(this.bestask.Item1);
                }
                else
                {
                    this.asks.Remove(this.bestask.Item1);
                    this.bids.Remove(this.bestbid.Item1);
                }
            }
            if (this.adjusted_bestask.Item1 > 0 && this.adjusted_bestbid.Item1 > 0)
            {
                this.adj_prev= this.adj_mid;
                this.adj_mid = (this.adjusted_bestask.Item1 + this.adjusted_bestbid.Item1) / 2;
            }
            else if (this.bestask.Item1 > 0)
            {
                this.adj_prev = this.adj_mid;
                this.adj_mid = this.adjusted_bestask.Item1;
            }
            else if (this.bestbid.Item1 > 0)
            {
                this.adj_prev = this.adj_mid;
                this.adj_mid = this.adjusted_bestbid.Item1;
            }
            else
            {
                this.adj_mid = 0;
            }

            if (this.bestask.Item1 > 0 && this.bestbid.Item1 > 0)
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

            if(this.mid > 0 && this.prev_mid > 0)
            {
                this.cumlative_RV += Math.Pow(Math.Log((double)this.mid / (double)this.prev_mid), 2);
                if (this.RV_startTime == null)
                {
                    this.RV_startTime = current;
                }
                else
                {
                    this.avg_RV = Math.Sqrt(this.cumlative_RV / (current - this.RV_startTime.Value).TotalMinutes * this.RV_minute);
                }
                if(this.RV_currentPeriodStart == null)
                {
                    this.RV_currentPeriodStart = current;
                    this.prev_cumRV = this.cumlative_RV;
                }
                else if((current - this.RV_currentPeriodStart.Value).TotalMinutes > this.RV_minute)
                {
                    this.prev_RV = this.realized_volatility;
                    this.realized_volatility = Math.Sqrt((this.cumlative_RV - this.prev_cumRV) / (current - this.RV_currentPeriodStart.Value).TotalMinutes * this.RV_minute);
                    this.prev_cumRV = this.cumlative_RV;
                    this.RV_currentPeriodStart = current;
                }
            }

            if(this.open_mid < 0 && this.mid > 0)
            {
                this.open_mid = this.mid;
            }
            return ret;
        }
        public decimal getPriceAfterSweep(orderSide side, decimal quantity)
        {
            
            decimal price = 0;
            decimal qtAtPrice = 0;
            decimal cumQuantity = 0;
            Dictionary<decimal,decimal> ordAtPrice = new Dictionary<decimal,decimal>();
            while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
            {

            }
            foreach (var ord in this.live_orders.Values)
            {
                if(ordAtPrice.ContainsKey(ord.order_price))
                {
                    switch (ord.side)
                    {
                        case orderSide.Buy:
                            ordAtPrice[ord.order_price] += ord.order_quantity - ord.filled_quantity;
                            break;
                        case orderSide.Sell:
                            ordAtPrice[ord.order_price] -= ord.order_quantity - ord.filled_quantity;
                            break;
                    }
                }
                else
                {
                    switch (ord.side)
                    {
                        case orderSide.Buy:
                            ordAtPrice[ord.order_price] = ord.order_quantity - ord.filled_quantity;
                            break;
                        case orderSide.Sell:
                            ordAtPrice[ord.order_price] = - (ord.order_quantity - ord.filled_quantity);
                            break;
                    }
                }
            }
            Volatile.Write(ref this.order_lock, 0);
            while (Interlocked.CompareExchange(ref this.quotes_lock,1,0) != 0)
            {

            }
            switch (side)
            {
                case orderSide.Buy:
                    foreach (var item in this.bids.Reverse())
                    {
                        qtAtPrice = item.Value;
                        if(ordAtPrice.ContainsKey(item.Key) && ordAtPrice[item.Key] > 0)
                        {
                            qtAtPrice -= ordAtPrice[item.Key];
                            if(qtAtPrice < 0)
                            {
                                qtAtPrice = 0;
                            }
                        }
                        cumQuantity += qtAtPrice;


                        if (cumQuantity > quantity)
                        {
                            price = item.Key;
                            break;
                        }
                    }
                    break;
                case orderSide.Sell:
                    foreach (var item in this.asks)
                    {
                        qtAtPrice = item.Value;
                        if (ordAtPrice.ContainsKey(item.Key) && ordAtPrice[item.Key] < 0)
                        {
                            qtAtPrice += ordAtPrice[item.Key];
                            if (qtAtPrice < 0)
                            {
                                qtAtPrice = 0;
                            }
                        }
                        cumQuantity += qtAtPrice;

                        if (cumQuantity > quantity)
                        {
                            price = item.Key;
                            break;
                        }
                    }
                    break;
            }
            Volatile.Write(ref this.quotes_lock, 0);
            return price;
        }
        public bool getWeightedAvgPrice(orderSide side, List<decimal> quantities, List<decimal> prices)
        {
            bool ret = false;
            int layer = 0;
            decimal quantity;
            decimal cumQuantity = 0;
            decimal weightedPrice = 0;
            if (quantities.Count > layer)
            {
                quantity = quantities[layer];
            }
            else
            {
                return ret;
            }
            switch (side)
            {
                case orderSide.Buy:
                    foreach (var item in this.bids.Reverse())
                    {
                        if (cumQuantity + item.Value < quantity)
                        {
                            cumQuantity += item.Value;
                            weightedPrice += item.Value * item.Key;
                            //Console.WriteLine($"layer {layer.ToString()} <= {item.Value.ToString()}@{item.Key.ToString()}");
                        }
                        else
                        {
                            //Console.WriteLine($"layer {layer.ToString()} <= {(quantity - cumQuantity).ToString()} from {item.Value.ToString()}@{item.Key.ToString()}");
                            decimal residual = item.Value - (quantity - cumQuantity);
                            weightedPrice += (quantity - cumQuantity) * item.Key;
                            cumQuantity += (quantity - cumQuantity);
                            if(prices.Count > layer)
                            {
                                prices[layer] = cumQuantity > 0 ? weightedPrice / cumQuantity * (1 + this.taker_fee) : 0;
                            }
                            else
                            {
                                prices.Add(cumQuantity > 0 ? weightedPrice / cumQuantity * (1 + this.taker_fee) : 0);
                            }
                            cumQuantity = 0;
                            weightedPrice = 0;
                            while (residual > 0)
                            {
                                ++layer;
                                if (quantities.Count > layer)
                                {
                                    quantity = quantities[layer];
                                    if (quantity > cumQuantity + residual)
                                    {
                                        cumQuantity += residual;
                                        weightedPrice += residual * item.Key;
                                        //Console.WriteLine($"layer {layer.ToString()} <= {residual.ToString()} from {item.Value.ToString()}@{item.Key.ToString()}");
                                        break;
                                    }
                                    else
                                    {
                                        //Console.WriteLine($"layer {layer.ToString()} <= {(quantity - cumQuantity).ToString()} from {item.Value.ToString()}@{item.Key.ToString()}");
                                        residual -= quantity - cumQuantity;
                                        weightedPrice += (quantity - cumQuantity) * item.Key;
                                        cumQuantity += quantity - cumQuantity;
                                        if (prices.Count > layer)
                                        {
                                            prices[layer] = cumQuantity > 0 ? weightedPrice / cumQuantity * (1 + this.taker_fee) : 0;
                                        }
                                        else
                                        {
                                            prices.Add(cumQuantity > 0 ? weightedPrice / cumQuantity * (1 + this.taker_fee) : 0);
                                        }
                                        cumQuantity = 0;
                                        weightedPrice = 0;
                                    }
                                }
                                else
                                {
                                    break;
                                }
                            }
                            if (quantities.Count <= layer)
                            {
                                ret = true;
                                break;
                            }
                        }
                    }
                    break;
                case orderSide.Sell:
                    foreach (var item in this.asks)
                    {
                        if (cumQuantity + item.Value < quantity)
                        {
                            cumQuantity += item.Value;
                            weightedPrice += item.Value * item.Key;
                            //Console.WriteLine($"layer {layer.ToString()} <= {item.Value.ToString()}@{item.Key.ToString()}");
                        }
                        else
                        {
                            //Console.WriteLine($"layer {layer.ToString()} <= {(quantity - cumQuantity).ToString()} from {item.Value.ToString()}@{item.Key.ToString()}");
                            decimal residual = item.Value - (quantity - cumQuantity);
                            weightedPrice += (quantity - cumQuantity) * item.Key;
                            cumQuantity += (quantity - cumQuantity);
                            if (prices.Count > layer)
                            {
                                prices[layer] = cumQuantity > 0 ? weightedPrice / cumQuantity * (1 + this.taker_fee) : 0;
                            }
                            else
                            {
                                prices.Add(cumQuantity > 0 ? weightedPrice / cumQuantity * (1 + this.taker_fee) : 0);
                            }
                            cumQuantity = 0;
                            weightedPrice = 0;
                            while (residual > 0)
                            {
                                ++layer;
                                if (quantities.Count > layer)
                                {
                                    quantity = quantities[layer];
                                    if(quantity > cumQuantity + residual)
                                    {
                                        cumQuantity += residual;
                                        weightedPrice += residual * item.Key;
                                        //Console.WriteLine($"layer {layer.ToString()} <= {residual.ToString()} from {item.Value.ToString()}@{item.Key.ToString()}");
                                        break;
                                    }
                                    else
                                    {
                                        //Console.WriteLine($"layer {layer.ToString()} <= {(quantity - cumQuantity).ToString()} from {item.Value.ToString()}@{item.Key.ToString()}");
                                        residual -= quantity - cumQuantity;
                                        weightedPrice += (quantity - cumQuantity) * item.Key;
                                        cumQuantity += quantity - cumQuantity;
                                        if (prices.Count > layer)
                                        {
                                            prices[layer] = cumQuantity > 0 ? weightedPrice / cumQuantity * (1 + this.taker_fee) : 0;
                                        }
                                        else
                                        {
                                            prices.Add(cumQuantity > 0 ? weightedPrice / cumQuantity * (1 + this.taker_fee) : 0);
                                        }
                                        cumQuantity = 0;
                                        weightedPrice = 0;
                                    }
                                }
                                else
                                {
                                    break;
                                }
                            }
                            if (quantities.Count <= layer)
                            {
                                ret = true;
                                break;
                            }
                        }
                    }
                    break;
            }
            return ret;
        }
        public void updateFills(DataSpotOrderUpdate prev_ord,DataSpotOrderUpdate new_ord)
        {
            decimal filledQuantity = 0;
            decimal filledPrice = 0;
            decimal fee = 0;

            filledQuantity = new_ord.filled_quantity - prev_ord.filled_quantity;

            if (!new_ord.isVirtual)
            {
                if(new_ord.market == "bitbank")
                {
                    filledQuantity = 0;
                    fee = 0;
                    filledPrice = 0;
                }
                else if(new_ord.market == "bittrade")
                {
                    if (new_ord.current_traded_quantity > 0)
                    {
                        filledQuantity = new_ord.current_traded_quantity;
                        filledPrice = new_ord.current_traded_price;
                        fee = 0;
                    }
                    else if(new_ord.fee > 0)//Fee can be retrived from only trade.clearing object 
                    {
                        fee = new_ord.fee;
                        filledPrice = 0;
                        filledQuantity = 0;
                    }
                    else
                    {
                        filledQuantity = 0;
                        fee = 0;
                        filledPrice = 0;

                    }
                }
                else if (new_ord.market == "coincheck")
                {
                    filledQuantity = 0;
                    fee = 0;
                    filledPrice = 0;
                }
                else
                {
                    if(new_ord == prev_ord)
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
                }
            }
            else
            {
                filledQuantity = 0;
                fee = 0;
                filledPrice = 0;
                //if (new_ord == prev_ord)
                //{
                //    filledQuantity = new_ord.filled_quantity;
                //    filledPrice = new_ord.average_price;
                //    fee = new_ord.fee;
                //}
                //else
                //{
                //    filledPrice = (new_ord.filled_quantity * new_ord.average_price - prev_ord.filled_quantity * prev_ord.average_price) / filledQuantity;
                //    fee = new_ord.fee - prev_ord.fee;
                //}
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
            this.baseBalance.AddBalance(filledQuantity,0);
            this.quoteBalance.AddBalance(-filledQuantity * filledPrice,0);

            if(new_ord.fee_asset.ToUpper() == this.baseCcy)
            {
                this.baseBalance.AddBalance(- fee, 0);
                this.base_fee += fee;
            }
            else if(new_ord.fee_asset.ToUpper() == this.quoteCcy)
            {
                this.quoteBalance.AddBalance(- fee, 0);
                this.quote_fee += fee;
            }
            else
            {
                this.unknown_fee += fee;
            }
        }

        public void updateOrders(DataSpotOrderUpdate ord)
        {

            this.orders[ord.internal_order_id] = ord;

            if (this.readyToTrade && ord.timestamp.HasValue && ord.update_time.HasValue)
            {
                ++(this.count_AllOrderUpdates);
                ++(this.cum_AllOrderUpdates);
                double theo = this.getTheoLatency(ord.timestamp.Value);
                double latency = (ord.timestamp.Value - ord.update_time.Value).TotalMilliseconds;

                if (latency - theo > this.latencyTh)
                {
                    ++(this.count_LatentOrderUpdates);
                    ++(this.cum_LatentOrderUpdates);
                }
            }
        }
        public void updateFills(DataFill fill)
        {
            if(fill.position_side == positionSide.Long)//Margin Long
            {
                if (fill.side == orderSide.Buy)
                {
                    this.longPostion.AddBalance(fill.quantity, 0);
                }
                else if (fill.side == orderSide.Sell)
                {
                    this.longPostion.AddBalance(-fill.quantity, 0);
                }
            }
            else if(fill.position_side == positionSide.Short)//Margin Short
            {
                if(fill.side == orderSide.Sell)
                {
                    this.shortPosition.AddBalance(fill.quantity, 0);
                }
                else if(fill.side == orderSide.Buy)
                {
                    this.shortPosition.AddBalance(-fill.quantity, 0);
                }
            }
            else//Spot Order
            {
                if (fill.side == orderSide.Buy)
                {
                    this.baseBalance.AddBalance(fill.quantity, 0);
                    this.quoteBalance.AddBalance(-fill.quantity * fill.price, 0);
                }
                else if (fill.side == orderSide.Sell)
                {
                    this.baseBalance.AddBalance(-fill.quantity, 0);
                    this.quoteBalance.AddBalance(fill.quantity * fill.price, 0);
                }
            }
            if (fill.side == orderSide.Buy)
            {
                this.my_buy_quantity += fill.quantity;
                this.my_buy_notional += fill.quantity * fill.price;
            }
            else if (fill.side == orderSide.Sell)
            {

                this.my_sell_quantity += fill.quantity;
                this.my_sell_notional += fill.quantity * fill.price;
            }
            this.baseBalance.AddBalance(-fill.fee_base, 0);
            this.quoteBalance.AddBalance(-fill.fee_quote, 0);
            this.quote_fee += fill.fee_quote;
            this.base_fee += fill.fee_base;
            this.unknown_fee += fill.fee_unknown;

            if(this.readyToTrade && fill.timestamp.HasValue && fill.filled_time.HasValue)
            {
                ++(this.count_AllFill);
                ++(this.cum_AllFill);
                double theo = this.getTheoLatency(fill.timestamp.Value);
                double latency = (fill.timestamp.Value - fill.filled_time.Value).TotalMilliseconds;

                if(latency - theo > this.latencyTh)
                {
                    ++(this.count_LatentFill);
                    ++(this.cum_LatentFill);
                }
            }
        }

        public void update_micurve(MarketImpact mi)
        {
            int sign = 1;
            if(mi.fill_side == orderSide.Sell)
            {
                sign = -1;
            }
            foreach (var item in mi.prices)
            {
                this.market_impact_curve[item.Key] =(this.market_impact_curve[item.Key] * this.mi_volume +  sign * (item.Value - mi.filled_price) / mi.filled_price * 1_000_000 * mi.filled_quantity) / (this.mi_volume + mi.filled_quantity);
            }
            this.mi_volume += mi.filled_quantity;
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
