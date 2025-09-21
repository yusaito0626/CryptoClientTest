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
        public decimal baseCcyQuantity;
        public decimal ToBsize;

        public decimal intervalAfterFill;
        public decimal modThreshold;

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
        public DataSpotOrderUpdate? prev_sellorder;
        public DataSpotOrderUpdate? prev_buyorder;

        public decimal live_askprice;
        public decimal live_bidprice;
        public decimal skew_point;

        public decimal last_updated_mid;

        public DateTime? last_filled_time;
        public Strategy() 
        {
            this.enabled = false;
            this.markup = 0;
            this.baseCcyQuantity = 0;
            this.ToBsize = 0;
            this.intervalAfterFill = 0;
            this.modThreshold = 0;
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
            this.prev_sellorder = null;
            this.prev_buyorder = null;

            this.live_askprice = 0;
            this.live_bidprice = 0;
            this.skew_point = 0;

            this.last_updated_mid = 0;

            this.oManager = OrderManager.GetInstance();
        }

        public void readStrategyFile(string jsonfilename)
        {
            string fileContent = File.ReadAllText(jsonfilename);
            using JsonDocument doc = JsonDocument.Parse(fileContent);
            var root = doc.RootElement;

            this.baseCcy = root.GetProperty("baseCcy").ToString();
            this.quoteCcy = root.GetProperty("quoteCcy").ToString();
            this.markup = root.GetProperty("markup").GetDecimal();
            this.baseCcyQuantity = root.GetProperty("baseCcyQuantity").GetDecimal();
            this.ToBsize = root.GetProperty("ToBsize").GetDecimal();
            this.intervalAfterFill = root.GetProperty("intervalAfterFill").GetDecimal();
            this.modThreshold = root.GetProperty("modThreshold").GetDecimal();
            this.skewThreshold = root.GetProperty("skewThreshold").GetDecimal();
            this.oneSideThreshold = root.GetProperty("oneSideThreshold").GetDecimal();
            this.taker_symbol_market = root.GetProperty("taker_symbol_market").ToString();
            this.maker_symbol_market = root.GetProperty("maker_symbol_market").ToString();

        }

        public async Task updateOrders()
        {
            if(this.enabled)
            {
                int desired = 1;
                int expected = 0;

                this.skew_point = this.skew();
                decimal bid_price = this.taker.adjusted_bestbid.Item1 * (1 - (this.markup + this.skew_point) / 1000000);
                decimal ask_price = this.taker.adjusted_bestask.Item1 * (1 + (this.markup + this.skew_point) / 1000000);

                bid_price = Math.Round(bid_price / this.maker.price_unit) * this.maker.price_unit;
                ask_price = Math.Round(ask_price / this.maker.price_unit) * this.maker.price_unit;

                bool isPriceChanged = this.checkPriceChange();

                if (isPriceChanged)
                {
                    this.last_updated_mid = this.taker.mid;
                }

                while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
                {

                }
                if (this.live_buyorder != null)
                {
                    if (this.maker.baseBalance.balance > this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200))
                    {
                        this.live_buyorder = await this.oManager.placeCancelSpotOrder(this.maker, this.live_buyorder.order_id);
                        this.live_bidprice = 0;
                    }
                    else if (isPriceChanged && this.live_buyorder.status == orderStatus.Open && this.live_buyorder.order_price != bid_price)
                    {
                        this.prev_buyorder = this.live_buyorder;
                        this.live_buyorder = await this.oManager.placeModSpotOrder(this.maker, this.live_buyorder.order_id, this.ToBsize, bid_price, false);
                        this.live_bidprice = bid_price;
                    }
                }
                else
                {
                    if (this.maker.baseBalance.balance > this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200))
                    {
                        //Do nothing
                    }
                    else if (this.last_filled_time == null || (decimal)(DateTime.UtcNow - this.last_filled_time).Value.TotalSeconds > this.intervalAfterFill)
                    {
                        this.live_buyorder = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.ToBsize, bid_price);
                        this.live_bidprice = bid_price;
                    }
                }

                if (this.live_sellorder != null)
                {
                    if (this.maker.baseBalance.balance < this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200))
                    {
                        this.live_sellorder = await this.oManager.placeCancelSpotOrder(this.maker, this.live_sellorder.order_id);
                        this.live_askprice = 0;
                    }
                    else if (isPriceChanged && this.live_sellorder.status == orderStatus.Open && this.live_sellorder.order_price != ask_price)
                    {
                        this.prev_sellorder = this.live_sellorder;
                        this.live_sellorder = await this.oManager.placeModSpotOrder(this.maker, this.live_sellorder.order_id, this.ToBsize * (1 - this.maker.maker_fee), ask_price, false);
                        this.live_askprice = ask_price;
                    }
                }
                else
                {
                    if (this.maker.baseBalance.balance < this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200))
                    {
                        //Do nothing
                    }
                    else if (this.last_filled_time == null || (decimal)(DateTime.UtcNow - this.last_filled_time).Value.TotalSeconds > this.intervalAfterFill)
                    {
                        this.live_sellorder = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ToBsize * (1 - this.maker.maker_fee), ask_price);
                        this.live_askprice = ask_price;
                    }
                }
                Volatile.Write(ref this.order_lock, 0);
            }
        }

        public bool checkPriceChange()
        {
            bool output = (this.last_updated_mid == 0 || this.taker.mid / this.last_updated_mid > 1 + this.modThreshold || this.taker.mid / this.last_updated_mid < 1 - this.modThreshold);
            return output;
        }
        public decimal skew()
        {
            decimal skew_point = 0;
            if(this.maker.baseBalance.balance > this.baseCcyQuantity * ((decimal)0.5 + this.skewThreshold / 200))
            {
                skew_point = - this.markup * (this.maker.baseBalance.balance - this.baseCcyQuantity * ((decimal)0.5 + this.skewThreshold / 200)) / (this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200) - this.baseCcyQuantity * ((decimal)0.5 + this.skewThreshold / 200));
                if(skew_point < - this.markup)
                {
                    skew_point = - this.markup;
                }
            }
            else if(this.maker.baseBalance.balance < this.baseCcyQuantity * ((decimal)0.5 - this.skewThreshold / 200))
            {
                skew_point = this.markup * (this.baseCcyQuantity * ((decimal)0.5 - this.skewThreshold / 200) - this.maker.baseBalance.balance) / (this.baseCcyQuantity * ((decimal)0.5 - this.skewThreshold / 200) - this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200));
                if (skew_point > this.markup)
                {
                    skew_point = this.markup;
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

                if (new_ord.status == orderStatus.WaitOpen || new_ord.status == orderStatus.WaitMod || new_ord.status == orderStatus.WaitCancel)
                {
                    return;
                }

                while (Interlocked.CompareExchange(ref this.order_lock, 1, 0) != 0)
                {

                }
                switch (new_ord.side)
                {
                    case orderSide.Buy:
                        if (this.live_buyorder != null && new_ord.order_id == this.live_buyorder.order_id)
                        {
                            if (filled_quantity > 0)
                            {
                                side = orderSide.Sell;
                                filled_quantity = filled_quantity / (1 - this.taker.taker_fee);
                                filled_quantity = Math.Round(filled_quantity / this.taker.quantity_unit) * this.taker.quantity_unit;
                                this.oManager.placeNewSpotOrder(this.taker, side, orderType.Market, filled_quantity, 0);
                            }

                            this.live_buyorder = new_ord;

                            if (new_ord.status == orderStatus.Filled)
                            {
                                this.prev_buyorder = this.live_buyorder;
                                this.live_buyorder = null;
                                this.live_bidprice = 0;
                                this.last_filled_time = DateTime.UtcNow;
                            }
                        }
                        else if (this.prev_buyorder != null && new_ord.order_id == this.prev_buyorder.order_id)
                        {
                            if (filled_quantity > 0)
                            {
                                side = orderSide.Sell;
                                filled_quantity = filled_quantity / (1 - this.taker.taker_fee);
                                filled_quantity = Math.Round(filled_quantity / this.taker.quantity_unit) * this.taker.quantity_unit;
                                this.oManager.placeNewSpotOrder(this.taker, side, orderType.Market, filled_quantity, 0);
                            }
                            this.prev_buyorder = new_ord;
                        }
                        break;
                    case orderSide.Sell:
                        if (this.live_sellorder != null && new_ord.order_id == this.live_sellorder.order_id)
                        {
                            if (filled_quantity > 0)
                            {
                                side = orderSide.Buy;
                                filled_quantity = Math.Round(filled_quantity / this.taker.quantity_unit) * this.taker.quantity_unit;
                                this.oManager.placeNewSpotOrder(this.taker, side, orderType.Market, filled_quantity, 0);
                            }

                            this.live_sellorder = new_ord;

                            if (new_ord.status == orderStatus.Filled)
                            {
                                this.prev_sellorder = this.live_sellorder;
                                this.live_sellorder = null;
                                this.live_askprice = 0;
                                this.last_filled_time = DateTime.UtcNow;
                            }
                        }
                        else if (this.prev_sellorder != null && new_ord.order_id == this.prev_sellorder.order_id)
                        {
                            if (filled_quantity > 0)
                            {
                                side = orderSide.Buy;
                                filled_quantity = Math.Round(filled_quantity / this.taker.quantity_unit) * this.taker.quantity_unit;
                                this.oManager.placeNewSpotOrder(this.taker, side, orderType.Market, filled_quantity, 0);
                            }
                            this.prev_sellorder = new_ord;
                        }
                        break;
                }
                Volatile.Write(ref this.order_lock, 0);
            }
        }
    }
}
