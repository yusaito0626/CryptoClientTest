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
    public class Balance
    {
        public string ccy;
        public Enums.market market;

        public decimal current_price;
        public string valuation_pair;

        private decimal _total;
        private decimal _inuse;

        public volatile int balance_lock;

        public Balance() 
        {
            this.ccy = "";
            this.market = market.NONE;
            this._total = 0;
            this._inuse = 0;
            this.current_price = 1;
            this.valuation_pair = "";
        }
        public void setFromBalanceInfo(balanceInfo info)
        {
            this.ccy = info.symbol;
            this.market = (market)Enum.Parse(typeof(market),info.market);
            this.total = info.total;
            this.current_price = info.current_price;
            this.valuation_pair = info.valuation_pair;
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
        public string ToString()
        {
            return $"{this.ccy},{this.market},{this._total.ToString()},{this._inuse.ToString()},{this.valuation_pair},{this.current_price.ToString()}";
        }
    }
    public class BalanceMargin
    {
        public string symbol;
        public Enums.market market;
        public string symbol_market { get { return symbol + "@" + market.ToString(); } }
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
            this.unrealized_pnl = position.unrealizedPnL;
        }
        public void setFromBalanceInfo(balanceInfo info)
        {
            this.symbol = info.symbol;
            this.market = (market)Enum.Parse(typeof(market),info.market);
            if(info.side.ToLower() == "long")
            {
                this.side = positionSide.Long;
            }
            else if(info.side.ToLower() == "short")
            {
                this.side= positionSide.Short;
            }
            else
            {
                this.side = positionSide.NONE;
            }
            this.avg_price = info.avg_price;
            this._total = info.total;
            this.unrealized_fee = info.unrealized_fee;
            this.unrealized_interest = info.unrealized_interest;
            this.current_price = info.current_price;
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
        public string ToString()
        {
            return $"{this.symbol},{this.market},{this.side.ToString()},{this.avg_price.ToString()},{this._total.ToString()},{this._inuse.ToString()},{this.current_price.ToString()},{this.unrealized_pnl.ToString()},{this.unrealized_fee.ToString()},{this.unrealized_interest.ToString()},{this.maxLeverage}";
        }
    }
}
