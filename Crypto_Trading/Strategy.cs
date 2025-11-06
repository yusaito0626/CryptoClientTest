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
using Utils;

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

        public bool predictFill;
        public volatile int fill_lock;
        public Dictionary<string, DataSpotOrderUpdate> executed_Orders;

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
        public volatile int queued;

        //public DataSpotOrderUpdate? live_sellorder;
        //public DataSpotOrderUpdate? live_buyorder;

        public string live_sellorder_id;
        public string live_buyorder_id;
        public List<string> stg_orders;


        public decimal live_askprice;
        public decimal live_bidprice;
        public decimal skew_point;

        public decimal taker_last_updated_mid;
        public decimal maker_last_updated_mid;

        public DateTime? last_filled_time_buy;
        public DateTime? last_filled_time_sell;

        public decimal notionalVolume;
        public decimal tradingPnL;
        public decimal totalFee;
        public decimal totalPnL;

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
            this.predictFill = false;

            this.taker_symbol_market = "";
            this.maker_symbol_market = "";
            this.taker_market = "";
            this.maker_market = "";

            this.taker = null;
            this.maker = null;

            this.num_of_layers = 1;

            this.updating = 0;
            this.queued= 0;

            this.live_sellorder_id = "";
            this.live_buyorder_id = "";
            this.stg_orders = new List<string>();
            this.executed_Orders = new Dictionary<string, DataSpotOrderUpdate>();

            this.live_askprice = 0;
            this.live_bidprice = 0;
            this.skew_point = 0;

            this.taker_last_updated_mid = 0;
            this.maker_last_updated_mid = 0;

            this.notionalVolume = 0;
            this.tradingPnL = 0;
            this.totalFee = 0;
            this.totalPnL = 0;

            this.oManager = OrderManager.GetInstance();
        }

        public void readStrategyFile(string jsonfilename)
        {
            if(File.Exists(jsonfilename))
            {
                string fileContent = File.ReadAllText(jsonfilename);
                using JsonDocument doc = JsonDocument.Parse(fileContent);
                var root = doc.RootElement;

                this.baseCcy = root.GetProperty("baseCcy").GetString();
                this.quoteCcy = root.GetProperty("quoteCcy").GetString();
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
                this.taker_symbol_market = root.GetProperty("taker_symbol_market").GetString();
                this.maker_symbol_market = root.GetProperty("maker_symbol_market").GetString();
                this.predictFill = root.GetProperty("fillPrediction").GetBoolean();
                //this.taker_market = root.GetProperty("taker_market").ToString();
                //this.maker_market = root.GetProperty("maker_market").ToString();
            }
        }

        public void setStrategy(JsonElement js)
        {
            this.name = js.GetProperty("name").GetString();
            this.baseCcy = js.GetProperty("baseCcy").GetString();
            this.quoteCcy = js.GetProperty("quoteCcy").GetString();
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
            this.taker_market = js.GetProperty("taker_market").GetString();
            this.maker_market = js.GetProperty("maker_market").GetString();
            this.predictFill = js.GetProperty("fillPrediction").GetBoolean();
        }
        public void setStrategy(strategySetting setting)
        {
            this.name = setting.name;
            this.baseCcy = setting.baseCcy;
            this.quoteCcy = setting.quoteCcy;
            this.markup = setting.markup;
            this.min_markup = setting.min_markup;
            this.baseCcyQuantity = setting.baseCcy_quantity;
            this.skewWidening = setting.skew_widening;
            this.ToBsize = setting.ToBsize;
            this.intervalAfterFill = setting.intervalAfterFill;
            this.modThreshold = setting.modThreshold;
            this.maxSkew = setting.max_skew;
            this.skewThreshold = setting.skewThreshold;
            this.oneSideThreshold = setting.oneSideThreshold;
            this.taker_market = setting.taker_market;
            this.maker_market = setting.maker_market;
            this.predictFill = setting.predictFill;
        }

        public async Task updateOrders()
        {

            if (this.enabled)
            {
                bool buyFirst = true;
                this.skew_point = this.skew();
                int i = 0;
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

                while (Interlocked.CompareExchange(ref this.updating, 1, 0) != 0)
                {

                }

                DataSpotOrderUpdate ord;
                if (this.live_buyorder_id != "")
                {
                    if (this.oManager.orders.ContainsKey(this.live_buyorder_id))
                    {
                        ord = this.oManager.orders[this.live_buyorder_id];
                        switch (ord.status)
                        {
                            case orderStatus.NONE:
                            case orderStatus.Filled:
                            case orderStatus.Canceled:
                            case orderStatus.INVALID:
                            case orderStatus.WaitCancel:
                                this.live_buyorder_id = "";
                                this.live_bidprice = 0;
                                break;
                        }
                    }
                }
                else
                {
                    this.live_bidprice = 0;
                }

                if (this.live_sellorder_id != "")
                {
                    if (this.oManager.orders.ContainsKey(this.live_sellorder_id))
                    {
                        ord = this.oManager.orders[this.live_sellorder_id];
                        switch (ord.status)
                        {
                            case orderStatus.NONE:
                            case orderStatus.Filled:
                            case orderStatus.Canceled:
                            case orderStatus.INVALID:
                            case orderStatus.WaitCancel:
                                this.live_sellorder_id = "";
                                this.live_askprice = 0;
                                break;
                        }
                    }
                }
                else
                {
                    this.live_askprice = 0;
                }

                await this.checkLiveOrders();

                bool newBuyOrder = false;
                bool newSellOrder = false;

                List<string> cancelling_ord = new List<string>();
                if (this.oManager.orders.ContainsKey(this.live_buyorder_id))
                {
                    ord = this.oManager.orders[this.live_buyorder_id];
                    if (bid_price == 0 || (this.maker.baseBalance.total > this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200)))
                    {
                        cancelling_ord.Add(this.live_buyorder_id);
                        this.live_buyorder_id = "";
                    }
                    else if (isPriceChanged && ord.status == orderStatus.Open && this.live_bidprice != bid_price)
                    {
                        cancelling_ord.Add(this.live_buyorder_id);
                        this.live_buyorder_id = "";
                        newBuyOrder = true;
                    }
                }
                else if(this.live_buyorder_id == "")
                {
                    if (bid_price > 0 && (this.last_filled_time_buy == null || (decimal)(DateTime.UtcNow - this.last_filled_time_buy).Value.TotalSeconds > this.intervalAfterFill))
                    {
                        newBuyOrder = true;
                    }
                }
                if (this.oManager.orders.ContainsKey(this.live_sellorder_id))
                {
                    ord = this.oManager.orders[this.live_sellorder_id];
                    if (ask_price == 0 || (this.maker.baseBalance.total < this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200)))
                    {
                        cancelling_ord.Add(this.live_sellorder_id);
                        this.live_sellorder_id = "";
                    }
                    else if (isPriceChanged && ord.status == orderStatus.Open && this.live_askprice != ask_price)
                    {
                        cancelling_ord.Add(this.live_sellorder_id);
                        this.live_sellorder_id = "";
                        newSellOrder = true;
                    }
                }
                else if (this.live_sellorder_id == "")
                {
                    if (bid_price > 0 && (this.last_filled_time_buy == null || (decimal)(DateTime.UtcNow - this.last_filled_time_buy).Value.TotalSeconds > this.intervalAfterFill))
                    {
                        newSellOrder = true;
                    }
                }

                this.oManager.placeCancelSpotOrders(this.maker, cancelling_ord);

                if(this.live_buyorder_id != "" && this.live_sellorder_id != "")//Both orders exist
                {
                    if(bid_price == 0)
                    {
                        buyFirst = true;
                    }
                    else if(ask_price == 0)
                    {
                        buyFirst = false;
                    }
                    else
                    {
                        if(ask_price - maker_ask > maker_bid - bid_price)
                        {
                            buyFirst = true;
                        }
                        else
                        {
                            buyFirst = false;
                        }
                    }
                }
                else if(this.live_buyorder_id != "")//Only buy order exists
                {
                    buyFirst = true;
                }
                else//Only sell order exists or no order exists
                {
                    buyFirst = false;
                }

                if(buyFirst)
                {
                    if(newBuyOrder)
                    {
                        this.live_buyorder_id = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.ToBsize, bid_price, null, true, false);
                        this.live_bidprice = bid_price;
                        this.stg_orders.Add(this.live_buyorder_id);
                    }
                    if(newSellOrder)
                    {
                        this.live_sellorder_id = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ToBsize, ask_price, null, true, false);
                        this.live_askprice = ask_price;
                        this.stg_orders.Add(this.live_sellorder_id);
                    }
                }
                else
                {
                    if (newSellOrder)
                    {
                        this.live_sellorder_id = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ToBsize, ask_price, null, true, false);
                        this.live_askprice = ask_price;
                        this.stg_orders.Add(this.live_sellorder_id);
                    }
                    if (newBuyOrder)
                    {
                        this.live_buyorder_id = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.ToBsize, bid_price, null, true, false);
                        this.live_bidprice = bid_price;
                        this.stg_orders.Add(this.live_buyorder_id);
                    }
                }


                //if (buyFirst)
                //{
                //    if (this.live_buyorder_id != "")
                //    {
                //        if (this.oManager.orders.ContainsKey(this.live_buyorder_id))
                //        {
                //            ord = this.oManager.orders[this.live_buyorder_id];
                //            if (bid_price == 0 || (this.maker.baseBalance.total > this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200)))
                //            {
                //                //this.live_buyorder_id = "";
                //                //this.oManager.placeCancelSpotOrder(this.maker, ord.internal_order_id, true);
                //                this.live_bidprice = 0;
                //            }
                //            else if (isPriceChanged && ord.status == orderStatus.Open && this.live_bidprice != bid_price)
                //            {
                //                //this.live_buyorder_id = "";
                //                //this.live_buyorder_id = await this.oManager.placeModSpotOrder(this.maker, ord.internal_order_id, this.ToBsize, bid_price, false, true, false);
                //                this.live_buyorder_id = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.ToBsize, bid_price, null, true, false);
                //                this.live_bidprice = bid_price;
                //                this.stg_orders.Add(this.live_buyorder_id);
                //            }
                //        }
                //    }
                //    else
                //    {
                //        if (this.maker.baseBalance.total > this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200))
                //        {
                //            //Do nothing
                //        }
                //        else if (bid_price > 0 && (this.last_filled_time_buy == null || (decimal)(DateTime.UtcNow - this.last_filled_time_buy).Value.TotalSeconds > this.intervalAfterFill))
                //        {
                //            this.live_buyorder_id = "";
                //            this.live_buyorder_id = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.ToBsize, bid_price, null, true, false);
                //            this.stg_orders.Add(this.live_buyorder_id);
                //            this.live_bidprice = bid_price;
                //        }
                //    }

                //    if (this.live_sellorder_id != "")
                //    {
                //        if (this.oManager.orders.ContainsKey(this.live_sellorder_id))
                //        {
                //            ord = this.oManager.orders[this.live_sellorder_id];
                //            if (ask_price == 0 || (this.maker.baseBalance.total < this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200)))
                //            {
                //                //this.live_sellorder_id = "";
                //                //this.oManager.placeCancelSpotOrder(this.maker, ord.internal_order_id, true, false);
                //                this.live_askprice = 0;
                //            }
                //            else if (isPriceChanged && ord.status == orderStatus.Open && this.live_askprice != ask_price)
                //            {
                //                //this.live_sellorder_id = "";
                //                //this.live_sellorder_id = await this.oManager.placeModSpotOrder(this.maker, ord.internal_order_id, this.ToBsize, ask_price, false, true, false);
                //                this.live_sellorder_id = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ToBsize, ask_price, null, true, false);
                //                this.stg_orders.Add(this.live_sellorder_id);
                //                this.live_askprice = ask_price;
                //            }
                //        }

                //    }
                //    else
                //    {
                //        if (this.maker.baseBalance.total < this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200))
                //        {
                //            //Do nothing
                //        }
                //        else if (ask_price > 0 && (this.last_filled_time_sell == null || (decimal)(DateTime.UtcNow - this.last_filled_time_sell).Value.TotalSeconds > this.intervalAfterFill))
                //        {
                //            this.live_sellorder_id = "";
                //            this.live_sellorder_id = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ToBsize, ask_price, null, true, false);
                //            this.stg_orders.Add(this.live_sellorder_id);
                //            this.live_askprice = ask_price;
                //        }
                //    }
                //}
                //else
                //{
                //    if (this.live_sellorder_id != "")
                //    {
                //        if (this.oManager.orders.ContainsKey(this.live_sellorder_id))
                //        {
                //            ord = this.oManager.orders[this.live_sellorder_id];
                //            if (ask_price == 0 || (this.maker.baseBalance.total < this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200)))
                //            {
                //                this.live_sellorder_id = "";
                //                this.oManager.placeCancelSpotOrder(this.maker, ord.internal_order_id, true, false);
                //                this.live_askprice = 0;
                //            }
                //            else if (isPriceChanged && ord.status == orderStatus.Open && ord.order_price != ask_price)
                //            {
                //                this.live_sellorder_id = "";
                //                this.live_sellorder_id = await this.oManager.placeModSpotOrder(this.maker, ord.internal_order_id, this.ToBsize, ask_price, false, true, false);
                //                this.stg_orders.Add(this.live_sellorder_id);
                //                this.live_askprice = ask_price;
                //            }
                //        }

                //    }
                //    else
                //    {
                //        if (this.maker.baseBalance.total < this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200))
                //        {
                //            //Do nothing
                //        }
                //        else if (ask_price > 0 && (this.last_filled_time_sell == null || (decimal)(DateTime.UtcNow - this.last_filled_time_sell).Value.TotalSeconds > this.intervalAfterFill))
                //        {
                //            this.live_sellorder_id = "";
                //            this.live_sellorder_id = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ToBsize, ask_price, null, true, false);
                //            this.stg_orders.Add(this.live_sellorder_id);
                //            this.live_askprice = ask_price;
                //        }
                //    }

                //    if (this.live_buyorder_id != "")
                //    {
                //        if (this.oManager.orders.ContainsKey(this.live_buyorder_id))
                //        {
                //            ord = this.oManager.orders[this.live_buyorder_id];
                //            if (bid_price == 0 || (this.maker.baseBalance.total > this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200)))
                //            {
                //                this.live_buyorder_id = "";
                //                this.oManager.placeCancelSpotOrder(this.maker, ord.internal_order_id, true);
                //                this.live_bidprice = 0;
                //            }
                //            else if (isPriceChanged && ord.status == orderStatus.Open && ord.order_price != bid_price)
                //            {
                //                this.live_buyorder_id = "";
                //                this.live_buyorder_id = await this.oManager.placeModSpotOrder(this.maker, ord.internal_order_id, this.ToBsize, bid_price, false, true, false);
                //                this.live_bidprice = bid_price;
                //                this.stg_orders.Add(this.live_buyorder_id);
                //            }
                //        }
                //    }
                //    else
                //    {
                //        if (this.maker.baseBalance.total > this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200))
                //        {
                //            //Do nothing
                //        }
                //        else if (bid_price > 0 && (this.last_filled_time_buy == null || (decimal)(DateTime.UtcNow - this.last_filled_time_buy).Value.TotalSeconds > this.intervalAfterFill))
                //        {
                //            this.live_buyorder_id = "";
                //            this.live_buyorder_id = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.ToBsize, bid_price, null, true, false);
                //            this.stg_orders.Add(this.live_buyorder_id);
                //            this.live_bidprice = bid_price;
                //        }
                //    }
                //}
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
                List<string> maker_orders = new List<string>();
                List<string> taker_orders = new List<string>();

                while (Interlocked.CompareExchange(ref this.oManager.order_lock, 1, 0) != 0)
                {
                }
                foreach(var ord in this.oManager.live_orders)
                {
                    if(ord.Key != this.live_buyorder_id && ord.Key != this.live_sellorder_id && ord.Value.status == orderStatus.Open)
                    {
                        if(this.maker.symbol_market == ord.Value.symbol_market)
                        {
                            maker_orders.Add(ord.Key);
                        }
                        else if (this.taker.symbol_market == ord.Value.symbol_market)
                        {
                            taker_orders.Add(ord.Key);
                        }
                        else
                        {
                        }
                    }
                }
                Volatile.Write(ref this.oManager.order_lock, 0);
                if(maker_orders.Count > 0)
                {
                    this.oManager.placeCancelSpotOrders(this.maker, maker_orders, true);
                }
                if(taker_orders.Count > 0)
                {
                    this.oManager.placeCancelSpotOrders(this.taker, taker_orders, true);
                }
                //foreach (var id in maker_orders)
                //{
                //    if (this.oManager.orders.ContainsKey(id))
                //    {
                //        cancelling_ord = this.oManager.orders[id];
                //        if (cancelling_ord.symbol_market == this.maker.symbol_market)
                //        {
                //            this.oManager.placeCancelSpotOrder(this.maker, cancelling_ord.order_id, true);
                //        }
                //        else if (cancelling_ord.symbol_market == this.taker.symbol_market)
                //        {
                //            this.oManager.placeCancelSpotOrder(this.taker, cancelling_ord.order_id,true);
                //        }
                //    }
                //}
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

        public void onTrades(DataTrade trade)
        {
            if(this.predictFill && trade.symbol + "@" + trade.market == this.maker.symbol_market)
            {
                int i = 0;
                while (Interlocked.CompareExchange(ref this.updating, 2, 0) != 0)
                {
                    ++i;
                    if(i > 1000000000)
                    {
                        addLog("Locked in updating. value:" + this.updating.ToString());
                        addLog("sendingOrder Stack. " + this.oManager.sendingOrdersStack.Count().ToString());
                        addLog("Push count:" + this.oManager.push_count.ToString() + "   Pop count:" + this.oManager.pop_count.ToString());
                        Thread.Sleep(1);
                        //return;
                        i = 0;
                    }
                }
                string ord_id;
                DataSpotOrderUpdate ord = null;
                decimal quantity = 0;
                switch(trade.side)
                {
                    case CryptoExchange.Net.SharedApis.SharedOrderSide.Buy:

                        ord_id = this.live_sellorder_id;
                        while (Interlocked.CompareExchange(ref this.fill_lock, 1, 0) != 0)
                        {
                        }
                        if (this.oManager.orders.ContainsKey(ord_id))
                        {
                            ord = this.oManager.orders[ord_id];
                        }
                        else
                        {
                            ord = null;
                        }
                        if (ord != null && ord.status == orderStatus.Open)
                        {
                            if (ord.order_price < trade.price && ord.update_time < trade.filled_time)//Assuming those 2 times are from same clock. If the buy trade price is higher than our ask
                            {
                                if (this.executed_Orders.ContainsKey(ord.internal_order_id))
                                {
                                    //Do nothing
                                }
                                else
                                {
                                    this.live_sellorder_id = "";
                                    decimal filled_quantity = ord.order_quantity - ord.filled_quantity;
                                    this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, null, true);
                                    this.last_filled_time_sell = DateTime.UtcNow;
                                    this.executed_Orders[ord.internal_order_id] = ord;
                                    ord.msg += "  onTrades at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                                    addLog(ord.ToString());
                                }
                            }
                        }
                        Volatile.Write(ref this.fill_lock, 0);
                        break;
                    case CryptoExchange.Net.SharedApis.SharedOrderSide.Sell:
                        ord_id = this.live_buyorder_id;
                        i = 0;
                        while (Interlocked.CompareExchange(ref this.fill_lock, 1, 0) != 0)
                        {
                        }
                        if (this.oManager.orders.ContainsKey(ord_id))
                        {
                            ord = this.oManager.orders[ord_id];
                        }
                        else
                        {
                            ord = null;
                        }
                        if (ord != null && ord.status == orderStatus.Open)
                        {
                            if (ord.order_price > trade.price && ord.update_time < trade.filled_time)//Assuming those 2 times are from same clock. If the sell trade price is lower than our bid
                            {
                                if (this.executed_Orders.ContainsKey(ord.internal_order_id))
                                {
                                    //Do nothing
                                }
                                else
                                {
                                    this.live_buyorder_id = "";
                                    decimal filled_quantity = ord.order_quantity - ord.filled_quantity;

                                    this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, null, true);
                                    this.last_filled_time_buy = DateTime.UtcNow;
                                    this.executed_Orders[ord.internal_order_id] = ord;
                                    ord.msg += "  onTrades at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                                    addLog(ord.ToString());
                                }
                            }
                        }
                        Volatile.Write(ref this.fill_lock, 0);
                        break;
                }
                Volatile.Write(ref this.updating, 0);
            }
        }
        public void onMakerQuotes(DataOrderBook quote)
        {
            if(this.predictFill && quote.symbol + "@" + quote.market == this.maker.symbol_market)
            {
                decimal diff_amount = this.maker.baseBalance.total + this.taker.baseBalance.total - this.baseCcyQuantity;
                while (Interlocked.CompareExchange(ref this.updating,3,0) != 0)
                {

                }
                string ord_id;
                DataSpotOrderUpdate ord;
                ord_id = this.live_sellorder_id;
                if (this.oManager.orders.ContainsKey(ord_id))
                {
                    ord = this.oManager.orders[ord_id];
                }
                else
                {
                    ord = null;
                }
                if (ord != null && ord.status == orderStatus.Open)
                {
                    if(ord.update_time < quote.orderbookTime && quote.asks.ContainsKey(ord.order_price) && quote.asks[ord.order_price] == 0)
                    {
                        while (Interlocked.CompareExchange(ref this.fill_lock, 1, 0) != 0)
                        {

                        }
                        if (this.executed_Orders.ContainsKey(ord.internal_order_id))
                        {
                            //Do nothing
                        }
                        else
                        {
                            decimal filled_quantity = ord.order_quantity - ord.filled_quantity;
                            switch (ord.side)
                            {
                                case orderSide.Buy://taker order will be sell
                                    filled_quantity += diff_amount;
                                    break;
                                case orderSide.Sell:
                                    filled_quantity -= diff_amount;
                                    break;

                            }
                            this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, null, true);
                            this.last_filled_time_sell = DateTime.UtcNow;
                            this.executed_Orders[ord.internal_order_id] = ord;
                            ord.msg += "  onMakerQuotes at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                            addLog(ord.ToString());
                        }
                        Volatile.Write(ref this.fill_lock, 0);
                    }
                }

                ord_id = this.live_buyorder_id;
                if (this.oManager.orders.ContainsKey(ord_id))
                {
                    ord = this.oManager.orders[ord_id];
                }
                else
                {
                    ord = null;
                }
                if (ord != null && ord.status == orderStatus.Open)
                {
                    if (ord.update_time < quote.orderbookTime && quote.bids.ContainsKey(ord.order_price) && quote.bids[ord.order_price] == 0)
                    {
                        while (Interlocked.CompareExchange(ref this.fill_lock, 1, 0) != 0)
                        {

                        }
                        if (this.executed_Orders.ContainsKey(ord.internal_order_id))
                        {
                            //Do nothing
                        }
                        else
                        {
                            decimal filled_quantity = ord.order_quantity - ord.filled_quantity;

                            this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, null, true);
                            this.last_filled_time_buy = DateTime.UtcNow;
                            this.executed_Orders[ord.internal_order_id] = ord;
                            ord.msg += "  onMakerQuotes at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                            addLog(ord.ToString());
                        }
                        Volatile.Write(ref this.fill_lock, 0);
                    }
                }
                Volatile.Write(ref this.updating, 0);
            }
        }
        public void onOrdUpdate(DataSpotOrderUpdate ord, DataSpotOrderUpdate prev)
        {
            if (this.predictFill)
            {
                if(ord.status == orderStatus.Filled && this.stg_orders.Contains(ord.internal_order_id))
                {
                    decimal diff_amount = this.maker.baseBalance.total + this.taker.baseBalance.total - this.baseCcyQuantity;
                    
                    while (Interlocked.CompareExchange(ref this.fill_lock, 1, 0) != 0)
                    {

                    }
                    if (this.executed_Orders.ContainsKey(ord.internal_order_id))
                    {
                        //Do nothing
                    }
                    else
                    {
                        decimal filled_quantity;
                        if(ord == prev)
                        {
                            filled_quantity = ord.filled_quantity;
                        }
                        else
                        {
                            filled_quantity = ord.filled_quantity - prev.filled_quantity;
                        }
                        switch (ord.side)
                        {
                            case orderSide.Buy://taker order will be sell
                                filled_quantity += diff_amount;
                                break;
                            case orderSide.Sell:
                                filled_quantity -= diff_amount;
                                break;

                        }
                        switch (ord.side)
                        {
                            case orderSide.Buy:
                                this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, null, true);
                                this.last_filled_time_buy = DateTime.UtcNow;
                                this.executed_Orders[ord.internal_order_id] = ord;
                                ord.msg += "  onOrdUpdate at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                                addLog(ord.ToString());
                                break;
                            case orderSide.Sell:
                                this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, null, true);
                                this.last_filled_time_sell = DateTime.UtcNow;
                                this.executed_Orders[ord.internal_order_id] = ord;
                                ord.msg += "  onOrdUpdate at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                                addLog(ord.ToString());
                                break;
                        }
                    }
                    Volatile.Write(ref this.fill_lock, 0);
                }
            }
        }

        public async Task onFill(DataFill fill)
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
                if (this.stg_orders.Contains(fill.internal_order_id) == false)
                {
                    //this.addLog("Unknown order order:" + fill.ToString());
                    return;
                }
                filled_quantity = Math.Round(filled_quantity / this.taker.quantity_unit) * this.taker.quantity_unit;
                if (filled_quantity > 0)
                {
                    if (this.predictFill)
                    {
                        while (Interlocked.CompareExchange(ref this.fill_lock, 1, 0) != 0)
                        {

                        }
                        if (this.executed_Orders.ContainsKey(fill.internal_order_id))
                        {
                            //Do nothing
                        }
                        else
                        {
                            switch (fill.side)
                            {
                                case orderSide.Buy:
                                    this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0,null,true);
                                    if (this.oManager.orders.ContainsKey(fill.internal_order_id))
                                    {
                                        ord = this.oManager.orders[fill.internal_order_id];
                                    }
                                    else
                                    {
                                        this.addLog("Order Not Found:" + fill.ToString());
                                        return;
                                    }
                                    if (ord.order_quantity - ord.filled_quantity <= fill.quantity || ord.status == orderStatus.Filled)
                                    {
                                        this.executed_Orders[ord.internal_order_id] = ord;
                                        ord.msg += "  onFill at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat) + fill.internal_order_id;
                                        addLog(ord.ToString());
                                        fill.msg = ord.msg;
                                        this.last_filled_time_buy = DateTime.UtcNow;
                                    }
                                    break;
                                case orderSide.Sell:
                                    this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, null, true);
                                    if (this.oManager.orders.ContainsKey(fill.internal_order_id))
                                    {
                                        ord = this.oManager.orders[fill.internal_order_id];
                                    }
                                    else
                                    {
                                        this.addLog("Order Not Found:" + fill.ToString());
                                        return;
                                    }
                                    if (ord.order_quantity - ord.filled_quantity <= fill.quantity || ord.status == orderStatus.Filled)
                                    {
                                        this.executed_Orders[ord.internal_order_id] = ord;
                                        this.last_filled_time_sell = DateTime.UtcNow;
                                        ord.msg += "  onFill at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat) + fill.internal_order_id;
                                        addLog(ord.ToString());
                                        fill.msg = ord.msg;
                                    }
                                    break;
                            }
                        }
                        Volatile.Write(ref this.fill_lock, 0);
                    }
                    else
                    {
                        switch (fill.side)
                        {
                            case orderSide.Buy:
                                this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, null, true);
                                if (this.oManager.orders.ContainsKey(fill.internal_order_id))
                                {
                                    ord = this.oManager.orders[fill.internal_order_id];
                                }
                                else
                                {
                                    this.addLog("Order Not Found:" + fill.ToString());
                                    return;
                                }
                                if (ord.order_quantity - ord.filled_quantity <= fill.quantity || ord.status == orderStatus.Filled)
                                {
                                    this.last_filled_time_buy = DateTime.UtcNow;
                                }
                                break;
                            case orderSide.Sell:
                                this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, null, true);
                                if (this.oManager.orders.ContainsKey(fill.internal_order_id))
                                {
                                    ord = this.oManager.orders[fill.internal_order_id];
                                }
                                else
                                {
                                    this.addLog("Order Not Found:" + fill.ToString());
                                    return;
                                }
                                if (ord.order_quantity - ord.filled_quantity <= fill.quantity || ord.status == orderStatus.Filled)
                                {
                                    this.last_filled_time_sell = DateTime.UtcNow;
                                }
                                break;
                        }
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
