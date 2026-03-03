using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utils;
using Enums;

namespace Crypto_Trading
{
    public class Exchange
    {
        public Enums.market market;
        public Dictionary<string, Balance> balance;
        public Dictionary<string, BalanceMargin> marginLong;
        public Dictionary<string, BalanceMargin> marginShort;

        public decimal marginAvailability;
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
            this.balance = new Dictionary<string, Balance>();
            this.marginLong = new Dictionary<string, BalanceMargin>();
            this.marginShort = new Dictionary<string, BalanceMargin>();

            this.valueAtSoD = 0;
            this.marginAvailability = 0;
            this.marginNotionalAmount = 0;
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

        public decimal getUnrealizedPnL(Dictionary<string, Instrument> ins_dict)
        {
            decimal res = 0;
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
                res += str_time + "," + this.market + ",SPOT," + b.Value.ccy + ",," + b.Value.total.ToString() + ",0," + b.Value.current_price.ToString() + "," + b.Value.valuation_pair + ",0,0\n";
            }
            foreach (var b in this.marginShort.Values)
            {
                res += str_time + "," + this.market + ",MARGIN," + b.symbol + "," + b.side.ToString() + "," + b.total.ToString() + "," + b.avg_price.ToString() + "," + b.current_price.ToString() + "," + b.symbol + "," + b.unrealized_fee.ToString() + "," + b.unrealized_interest.ToString() + "\n";
            }
            foreach (var b in this.marginLong.Values)
            {
                res += str_time + "," + this.market + ",MARGIN," + b.symbol + "," + b.side.ToString() + "," + b.total.ToString() + "," + b.avg_price.ToString() + "," + b.current_price.ToString() + "," + b.symbol + "," + b.unrealized_fee.ToString() + "," + b.unrealized_interest.ToString() + "\n";
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
                res += $"{str_time},{this.market},MARGIN,{b.symbol},{b.side},{- b.total},{b.unrealized_pnl - b.unrealized_interest - b.unrealized_fee}\n";
                //res += str_time + "," + this.market + ",MARGIN," + b.symbol + "," + b.side.ToString() + "," + b.total.ToString() + "," + b.avg_price.ToString() + "," + b.current_price.ToString() + "," + b.symbol + "," + b.unrealized_fee.ToString() + "," + b.unrealized_interest.ToString() + "\n";
            }
            foreach (var b in this.marginLong.Values)
            {
                res += $"{str_time},{this.market},MARGIN,{b.symbol},{b.side},{b.total},{b.unrealized_pnl - b.unrealized_interest - b.unrealized_fee}\n";
                //res += str_time + "," + this.market + ",MARGIN," + b.symbol + "," + b.side.ToString() + "," + b.total.ToString() + "," + b.avg_price.ToString() + "," + b.current_price.ToString() + "," + b.symbol + "," + b.unrealized_fee.ToString() + "," + b.unrealized_interest.ToString() + "\n";
            }
            return res;
        }
    }
}
