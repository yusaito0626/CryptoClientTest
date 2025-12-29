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
        public string market;

        private decimal _total;
        private decimal _inuse;

        public volatile int balance_lock;

        public Balance() 
        {
            this.ccy = "";
            this.market = "";
            this._total = 0;
            this._inuse = 0;
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

        public volatile int balance_lock;

        public BalanceMargin()
        {
            this.symbol = "";
            this.side = positionSide.NONE;
            this.avg_price = 0;
            this._total= 0;
            this._inuse = 0;
            this.unrealized_fee = 0;
            this.unrealized_interest = 0;
        }

        public void setPosition(DataMarginPos position)
        {
            this.side = position.side;
            this.avg_price = position.avgPrice;
            this._total = position.quantity;
            this.inuse = 0;
            this.unrealized_fee= position.unrealizedFee;
            this.unrealized_interest= position.unrealizedInterest;
        }
        public void AddBalance(decimal total = 0, decimal inuse = 0)
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

        public decimal inuse
        {
            get { return _inuse; }
            set { this._inuse = value; }
        }
    }
}
