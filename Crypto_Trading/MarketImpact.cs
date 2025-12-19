using Enums;
using OKX.Net.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utils;

namespace Crypto_Trading
{
    public class MarketImpact
    {
        public string symbolmarket;
        public string stg_name;
        public Instrument ref_ins;
        public string fill_id;

        public decimal filled_quantity;
        public decimal filled_price;
        public orderSide fill_side;
        public DateTime filled_time;

        public bool myOrder;

        public Dictionary<double,decimal> prices = new Dictionary<double,decimal>();

        public MarketImpact()
        {
            this.symbolmarket = "";
            this.stg_name = "";
            this.ref_ins = null;
            this.fill_id = "";
            this.filled_quantity = 0;
            this.filled_price = 0;
            this.fill_side = orderSide.NONE;
            this.filled_time = DateTime.UtcNow;
            this.myOrder = false;
            foreach (double d in GlobalVariables.MI_period)
            {
                this.prices[d] = 0;
            }
        }

        public void startRecording(DataFill fill,Instrument ins,string stg_name = "")
        {
            this.fill_id = fill.trade_id;
            this.stg_name= stg_name;
            this.symbolmarket = fill.symbol_market;
            this.filled_quantity = fill.quantity;
            this.filled_price = fill.price;
            this.fill_side = fill.side;
            this.myOrder = true;
            if(fill.filled_time.HasValue)
            {
                this.filled_time = fill.timestamp.Value;
            }
            else
            {
                this.filled_time = DateTime.UtcNow;
            }
            this.ref_ins = ins;
            this.prices[0] = ins.mid;
        }
        public void startRecording(DataTrade trd, Instrument ins)
        {
            this.fill_id = "";
            this.symbolmarket = trd.symbol + "@" + trd.market;
            this.filled_quantity = trd.quantity;
            this.filled_price = trd.price;
            switch(trd.side)
            {
                case CryptoExchange.Net.SharedApis.SharedOrderSide.Buy:
                    this.fill_side = orderSide.Sell;
                    break;
                case CryptoExchange.Net.SharedApis.SharedOrderSide.Sell:
                    this.fill_side = orderSide.Buy;
                    break;
            }
            this.myOrder = false;
            if (trd.timestamp.HasValue)
            {
                this.filled_time = trd.timestamp.Value;
            }
            else
            {
                this.filled_time = DateTime.UtcNow;
            }
            this.ref_ins = ins;
            this.prices[0] = ins.mid;
        }

        public void recordPrice(DateTime current)
        {
            foreach(var p in this.prices)
            {
                if(p.Value == 0)
                {
                    if(current > this.filled_time + TimeSpan.FromSeconds(p.Key))
                    {
                        this.prices[p.Key] = this.ref_ins.prev_mid;//The function is called after the price is updated
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        public void init()
        {
            this.symbolmarket = "";
            this.stg_name = "";
            this.ref_ins = null;
            this.fill_id = "";
            this.filled_quantity = 0;
            this.filled_price = 0;
            this.filled_time = DateTime.UtcNow;
            this.myOrder = false;
            foreach (double d in GlobalVariables.MI_period)
            {
                this.prices[d] = 0;
            }
        }

        public string ToString()
        {
            string res = this.filled_time.ToString(GlobalVariables.tmMsecFormat) + "," + this.fill_id + "," + this.stg_name + "," + this.symbolmarket + "," + this.fill_side.ToString() + "," + this.filled_quantity.ToString() + "," + this.filled_price.ToString() + "," + this.myOrder.ToString();
            foreach(var p in this.prices)
            {
                res += "," + p.Value.ToString();
            }
            return res;
        }
    }
}
