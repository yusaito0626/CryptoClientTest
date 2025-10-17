using Binance.Net.Enums;
using Crypto_Clients;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Crypto_Trading
{
    public class Strategy
    {
        public string name;

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
        public decimal skewWidening;

        public bool abook;
        public bool includeFee;

        public string baseCcy;
        public string quoteCcy;
        public string taker_symbol_market;
        public string maker_symbol_market;

        public string taker_market;
        public string maker_market;

        public Instrument? taker;
        public Instrument? maker;

        public int num_of_layers;

        public volatile int updating;

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
            this.name = "";
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
            this.skewWidening = 0;
            this.abook = true;
            this.includeFee = true;

            this.taker_symbol_market = "";
            this.maker_symbol_market = "";
            this.taker_market = "";
            this.maker_market = "";

            this.taker = null;
            this.maker = null;

            this.num_of_layers = 1;

            this.updating = 0;

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
                this.skewWidening = root.GetProperty("skewWidening").GetDecimal();
                this.ToBsize = root.GetProperty("ToBsize").GetDecimal();
                this.intervalAfterFill = root.GetProperty("intervalAfterFill").GetDecimal();
                this.modThreshold = root.GetProperty("modThreshold").GetDecimal();
                this.maxSkew = root.GetProperty("max_skew").GetDecimal();
                this.skewThreshold = root.GetProperty("skewThreshold").GetDecimal();
                this.oneSideThreshold = root.GetProperty("oneSideThreshold").GetDecimal();
                this.taker_symbol_market = root.GetProperty("taker_symbol_market").ToString();
                this.maker_symbol_market = root.GetProperty("maker_symbol_market").ToString();
                //this.taker_market = root.GetProperty("taker_market").ToString();
                //this.maker_market = root.GetProperty("maker_market").ToString();
            }
        }

        public void setStrategy(JsonElement js)
        {
            this.name = js.GetProperty("name").ToString();
            this.baseCcy = js.GetProperty("baseCcy").ToString();
            this.quoteCcy = js.GetProperty("quoteCcy").ToString();
            this.markup = js.GetProperty("markup").GetDecimal();
            this.min_markup = js.GetProperty("min_markup").GetDecimal();
            this.baseCcyQuantity = js.GetProperty("baseCcyQuantity").GetDecimal();
            this.skewWidening = js.GetProperty("skewWidening").GetDecimal();
            this.ToBsize = js.GetProperty("ToBsize").GetDecimal();
            this.intervalAfterFill = js.GetProperty("intervalAfterFill").GetDecimal();
            this.modThreshold = js.GetProperty("modThreshold").GetDecimal();
            this.maxSkew = js.GetProperty("max_skew").GetDecimal();
            this.skewThreshold = js.GetProperty("skewThreshold").GetDecimal();
            this.oneSideThreshold = js.GetProperty("oneSideThreshold").GetDecimal();
            this.taker_market = js.GetProperty("taker_market").ToString();
            this.maker_market = js.GetProperty("maker_market").ToString();
        }

        public async Task updateOrders()
        {

            if (this.enabled)
            {

                this.skew_point = this.skew();
                while (Interlocked.CompareExchange(ref this.taker.quotes_lock, 1, 0) != 0)
                {

                }
                decimal taker_bid = this.taker.adjusted_bestbid.Item1;
                decimal taker_ask = this.taker.adjusted_bestask.Item1;
                Volatile.Write(ref this.taker.quotes_lock, 0);

                decimal bid_price = taker_bid;
                decimal ask_price = taker_ask;
                decimal min_markup_bid = bid_price * (1 - this.min_markup / 1000000);
                decimal min_markup_ask = ask_price * (1 + this.min_markup / 1000000);

                if (this.skew_point > 0)
                {
                    bid_price *= (1 + (-this.markup + this.skew_point) / 1000000);
                    ask_price *= (1 + (this.markup + (decimal)(1 + this.skewWidening) * this.skew_point) / 1000000);
                }
                else if (this.skew_point < 0)
                {
                    bid_price *= (1 + (-this.markup + (decimal)(1 + this.skewWidening) * this.skew_point) / 1000000);
                    ask_price *= (1 + (this.markup + this.skew_point) / 1000000);
                }
                else
                {
                    bid_price *= (1 + (-this.markup) / 1000000);
                    ask_price *= (1 + (this.markup) / 1000000);
                }
                while (Interlocked.CompareExchange(ref this.maker.quotes_lock, 1, 0) != 0)
                {

                }
                decimal maker_bid = this.maker.bestbid.Item1;
                decimal maker_ask = this.maker.bestask.Item1;
                Volatile.Write(ref this.maker.quotes_lock, 0);

                if (bid_price > maker_bid + this.maker.price_unit)
                {
                    bid_price = maker_bid + this.maker.price_unit;
                    if (bid_price >= maker_ask)
                    {
                        bid_price = maker_bid;
                    }
                }

                if (ask_price < maker_ask - this.maker.price_unit)
                {
                    ask_price = maker_ask - this.maker.price_unit;
                    if (ask_price <= maker_bid)
                    {
                        ask_price = maker_ask;
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

                if (this.ToBsize * (1 + this.taker.taker_fee) > this.taker.baseBalance.available)
                {
                    bid_price = 0;
                }
                if (taker_ask * this.ToBsize * (1 + this.taker.taker_fee) > this.taker.quoteBalance.available)
                {
                    ask_price = 0;
                }

                bool isPriceChanged = this.checkPriceChange();

                if (isPriceChanged)
                {
                    this.taker_last_updated_mid = this.taker.adj_mid;
                    this.maker_last_updated_mid = this.maker.adj_mid;
                }


                if (this.live_buyorder_id != "")
                {
                    if (this.oManager.orders.ContainsKey(this.live_buyorder_id))
                    {
                        this.live_buyorder = this.oManager.orders[this.live_buyorder_id];
                        this.stg_orders[this.live_buyorder_id] = this.live_buyorder;
                    }
                    if (this.live_buyorder != null)
                    {
                        switch (this.live_buyorder.status)
                        {
                            case orderStatus.NONE:
                            case orderStatus.Filled:
                            case orderStatus.Canceled:
                            case orderStatus.INVALID:
                                this.live_buyorder = null;
                                this.live_buyorder_id = "";
                                this.live_bidprice = 0;
                                break;
                            case orderStatus.WaitCancel:
                                this.addLog(this.live_buyorder.ToString());
                                this.live_buyorder = null;
                                this.live_buyorder_id = "";
                                this.live_bidprice = 0;
                                break;
                        }
                    }
                }
                else
                {
                    this.live_buyorder = null;
                    this.live_buyorder_id = "";
                    this.live_bidprice = 0;
                }
                if (this.live_sellorder_id != "")
                {
                    if (this.oManager.orders.ContainsKey(this.live_sellorder_id))
                    {
                        this.live_sellorder = this.oManager.orders[this.live_sellorder_id];
                        this.stg_orders[this.live_sellorder_id] = this.live_sellorder;
                    }
                    if (this.live_sellorder != null)
                    {
                        switch (this.live_sellorder.status)
                        {
                            case orderStatus.NONE:
                            case orderStatus.Filled:
                            case orderStatus.Canceled:
                            case orderStatus.INVALID:
                                this.live_sellorder = null;
                                this.live_sellorder_id = "";
                                this.live_askprice = 0;
                                break;
                            case orderStatus.WaitCancel:
                                this.addLog(this.live_sellorder.ToString());
                                this.live_sellorder = null;
                                this.live_sellorder_id = "";
                                this.live_askprice = 0;
                                break;
                        }
                    }
                }
                else
                {
                    this.live_sellorder = null;
                    this.live_sellorder_id = "";
                    this.live_askprice = 0;
                }

                await this.checkLiveOrders();

                if (this.live_buyorder != null)
                {
                    if (bid_price == 0 || (this.maker.baseBalance.total > this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200)))
                    {
                        this.live_buyorder = await this.oManager.placeCancelSpotOrder(this.maker, this.live_buyorder.order_id);
                        if (this.live_buyorder != null)
                        {
                            this.stg_orders[this.live_buyorder.client_order_id] = this.live_buyorder;
                        }
                        this.live_buyorder_id = "";
                        this.live_bidprice = 0;
                    }
                    else if (isPriceChanged && this.live_buyorder.status == orderStatus.Open && this.live_buyorder.order_price != bid_price)
                    {
                        this.live_buyorder = await this.oManager.placeModSpotOrder(this.maker, this.live_buyorder.order_id, this.ToBsize, bid_price, false);
                        if (this.live_buyorder != null)
                        {
                            this.live_buyorder_id = this.live_buyorder.client_order_id;
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
                    if (this.maker.baseBalance.total > this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200))
                    {
                        //Do nothing
                    }
                    else if (bid_price > 0 && (this.last_filled_time == null || (decimal)(DateTime.UtcNow - this.last_filled_time).Value.TotalSeconds > this.intervalAfterFill))
                    {
                        this.live_buyorder = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.ToBsize, bid_price);
                        if (this.live_buyorder != null)
                        {
                            this.live_buyorder_id = this.live_buyorder.client_order_id;
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
                    if (ask_price == 0 || (this.maker.baseBalance.total < this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200)))
                    {
                        this.live_sellorder = await this.oManager.placeCancelSpotOrder(this.maker, this.live_sellorder.order_id);
                        if (this.live_sellorder != null)
                        {
                            this.stg_orders[this.live_sellorder.client_order_id] = this.live_sellorder;
                        }
                        this.live_sellorder_id = "";
                        this.live_askprice = 0;
                    }
                    else if (isPriceChanged && this.live_sellorder.status == orderStatus.Open && this.live_sellorder.order_price != ask_price)
                    {
                        this.live_sellorder = await this.oManager.placeModSpotOrder(this.maker, this.live_sellorder.order_id, this.ToBsize, ask_price, false);
                        if (this.live_sellorder != null)
                        {
                            this.live_sellorder_id = this.live_sellorder.client_order_id;
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
                    if (this.maker.baseBalance.total < this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200))
                    {
                        //Do nothing
                    }
                    else if (ask_price > 0 && (this.last_filled_time == null || (decimal)(DateTime.UtcNow - this.last_filled_time).Value.TotalSeconds > this.intervalAfterFill))
                    {
                        this.live_sellorder = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ToBsize, ask_price);
                        if (this.live_sellorder != null)
                        {
                            this.live_sellorder_id = this.live_sellorder.client_order_id;
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

                Volatile.Write(ref this.updating, 0);
            }
        }

        public bool checkPriceChange()
        {
            bool taker_check = (this.taker_last_updated_mid == 0 || this.taker.mid / this.taker_last_updated_mid > 1 + this.modThreshold || this.taker.mid / this.taker_last_updated_mid < 1 - this.modThreshold);
            bool maker_check = (this.maker_last_updated_mid == 0 || this.maker.mid / this.maker_last_updated_mid > 1 + this.modThreshold || this.maker.mid / this.maker_last_updated_mid < 1 - this.modThreshold);
            return (taker_check || maker_check);
        }

        public async Task checkLiveOrders()
        {
            if(this.oManager.live_orders.Count > 2)
            {
                List<string> cancelling_ids = new List<string>();
                while (Interlocked.CompareExchange(ref this.oManager.order_lock, 1, 0) != 0)
                {

                }
                foreach(var ord in this.oManager.live_orders)
                {
                    if(ord.Key != this.live_buyorder_id && ord.Key != this.live_sellorder_id && ord.Value.status == orderStatus.Open)
                    {
                        if(this.maker.symbol_market == ord.Value.symbol_market)
                        {
                            this.addLog("Found an old order that is not cancelled",Enums.logType.WARNING);
                            this.addLog(ord.Value.ToString(), Enums.logType.WARNING);
                            cancelling_ids.Add(ord.Key);
                            //await this.oManager.placeCancelSpotOrder(this.maker, ord.Key);
                        }
                        else if (this.taker.symbol_market == ord.Value.symbol_market)
                        {
                            //this.addLog("Found an open order on taker side", Enums.logType.WARNING);
                            //this.addLog(ord.Value.ToString(), Enums.logType.WARNING);
                            cancelling_ids.Add(ord.Key);
                            //await this.oManager.placeCancelSpotOrder(this.taker, ord.Key);
                        }
                        else
                        {
                            //this.addLog("Unknown order", Enums.logType.WARNING);
                            //this.addLog(ord.Value.ToString(), Enums.logType.WARNING);
                            //cancelling_ids.Add(ord.Key);
                        }
                    }
                }
                Volatile.Write(ref this.oManager.order_lock, 0);
                DataSpotOrderUpdate cancelling_ord;
                foreach (var id in cancelling_ids)
                {
                    if (this.oManager.orders.ContainsKey(id))
                    {
                        cancelling_ord = this.oManager.orders[id];
                        if (cancelling_ord.symbol_market == this.maker.symbol_market)
                        {
                            await this.oManager.placeCancelSpotOrder(this.maker, cancelling_ord.order_id);
                        }
                        else if (cancelling_ord.symbol_market == this.taker.symbol_market)
                        {
                            await this.oManager.placeCancelSpotOrder(this.taker, cancelling_ord.order_id);
                        }
                    }
                }
            }
        }
        public decimal skew()
        {
            decimal skew_point = 0;
            if(this.maker.baseBalance.total > this.baseCcyQuantity * ((decimal)0.5 + this.skewThreshold / 200))
            {
                skew_point = - this.maxSkew * (this.maker.baseBalance.total - this.baseCcyQuantity * ((decimal)0.5 + this.skewThreshold / 200)) / (this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200) - this.baseCcyQuantity * ((decimal)0.5 + this.skewThreshold / 200));
                if (skew_point < -this.maxSkew)
                {
                    skew_point = -this.maxSkew;
                }
            }
            else if(this.maker.baseBalance.total < this.baseCcyQuantity * ((decimal)0.5 - this.skewThreshold / 200))
            {
                skew_point = this.maxSkew * (this.baseCcyQuantity * ((decimal)0.5 - this.skewThreshold / 200) - this.maker.baseBalance.total) / (this.baseCcyQuantity * ((decimal)0.5 - this.skewThreshold / 200) - this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200));
                if (skew_point > this.maxSkew)
                {
                    skew_point = this.maxSkew;
                }
            }
            return skew_point;
        }

        public async Task on_Message(DataFill fill)
        {
            if (this.enabled)
            {
                decimal diff_amount = this.maker.baseBalance.total + this.taker.baseBalance.total - this.baseCcyQuantity; 
                decimal filled_quantity = fill.quantity;
                switch (fill.side)
                {
                    case orderSide.Buy://taker order will be sell
                        filled_quantity += diff_amount;
                        break;
                    case orderSide.Sell:
                        filled_quantity -= diff_amount;
                        break;

                }
                if (filled_quantity > this.ToBsize * 2)
                {
                    filled_quantity = this.ToBsize * 2;
                }
                DataSpotOrderUpdate ord;
                if (fill.market != this.maker.market)
                {
                    return;
                }
                if (this.stg_orders.ContainsKey(fill.client_order_id) == false)
                {
                    this.addLog("Unknown order order:" + fill.ToString());
                    return;
                }
                else
                {
                    ord = this.stg_orders[fill.client_order_id];
                }
                filled_quantity = Math.Round(filled_quantity / this.taker.quantity_unit) * this.taker.quantity_unit;
                if (filled_quantity > 0)
                {
                    switch (fill.side)
                    {
                        case orderSide.Buy:
                            ord = await this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0);
                            if(ord != null)
                            {
                                this.stg_orders[ord.client_order_id] = ord;
                            }
                            break;
                        case orderSide.Sell:
                            ord = await this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0);
                            if (ord != null)
                            {
                                this.stg_orders[ord.client_order_id] = ord;
                            }
                            break;
                    }
                }
                
            }
        }
        public void addLog(string line, Enums.logType logtype = Enums.logType.INFO)
        {
            this._addLog("[Strategy]" + line, logtype);
        }
    }
}
