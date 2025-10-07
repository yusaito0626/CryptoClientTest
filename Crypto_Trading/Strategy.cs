using Binance.Net.Enums;
using Crypto_Clients;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Crypto_Trading
{
    public class Strategy
    {
        public OrderManager oManager;

        public bool enabled;

        public decimal markup;
        public decimal min_markup;
        public decimal baseCcyQuantity;
        public decimal ToBsize;

        public decimal intervalAfterFill;
        public decimal modThreshold;

        public decimal maxSkew;
        public decimal skewThreshold;
        public decimal oneSideThreshold;

        public bool abook;
        public bool includeFee;

        public string baseCcy;
        public string quoteCcy;
        public string taker_symbol_market;
        public string maker_symbol_market;

        public Instrument? taker;
        public Instrument? maker;

        public int num_of_layers;

        public volatile int order_lock;

        public DataSpotOrderUpdate? live_sellorder;
        public DataSpotOrderUpdate? live_buyorder;

        public string live_sellorder_id;
        public string live_buyorder_id;
        public Dictionary<string,DataSpotOrderUpdate> stg_orders;

        public decimal live_askprice;
        public decimal live_bidprice;
        public decimal skew_point;

        public decimal taker_last_updated_mid;
        public decimal maker_last_updated_mid;

        public DateTime? last_filled_time;

        public Action<string, Enums.logType> _addLog;
        public Strategy() 
        {
            this.enabled = false;
            this.markup = 0;
            this.min_markup = 0;
            this.baseCcyQuantity = 0;
            this.ToBsize = 0;
            this.intervalAfterFill = 0;
            this.modThreshold = 0;
            this.maxSkew = 0;
            this.skewThreshold = 0;
            this.oneSideThreshold = 0;
            this.abook = true;
            this.includeFee = true;

            this.taker_symbol_market = "";
            this.maker_symbol_market = "";

            this.taker = null;
            this.maker = null;

            this.num_of_layers = 1;

            this.order_lock = 0;

            this.live_sellorder = null;
            this.live_buyorder = null;

            this.live_sellorder_id = "";
            this.live_buyorder_id = "";
            this.stg_orders = new Dictionary<string, DataSpotOrderUpdate>();

            this.live_askprice = 0;
            this.live_bidprice = 0;
            this.skew_point = 0;

            this.taker_last_updated_mid = 0;
            this.maker_last_updated_mid = 0;

            this.oManager = OrderManager.GetInstance();
        }

        public void readStrategyFile(string jsonfilename)
        {
            if(File.Exists(jsonfilename))
            {
                string fileContent = File.ReadAllText(jsonfilename);
                using JsonDocument doc = JsonDocument.Parse(fileContent);
                var root = doc.RootElement;

                this.baseCcy = root.GetProperty("baseCcy").ToString();
                this.quoteCcy = root.GetProperty("quoteCcy").ToString();
                this.markup = root.GetProperty("markup").GetDecimal();
                this.min_markup = root.GetProperty("min_markup").GetDecimal();
                this.baseCcyQuantity = root.GetProperty("baseCcyQuantity").GetDecimal();
                this.ToBsize = root.GetProperty("ToBsize").GetDecimal();
                this.intervalAfterFill = root.GetProperty("intervalAfterFill").GetDecimal();
                this.modThreshold = root.GetProperty("modThreshold").GetDecimal();
                this.maxSkew = root.GetProperty("max_skew").GetDecimal();
                this.skewThreshold = root.GetProperty("skewThreshold").GetDecimal();
                this.oneSideThreshold = root.GetProperty("oneSideThreshold").GetDecimal();
                this.taker_symbol_market = root.GetProperty("taker_symbol_market").ToString();
                this.maker_symbol_market = root.GetProperty("maker_symbol_market").ToString();
            }
        }

        public async Task updateOrders()
        {

            if (this.enabled)
            {
                int desired = 1;
                int expected = 0;

                this.skew_point = this.skew();
                decimal bid_price;
                decimal ask_price;
                if (this.skew_point > 0)
                {
                    bid_price = this.taker.adjusted_bestbid.Item1 * (1 + (-this.markup + this.skew_point) / 1000000);
                    ask_price = this.taker.adjusted_bestask.Item1 * (1 + (this.markup + (decimal)1.05 * this.skew_point) / 1000000);
                }
                else if (this.skew_point < 0)
                {
                    bid_price = this.taker.adjusted_bestbid.Item1 * (1 + (-this.markup + (decimal)1.05 * this.skew_point) / 1000000);
                    ask_price = this.taker.adjusted_bestask.Item1 * (1 + (this.markup + this.skew_point) / 1000000);
                }
                else
                {
                    bid_price = this.taker.adjusted_bestbid.Item1 * (1 + (-this.markup) / 1000000);
                    ask_price = this.taker.adjusted_bestask.Item1 * (1 + (this.markup) / 1000000);
                }

                decimal min_markup_bid = this.taker.adjusted_bestbid.Item1 * (1 - this.min_markup / 1000000);
                decimal min_markup_ask = this.taker.adjusted_bestask.Item1 * (1 + this.min_markup / 1000000);

                if (bid_price >= this.maker.bestask.Item1)
                {
                    bid_price = this.maker.bestbid.Item1 + this.maker.price_unit;
                    if (bid_price >= this.maker.bestask.Item1)
                    {
                        bid_price = this.maker.bestbid.Item1;
                    }
                }

                if (ask_price <= this.maker.bestbid.Item1)
                {
                    ask_price = this.maker.bestask.Item1 - this.maker.price_unit;
                    if (ask_price <= this.maker.bestbid.Item1)
                    {
                        ask_price = this.maker.bestask.Item1;
                    }
                }

                if (bid_price > min_markup_bid)
                {
                    bid_price = min_markup_bid;
                }
                if (ask_price < min_markup_ask)
                {
                    ask_price = min_markup_ask;
                }

                bid_price = Math.Floor(bid_price / this.maker.price_unit) * this.maker.price_unit;
                ask_price = Math.Ceiling(ask_price / this.maker.price_unit) * this.maker.price_unit;

                if (this.ToBsize * (1 + this.taker.taker_fee) > this.taker.baseBalance.balance)
                {
                    bid_price = 0;
                }
                if (this.taker.adjusted_bestask.Item1 * this.ToBsize * (1 + this.taker.taker_fee) > this.taker.quoteBalance.balance)
                {
                    ask_price = 0;
                }

                bool isPriceChanged = this.checkPriceChange();

                if (isPriceChanged)
                {
                    this.taker_last_updated_mid = this.taker.adj_mid;
                    this.maker_last_updated_mid = this.maker.adj_mid;
                }

                while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
                {

                }
                if (this.live_buyorder_id != "")
                {
                    this.live_buyorder = this.oManager.orders[this.live_buyorder_id];
                    this.stg_orders[this.live_buyorder_id] = this.live_buyorder;
                    switch (this.live_buyorder.status)
                    {
                        case orderStatus.NONE:
                        case orderStatus.Filled:
                        case orderStatus.Canceled:
                        case orderStatus.WaitCancel:
                        case orderStatus.INVALID:
                            this.live_buyorder = null;
                            break;
                    }
                }
                else
                {
                    this.live_buyorder = null;
                }
                if (this.live_sellorder_id != "")
                {
                    this.live_sellorder = this.oManager.orders[this.live_sellorder_id];
                    this.stg_orders[this.live_sellorder_id] = this.live_sellorder;
                    switch (this.live_sellorder.status)
                    {
                        case orderStatus.NONE:
                        case orderStatus.Filled:
                        case orderStatus.Canceled:
                        case orderStatus.WaitCancel:
                        case orderStatus.INVALID:
                            this.live_sellorder = null;
                            break;
                    }
                }
                else
                {
                    this.live_sellorder = null;
                }

                if (this.live_buyorder != null)
                {
                    //this.live_buyorder = this.oManager.orders[this.live_buyorder.order_id];
                    if (bid_price == 0 || (this.maker.baseBalance.balance > this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200)))
                    {
                        this.live_buyorder = await this.oManager.placeCancelSpotOrder(this.maker, this.live_buyorder.order_id);
                        if (this.live_buyorder != null)
                        {
                            this.stg_orders[this.live_buyorder.order_id] = this.live_buyorder;
                        }
                        this.live_buyorder_id = "";
                        this.live_bidprice = 0;
                    }
                    else if (isPriceChanged && this.live_buyorder.status == orderStatus.Open && this.live_buyorder.order_price != bid_price)
                    {
                        this.live_buyorder = await this.oManager.placeModSpotOrder(this.maker, this.live_buyorder.order_id, this.ToBsize, bid_price, false);
                        if (this.live_buyorder != null)
                        {
                            this.live_buyorder_id = this.live_buyorder.order_id;
                            this.stg_orders[this.live_buyorder_id] = this.live_buyorder;
                            this.live_bidprice = bid_price;
                        }
                        else
                        {
                            this.live_buyorder_id = "";
                            this.live_bidprice = 0;
                        }
                    }
                }
                else
                {
                    if (this.maker.baseBalance.balance > this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200))
                    {
                        //Do nothing
                    }
                    else if (bid_price > 0 && (this.last_filled_time == null || (decimal)(DateTime.UtcNow - this.last_filled_time).Value.TotalSeconds > this.intervalAfterFill))
                    {
                        this.live_buyorder = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.ToBsize, bid_price);
                        if (this.live_buyorder != null)
                        {
                            this.live_buyorder_id = this.live_buyorder.order_id;
                            this.stg_orders[this.live_buyorder_id] = this.live_buyorder;
                            this.live_bidprice = bid_price;
                        }
                        else
                        {
                            this.live_buyorder_id = "";
                            this.live_bidprice = 0;
                        }
                    }
                }

                if (this.live_sellorder != null)
                {
                    //this.live_sellorder = this.oManager.orders[this.live_sellorder.order_id];
                    if (ask_price == 0 || (this.maker.baseBalance.balance < this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200)))
                    {
                        this.live_sellorder = await this.oManager.placeCancelSpotOrder(this.maker, this.live_sellorder.order_id);
                        if (this.live_sellorder != null)
                        {
                            this.stg_orders[this.live_sellorder.order_id] = this.live_sellorder;
                        }
                        this.live_sellorder_id = "";
                        this.live_askprice = 0;
                    }
                    else if (isPriceChanged && this.live_sellorder.status == orderStatus.Open && this.live_sellorder.order_price != ask_price)
                    {
                        this.live_sellorder = await this.oManager.placeModSpotOrder(this.maker, this.live_sellorder.order_id, this.ToBsize, ask_price, false);
                        if (this.live_sellorder != null)
                        {
                            this.live_sellorder_id = this.live_sellorder.order_id;
                            this.stg_orders[this.live_sellorder_id] = this.live_sellorder;
                            this.live_askprice = ask_price;
                        }
                        else
                        {
                            this.live_sellorder_id = "";
                            this.live_askprice = 0;
                        }
                    }
                }
                else
                {
                    if (this.maker.baseBalance.balance < this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200))
                    {
                        //Do nothing
                    }
                    else if (ask_price > 0 && (this.last_filled_time == null || (decimal)(DateTime.UtcNow - this.last_filled_time).Value.TotalSeconds > this.intervalAfterFill))
                    {
                        this.live_sellorder = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ToBsize, ask_price);
                        if (this.live_sellorder != null)
                        {
                            this.live_sellorder_id = this.live_sellorder.order_id;
                            this.stg_orders[this.live_sellorder_id] = this.live_sellorder;
                            this.live_askprice = ask_price;
                        }
                        else
                        {
                            this.live_sellorder_id = "";
                            this.live_askprice = 0;
                        }
                    }
                }
                if (this.live_sellorder != null)
                {
                    switch (this.live_sellorder.status)
                    {
                        case orderStatus.Filled:
                        case orderStatus.Canceled:
                        case orderStatus.WaitCancel:
                        case orderStatus.NONE:
                        case orderStatus.INVALID:
                            this.live_sellorder = null;
                            this.live_sellorder_id = "";
                            this.live_askprice = 0;
                            break;
                    }
                }
                if (this.live_buyorder != null)
                {
                    switch (this.live_buyorder.status)
                    {
                        case orderStatus.Filled:
                        case orderStatus.Canceled:
                        case orderStatus.WaitCancel:
                        case orderStatus.NONE:
                        case orderStatus.INVALID:
                            this.live_buyorder = null;
                            this.live_buyorder_id = "";
                            this.live_bidprice = 0;
                            break;
                    }
                }

                Volatile.Write(ref this.order_lock, 0);
            }
        }

        public bool checkPriceChange()
        {
            bool taker_check = (this.taker_last_updated_mid == 0 || this.taker.mid / this.taker_last_updated_mid > 1 + this.modThreshold || this.taker.mid / this.taker_last_updated_mid < 1 - this.modThreshold);
            bool maker_check = (this.maker_last_updated_mid == 0 || this.maker.mid / this.maker_last_updated_mid > 1 + this.modThreshold || this.maker.mid / this.maker_last_updated_mid < 1 - this.modThreshold);
            return (taker_check || maker_check);
        }
        public decimal skew()
        {
            decimal skew_point = 0;
            if(this.maker.baseBalance.balance > this.baseCcyQuantity * ((decimal)0.5 + this.skewThreshold / 200))
            {
                skew_point = - this.maxSkew * (this.maker.baseBalance.balance - this.baseCcyQuantity * ((decimal)0.5 + this.skewThreshold / 200)) / (this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200) - this.baseCcyQuantity * ((decimal)0.5 + this.skewThreshold / 200));
                if (skew_point < -this.maxSkew)
                {
                    skew_point = -this.maxSkew;
                }
            }
            else if(this.maker.baseBalance.balance < this.baseCcyQuantity * ((decimal)0.5 - this.skewThreshold / 200))
            {
                skew_point = this.maxSkew * (this.baseCcyQuantity * ((decimal)0.5 - this.skewThreshold / 200) - this.maker.baseBalance.balance) / (this.baseCcyQuantity * ((decimal)0.5 - this.skewThreshold / 200) - this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200));
                if (skew_point > this.maxSkew)
                {
                    skew_point = this.maxSkew;
                }
            }
            return skew_point;
        }
        public void on_Message(DataSpotOrderUpdate prev_ord, DataSpotOrderUpdate new_ord)
        {
            if(this.enabled)
            {
                decimal filled_quantity = new_ord.filled_quantity - prev_ord.filled_quantity;
                if (filled_quantity == 0)
                {
                    filled_quantity = new_ord.filled_quantity;
                }
                orderSide side;
                if (new_ord.market != this.maker.market || new_ord.status == orderStatus.WaitOpen || new_ord.status == orderStatus.WaitMod || new_ord.status == orderStatus.WaitCancel)
                {       
                    return;
                }
                while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
                {

                }
                if (this.stg_orders.ContainsKey(new_ord.order_id) == false)
                {
                    this.addLog("Unknown order order:" + new_ord.ToString());
                    Volatile.Write(ref this.order_lock, 0);
                    return;
                }

                switch (new_ord.side)
                {
                    case orderSide.Buy:
                        if (filled_quantity > 0)
                        {
                            side = orderSide.Sell;
                            //filled_quantity = filled_quantity / (1 - this.taker.taker_fee);
                            filled_quantity = Math.Round(filled_quantity / this.taker.quantity_unit) * this.taker.quantity_unit;
                            this.oManager.placeNewSpotOrder(this.taker, side, orderType.Market, filled_quantity, 0);
                        }
                        if (this.live_buyorder != null && this.live_buyorder.order_id == new_ord.order_id)
                        {
                            this.live_buyorder = new_ord;
                            switch (new_ord.status)
                            {
                                case orderStatus.Filled:
                                    this.last_filled_time = DateTime.UtcNow;
                                    this.live_buyorder = null;
                                    this.live_buyorder_id = "";
                                    this.live_bidprice = 0;
                                    break;
                                case orderStatus.Canceled:
                                case orderStatus.WaitCancel:
                                case orderStatus.NONE:
                                case orderStatus.INVALID:
                                    this.live_buyorder = null;
                                    this.live_buyorder_id = "";
                                    this.live_bidprice = 0;
                                    break;
                            }
                        }
                        break;
                    case orderSide.Sell:
                        if (filled_quantity > 0)
                        {
                            side = orderSide.Buy;
                            filled_quantity = Math.Round(filled_quantity / this.taker.quantity_unit) * this.taker.quantity_unit;
                            this.oManager.placeNewSpotOrder(this.taker, side, orderType.Market, filled_quantity, 0);
                        }
                        if (this.live_sellorder != null && this.live_sellorder.order_id == new_ord.order_id)
                        {
                            this.live_sellorder = new_ord;
                            switch (new_ord.status)
                            {
                                case orderStatus.Filled:
                                    this.last_filled_time = DateTime.UtcNow;
                                    this.live_sellorder = null;
                                    this.live_sellorder_id = "";
                                    this.live_askprice = 0;
                                    break;
                                case orderStatus.Canceled:
                                case orderStatus.WaitCancel:
                                case orderStatus.NONE:
                                case orderStatus.INVALID:
                                    this.live_sellorder = null;
                                    this.live_sellorder_id = "";
                                    this.live_askprice = 0;
                                    break;
                            }
                        }
                        break;
                }

                Volatile.Write(ref this.order_lock, 0);
            }
        }
        public void addLog(string line, Enums.logType logtype = Enums.logType.INFO)
        {
            this._addLog("[Strategy]" + line, logtype);
        }
    }
}
