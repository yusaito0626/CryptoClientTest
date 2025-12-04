using Binance.Net.Enums;
using Binance.Net.Objects.Models.Spot.Loans;
using Crypto_Clients;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Utils;
using Enums;

namespace Crypto_Trading
{
    public class Strategy
    {
        public string name;

        public OrderManager oManager;

        public bool enabled;
        private bool ready;

        public double order_throttle;

        public decimal markup;
        public decimal min_markup;
        public decimal baseCcyQuantity;
        public decimal ToBsize;
        public decimal ToBsizeMultiplier;

        public decimal intervalAfterFill;
        public decimal modThreshold;
        public decimal config_modThreshold;

        public skewType skew_type;
        public decimal skew_step;
        public decimal maxSkew;
        public decimal skewThreshold;
        public decimal oneSideThreshold;
        public decimal skewWidening;
        public decimal markupAdjustment;
        public decimal max_baseMarkup = 2000;

        public bool abook;

        public bool predictFill;
        public volatile int fill_lock;
        public Dictionary<string, DataSpotOrderUpdate> executed_Orders_old;
        public Dictionary<string, fillType> executed_OrderIds;

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

        public DateTime lastPosAdjustment;

        //public DataSpotOrderUpdate? live_sellorder;
        //public DataSpotOrderUpdate? live_buyorder;

        public string live_sellorder_id;
        public string live_buyorder_id;
        public DateTime live_sellorder_time;
        public DateTime live_buyorder_time;
        public HashSet<string> stg_orders;


        public decimal live_askprice;
        public decimal live_bidprice;
        public decimal skew_point;
        public decimal base_markup;

        decimal cancelling_qty_buy;
        decimal cancelling_qty_sell;

        public DateTime prevMarkupTime;
        public decimal prev_markup;
        public decimal RVMarkup_multiplier = 1;

        public decimal taker_last_updated_mid;
        public decimal maker_last_updated_mid;

        public DateTime? last_filled_time;
        public DateTime? last_filled_time_buy;
        public DateTime? last_filled_time_sell;

        public decimal markup_decay_basetime = 60; //minutes

        public decimal temp_markup_bid = 0;
        public decimal temp_markup_ask = 0;

        public decimal SoD_baseCcyPos;
        public decimal SoD_price;
        public decimal netExposure;
        public decimal notionalVolume;
        public decimal posPnL;
        public decimal tradingPnL;
        public decimal totalFee;
        public decimal totalPnL;

        //For intradayPnL
        public decimal prev_notionalVolume = 0;

        public double onFill_latency1;
        public double onFill_latency2;
        public double onFill_latency3;
        public int onFill_count1;
        public int onFill_count2;
        public int onFill_count3;
        public double placingOrderLatencyOnFill;
        public double placingOrdercountOnFill;
        public double placingOrderLatencyUpdate;
        public double placingOrdercountUpdate;
        Stopwatch sw;
        Stopwatch sw2;
        Stopwatch sw3;

        public Action<string, Enums.logType> _addLog;
        public Strategy() 
        {
            this.name = "";
            this.enabled = false;
            this.ready = false;
            this.order_throttle = 0;
            this.markup = 0;
            this.min_markup = 0;
            this.baseCcyQuantity = 0;
            this.ToBsize = 0;
            this.ToBsizeMultiplier = 1;
            this.intervalAfterFill = 0;
            this.modThreshold = 0;
            this.maxSkew = 0;
            this.skew_type = skewType.LINEAR;
            this.skew_step = 100;
            this.skewThreshold = 0;
            this.oneSideThreshold = 0;
            this.skewWidening = 0;
            this.markupAdjustment = 0;
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
            this.live_buyorder_time = DateTime.UtcNow; 
            this.live_sellorder_time = DateTime.UtcNow;
            this.stg_orders = new HashSet<string>();
            this.executed_Orders_old = new Dictionary<string, DataSpotOrderUpdate>();
            this.executed_OrderIds = new Dictionary<string, fillType>();

            this.live_askprice = 0;
            this.live_bidprice = 0;
            this.skew_point = 0;
            this.prev_markup = 0;
            this.prevMarkupTime = DateTime.UtcNow;

            this.cancelling_qty_buy = 0;
            this.cancelling_qty_sell = 0;

            this.taker_last_updated_mid = 0;
            this.maker_last_updated_mid = 0;

            this.last_filled_time = null;
            this.last_filled_time_buy = null;
            this.last_filled_time_sell = null;

            this.netExposure = 0;
            this.SoD_baseCcyPos = 0;
            this.SoD_price = 0;
            this.notionalVolume = 0;
            this.posPnL = 0;
            this.tradingPnL = 0;
            this.totalFee = 0;
            this.totalPnL = 0;

            this.oManager = OrderManager.GetInstance();
            this.onFill_latency1 = 0;
            this.onFill_latency2 = 0;
            this.onFill_latency3 = 0;
            this.placingOrderLatencyOnFill = 0;
            this.placingOrderLatencyUpdate = 0;
            this.onFill_count1 = 0;
            this.onFill_count2 = 0;
            this.onFill_count3 = 0;
            this.placingOrdercountOnFill = 0;
            this.placingOrdercountUpdate = 0;
            this.sw = new Stopwatch();
            this.sw2 = new Stopwatch();
            this.sw3 = new Stopwatch();
            this.sw.Start();
            this.sw2.Start();
            this.sw3.Start();
            Thread.Sleep(1);
            this.sw.Stop();
            this.sw.Reset();
            this.sw.Start();
            this.sw2.Stop();
            this.sw2.Reset();
            this.sw2.Start();
            this.sw3.Stop();
            this.sw3.Reset();
            this.sw3.Start();
            Thread.Sleep(1);
            this.sw.Stop();
            this.sw.Reset();
            this.sw.Start();
            this.sw2.Stop();
            this.sw2.Reset();
            this.sw2.Start();
            this.sw3.Stop();
            this.sw3.Reset();
            this.sw3.Start();
            Thread.Sleep(1);
            this.sw.Stop();
            this.sw.Reset();
            this.sw2.Stop();
            this.sw2.Reset();
            this.sw3.Stop();
            this.sw3.Reset();
        }

        public void readStrategyFile(string jsonfilename)
        {
            if(File.Exists(jsonfilename))
            {
                string fileContent = File.ReadAllText(jsonfilename);
                using JsonDocument doc = JsonDocument.Parse(fileContent);
                var root = doc.RootElement;
                this.setStrategy(root);
            }
        }

        public void setStrategy(JsonElement root)
        {
            JsonElement item;

            if (root.TryGetProperty("name", out item))
            {
                this.name = item.GetString();
            }
            else
            {
                addLog("Name is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("baseCcy", out item))
            {
                this.baseCcy = item.GetString();
            }
            else
            {
                addLog("Base Currency is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("quoteCcy", out item))
            {
                this.quoteCcy = item.GetString();
            }
            else
            {
                addLog("Quote Currency is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("taker_market", out item))
            {
                this.taker_market = item.GetString();
            }
            else
            {
                addLog("Taker symbol market is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("maker_market", out item))
            {
                this.maker_market = item.GetString();
            }
            else
            {
                addLog("Taker symbol market is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("markup", out item))
            {
                this.markup = item.GetDecimal();
            }
            else
            {
                addLog("Markup is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("min_markup", out item))
            {
                this.min_markup = item.GetDecimal();
            }
            else
            {
                addLog("Minimum markup is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("baseCcyQuantity", out item))
            {
                this.baseCcyQuantity = item.GetDecimal();
            }
            else
            {
                addLog("The quantity of the base currency is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("ToBsize", out item))
            {
                this.ToBsize = item.GetDecimal();
            }
            else
            {
                addLog("ToB size is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("modThreshold", out item))
            {
                this.modThreshold = item.GetDecimal();
                this.config_modThreshold = this.modThreshold;
            }
            else
            {
                addLog("Threshold of modifying is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("max_skew", out item))
            {
                this.maxSkew = item.GetDecimal();
            }
            else
            {
                addLog("Maximum skew is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("skewThreshold", out item))
            {
                this.skewThreshold = item.GetDecimal();
            }
            else
            {
                addLog("Threshold of skewing is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("oneSideThreshold", out item))
            {
                this.oneSideThreshold = item.GetDecimal();
            }
            else
            {
                addLog("Threshold of one-side quote is not configurated", logType.ERROR);
            }

            //Not mandatory
            if (root.TryGetProperty("orderThrottle", out item))
            {
                this.order_throttle = item.GetDouble();
            }
            else
            {
                this.order_throttle = 0;
            }
            if (root.TryGetProperty("skewWidening", out item))
            {
                this.skewWidening = item.GetDecimal();
            }
            else
            {
                addLog("Skew widening is not configurated. Default value will be set. value:0", logType.WARNING);
                this.skewWidening = 0;
            }
            if (root.TryGetProperty("intervalAfterFill", out item))
            {
                this.intervalAfterFill = item.GetDecimal();
            }
            else
            {
                addLog("Interval after fill is not configurated. Default value will be set. value:1", logType.WARNING);
                this.intervalAfterFill = 1;
            }
            if (root.TryGetProperty("fillPrediction", out item))
            {
                this.predictFill = item.GetBoolean();
            }
            else
            {
                addLog("Fill prediction is not configurated. Default value will be set. value:false", logType.WARNING);
                this.predictFill = false;
            }
            if (root.TryGetProperty("ToBsizeMultiplier", out item))
            {
                this.ToBsizeMultiplier = item.GetDecimal();
            }
            else
            {
                this.ToBsizeMultiplier = 1;
            }
            if (root.TryGetProperty("RVMarkupMultiplier", out item))
            {
                this.RVMarkup_multiplier = item.GetDecimal();
            }
            else
            {
                this.RVMarkup_multiplier = 1;
            }
            if (root.TryGetProperty("MarkupAdjustment", out item))
            {
                this.markupAdjustment = item.GetDecimal();
            }
            else
            {
                this.markupAdjustment = 0;
            }
            if (root.TryGetProperty("MaxBaseMarkup", out item))
            {
                this.max_baseMarkup = item.GetDecimal();
            }
            else
            {
                this.max_baseMarkup = 2000;
            }

            if (root.TryGetProperty("skew_type", out item))
            {
                string type = item.GetString().ToLower();
                if (type == "linear")
                {
                    this.skew_type = skewType.LINEAR;
                }
                else if (type == "step")
                {
                    this.skew_type = skewType.STEP;
                    if (root.TryGetProperty("skew_step", out item))
                    {
                        this.skew_step = item.GetDecimal();
                    }
                    else
                    {
                        addLog("Skew step needs to be configured for step skew. Default value will be set. value:100[dpm]", logType.WARNING);
                        this.skew_step = 100;
                    }
                }
                else
                {
                    addLog("Inappropriate value for skew type. Default value will be set. value:Linear", logType.WARNING);
                    this.skew_type = skewType.LINEAR;
                }
            }
            else
            {
                addLog("Skew type is not configurated. Default value will be set. value:Linear", logType.WARNING);
                this.skew_type = skewType.LINEAR;
            }
            if (root.TryGetProperty("decaying_time", out item))
            {
                this.markup_decay_basetime = item.GetDecimal();
            }
            else
            {
                this.markup_decay_basetime = 999999;
            }
        }
        public void setStrategy(strategySetting setting)
        {
            this.name = setting.name;
            this.baseCcy = setting.baseCcy;
            this.quoteCcy = setting.quoteCcy;
            this.order_throttle = setting.order_throttle;
            this.markup = setting.markup;
            this.min_markup = setting.min_markup;
            this.baseCcyQuantity = setting.baseCcy_quantity;
            this.skewWidening = setting.skew_widening;
            this.ToBsize = setting.ToBsize;
            this.ToBsizeMultiplier = setting.ToBsizeMultiplier;
            this.intervalAfterFill = setting.intervalAfterFill;
            this.modThreshold = setting.modThreshold;
            this.maxSkew = setting.max_skew;
            this.skewThreshold = setting.skewThreshold;
            this.oneSideThreshold = setting.oneSideThreshold;
            this.markup_decay_basetime = setting.decaying_time;
            this.RVMarkup_multiplier = setting.markupMultiplier;
            this.markupAdjustment = setting.markup_adjustment;
            this.max_baseMarkup = setting.maxBaseMarkup;
            this.taker_market = setting.taker_market;
            this.maker_market = setting.maker_market;
            this.predictFill = setting.predictFill;
            this.skew_type = setting.skew_type.ToLower() == "step" ? skewType.STEP : skewType.LINEAR;
            this.skew_step = setting.skew_step;
        }

        public async Task<bool> updateOrders()
        {
            bool ret = true;
            if (this.enabled)
            {
                DateTime current = DateTime.UtcNow;
                if (this.ready == false)
                {
                    if(this.maker.readyToTrade && this.taker.readyToTrade)
                    {
                        addLog("Strategy " + this.name + " ready to trade.");
                        addLog(this.maker.symbol_market + " Latency coef:" + this.maker.coef.ToString() + "  intercept:" + this.maker.intercept);
                        addLog(this.taker.symbol_market +  " Latency coef:" + this.taker.coef.ToString() + "  intercept:" + this.taker.intercept);
                        this.ready = true;
                    }
                    else
                    {
                        return true;
                    }
                }


                bool buyFirst = true;
                this.skew_point = this.skew();
                decimal modTh_buffer = 0;
                //decimal modTh_buffer = 100 * Math.Abs(this.skew_point) / this.maxSkew / 1000000;
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

                decimal markup_bid = this.markup;
                decimal markup_ask = this.markup;

                double taker_VR = this.taker.realized_volatility;
                if(this.taker.prev_RV > 0)
                {
                    taker_VR = 0.7 * taker_VR + 0.3 * this.taker.prev_RV;
                }
                decimal vr_markup = this.markup * (decimal)Math.Exp(taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000 / (double)this.markup) * this.RVMarkup_multiplier + this.markupAdjustment;

                //if (vr_markup > this.markup)
                //{
                //    vr_markup = Math.Ceiling(vr_markup / 50) * 50;
                //}

                this.modThreshold = this.config_modThreshold;

                if (vr_markup >= this.prev_markup)
                {
                    this.base_markup = vr_markup;
                    this.prev_markup = vr_markup;
                    this.prevMarkupTime = DateTime.UtcNow;
                    //if (vr_markup > this.markup)
                    //{
                    //    this.base_markup = vr_markup;
                    //    this.prev_markup = vr_markup;
                    //    this.prevMarkupTime = DateTime.UtcNow;
                    //}
                    //else
                    //{

                    //    this.base_markup = this.markup;
                    //    this.prev_markup = this.markup;
                    //}
                }
                else
                {
                    double elapsedTime = (DateTime.UtcNow - this.prevMarkupTime).TotalMinutes;
                    if (elapsedTime > this.taker.RV_minute * 2)
                    {
                        this.base_markup = vr_markup;
                        this.prev_markup = vr_markup;
                    }
                    else
                    {
                        this.base_markup = this.prev_markup;
                        //markup_bid = this.prev_markup;
                        //markup_ask = this.prev_markup;
                    }
                    //if (this.prev_markup > this.markup)
                    //{
                    //    double elapsedTime = (DateTime.UtcNow - this.prevMarkupTime).TotalMinutes;
                    //    if (elapsedTime > this.taker.RV_minute * 2)
                    //    {
                    //        if (vr_markup > this.markup)
                    //        {
                    //            this.base_markup = vr_markup;
                    //            //markup_bid = vr_markup;
                    //            //markup_ask = vr_markup;
                    //            this.prev_markup = vr_markup;
                    //        }
                    //        else
                    //        {
                    //            this.base_markup = this.markup;
                    //            this.prev_markup = this.markup;
                    //        }
                    //    }
                    //    else
                    //    {
                    //        this.base_markup = this.prev_markup;
                    //        markup_bid = this.prev_markup;
                    //        markup_ask = this.prev_markup;
                    //    }
                    //}
                    //else
                    //{

                    //    this.base_markup = this.markup;
                    //    this.prev_markup = this.markup;
                    //}
                }
                markup_bid = this.base_markup;
                markup_ask = this.base_markup;

                decimal ordersize_bid = this.ToBsize;
                decimal ordersize_ask = this.ToBsize;

                decimal elapsedTimeFromLastfill = (decimal)(DateTime.UtcNow - (this.last_filled_time ?? DateTime.UtcNow)).TotalMinutes - 5;
                if(elapsedTimeFromLastfill < 0)
                {
                    elapsedTimeFromLastfill = 0;
                }
                decimal markup_decay = - elapsedTimeFromLastfill / this.markup_decay_basetime;
                if (this.skew_point > 0)
                {
                    markup_ask += (decimal)(1 + this.skewWidening) * this.skew_point + Math.Max(markup_decay,-1) * this.markup;
                    if(this.skew_point == this.maxSkew)
                    {
                        markup_bid += -this.skew_point + markup_decay * this.markup;
                    }
                    else
                    {
                        markup_bid += -this.skew_point + Math.Max(markup_decay, -1) * this.markup;
                    }
                }
                else if (this.skew_point < 0)
                {
                    markup_bid += - (decimal)(1 + this.skewWidening) * this.skew_point + Math.Max(markup_decay, -1) * this.markup;
                    if(this.skew_point == - this.maxSkew)
                    {
                        markup_ask += this.skew_point + markup_decay * this.markup;
                    }
                    else
                    {
                        markup_ask += this.skew_point + Math.Max(markup_decay, -1) * this.markup;
                    }
                }
                else
                {
                    markup_bid += Math.Max(markup_decay, -1) * this.markup;
                    markup_ask += Math.Max(markup_decay, -1) * this.markup;

                    decimal temp_askSize = ordersize_ask * this.ToBsizeMultiplier;
                    if(this.maker.baseBalance.total - temp_askSize >= this.baseCcyQuantity * ((decimal)0.5 - this.skewThreshold / 200))
                    {
                        ordersize_ask = temp_askSize;
                    }

                    decimal temp_bidSize = ordersize_bid * this.ToBsizeMultiplier;
                    if (this.maker.baseBalance.total + temp_bidSize <= this.baseCcyQuantity * ((decimal)0.5 + this.skewThreshold / 200))
                    {
                        ordersize_bid = temp_bidSize;
                    }
                }
                this.temp_markup_ask = markup_ask;
                this.temp_markup_bid = markup_bid;

                if(this.base_markup > this.max_baseMarkup || this.base_markup < 0)
                {
                    bid_price = 0;
                    ask_price = 0;
                }
                else
                {
                    bid_price *= (1 - markup_bid / 1000000);
                    ask_price *= (1 + markup_ask / 1000000);
                }

                while (Interlocked.CompareExchange(ref this.maker.quotes_lock, 1, 0) != 0)
                {
                }
                decimal maker_bid = this.maker.bestbid.Item1;
                decimal maker_ask = this.maker.bestask.Item1;

                Volatile.Write(ref this.maker.quotes_lock, 0);
                decimal maker_adjustedbid = this.maker.getPriceAfterSweep(orderSide.Buy, ordersize_bid);
                decimal maker_adjustedask = this.maker.getPriceAfterSweep(orderSide.Sell, ordersize_ask);

                if(bid_price > 0)
                {
                    if (bid_price > maker_adjustedbid + this.maker.price_unit)
                    {
                        if (maker_adjustedbid == live_bidprice)
                        {
                            bid_price = maker_adjustedbid;
                        }
                        else
                        {
                            bid_price = maker_adjustedbid + this.maker.price_unit;
                        }

                        if (bid_price >= maker_ask)
                        {
                            bid_price = maker_bid;
                        }
                    }

                    if (bid_price > min_markup_bid)
                    {
                        bid_price = min_markup_bid;
                    }
                }
                
                if(ask_price > 0)
                {
                    if (ask_price < maker_adjustedask - this.maker.price_unit)
                    {
                        if (maker_adjustedask == live_askprice)
                        {
                            ask_price = maker_adjustedask;
                        }
                        else
                        {
                            ask_price = maker_adjustedask - this.maker.price_unit;
                        }

                        if (ask_price <= maker_bid)
                        {
                            ask_price = maker_ask;
                        }
                    }

                    if (ask_price < min_markup_ask)
                    {
                        ask_price = min_markup_ask;
                    }
                }
                

                bid_price = Math.Floor(bid_price / this.maker.price_unit) * this.maker.price_unit;
                ask_price = Math.Ceiling(ask_price / this.maker.price_unit) * this.maker.price_unit;

                if (ordersize_bid * (1 + this.taker.taker_fee) * 2 > this.taker.baseBalance.available)
                {
                    bid_price = 0;
                }
                if (taker_ask * ordersize_ask * (1 + this.taker.taker_fee) * 2 > this.taker.quoteBalance.available)
                {
                    ask_price = 0;
                }

                if(bid_price < 0)
                {
                    addLog($"Invalid bid price. bid:{bid_price} skew:{this.skew_point} base markup:{this.base_markup}", logType.WARNING);
                    bid_price = 0;
                }
                else if(bid_price > 0 && maker_bid > 0 && (bid_price / maker_bid > (decimal)1.1 || bid_price / maker_bid < (decimal)0.9))
                {
                    addLog($"The bid price is too far from the market price. bid:{bid_price} skew:{this.skew_point} base markup:{this.base_markup}", logType.WARNING);
                    bid_price = 0;
                }

                if (ask_price < 0)
                {
                    addLog($"Invalid ask price. ask:{ask_price} skew:{this.skew_point} base markup:{this.base_markup}", logType.WARNING);
                    ask_price = 0;
                }
                else if (ask_price > 0 && maker_ask > 0 && (ask_price / maker_ask > (decimal)1.1 || ask_price / maker_ask < (decimal)0.9))
                {
                    addLog($"The ask price is too far from the market price. ask:{ask_price} skew:{this.skew_point} base markup:{this.base_markup}", logType.WARNING);
                    ask_price = 0;
                }

                if(this.base_markup / 5 > this.modThreshold * 1000000)
                {
                    modTh_buffer = this.base_markup / 5 / 1000000 - this.modThreshold;
                }

                bool isPriceChanged = this.checkPriceChange("taker",modTh_buffer);

                while (Interlocked.CompareExchange(ref this.updating, 1, 0) != 0)
                {

                }

                DataSpotOrderUpdate ord;
                DateTime currentTime = DateTime.UtcNow;
                if (this.live_buyorder_id != "")
                {
                    if (this.oManager.orders.ContainsKey(this.live_buyorder_id))
                    {
                        ord = this.oManager.orders[this.live_buyorder_id];
                        switch (ord.status)
                        {
                            case orderStatus.NONE:
                                addLog("Something wrong. " + ord.ToString(), logType.WARNING);
                                this.live_buyorder_id = "";
                                this.live_bidprice = 0;
                                break;
                            case orderStatus.Filled:
                            case orderStatus.Canceled:
                            case orderStatus.INVALID:
                            case orderStatus.WaitCancel:
                                this.live_buyorder_id = "";
                                this.live_bidprice = 0;
                                break;
                        }
                        if (ord.status == orderStatus.Open && !this.oManager.live_orders.ContainsKey(ord.internal_order_id))
                        {
                            addLog("The order status is open but doesn't exist in liveorders", logType.WARNING);
                            addLog(ord.ToString(), logType.WARNING);
                        }
                    }
                    else if(currentTime - this.live_buyorder_time > TimeSpan.FromSeconds(10))
                    {
                        addLog("Strategy buy order not found. order_id:" + this.live_buyorder_id);
                        //await RefreshLiveOrders();
                        ret = false;
                        this.live_buyorder_id = "";
                        this.live_bidprice = 0;
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
                                addLog("Something wrong. " + ord.ToString(), logType.WARNING);
                                this.live_sellorder_id = "";
                                this.live_askprice = 0;
                                break;
                            case orderStatus.Filled:
                            case orderStatus.Canceled:
                            case orderStatus.INVALID:
                            case orderStatus.WaitCancel:
                                this.live_sellorder_id = "";
                                this.live_askprice = 0;
                                break;
                        }
                        if(ord.status == orderStatus.Open && !this.oManager.live_orders.ContainsKey(ord.internal_order_id))
                        {
                            addLog("The order status is open but doesn't exist in liveorders", logType.WARNING);
                            addLog(ord.ToString(), logType.WARNING);
                        }
                    }
                    else if (currentTime - this.live_sellorder_time > TimeSpan.FromSeconds(10))
                    {
                        addLog("Strategy sell order not found. order_id:" + this.live_sellorder_id);
                        //await RefreshLiveOrders();
                        ret = false;
                        this.live_sellorder_id = "";
                        this.live_askprice = 0;
                    }
                }
                else
                {
                    this.live_askprice = 0;
                }


                if (this.maker.baseBalance.total - this.maker.baseBalance.inuse < ordersize_ask)
                {
                    ask_price = 0;
                }
                if (this.maker.quoteBalance.total - this.maker.quoteBalance.inuse < ordersize_bid * bid_price)
                {
                    bid_price = 0;
                }

                await this.checkLiveOrders();

                bool newBuyOrder = false;
                bool newSellOrder = false;

                List<string> cancelling_ord = new List<string>();
                if (this.oManager.orders.ContainsKey(this.live_buyorder_id))
                {
                    ord = this.oManager.orders[this.live_buyorder_id];
                    if (ord.status == orderStatus.Open && (bid_price == 0 || (this.maker.baseBalance.total > this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200))))
                    {
                        cancelling_ord.Add(this.live_buyorder_id);
                        this.cancelling_qty_buy += ord.order_quantity - ord.filled_quantity;
                        this.live_buyorder_id = "";
                        this.live_bidprice = 0;
                    }
                    else if ((isPriceChanged || bid_price > this.live_bidprice) && ord.status == orderStatus.Open && this.live_bidprice != bid_price)
                    {
                        cancelling_ord.Add(this.live_buyorder_id);
                        this.cancelling_qty_buy += ord.order_quantity - ord.filled_quantity;
                        this.live_buyorder_id = "";
                        this.live_bidprice = 0;
                        newBuyOrder = true;
                    }
                }
                else if(this.live_buyorder_id == "")
                {
                    if (bid_price == 0 || (this.maker.baseBalance.total > this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200)))
                    {

                    }
                    else if (this.last_filled_time_buy == null || (decimal)(DateTime.UtcNow - this.last_filled_time_buy).Value.TotalSeconds > this.intervalAfterFill)
                    {
                        newBuyOrder = true;
                    }
                }
                if (this.oManager.orders.ContainsKey(this.live_sellorder_id))
                {
                    ord = this.oManager.orders[this.live_sellorder_id];
                    if (ord.status == orderStatus.Open && (ask_price == 0 || (this.maker.baseBalance.total < this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200))))
                    {
                        cancelling_ord.Add(this.live_sellorder_id);
                        this.cancelling_qty_sell += ord.order_quantity - ord.filled_quantity;
                        this.live_sellorder_id = "";
                        this.live_askprice = 0;
                    }
                    else if ((isPriceChanged || ask_price < this.live_askprice) && ord.status == orderStatus.Open && this.live_askprice != ask_price)
                    {
                        cancelling_ord.Add(this.live_sellorder_id);
                        this.cancelling_qty_sell += ord.order_quantity - ord.filled_quantity;
                        this.live_sellorder_id = "";
                        this.live_askprice = 0;
                        newSellOrder = true;
                    }
                }
                else if (this.live_sellorder_id == "")
                {
                    if (ask_price == 0 || (this.maker.baseBalance.total < this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200)))
                    {

                    }
                    else if (this.last_filled_time_sell == null || (decimal)(DateTime.UtcNow - this.last_filled_time_sell).Value.TotalSeconds > this.intervalAfterFill)
                    {
                        newSellOrder = true;
                    }
                }

                this.oManager.placeCancelSpotOrders(this.maker, cancelling_ord);

                if ((current - this.live_buyorder_time).TotalMilliseconds < this.order_throttle || (current - this.live_sellorder_time).TotalMilliseconds < this.order_throttle)
                {
                    //Only do cancelling orders to avoid excessive orders
                    //addLog("Order throttle triggered. Buy Time:" + (current - this.live_buyorder_time).TotalMilliseconds.ToString("N0") + "   Sell Time:" + (current - this.live_sellorder_time).TotalMilliseconds.ToString("N0"));
                    Volatile.Write(ref this.updating, 0); 
                    return ret;
                }


                if (!this.checkLatency(this.taker, 1000))
                {
                    Volatile.Write(ref this.updating, 0);
                    return ret;
                }

                this.cancelling_qty_sell = 0;
                this.cancelling_qty_buy = 0;

                if (newBuyOrder && newSellOrder)//Both orders exist
                {
                    if (ask_price - maker_ask > maker_bid - bid_price)
                    {
                        buyFirst = true;
                    }
                    else
                    {
                        buyFirst = false;
                    }
                }
                else if(newBuyOrder)//Only buy order exists
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
                        sw3.Start();
                        this.live_buyorder_id = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, ordersize_bid, bid_price, null, true, false);
                        sw3.Stop();
                        this.placingOrderLatencyUpdate = (this.placingOrderLatencyUpdate * 1000 * this.placingOrdercountUpdate + this.sw3.Elapsed.TotalNanoseconds) / (this.placingOrdercountUpdate + 1) / 1000;
                        ++(this.placingOrdercountUpdate);
                        sw3.Reset();
                        this.live_bidprice = bid_price;
                        this.stg_orders.Add(this.live_buyorder_id);
                        this.live_buyorder_time = current;
                    }
                    if(newSellOrder)
                    {
                        this.sw3.Start();
                        this.live_sellorder_id = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, ordersize_ask, ask_price, null, true, false);
                        this.sw3.Stop();
                        this.placingOrderLatencyUpdate = (this.placingOrderLatencyUpdate * 1000 * this.placingOrdercountUpdate + this.sw3.Elapsed.TotalNanoseconds) / (this.placingOrdercountUpdate + 1) / 1000;
                        ++(this.placingOrdercountUpdate);
                        this.sw3.Reset();
                        this.live_askprice = ask_price;
                        this.stg_orders.Add(this.live_sellorder_id);
                        this.live_sellorder_time = current;
                    }
                }
                else
                {
                    if (newSellOrder)
                    {
                        this.sw3.Start();
                        this.live_sellorder_id = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, ordersize_ask, ask_price, null, true, false);
                        this.sw3.Stop();
                        this.placingOrderLatencyUpdate = (this.placingOrderLatencyUpdate * 1000 * this.placingOrdercountUpdate + this.sw3.Elapsed.TotalNanoseconds) / (this.placingOrdercountUpdate + 1) / 1000;
                        ++(this.placingOrdercountUpdate);
                        this.sw3.Reset();
                        this.live_askprice = ask_price;
                        this.stg_orders.Add(this.live_sellorder_id);
                        this.live_sellorder_time = current;
                    }
                    if (newBuyOrder)
                    {
                        this.sw3.Start();
                        this.live_buyorder_id = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, ordersize_bid, bid_price, null, true, false);
                        this.sw3.Stop();
                        this.placingOrderLatencyUpdate = (this.placingOrderLatencyUpdate * 1000 * this.placingOrdercountUpdate + this.sw3.Elapsed.TotalNanoseconds) / (this.placingOrdercountUpdate + 1) / 1000;
                        ++(this.placingOrdercountUpdate);
                        this.sw3.Reset();
                        this.live_bidprice = bid_price;
                        this.stg_orders.Add(this.live_buyorder_id);
                        this.live_buyorder_time = current;
                    }
                }
                Volatile.Write(ref this.updating, 0);
            }
            return ret;
        }

        public bool checkLatency(Instrument ins,double threshold = 1000)
        {
            double theoLatency;
            double currentLatency;

            bool ret = true; 

            theoLatency = ins.getTheoLatency(this.taker.last_quote_updated_time.Value);
            currentLatency = (ins.last_quote_updated_time.Value - ins.quoteTime).TotalMilliseconds;

            if(ins.market == "coincheck")
            {
                if((ins.last_quote_updated_time.Value - ins.quoteTime).TotalMilliseconds > threshold + 500)
                {
                    addLog("Observing large latency on " + ins.symbol_market + ".   SeqNo:" + ins.quoteSeqNo.ToString() +  "  Latency:" + (currentLatency).ToString("N3"), logType.WARNING);
                    ret = false;
                }
            }
            else if (currentLatency - theoLatency > threshold)
            {
                addLog("Observing large latency on " + ins.symbol_market + ".   SeqNo:" + ins.quoteSeqNo.ToString() + " Latency:" + (currentLatency - theoLatency).ToString("N3"), logType.WARNING);
                addLog("Raw Latency:" + (currentLatency).ToString("N3"), logType.WARNING);
                ret = false;
            }
            return ret;
        }
        public bool checkPriceChange(string takerORmaker = "",decimal buf = 0)
        {
            bool taker_check = (this.taker_last_updated_mid == 0 || this.taker.adj_mid / this.taker_last_updated_mid > 1 + this.modThreshold + buf || this.taker.adj_mid / this.taker_last_updated_mid < 1 - this.modThreshold - buf);
            bool maker_check = (this.maker_last_updated_mid == 0 || this.maker.adj_mid / this.maker_last_updated_mid > 1 + this.modThreshold + buf || this.maker.adj_mid / this.maker_last_updated_mid < 1 - this.modThreshold - buf);
            if(taker_check)
            {
                this.taker_last_updated_mid = this.taker.adj_mid;
            }
            if(maker_check)
            {
                this.maker_last_updated_mid = this.maker.adj_mid;
            }
            if(takerORmaker.ToLower() == "taker")
            {
                return taker_check;
            }
            else if(takerORmaker.ToLower() == "maker")
            {
                return maker_check;
            }
            else
            {
                return (taker_check || maker_check);
            }
        }

        public async Task adjustPosition()
        {
            decimal diff_amount = this.maker.baseBalance.total + this.taker.baseBalance.total - this.baseCcyQuantity;
            addLog("Strategy[" + this.name + "] Adjusting the position diff_amount:" + diff_amount.ToString("N" + this.taker.quantity_scale));
            orderSide side = orderSide.Sell;
            if(diff_amount < 0)
            {
                diff_amount *= -1;
                side = orderSide.Buy;
            }
            diff_amount = Math.Round(diff_amount / this.taker.quantity_unit) * this.taker.quantity_unit; 

            if(diff_amount > 0)
            {
                this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, diff_amount, 0, null, true);
            }
        }

        public async Task checkLiveOrders()
        {
            List<string> maker_orders = new List<string>();
            List<string> taker_orders = new List<string>();

            while (Interlocked.CompareExchange(ref this.oManager.order_lock, 1, 0) != 0)
            {
            }
            foreach (var ord in this.oManager.live_orders)
            {
                if (ord.Key != this.live_buyorder_id && ord.Key != this.live_sellorder_id && ord.Value.status == orderStatus.Open)
                {
                    if (this.maker.symbol_market == ord.Value.symbol_market)
                    {
                        maker_orders.Add(ord.Key);
                        switch (ord.Value.side)
                        {
                            case orderSide.Buy:
                                this.cancelling_qty_buy += ord.Value.order_quantity - ord.Value.filled_quantity;
                                break;
                            case orderSide.Sell:
                                this.cancelling_qty_sell += ord.Value.order_quantity - ord.Value.filled_quantity;
                                break;
                        }
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
            if (maker_orders.Count > 0)
            {
                this.oManager.placeCancelSpotOrders(this.maker, maker_orders, true);
            }
            if (taker_orders.Count > 0)
            {
                this.oManager.placeCancelSpotOrders(this.taker, taker_orders, true);
            }
        }
        public decimal skew()
        {
            decimal skew_point = 0;
            if (this.maker.baseBalance.total > this.baseCcyQuantity * ((decimal)0.5 + this.skewThreshold / 200))
            {
                skew_point = -this.maxSkew * (this.maker.baseBalance.total - this.baseCcyQuantity * ((decimal)0.5 + this.skewThreshold / 200)) / (this.baseCcyQuantity * ((decimal)0.5 + this.oneSideThreshold / 200) - this.baseCcyQuantity * ((decimal)0.5 + this.skewThreshold / 200));
                if (skew_point < -this.maxSkew)
                {
                    skew_point = -this.maxSkew;
                }
            }
            else if (this.maker.baseBalance.total < this.baseCcyQuantity * ((decimal)0.5 - this.skewThreshold / 200))
            {
                skew_point = this.maxSkew * (this.baseCcyQuantity * ((decimal)0.5 - this.skewThreshold / 200) - this.maker.baseBalance.total) / (this.baseCcyQuantity * ((decimal)0.5 - this.skewThreshold / 200) - this.baseCcyQuantity * ((decimal)0.5 - this.oneSideThreshold / 200));
                if (skew_point > this.maxSkew)
                {
                    skew_point = this.maxSkew;
                }
            }
            if (this.skew_type == skewType.STEP)
            {
                skew_point = Math.Floor(skew_point / this.skew_step) * this.skew_step;
            }
            
            return skew_point;
        }

        public void onTrades(DataTrade trade)
        {
            if(this.enabled && this.abook && this.predictFill && trade.symbol + "@" + trade.market == this.maker.symbol_market)
            {
                if (!this.oManager.ready)
                {
                    int j = 0;
                    while (!this.oManager.ready)
                    {
                        Thread.Sleep(1);
                        ++j;
                        if (j > 10000)
                        {
                            this.addLog("Order Manager is not ready", logType.ERROR);
                            return;
                        }
                    }
                }
                int i = 0;
                while (Interlocked.CompareExchange(ref this.updating, 2, 0) != 0)
                {
                    ++i;
                    if(i > 1000000000)
                    {
                        addLog("Locked in updating. value:" + this.updating.ToString());
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
                                //if (this.executed_Orders_old.ContainsKey(ord.internal_order_id))
                                if (this.executed_OrderIds.ContainsKey(ord.internal_order_id))
                                    {
                                    //Do nothing
                                }
                                else
                                {
                                    this.live_sellorder_id = "";
                                    decimal filled_quantity = ord.order_quantity - ord.filled_quantity;
                                    decimal diff_amount = this.maker.baseBalance.total + this.taker.baseBalance.total - this.baseCcyQuantity;
                                    if (DateTime.UtcNow - this.lastPosAdjustment > TimeSpan.FromSeconds(10))
                                    {
                                        this.lastPosAdjustment = DateTime.UtcNow;
                                        filled_quantity -= diff_amount;
                                    }

                                    if (filled_quantity > 0)
                                    {
                                        this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, null, true);
                                    }
                                    this.last_filled_time_sell = DateTime.UtcNow;
                                    this.last_filled_time = this.last_filled_time_sell;
                                    //this.executed_Orders_old[ord.internal_order_id] = ord;
                                    this.executed_OrderIds[ord.internal_order_id] = fillType.onTrade;
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
                                //if (this.executed_Orders_old.ContainsKey(ord.internal_order_id))
                                if (this.executed_OrderIds.ContainsKey(ord.internal_order_id))
                                {
                                    //Do nothing
                                }
                                else
                                {
                                    this.live_buyorder_id = "";
                                    decimal filled_quantity = ord.order_quantity - ord.filled_quantity;
                                    decimal diff_amount = this.maker.baseBalance.total + this.taker.baseBalance.total - this.baseCcyQuantity;
                                    if (DateTime.UtcNow - this.lastPosAdjustment > TimeSpan.FromSeconds(10))
                                    {
                                        this.lastPosAdjustment = DateTime.UtcNow;
                                        filled_quantity += diff_amount;
                                    }
                                    if (filled_quantity > 0)
                                    {
                                        this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, null, true);
                                    }
                                    this.last_filled_time_buy = DateTime.UtcNow;
                                    this.last_filled_time = this.last_filled_time_buy;
                                    //this.executed_Orders_old[ord.internal_order_id] = ord;
                                    this.executed_OrderIds[ord.internal_order_id] = fillType.onTrade;
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
            if(this.enabled && this.abook && this.predictFill && quote.symbol + "@" + quote.market == this.maker.symbol_market)
            {
                if (!this.oManager.ready)
                {
                    int i = 0;
                    while (!this.oManager.ready)
                    {
                        Thread.Sleep(1);
                        ++i;
                        if (i > 10000)
                        {
                            this.addLog("Order Manager is not ready", logType.ERROR);
                            return;
                        }
                    }
                }
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
                        //if (this.executed_Orders_old.ContainsKey(ord.internal_order_id))
                        if (this.executed_OrderIds.ContainsKey(ord.internal_order_id))
                        {
                            //Do nothing
                        }
                        else
                        {
                            decimal filled_quantity = ord.order_quantity - ord.filled_quantity;
                            if(DateTime.UtcNow - this.lastPosAdjustment > TimeSpan.FromSeconds(10))
                            {
                                this.lastPosAdjustment = DateTime.UtcNow;
                                filled_quantity -= diff_amount;
                            }
                            
                            if(filled_quantity > 0)
                            {
                                this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, null, true);
                            }
                            this.last_filled_time_sell = DateTime.UtcNow;
                            this.last_filled_time = this.last_filled_time_sell;
                            //this.executed_Orders_old[ord.internal_order_id] = ord;
                            this.executed_OrderIds[ord.internal_order_id] = fillType.onQuotes;
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
                        //if (this.executed_Orders_old.ContainsKey(ord.internal_order_id))
                        if (this.executed_OrderIds.ContainsKey(ord.internal_order_id))
                        {
                            //Do nothing
                        }
                        else
                        {
                            decimal filled_quantity = ord.order_quantity - ord.filled_quantity;
                            if (DateTime.UtcNow - this.lastPosAdjustment > TimeSpan.FromSeconds(10))
                            {
                                this.lastPosAdjustment = DateTime.UtcNow;
                                filled_quantity += diff_amount;
                            }
                            this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, null, true);
                            this.last_filled_time_buy = DateTime.UtcNow;
                            this.last_filled_time = this.last_filled_time_buy;
                            //this.executed_Orders_old[ord.internal_order_id] = ord;
                            this.executed_OrderIds[ord.internal_order_id] = fillType.onQuotes;
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
            if (this.enabled && this.abook && this.predictFill)
            {
                if(ord.status == orderStatus.Filled)
                {
                    if (this.stg_orders.Contains(ord.internal_order_id) == false && this.stg_orders.Contains(ord.market + ord.order_id))
                    {
                        if(ord.symbol_market == this.maker.symbol_market)
                        {
                            this.stg_orders.Add(ord.market + ord.order_id);
                        }
                        else
                        {
                            return;
                        }
                    }
                    decimal diff_amount = this.maker.baseBalance.total + this.taker.baseBalance.total - this.baseCcyQuantity;

                    if (!this.oManager.ready)
                    {
                        int i = 0;
                        while (!this.oManager.ready)
                        {
                            Thread.Sleep(1);
                            ++i;
                            if (i > 10000)
                            {
                                this.addLog("Order Manager is not ready", logType.ERROR);
                                return;
                            }
                        }
                    }

                    while (Interlocked.CompareExchange(ref this.fill_lock, 1, 0) != 0)
                    {

                    }
                    //if (this.executed_Orders_old.ContainsKey(ord.internal_order_id) || this.executed_Orders_old.ContainsKey(ord.market + ord.order_id))
                    if (this.executed_OrderIds.ContainsKey(ord.internal_order_id) || this.executed_OrderIds.ContainsKey(ord.market + ord.order_id))
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
                        if(DateTime.UtcNow - this.lastPosAdjustment > TimeSpan.FromSeconds(10))
                        {
                            this.lastPosAdjustment = DateTime.UtcNow;
                            switch (ord.side)
                            {
                                case orderSide.Buy://taker order will be sell
                                    filled_quantity += diff_amount;
                                    break;
                                case orderSide.Sell:
                                    filled_quantity -= diff_amount;
                                    break;

                            }
                        }

                        switch (ord.side)
                        {
                            case orderSide.Buy:
                                if (filled_quantity > 0)
                                {
                                    this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, null, true);
                                }
                                this.last_filled_time_buy = DateTime.UtcNow;
                                this.last_filled_time = this.last_filled_time_buy;
                                break;
                            case orderSide.Sell:

                                if (filled_quantity > 0)
                                {
                                    this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, null, true);
                                }
                                this.last_filled_time_sell = DateTime.UtcNow;
                                this.last_filled_time = this.last_filled_time_sell;
                                break;
                        }
                        //this.executed_Orders_old[ord.internal_order_id] = ord;
                        this.executed_OrderIds[ord.internal_order_id] = fillType.onOrderUpdate;
                        ord.msg += "  onOrdUpdate at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                        addLog(ord.ToString());
                    }
                    Volatile.Write(ref this.fill_lock, 0);
                }
            }
        }

        public async Task onFill(DataFill fill)
        {
            if (/*this.enabled && */this.abook)//onFill should work even if the strategy disabled
            {
                if (!this.oManager.ready)
                {
                    int i = 0;
                    while (!this.oManager.ready)
                    {
                        Thread.Sleep(1);
                        ++i;
                        if (i > 10000)
                        {
                            this.addLog("Order Manager is not ready", logType.ERROR);
                            return;
                        }
                    }
                }
                this.sw.Start();
                decimal diff_amount = this.maker.baseBalance.total + this.taker.baseBalance.total - this.baseCcyQuantity; 
                decimal filled_quantity = fill.quantity;
                if (filled_quantity > this.ToBsize * 2)
                {
                    filled_quantity = this.ToBsize * 2;
                }
                DataSpotOrderUpdate ord;
                if (fill.market != this.maker.market)
                {
                    this.sw.Stop();
                    this.sw.Reset();
                    return;
                }
                if (this.stg_orders.Contains(fill.internal_order_id) == false && this.stg_orders.Contains(fill.market + fill.order_id))
                {
                    //Even if the order id is not registered, if the market and symbol meets the strategy handle the fill.
                    //Register the market + order_id
                    if(fill.symbol_market == this.maker.symbol_market)
                    {
                        this.stg_orders.Add(fill.market + fill.order_id);
                    }
                    else
                    {
                        this.sw.Stop();
                        this.sw.Reset();
                        return;
                    }
                    //this.addLog("Unknown order order:" + fill.ToString());
                    //return;
                }

                if (this.predictFill)
                {
                    this.sw.Stop();
                    this.onFill_latency1 = (this.sw.Elapsed.TotalNanoseconds + this.onFill_latency1 * 1000 * this.onFill_count1) / (this.onFill_count1 + 1) / 1000;
                    ++(this.onFill_count1);
                    this.sw.Restart();
                    while (Interlocked.CompareExchange(ref this.fill_lock, 1, 0) != 0)
                    {

                    }
                    if ((this.executed_OrderIds.ContainsKey(fill.internal_order_id) && this.executed_OrderIds[fill.internal_order_id] != fillType.onFill) 
                        || (this.executed_OrderIds.ContainsKey(fill.market + fill.order_id) && this.executed_OrderIds[fill.market + fill.order_id] != fillType.onFill))
                    {
                        //Do nothing
                    }
                    else
                    {
                        if(DateTime.UtcNow - this.lastPosAdjustment > TimeSpan.FromSeconds(10))
                        {
                            this.lastPosAdjustment = DateTime.UtcNow;
                            switch (fill.side)
                            {
                                case orderSide.Buy://taker order will be sell
                                    filled_quantity += diff_amount;
                                    break;
                                case orderSide.Sell:
                                    filled_quantity -= diff_amount;
                                    break;

                            }
                        }
                        
                        filled_quantity = Math.Round(filled_quantity / this.taker.quantity_unit) * this.taker.quantity_unit;

                        this.sw.Stop();
                        this.onFill_latency2 = (this.sw.Elapsed.TotalNanoseconds + this.onFill_latency2 * 1000 * this.onFill_count2) / (this.onFill_count2 + 1) / 1000;
                        ++(this.onFill_count2);
                        if (filled_quantity > 0)
                        {
                            this.sw.Restart();
                            switch (fill.side)
                            {
                                case orderSide.Buy:
                                    this.sw2.Start();
                                    await this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, null,true,false);
                                    this.sw2.Stop();
                                    this.placingOrderLatencyOnFill = (this.sw2.Elapsed.TotalNanoseconds + this.placingOrderLatencyOnFill * 1000 * this.placingOrdercountOnFill) / (this.placingOrdercountOnFill+ 1) / 1000;
                                    ++(this.placingOrdercountOnFill);
                                    this.sw2.Reset();
                                    if (this.oManager.orders.ContainsKey(fill.internal_order_id))
                                    {
                                        ord = this.oManager.orders[fill.internal_order_id];
                                        if (ord.order_quantity - ord.filled_quantity <= fill.quantity || ord.status == orderStatus.Filled)
                                        {
                                            //this.executed_Orders_old[ord.internal_order_id] = ord;
                                            ord.msg += "  onFill at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat) + fill.internal_order_id;
                                            addLog(ord.ToString());
                                            fill.msg = ord.msg;
                                            this.last_filled_time_buy = DateTime.UtcNow;
                                            this.last_filled_time = this.last_filled_time_buy;
                                        }
                                    }
                                    else
                                    {
                                        this.addLog("[OnFill]Order Not Found:" + fill.ToString(), Enums.logType.WARNING);
                                        if (fill.quantity == this.ToBsize)
                                        {
                                            //this.executed_Orders_old[fill.internal_order_id] = null;
                                            this.last_filled_time_buy = DateTime.UtcNow;
                                            this.last_filled_time = this.last_filled_time_buy;
                                            fill.msg = "  onFill at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat) + fill.internal_order_id;
                                        }
                                    }
                                    break;
                                case orderSide.Sell:
                                    this.sw2.Start();
                                    await this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, null, true,false);
                                    this.sw2.Stop();
                                    this.placingOrderLatencyOnFill = (this.sw2.Elapsed.TotalNanoseconds + this.placingOrderLatencyOnFill * 1000 * this.placingOrdercountOnFill) / (this.placingOrdercountOnFill + 1) / 1000;
                                    ++(this.placingOrdercountOnFill);
                                    this.sw2.Reset();
                                    if (this.oManager.orders.ContainsKey(fill.internal_order_id))
                                    {
                                        ord = this.oManager.orders[fill.internal_order_id];

                                        if (ord.order_quantity - ord.filled_quantity <= fill.quantity || ord.status == orderStatus.Filled)
                                        {
                                            //this.executed_Orders_old[ord.internal_order_id] = ord;
                                            this.last_filled_time_sell = DateTime.UtcNow;
                                            this.last_filled_time = this.last_filled_time_sell;
                                            ord.msg += "  onFill at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat) + fill.internal_order_id;
                                            addLog(ord.ToString());
                                            fill.msg = ord.msg;
                                        }
                                    }
                                    else
                                    {
                                        this.addLog("[OnFill]Order Not Found:" + fill.ToString(),Enums.logType.WARNING);
                                        if(fill.quantity == this.ToBsize)
                                        {
                                            //this.executed_Orders_old[fill.internal_order_id] = null;
                                            this.last_filled_time_sell = DateTime.UtcNow;
                                            this.last_filled_time = this.last_filled_time_sell;
                                            fill.msg = "  onFill at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat) + fill.internal_order_id;
                                        }
                                    }
                                    break;
                            }
                            //Once onFill triggered, onFill has the responsibility to hedge the entire order.
                            this.executed_OrderIds[fill.internal_order_id] = fillType.onFill;

                            this.sw.Stop();
                            this.onFill_latency3 = (this.sw.Elapsed.TotalNanoseconds + this.onFill_latency3 * 1000 * this.onFill_count3) / (this.onFill_count3 + 1) / 1000;
                            ++(this.onFill_count3);
                        }
                        
                    }
                    Volatile.Write(ref this.fill_lock, 0);
                }
                else
                {
                    if (DateTime.UtcNow - this.lastPosAdjustment > TimeSpan.FromSeconds(10))
                    {
                        this.lastPosAdjustment = DateTime.UtcNow;
                        switch (fill.side)
                        {
                            case orderSide.Buy://taker order will be sell
                                filled_quantity += diff_amount;
                                break;
                            case orderSide.Sell:
                                filled_quantity -= diff_amount;
                                break;

                        }
                    }

                    filled_quantity = Math.Round(filled_quantity / this.taker.quantity_unit) * this.taker.quantity_unit;
                    if (filled_quantity > 0)
                    {
                        switch (fill.side)
                        {
                            case orderSide.Buy:
                                this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, null, true);
                                if (this.oManager.orders.ContainsKey(fill.internal_order_id))
                                {
                                    ord = this.oManager.orders[fill.internal_order_id];
                                    if (ord.order_quantity - ord.filled_quantity <= fill.quantity || ord.status == orderStatus.Filled)
                                    {
                                        this.last_filled_time_buy = DateTime.UtcNow;
                                        this.last_filled_time = this.last_filled_time_buy;
                                    }
                                }
                                else
                                {
                                    this.addLog("[OnFill]Order Not Found:" + fill.ToString());
                                }
                                break;
                            case orderSide.Sell:
                                this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, null, true);
                                if (this.oManager.orders.ContainsKey(fill.internal_order_id))
                                {
                                    ord = this.oManager.orders[fill.internal_order_id];
                                    if (ord.order_quantity - ord.filled_quantity <= fill.quantity || ord.status == orderStatus.Filled)
                                    {
                                        this.last_filled_time_sell = DateTime.UtcNow;
                                        this.last_filled_time = this.last_filled_time_sell;
                                    }
                                }
                                else
                                {
                                    this.addLog("[OnFill]Order Not Found:" + fill.ToString());
                                }
                                break;
                        }
                    }
                }
                this.sw.Reset();
            }
        }
        public void addLog(string line, Enums.logType logtype = Enums.logType.INFO)
        {
            this._addLog("[Strategy]" + line, logtype);
        }
    }
}
