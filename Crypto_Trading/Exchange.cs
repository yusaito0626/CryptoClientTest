using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utils;
using Enums;
using CryptoExchange.Net.SharedApis;

namespace Crypto_Trading
{
    public class Exchange
    {
        public Enums.market market;
        public Dictionary<string, Instrument> instruments;
        public Dictionary<string, Balance> balance;
        public Dictionary<string, BalanceMargin> marginLong;
        public Dictionary<string, BalanceMargin> marginShort;

        private myLock margin_lock;
        public decimal marginTotal;
        public decimal marginLocked;//using with existing orders

        public decimal marginNotionalAmount;
        public decimal valueAtSoD;

        //Latency Calculation
        public DateTime sample1Time;
        public double sample_latency1;
        public DateTime intercept_time1;
        public double intercept_latency1;
        public DateTime intercept_time2;
        public double intercept_latency2;
        public double sample_gap = 60;
        public double sample_gap2 = 3600;
        public int count = 0;
        public int currentSampling = 0;
        public int NofSample = 100;
        public double coef;
        public double intercept;
        public DateTime intercept_time;
        public double base_latency = 20;

        public Exchange()
        {
            this.market = market.NONE;
            this.instruments = new Dictionary<string, Instrument>();
            this.balance = new Dictionary<string, Balance>();
            this.marginLong = new Dictionary<string, BalanceMargin>();
            this.marginShort = new Dictionary<string, BalanceMargin>();

            this.valueAtSoD = 0;
            this.marginTotal = 0;
            this.marginNotionalAmount = 0;
        }

        public bool updateBalance(DataFill fill)
        {
            bool res = true;
            Instrument ins;

            Balance quoteBalance;
            Balance baseBalance;
            BalanceMargin longPosition;
            BalanceMargin shortPosition;

            decimal marginTotal_chg = 0;

            if (this.instruments.ContainsKey(fill.symbol_market))
            {
                ins = this.instruments[fill.symbol_market];
            }
            else
            {
                return false;
            }

            decimal filled_fee;
            if (fill.order_type == orderType.LimitMaker || fill.order_type == orderType.Limit)
            {
                filled_fee = fill.quantity * fill.price * ins.maker_fee;
            }
            else
            {
                filled_fee = fill.quantity * fill.price * ins.taker_fee;
            }

            //If the symbol is Spot, update marginTotal
            //If the symbol is margin, update margninInuse
            switch (fill.position_side)
            {
                case positionSide.NONE://Spot
                    if (this.balance.ContainsKey(ins.baseCcy))
                    {
                        baseBalance = this.balance[ins.baseCcy];
                    }
                    else
                    {
                        return false;
                    }
                    if (this.balance.ContainsKey(ins.quoteCcy))
                    {
                        quoteBalance = this.balance[ins.quoteCcy];
                    }
                    else
                    {
                        return false;
                    }

                    if (fill.side == orderSide.Buy)
                    {
                        if (baseBalance != null)
                        {
                            baseBalance.AddBalance(fill.quantity, 0);
                            if (baseBalance.ccy == "JPY")
                            {
                                marginTotal_chg += fill.quantity;
                            }
                            else if(this.market == market.bitbank)
                            {
                                marginTotal_chg += fill.quantity / 2;
                            }
                        }
                        if (quoteBalance != null)
                        {
                            quoteBalance.AddBalance(-fill.quantity * fill.price, 0);
                            if (quoteBalance.ccy == "JPY")
                            {
                                marginTotal_chg -= fill.quantity * fill.price;
                            }
                            else if (this.market == market.bitbank)
                            {
                                marginTotal_chg -= fill.quantity * fill.price / 2;
                            }
                        }
                    }
                    else if (fill.side == orderSide.Sell)
                    {
                        if (baseBalance != null)
                        {
                            baseBalance.AddBalance(-fill.quantity, 0);
                            if (baseBalance.ccy == "JPY")
                            {
                                marginTotal_chg -= fill.quantity;
                            }
                            else if (this.market == market.bitbank)
                            {
                                marginTotal_chg -= fill.quantity / 2;
                            }
                        }
                        if (quoteBalance != null)
                        {
                            quoteBalance.AddBalance(fill.quantity * fill.price, 0);
                            if (quoteBalance.ccy == "JPY")
                            {
                                marginTotal_chg += fill.quantity * fill.price;
                            }
                            else if (this.market == market.bitbank)
                            {
                                marginTotal_chg += fill.quantity * fill.price / 2;
                            }
                        }
                    }
                    if (quoteBalance != null)
                    {
                        quoteBalance.AddBalance(-fill.fee_quote, 0);
                        if (quoteBalance.ccy == "JPY")
                        {
                            marginTotal_chg -= fill.fee_quote;
                        }
                        else if (this.market == market.bitbank)
                        {
                            marginTotal_chg -= fill.fee_quote;
                        }
                    }
                    break;
                case positionSide.Long:
                    if(this.marginLong.ContainsKey(fill.symbol_market))
                    {
                        if (this.balance.ContainsKey(ins.quoteCcy))
                        {
                            quoteBalance = this.balance[ins.quoteCcy];
                        }
                        else
                        {
                            return false;
                        }
                        longPosition = this.marginLong[fill.symbol_market];
                        if (fill.side == orderSide.Buy)
                        {
                            longPosition.unrealized_fee += filled_fee;
                            longPosition.AddBalance(fill.quantity, 0, fill.price);
                        }
                        else if (fill.side == orderSide.Sell)
                        {
                            //decimal realize_pnl = fill.profit_loss;// fill.quantity * (fill.price - this.longPosition.avg_price);
                            //decimal realized_fee = fill.fee_quote;
                            //decimal realized_interest = fill.interest;//this.longPosition.unrealized_interest * (fill.quantity / this.longPosition.total);
                            //fill.msg += $" Realize PnL: {fill.profit_loss.ToString("N8")} avg_price: {this.longPosition.avg_price.ToString()}";
                            longPosition.unrealized_fee -= fill.fee_quote - filled_fee;
                            longPosition.unrealized_interest -= fill.interest;
                            quoteBalance.total += fill.profit_loss - fill.fee_quote - fill.interest;
                            longPosition.AddBalance(-fill.quantity, 0, fill.price);
                        }
                    }
                    else
                    {
                        return false; 
                    }
                    break;
                case positionSide.Short:
                    if(this.marginShort.ContainsKey(fill.symbol_market))
                    {
                        shortPosition = this.marginShort[fill.symbol_market];
                        if (this.balance.ContainsKey(ins.quoteCcy))
                        {
                            quoteBalance = this.balance[ins.quoteCcy];
                        }
                        else
                        {
                            return false;
                        }
                        if (fill.side == orderSide.Sell)
                        {
                            shortPosition.unrealized_fee += filled_fee;
                            shortPosition.AddBalance(fill.quantity, 0, fill.price);
                        }
                        else if (fill.side == orderSide.Buy)
                        {
                            //decimal realize_pnl = fill.profit_loss; //fill.quantity * (this.shortPosition.avg_price - fill.price);
                            //decimal realized_fee = fill.fee_quote;
                            //decimal realized_interest = fill.interest;//this.shortPosition.unrealized_interest * (fill.quantity / this.shortPosition.total);
                            //fill.msg += $" Realize PnL: {fill.profit_loss.ToString("N8")} avg_price: {this.shortPosition.avg_price.ToString()}";
                            shortPosition.unrealized_fee -= fill.fee_quote - filled_fee;
                            shortPosition.unrealized_interest -= fill.interest;
                            quoteBalance.total += fill.profit_loss - fill.fee_quote - fill.interest;
                            shortPosition.AddBalance(-fill.quantity, 0, fill.price);
                        }
                    }
                    else
                    {
                        return false; 
                    }
                        break;
            }
            
            if(marginTotal_chg != 0)
            {
                using(var mlock = this.margin_lock.getlock())
                {
                    this.marginTotal += marginTotal_chg;
                }
            }

            return res;
        }
        public void updateMarginLocked(decimal chg)
        {
            using(var mlock = this.margin_lock.getlock())
            {
                this.marginLocked += chg;
            }
        }
        public decimal getMarginAvailability()
        {
            decimal availability = 0;
            using (var mlock = this.margin_lock.getlock())
            {
                availability = this.marginTotal - this.marginLocked;
                foreach(var b in this.marginLong.Values)
                {
                    availability -= b.total * b.current_price / b.leverage;
                }
                foreach (var b in this.marginShort.Values)
                {
                    availability -= b.total * b.current_price / b.leverage;
                }
                availability += this.getUnrealizedPnL();
            }
            return availability;
        }
        public bool recordLatency(DataOrderBook update)
        {
            bool res = false;
            if (update.orderbookTime.HasValue && update.timestamp.HasValue)
            {

                if (this.count < this.NofSample)
                {
                    if (this.count == 0)
                    {
                        if (this.currentSampling == 0)
                        {
                            this.sample_latency1 = (update.timestamp.Value - update.orderbookTime.Value).TotalMilliseconds;
                            this.sample1Time = update.timestamp.Value;
                            ++(this.count);
                        }
                        else if (currentSampling == 1 && (update.timestamp.Value - this.sample1Time).TotalSeconds > this.sample_gap)
                        {
                            this.intercept_latency1 = (update.timestamp.Value - update.orderbookTime.Value).TotalMilliseconds;
                            this.intercept_time1 = update.timestamp.Value;
                            ++(this.count);
                        }
                        else if (currentSampling == 2 && (update.timestamp.Value - this.sample1Time).TotalSeconds > this.sample_gap2)
                        {
                            this.intercept_latency2 = (update.timestamp.Value - update.orderbookTime.Value).TotalMilliseconds;
                            this.intercept_time2 = update.timestamp.Value;
                            ++(this.count);
                        }
                    }
                    else
                    {
                        if (this.currentSampling == 0)
                        {
                            double currentValue = (update.timestamp.Value - update.orderbookTime.Value).TotalMilliseconds;
                            if (currentValue < this.sample_latency1)
                            {
                                this.sample_latency1 = currentValue;
                                this.sample1Time = update.timestamp.Value;
                            }
                            ++(this.count);
                            if (count == this.NofSample)
                            {
                                count = 0;
                                ++(this.currentSampling);
                            }
                        }
                        else if (currentSampling == 1)
                        {
                            double currentValue = (update.timestamp.Value - update.orderbookTime.Value).TotalMilliseconds;
                            if (currentValue < this.intercept_latency1)
                            {
                                this.intercept_latency1 = currentValue;
                                this.intercept_time1 = update.timestamp.Value;
                            }
                            ++(this.count);
                            if (count == this.NofSample)
                            {
                                this.coef = (this.intercept_latency1 - this.sample_latency1) / (this.intercept_time1 - this.sample1Time).TotalSeconds;
                                this.intercept = this.intercept_latency1;
                                this.intercept_time = this.intercept_time1;
                                res = true;
                                count = 0;
                                ++(this.currentSampling);
                            }
                        }
                        else if (currentSampling == 2)
                        {
                            double currentValue = (update.timestamp.Value - update.orderbookTime.Value).TotalMilliseconds;
                            if (currentValue < this.intercept_latency2)
                            {
                                this.intercept_latency2 = currentValue;
                                this.intercept_time2 = update.timestamp.Value;
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
                    double currentValue = (update.timestamp.Value - update.orderbookTime.Value).TotalMilliseconds;
                    if (currentValue < this.intercept)
                    {
                        this.coef = (currentValue - this.sample_latency1) / (update.timestamp.Value - this.sample1Time).TotalSeconds;
                        this.intercept = currentValue;
                        this.intercept_time = update.timestamp.Value;
                        res = true;
                    }
                }
            }
            return res;
        }

        public decimal getUnrealizedPnL(Dictionary<string, Instrument> ins_dict = null)
        {
            decimal res = 0;
            if(ins_dict == null)
            {
                ins_dict = this.instruments;
            }
            foreach (BalanceMargin b in this.marginShort.Values)
            {
                if (ins_dict.ContainsKey(b.symbol_market))
                {
                    Instrument ins = ins_dict[b.symbol_market];
                    b.setUnrealizedPnL(ins.mid);
                    res += b.unrealized_pnl;
                }
            }
            foreach (BalanceMargin b in this.marginLong.Values)
            {
                if (ins_dict.ContainsKey(b.symbol_market))
                {
                    Instrument ins = ins_dict[b.symbol_market];
                    b.setUnrealizedPnL(ins.mid);
                    res += b.unrealized_pnl;
                }
            }
            foreach (Balance b in this.balance.Values)
            {
                if (b.valuation_pair == "")
                {
                    b.current_price = 1;
                }
                else if (ins_dict.ContainsKey(b.valuation_pair) && ins_dict[b.valuation_pair].mid > 0)
                {
                    b.current_price = ins_dict[b.valuation_pair].mid;
                }

            }
            return res;
        }
        public string OutputToFile(Dictionary<string, Instrument> ins_dict, DateTime? currentTime = null,bool updateUnrealizedPnL = true)
        {
            string str_time;
            if (currentTime.HasValue)
            {
                str_time = currentTime.Value.ToString(GlobalVariables.tmMsecFormat);
            }
            else
            {
                str_time = DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
            }
            //Timestamp,Exchange,Margin or Spot,symbol,side(margin),quantity,avg_price(margin),current_price,valuation_pair,unrealized_fee(margin),unrealized_interest
            string res = "";
            if(updateUnrealizedPnL)
            {
                this.getUnrealizedPnL(ins_dict);
            }

            foreach (var b in this.balance)
            {
                if(b.Value.total != 0)
                {
                    res += str_time + "," + this.market + ",SPOT," + b.Value.ccy + ",," + b.Value.total.ToString() + ",0," + b.Value.current_price.ToString() + "," + b.Value.valuation_pair + ",0,0\n";
                }
            }
            foreach (var b in this.marginShort.Values)
            {
                if(b.total != 0)
                {
                    res += str_time + "," + this.market + ",MARGIN," + b.symbol + "," + b.side.ToString() + "," + b.total.ToString() + "," + b.avg_price.ToString() + "," + b.current_price.ToString() + "," + b.symbol + "," + b.unrealized_fee.ToString() + "," + b.unrealized_interest.ToString() + "\n";
                }
            }
            foreach (var b in this.marginLong.Values)
            {
                if (b.total != 0)
                {
                    res += str_time + "," + this.market + ",MARGIN," + b.symbol + "," + b.side.ToString() + "," + b.total.ToString() + "," + b.avg_price.ToString() + "," + b.current_price.ToString() + "," + b.symbol + "," + b.unrealized_fee.ToString() + "," + b.unrealized_interest.ToString() + "\n";
                }
            }
            return res;
        }
        public string outputHistorical(Dictionary<string, Instrument> ins_dict, string today = "", bool updateUnrealizedPnL = true)
        {
            string str_time;
            if (today == "")
            {
                str_time = DateTime.UtcNow.ToString("yyyy-MM-dd");
            }
            else
            {
                str_time = today;
            }
            string res = "";
            if (updateUnrealizedPnL)
            {
                this.getUnrealizedPnL(ins_dict);
            }
            //"date,exchange,type,ccy,side,amount,value(UnrealizedPnL)"
            foreach (var b in this.balance)
            {
                if(!updateUnrealizedPnL)
                {
                    if (b.Value.valuation_pair == "")
                    {
                        b.Value.current_price = 1;
                    }
                    else if (ins_dict.ContainsKey(b.Value.valuation_pair) && ins_dict[b.Value.valuation_pair].mid > 0)
                    {
                        b.Value.current_price = ins_dict[b.Value.valuation_pair].mid;
                    }
                }
                if(b.Value.total != 0)
                {
                    res += $"{str_time},{this.market},SPOT,{b.Value.ccy},,{b.Value.total},{b.Value.total * b.Value.current_price}\n";
                }
                //res += str_time + "," + this.market + ",SPOT," + b.Value.ccy + "," + b.Value.total.ToString() + ",0," + b.Value.current_price.ToString() + "," + b.Value.valuation_pair + ",0,0\n";
            }
            foreach (var b in this.marginShort.Values)
            {
                if (b.total != 0)
                {
                    res += $"{str_time},{this.market},MARGIN,{b.symbol},{b.side},{-b.total},{b.unrealized_pnl - b.unrealized_interest - b.unrealized_fee}\n";
                }
                //res += str_time + "," + this.market + ",MARGIN," + b.symbol + "," + b.side.ToString() + "," + b.total.ToString() + "," + b.avg_price.ToString() + "," + b.current_price.ToString() + "," + b.symbol + "," + b.unrealized_fee.ToString() + "," + b.unrealized_interest.ToString() + "\n";
            }
            foreach (var b in this.marginLong.Values)
            {
                if (b.total != 0)
                {
                    res += $"{str_time},{this.market},MARGIN,{b.symbol},{b.side},{b.total},{b.unrealized_pnl - b.unrealized_interest - b.unrealized_fee}\n";
                }
                //res += str_time + "," + this.market + ",MARGIN," + b.symbol + "," + b.side.ToString() + "," + b.total.ToString() + "," + b.avg_price.ToString() + "," + b.current_price.ToString() + "," + b.symbol + "," + b.unrealized_fee.ToString() + "," + b.unrealized_interest.ToString() + "\n";
            }
            return res;
        }
    }
}
