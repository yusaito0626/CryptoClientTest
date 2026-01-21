using Enums;
using HyperLiquid.Net.Objects.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Utils;

namespace Crypto_Trading
{
    public class ExchangeBalance
    {
        public string market;
        public Dictionary<string, Balance> balance;
        public Dictionary<string, BalanceMargin> marginLong;
        public Dictionary<string,BalanceMargin> marginShort;

        public decimal marginAvailability;
        public decimal marginNotionalAmount;

        public ExchangeBalance()
        {
            this.market = "";
            this.balance = new Dictionary<string,Balance>();
            this.marginLong = new Dictionary<string,BalanceMargin>();
            this.marginShort = new Dictionary<string, BalanceMargin>();

            this.marginAvailability = 0;
            this.marginNotionalAmount = 0;
        }

        public decimal getUnrealizedPnL(Dictionary<string,Instrument> ins_dict)
        {
            decimal res = 0;
            foreach(BalanceMargin b in this.marginShort.Values)
            {
                if(ins_dict.ContainsKey(b.symbol_market))
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
        public string OutputToFile(Dictionary<string, Instrument> ins_dict,DateTime? currentTime = null)
        {
            string str_time;
            if(currentTime.HasValue)
            {
                str_time = currentTime.Value.ToString(GlobalVariables.tmMsecFormat);
            }
            else
            {
                str_time = DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
            }
            //Timestamp,Exchange,Margin or Spot,symbol,side(margin),quantity,avg_price(margin),current_price,valuation_pair,unrealized_fee(margin),unrealized_interest
            string res = "";

            this.getUnrealizedPnL(ins_dict);

            foreach (var b in this.balance.Values)
            {
                res += str_time + "," + this.market + ",SPOT," + b.ccy + ",," + b.total.ToString() + ",0," + b.current_price.ToString() + "," + b.valuation_pair + ",0,0\n";
            }
            foreach (var b in this.marginShort.Values)
            {
                res += str_time + "," + this.market + ",MARGIN," + b.symbol + "," + b.side.ToString() + "," + b.total.ToString() + "," + b.avg_price.ToString() + "," + b.current_price.ToString() + "," + b.symbol + "," + b.unrealized_fee.ToString() + "," + b.unrealized_interest.ToString() + "\n"; 
            }       
            return res;
        }
    }
    public class Balance
    {
        public string ccy;
        public string market;

        public decimal current_price;
        public string valuation_pair;

        private decimal _total;
        private decimal _inuse;

        public volatile int balance_lock;

        public Balance() 
        {
            this.ccy = "";
            this.market = "";
            this._total = 0;
            this._inuse = 0;
            this.current_price = 1;
            this.valuation_pair = "";
        }

        public void AddBalance(decimal total = 0,decimal inuse = 0)
        {
            while (Interlocked.CompareExchange(ref this.balance_lock, 1, 0) != 0)
            {

            }
            this._total += total;
            this._inuse += inuse;
            Volatile.Write(ref this.balance_lock, 0);
        }

        public decimal available
        {
            get { return this._total - this._inuse; }
        }

        
        public decimal total
        {
            get { return _total; }
            set {  _total = value; }
        }

        public void init()
        {
            this._total = 0;
            this._inuse = 0;
        }

        public decimal inuse
        {
            get { return _inuse; }
            set {  this._inuse = value; }
        }
    }
    public class BalanceMargin
    {
        public string symbol;
        public string market;
        public string symbol_market { get { return symbol + "@" + market; } }
        public positionSide side;
        public decimal avg_price;
        private decimal _total;
        private decimal _inuse;
        public decimal unrealized_fee;
        public decimal unrealized_interest;

        public decimal current_price;
        public decimal unrealized_pnl;

        public decimal maxLeverage;

        public volatile int balance_lock;

        public BalanceMargin()
        {
            this.symbol = "";
            this.side = positionSide.NONE;
            this.avg_price = 0;
            this._total = 0;
            this._inuse = 0;
            this.unrealized_fee = 0;
            this.unrealized_interest = 0;
            this.current_price = 0;
            this.unrealized_pnl = 0;
            this.maxLeverage = 1;
        }

        public void setPosition(DataMarginPos position)
        {
            this.symbol = position.symbol;
            this.market = position.market;
            this.side = position.side;
            this.avg_price = position.avgPrice;
            this._total = position.quantity;
            this._inuse = 0;
            this.unrealized_fee= position.unrealizedFee;
            this.unrealized_interest= position.unrealizedInterest;
        }
        public void AddBalance(decimal total = 0, decimal inuse = 0, decimal price = 0)
        {
            while (Interlocked.CompareExchange(ref this.balance_lock, 1, 0) != 0)
            {

            }
            if(total > 0)
            {
                this.avg_price = (this.avg_price * this._total + price * total) / (this._total + total);
            }
            this._total += total;
            this._inuse += inuse;
            Volatile.Write(ref this.balance_lock, 0);
        }
        public void addFill(DataFill fill)
        {
            while (Interlocked.CompareExchange(ref this.balance_lock, 1, 0) != 0)
            {

            }
            switch(this.side)
            {
                case positionSide.Long:
                    if(fill.side == orderSide.Buy)
                    {
                        this.avg_price = (this.avg_price * this._total + fill.price * fill.quantity) / (this._total + fill.quantity);
                        this._total += fill.quantity;
                    }
                    else if(fill.side == orderSide.Sell)
                    {
                        this._total -= fill.quantity;
                    }
                    break;
                case positionSide.Short:
                    if (fill.side == orderSide.Sell)
                    {
                        this.avg_price = (this.avg_price * this._total + fill.price * fill.quantity) / (this._total + fill.quantity);
                        this._total += fill.quantity;
                    }
                    else if (fill.side == orderSide.Buy)
                    {
                        this._total -= fill.quantity;
                    }
                    break;
            }
            Volatile.Write(ref this.balance_lock, 0);
        }

        public void setUnrealizedPnL(decimal price)
        {
            if(price > 0)
            {
                this.current_price = price;
                switch (this.side)
                {
                    case positionSide.Long:
                        this.unrealized_pnl = (this.current_price - this.avg_price) * this._total;
                        break;
                    case positionSide.Short:
                        this.unrealized_pnl = (this.avg_price - this.current_price) * this._total;
                        break;
                }
            }
        }

        public decimal available
        {
            get { return this._total - this._inuse; }
        }


        public decimal total
        {
            get { return _total; }
            set { _total = value; }
        }

        public void init()
        {
            this.avg_price = 0;
            this._total = 0;
            this._inuse = 0;
            this.unrealized_fee = 0;
            this.unrealized_interest = 0;
        }

        public void copy(BalanceMargin org)
        {
            this.symbol = org.symbol;
            this.market = org.market;
            this.side = org.side;
            this.avg_price = org.avg_price;
            this._total = org.total;
            this._inuse = org.inuse;
            this.unrealized_fee = org.unrealized_fee;
            this.unrealized_interest = org.unrealized_interest;
        }

        public decimal inuse
        {
            get { return _inuse; }
            set { this._inuse = value; }
        }
    }
}
