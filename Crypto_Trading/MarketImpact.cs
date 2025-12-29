using Enums;
using OKX.Net.Enums;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utils;

namespace Crypto_Trading
{
    public class MarketImpact
    {
        public string symbol_market;
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
            this.symbol_market = "";
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
            this.symbol_market = fill.symbol_market;
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
            this.symbol_market = trd.symbol + "@" + trd.market;
            this.filled_quantity = trd.quantity;
            this.filled_price = trd.price;
            switch(trd.side)
            {
                case orderSide.Buy:
                    this.fill_side = orderSide.Sell;
                    break;
                case orderSide.Sell:
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
            this.symbol_market = "";
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
            string res = this.filled_time.ToString(GlobalVariables.tmMsecFormat) + "," + this.fill_id + "," + this.stg_name + "," + this.symbol_market + "," + this.fill_side.ToString() + "," + this.filled_quantity.ToString() + "," + this.filled_price.ToString() + "," + this.myOrder.ToString();
            foreach(var p in this.prices)
            {
                res += "," + p.Value.ToString();
            }
            return res;
        }
        public void FromString(string[] arr)
        {
            if(arr.Length < 8)
            {
                return;
            }
            this.filled_time = DateTime.ParseExact(arr[0], GlobalVariables.tmMsecFormat, CultureInfo.InvariantCulture);
            this.fill_id = arr[1];
            this.stg_name = arr[2];
            this.symbol_market = arr[3];
            if (arr[4] == "Sell")
            {
                this.fill_side = orderSide.Sell;
            }
            else if (arr[4] == "Buy")
            {
                this.fill_side = orderSide.Buy;
            }
            else
            {
                this.fill_side = orderSide.NONE;
            }
            this.filled_quantity = decimal.Parse(arr[5]);
            this.filled_price = decimal.Parse(arr[6]);
            if (arr[7].ToLower() == "true")
            {
                this.myOrder = true;
            }
            else
            {
                this.myOrder = false;
            }
            int i = 8;
            foreach(double d in GlobalVariables.MI_period)
            {
                if(i < arr.Length)
                {
                    this.prices[d] = Decimal.Parse(arr[i]);
                }
                else
                {
                    this.prices[d] = 0;
                }
                ++i;
            }
        }
    }
}
