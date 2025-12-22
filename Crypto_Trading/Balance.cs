using HyperLiquid.Net.Objects.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

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
}
