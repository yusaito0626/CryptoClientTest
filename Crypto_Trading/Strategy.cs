using Binance.Net.Enums;
using Binance.Net.Objects.Models.Spot.Loans;
using Crypto_Clients;
using CryptoExchange.Net;
using Discord;
using Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reactive.Concurrency;
using System.Runtime.CompilerServices;
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
        private bool ready;

        public double order_throttle;

        public decimal const_markup;
        public decimal min_markup;
        public decimal maxMakerPosition;
        public decimal ToBsize;
        public decimal ToBsizeMultiplier;
        public decimal rv_penalty_multiplier = 1;
        public double rv_base_param;

        public decimal targetMakerPosition;//The position stays between target + max and target - max        

        public decimal intervalAfterFill;
        public decimal modThreshold;
        public decimal config_modThreshold;

        public skewType skew_type;
        public decimal skew_step;
        public decimal maxSkew;
        public decimal skewThreshold;
        public decimal oneSideThreshold;
        public decimal skewWidening;
        public decimal max_baseMarkup = 2000;

        public bool maker_margin_trade = true;
        //public bool taker_margin_trade = false;
        public decimal max_maker_levarage = 2;
        //public decimal max_taker_levarage = 2;

        public TimeSpan onTrade_timeBuf = TimeSpan.FromMilliseconds(100);

        public bool abook;

        private myLock fill_lock = new myLock();
        public Dictionary<string, DataSpotOrderUpdate> executed_Orders_old;
        public Dictionary<string, fillType> executed_OrderIds;

        public string baseCcy;
        public string quoteCcy;
        public string taker_symbol_market;
        public string maker_symbol_market;

        public market taker_market;
        public market maker_market;

        public Instrument? taker;
        public Instrument? maker;

        public Exchange? taker_exchange;
        public Exchange? maker_exchange;

        public SortedDictionary<int, Instrument> takers;
        public decimal taker_min_quote_unit;
        public SortedDictionary<decimal, decimal> agg_asks;
        public SortedDictionary<decimal, decimal> agg_bids;

        public myLock updating = new myLock();
        public volatile int queued;

        public DateTime lastPosAdjustment;

        public string live_sellorder_id;
        public string live_buyorder_id;
        public DateTime live_sellorder_time;
        public DateTime live_buyorder_time;
        public HashSet<string> stg_orders;
        public Dictionary<string,decimal> stg_orders_dict;

        public int layers;
        public List<decimal> order_size;
        public decimal total_order_size;
        public List<decimal> markups;
        public List<string> live_sellorders;
        public List<string> live_buyorders;
        public List<decimal> current_bids;
        public List<decimal> current_asks;
        //public List<decimal> ordersize_ask;
        //public List<decimal> ordersize_bid;
        //public List<decimal> bids;
        //public List<decimal> asks;

        
        private List<orderCondition> bid_orders;
        private List<orderCondition> ask_orders; 

        public decimal live_askprice;
        public decimal live_bidprice;
        public decimal skew_point;
        public decimal base_markup;

        public DateTime prevMarkupTime;
        public decimal prev_markup;

        public decimal taker_last_updated_mid;
        public decimal maker_last_updated_mid;

        public DateTime? last_filled_time;
        public DateTime? last_filled_time_buy;
        public DateTime? last_filled_time_sell;

        public decimal markup_decay_basetime = 60; //minutes
        public decimal markup_decay;

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

        public decimal mi_volume;
        public Dictionary<double, decimal> market_impact_curve;

        //For intradayPnL
        public decimal prev_notionalVolume = 0;

        public bool multiLayer_strategy = false;

        public Dictionary<string, latency> Latency;

        public myLock tradeSummary_lock = new myLock();
        public string tradeSummary_id_prefix = "";
        public int tradeid_num = 0;
        public Dictionary<string, tradeSummary> tempTradeSummaries;
        public Dictionary<string, tradeSummary> tradeSummaries;

        public Action<string, Enums.logType> _addLog;
        public Strategy() 
        {
            this.name = "";
            this.enabled = false;
            this.ready = false;
            this.order_throttle = 0;
            this.const_markup = 0;
            this.min_markup = 0;
            this.maxMakerPosition = 0;
            this.targetMakerPosition = 0;
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
            this.rv_base_param = 100;
            this.abook = true;

            this.taker_symbol_market = "";
            this.maker_symbol_market = "";
            this.taker_market = market.NONE;
            this.maker_market = market.NONE;

            this.taker = null;
            this.maker = null;
            this.takers = new SortedDictionary<int, Instrument>();
            this.agg_asks = new SortedDictionary<decimal, decimal>();
            this.agg_bids = new SortedDictionary<decimal, decimal>();
            this.taker_min_quote_unit = (decimal)0.0001;

            this.queued= 0;

            this.live_sellorder_id = "";
            this.live_buyorder_id = "";
            this.live_buyorder_time = DateTime.UtcNow; 
            this.live_sellorder_time = DateTime.UtcNow;
            this.stg_orders = new HashSet<string>();
            this.stg_orders_dict = new Dictionary<string, decimal>();
            this.executed_Orders_old = new Dictionary<string, DataSpotOrderUpdate>();
            this.executed_OrderIds = new Dictionary<string, fillType>();

            this.layers = 1;
            this.order_size = new List<decimal>();
            this.markups = new List<decimal>();
            this.live_sellorders = new List<string>();
            this.live_buyorders = new List<string>();
            this.current_bids = new List<decimal>();
            this.current_asks = new List<decimal>();
            //this.ordersize_ask = new List<decimal>();
            //this.ordersize_bid = new List<decimal>();
            //this.bids = new List<decimal>();
            //this.asks = new List<decimal>();


            this.bid_orders = new List<orderCondition> ();
            this.ask_orders = new List<orderCondition> (); 

            this.live_askprice = 0;
            this.live_bidprice = 0;
            this.skew_point = 0;
            this.prev_markup = 0;
            this.prevMarkupTime = DateTime.UtcNow;

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

            this.last_filled_time = DateTime.UtcNow;

            this.mi_volume = 0;
            this.market_impact_curve = new Dictionary<double, decimal>();
            foreach (double d in GlobalVariables.MI_period)
            {
                this.market_impact_curve[d] = 0;
            }

            this.oManager = OrderManager.GetInstance();

            this.tempTradeSummaries = new Dictionary<string, tradeSummary>();
            this.tradeSummaries = new Dictionary<string, tradeSummary>();

            this.Latency = new Dictionary<string, latency>();
        }

        public void readStrategyFile(string jsonfilename)
        {
            if(System.IO.File.Exists(jsonfilename))
            {
                string fileContent = System.IO.File.ReadAllText(jsonfilename);
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
                this.taker_market = (market)Enum.Parse(typeof(market),item.GetString());
            }
            else
            {
                addLog("Taker symbol market is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("maker_market", out item))
            {
                this.maker_market = (market)Enum.Parse(typeof(market), item.GetString());
            }
            else
            {
                addLog("Taker symbol market is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("markup", out item))
            {
                this.const_markup = item.GetDecimal();
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
            if (root.TryGetProperty("maxMakerPosition", out item))
            {
                this.maxMakerPosition = item.GetDecimal();
            }
            else
            {
                addLog("The quantity of the base currency is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("targetMakerPosition", out item))
            {
                this.targetMakerPosition = item.GetDecimal();
            }
            else
            {
                addLog("The targetMakerPosition is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("ToBsize", out item))
            {
                this.ToBsize = item.GetDecimal();
            }
            else
            {
                addLog("ToB size is not configurated", logType.ERROR);
            }
            if (root.TryGetProperty("layers", out item))
            {
                this.layers = item.GetInt32();
                this.addLog("Layers:" + this.layers.ToString());
                //Initialize
                for (int i = 0;i < this.layers;++i)
                {
                    this.live_sellorders.Add("");
                    this.live_buyorders.Add("");
                    this.current_bids.Add(0);
                    this.current_asks.Add(0);
                    //this.ordersize_ask.Add(0);
                    //this.ordersize_bid.Add(0);
                    //this.bids.Add(0);
                    //this.asks.Add(0);
                    this.bid_orders.Add(new orderCondition());
                    this.ask_orders.Add(new orderCondition());
                }
            }
            else
            {
                this.layers = 1;
                for (int i = 0; i < this.layers; ++i)
                {
                    this.live_sellorders.Add("");
                    this.live_buyorders.Add("");
                    this.current_bids.Add(0);
                    this.current_asks.Add(0);
                    //this.ordersize_ask.Add(0);
                    //this.ordersize_bid.Add(0);
                    //this.bids.Add(0);
                    //this.asks.Add(0);
                    this.bid_orders.Add(new orderCondition());
                    this.ask_orders.Add(new orderCondition());
                }
            }
            if (root.TryGetProperty("order_size", out item))
            {
                foreach (var size in item.EnumerateArray())
                {
                    this.order_size.Add(size.GetDecimal());
                }
                for(int i = 0;i < this.layers;++i)
                {
                    addLog("order size:" + this.order_size[i].ToString());
                }
                if (this.order_size.Count != this.layers)
                {
                    addLog("The number of order sizes doesn't match the number of layers", logType.ERROR);
                }
            }
            else
            {
                int i = 0;
                while(i < this.layers)
                {
                    this.order_size.Add(this.ToBsize);
                    ++i;
                }
            }
            this.total_order_size = this.order_size.Sum();
            if (root.TryGetProperty("markups", out item))
            {
                foreach (var m in item.EnumerateArray())
                {
                    this.markups.Add(m.GetDecimal());
                }
                for (int i = 0; i < this.layers; ++i)
                {
                    addLog("markup:" + this.markups[i].ToString());
                }
                if (this.markups.Count != this.layers)
                {
                    addLog("The number of order sizes doesn't match the number of layers", logType.ERROR);
                }
            }
            else
            {
                int i = 0;
                while (i < this.layers)
                {
                    this.markups.Add(this.const_markup);
                    ++i;
                }
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
            //if (root.TryGetProperty("fillPrediction", out item))
            //{
            //    this.predictFill = item.GetBoolean();
            //}
            //else
            //{
            //    addLog("Fill prediction is not configurated. Default value will be set. value:false", logType.WARNING);
            //    this.predictFill = false;
            //}
            if (root.TryGetProperty("ToBsizeMultiplier", out item))
            {
                this.ToBsizeMultiplier = item.GetDecimal();
            }
            else
            {
                this.ToBsizeMultiplier = 1;
            }
            if (root.TryGetProperty("RVPenaltyMultiplier", out item))
            {
                this.rv_penalty_multiplier = item.GetDecimal();
            }
            else
            {
                this.rv_penalty_multiplier = 100;
            }
            if (root.TryGetProperty("RVBaseParam", out item))
            {
                this.rv_base_param = item.GetDouble();
            }
            else
            {
                this.rv_base_param = 100;
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
            this.const_markup = setting.markup;
            this.min_markup = setting.min_markup;
            this.maxMakerPosition = setting.maxMakerPosition;
            this.targetMakerPosition = setting.targetMakerPosition;
            this.skewWidening = setting.skew_widening;
            this.ToBsize = setting.ToBsize;
            this.ToBsizeMultiplier = setting.ToBsizeMultiplier;
            this.intervalAfterFill = setting.intervalAfterFill;
            this.modThreshold = setting.modThreshold;
            this.maxSkew = setting.max_skew;
            this.skewThreshold = setting.skewThreshold;
            this.oneSideThreshold = setting.oneSideThreshold;
            this.markup_decay_basetime = setting.decaying_time;
            this.rv_penalty_multiplier = setting.rv_penalty_multiplier;
            this.rv_base_param = setting.rv_base_param;
            this.max_baseMarkup = setting.maxBaseMarkup;
            this.taker_market = (market)Enum.Parse(typeof(market),setting.taker_market);
            this.maker_market = (market)Enum.Parse(typeof(market), setting.maker_market);
            //this.predictFill = setting.predictFill;
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
                    if (this.maker.readyToTrade && this.taker.readyToTrade)
                    {
                        addLog("Strategy " + this.name + " ready to trade.");
                        addLog(this.maker.symbol_market + " Latency coef:" + this.maker.coef.ToString() + "  intercept:" + this.maker.intercept);
                        addLog(this.taker.symbol_market + " Latency coef:" + this.taker.coef.ToString() + "  intercept:" + this.taker.intercept);
                        this.tradeSummary_id_prefix = current.ToString("HHmmss");
                        this.ready = true;
                    }
                    else
                    {
                        return true;
                    }
                }

                int i = 0;
                bool buyFirst = true;
                this.skew_point = this.skew();
                decimal modTh_buffer = 0;

                decimal consumed_ask = 0;
                decimal consumed_bid = 0;

                decimal cumAskSize = 0;
                decimal cumBidSize = 0;

                decimal totalSize_Ask = 0;
                decimal totalSize_Bid = 0;

                positionSide buyOrdersSide = positionSide.NONE;
                positionSide sellOrdersSide = positionSide.NONE;

                if (this.skew_point == 0)
                {
                    for (i = 0;i < this.layers;++i)
                    {
                        decimal temp_ordersize_bid = this.order_size[i];
                        decimal temp_ordersize_ask = this.order_size[i];
                        decimal temp_askSize = temp_ordersize_ask * this.ToBsizeMultiplier;
                        if (- (this.maker.net_pos - this.targetMakerPosition) + cumAskSize + temp_askSize <= this.maxMakerPosition * this.skewThreshold / 100)
                        {
                            temp_ordersize_ask = temp_askSize;
                        }

                        decimal temp_bidSize = temp_ordersize_bid * this.ToBsizeMultiplier;
                        if ((this.maker.net_pos - this.targetMakerPosition) + cumBidSize + temp_bidSize <= this.maxMakerPosition * this.skewThreshold / 100)
                        {
                            temp_ordersize_bid = temp_bidSize;
                        }
                        //this.ordersize_ask[i] = temp_ordersize_ask;
                        //this.ordersize_bid[i] = temp_ordersize_bid;
                        this.ask_orders[i].quantity = temp_ordersize_ask;
                        this.bid_orders[i].quantity = temp_ordersize_bid;
                        if (this.maker.marginTrade)
                        {
                            if (this.maker.marginLong)
                            {
                                if (this.maker.longPosition.available > temp_ordersize_ask + consumed_ask)
                                {
                                    this.ask_orders[i].pos_side = positionSide.Long;
                                }
                                else
                                {
                                    this.ask_orders[i].pos_side = positionSide.Short;
                                }
                                if (this.maker.shortPosition.available > temp_ordersize_bid + consumed_bid)
                                {
                                    this.bid_orders[i].pos_side = positionSide.Short;
                                }
                                else
                                {
                                    this.bid_orders[i].pos_side = positionSide.Long;
                                }
                            }
                            else
                            {
                                if (this.maker.baseBalance.available > temp_ordersize_ask + consumed_ask)
                                {
                                    this.ask_orders[i].pos_side = positionSide.NONE;
                                }
                                else
                                {
                                    this.ask_orders[i].pos_side = positionSide.Short;
                                }
                                if (this.maker.shortPosition.available > temp_ordersize_bid + consumed_bid)
                                {
                                    this.bid_orders[i].pos_side = positionSide.Short;
                                }
                                else
                                {
                                    this.bid_orders[i].pos_side = positionSide.NONE;
                                }
                            }
                        }
                        else
                        {
                            this.bid_orders[i].pos_side = positionSide.NONE;
                            this.ask_orders[i].pos_side = positionSide.NONE;
                        }
                        consumed_ask += temp_ordersize_ask;
                        consumed_bid += temp_ordersize_bid;
                        cumAskSize += temp_ordersize_ask;
                        cumBidSize += temp_ordersize_bid;
                        totalSize_Ask += temp_ordersize_ask;
                        totalSize_Bid += temp_ordersize_bid;
                        
                    }
                }
                else
                {
                    for (i = 0; i < this.layers; ++i)
                    {
                        //this.ordersize_ask[i] = this.order_size[i];
                        //this.ordersize_bid[i] = this.order_size[i];
                        this.ask_orders[i].quantity = this.order_size[i];
                        this.bid_orders[i].quantity = this.order_size[i];
                        if (this.maker.marginTrade)
                        {
                            if (this.maker.marginLong)
                            {
                                if (this.maker.longPosition.available > this.order_size[i] + consumed_ask)
                                {
                                    this.ask_orders[i].pos_side = positionSide.Long;
                                }
                                else
                                {
                                    this.ask_orders[i].pos_side = positionSide.Short;
                                }
                                if (this.maker.shortPosition.available > this.order_size[i] + consumed_bid)
                                {
                                    this.bid_orders[i].pos_side = positionSide.Short;
                                }
                                else
                                {
                                    this.bid_orders[i].pos_side = positionSide.Long;
                                }
                            }
                            else
                            {
                                if (this.maker.baseBalance.available > this.order_size[i] + consumed_ask)
                                {
                                    this.ask_orders[i].pos_side = positionSide.NONE;
                                }
                                else
                                {
                                    this.ask_orders[i].pos_side = positionSide.Short;
                                }
                                if (this.maker.shortPosition.available > this.order_size[i] + consumed_bid)
                                {
                                    this.bid_orders[i].pos_side = positionSide.Short;
                                }
                                else
                                {
                                    this.bid_orders[i].pos_side = positionSide.NONE;
                                }
                            }
                        }
                        else
                        {
                            this.bid_orders[i].pos_side = positionSide.NONE;
                            this.ask_orders[i].pos_side = positionSide.NONE;
                        }
                        consumed_ask += this.order_size[i];
                        consumed_bid += this.order_size[i];
                        totalSize_Ask += this.order_size[i];
                        totalSize_Bid += this.order_size[i];
                    }
                }

                this.taker.getWeightedAvgPrice(orderSide.Buy, this.bid_orders);
                this.taker.getWeightedAvgPrice(orderSide.Sell, this.ask_orders);
                //this.taker.getWeightedAvgPrice(orderSide.Buy, this.ordersize_bid, this.bids);
                //this.taker.getWeightedAvgPrice(orderSide.Sell, this.ordersize_ask, this.asks);

                decimal maker_bid;
                decimal maker_ask;
                using (var qlock = this.maker.quotes_lock.getlock())
                {
                    maker_bid = this.maker.bestbid.Item1;
                    maker_ask = this.maker.bestask.Item1;
                }                

                double taker_VR = this.taker.realized_volatility;
                if (this.taker.prev_RV > 0)
                {
                    taker_VR = 0.7 * taker_VR + 0.3 * this.taker.prev_RV;
                }
                decimal vr_markup = this.const_markup + this.rv_penalty_multiplier * ((decimal)Math.Exp(taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000 / this.rv_base_param) - 1);

                if (vr_markup >= this.prev_markup)
                {
                    this.base_markup = vr_markup;
                    this.prev_markup = vr_markup;
                    this.prevMarkupTime = DateTime.UtcNow;
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
                    }
                }
                decimal elapsedTimeFromLastfill = (decimal)(DateTime.UtcNow - (this.last_filled_time ?? DateTime.UtcNow)).TotalMinutes - 5;
                if (elapsedTimeFromLastfill < 0)
                {
                    elapsedTimeFromLastfill = 0;
                }

                //decimal min_basemarkup = this.const_markup;
                this.markup_decay = -elapsedTimeFromLastfill / this.markup_decay_basetime * this.const_markup;

                //if (this.base_markup + this.markup_decay < min_basemarkup)
                //{
                //    this.markup_decay = min_basemarkup - this.base_markup;
                //}

                this.base_markup += this.markup_decay;

                if(this.base_markup < this.min_markup)
                {
                    this.base_markup = this.min_markup;
                }

                decimal maker_adjustedbid = this.maker.getPriceAfterSweep(orderSide.Buy, this.order_size[0]);
                decimal maker_adjustedask = this.maker.getPriceAfterSweep(orderSide.Sell, this.order_size[0]);

                if (this.base_markup / 5 > this.modThreshold * 1000000)
                {
                    modTh_buffer = this.base_markup / 5 / 1000000 - this.modThreshold;
                }

                bool isPriceChanged = this.checkPriceChange("taker", modTh_buffer);

                cumAskSize = 0;
                cumBidSize = 0;
                decimal cumAskAmount = 0;
                decimal cumBidAmount = 0;

                //Open or close should be determined at beginning based on the total order size.
                //Close only if the total order size is smaller than the availability.
                //This is applied only for margin trade
                bool closingBuy = false;
                bool closingSell = false;
                if (this.maker.marginTrade)
                {
                    if(this.maker.shortPosition.available >= totalSize_Bid)
                    {
                        if(totalSize_Bid == 0)
                        {
                            addLog("totalSize_bid = 0");
                        }
                        closingBuy = true;
                    }
                    else
                    {
                        //if (this.maker.shortPosition.total > 0)
                        //{
                        //    addLog($"Going two legs Total Short:{this.maker.shortPosition.total} Inuse:{this.maker.shortPosition.inuse}");
                        //}
                    }
                    if(this.maker.marginLong)
                    {
                        if(this.maker.longPosition.available >= totalSize_Ask)
                        {
                            if (totalSize_Ask == 0)
                            {
                                addLog("totalSize_bid = 0");
                            }
                            closingSell = true;
                        }
                        else
                        {
                            //if (this.maker.longPosition.total > 0)
                            //{
                            //    addLog($"Going two legs Total Long:{this.maker.longPosition.total} Inuse:{this.maker.longPosition.inuse}");
                            //}
                        }
                    }
                    else
                    {
                        if(this.maker.baseBalance.available >= totalSize_Ask)
                        {
                            if (totalSize_Ask == 0)
                            {
                                addLog("totalSize_bid = 0");
                            }
                            closingSell = true;
                        }
                        else
                        {

                            //if (this.maker.baseBalance.total > 0)
                            //{
                            //    addLog($"Going two legs Total BaseBalance:{this.maker.baseBalance.total} Inuse:{this.maker.baseBalance.inuse}");
                            //}
                        }

                    }
                }


                for (i = 0;i < this.layers;++i)
                {
                    decimal bid_price = this.bid_orders[i].price;
                    decimal ask_price = this.ask_orders[i].price;
                    //decimal min_markup_bid = bid_price * (1 - this.min_markup / 1000000);
                    //decimal min_markup_ask = ask_price * (1 + this.min_markup / 1000000);

                    decimal markup_bid = this.markups[i];
                    decimal markup_ask = this.markups[i];

                    decimal temp_ordersize_bid = this.bid_orders[i].quantity;
                    decimal temp_ordersize_ask = this.ask_orders[i].quantity;

                    this.modThreshold = this.config_modThreshold;

                    markup_bid += this.base_markup;
                    markup_ask += this.base_markup;

                    if (this.skew_point > 0)
                    {
                        markup_ask += (decimal)(1 + this.skewWidening) * this.skew_point;
                        markup_bid += -this.skew_point;
                    }
                    else if (this.skew_point < 0)
                    {
                        markup_bid += -(decimal)(1 + this.skewWidening) * this.skew_point;
                        markup_ask += this.skew_point;
                    }

                    if (i == 0)
                    {
                        this.temp_markup_ask = markup_ask;
                        this.temp_markup_bid = markup_bid;
                    }

                    if (this.base_markup > this.max_baseMarkup || this.base_markup < 0)
                    {
                        bid_price = 0;
                        ask_price = 0;
                    }
                    else
                    {
                        bid_price *= (1 - markup_bid / 1000000);
                        ask_price *= (1 + markup_ask / 1000000);
                    }

                    if (bid_price > 0)
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

                    if (ask_price > 0)
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


                    //Availability check
                    //Maker Side
                    if(!this.maker.marginTrade)
                    {
                        if(this.maker.baseBalance.total - this.maker.baseBalance.inuse < temp_ordersize_ask + cumAskSize)
                        {
                            ask_price = 0;
                        }
                        if(this.maker.quoteBalance.total - this.maker.quoteBalance.inuse < temp_ordersize_bid * bid_price + cumBidAmount)
                        {
                            bid_price = 0;
                        }
                    }
                    else
                    {
                        decimal marginAvailability = this.maker_exchange.getMarginAvailability() - this.maker_exchange.marginLocked;
                        if(this.maker.marginLong)//Both margin
                        {
                            if (!closingBuy)//Build new long but not enough availablity
                            {
                                if (marginAvailability < (temp_ordersize_bid * bid_price + cumAskAmount) / this.maker.leverage)//Build new long but not enough availablity
                                {
                                    bid_price = 0;
                                }
                                buyOrdersSide = positionSide.Long;
                            }
                            else
                            {
                                buyOrdersSide = positionSide.Short;
                            }
                            if (!closingSell)//Build new short but not enough availablity
                            {
                                if (marginAvailability < (temp_ordersize_ask * ask_price + cumAskAmount) / this.maker.leverage)//Build new short but not enough availablity
                                {
                                    ask_price = 0;
                                }
                                sellOrdersSide = positionSide.Short;
                            }
                            else
                            {
                                sellOrdersSide = positionSide.Long;
                            }

                        }
                        else
                        {
                            if (!closingBuy)//Build new long but not enough availablity
                            {

                                if (!closingBuy && this.maker.quoteBalance.available < temp_ordersize_bid * bid_price + cumBidAmount)//Build new Spot long but not enough quoteBalance
                                {
                                    bid_price = 0;
                                }
                                buyOrdersSide = positionSide.NONE;
                            }
                            else
                            {
                                buyOrdersSide = positionSide.Short;
                            }
                            if (!closingSell)//Build new short but not enough availablity
                            {
                                if (marginAvailability < (temp_ordersize_ask * ask_price + cumAskAmount) / this.maker.leverage)//Build new short but not enough availablity
                                {
                                    ask_price = 0;
                                }
                                sellOrdersSide = positionSide.Short;
                            }
                            else
                            {
                                sellOrdersSide = positionSide.NONE;
                            }
                        }
                    }
                    //Taker side
                    if(!this.taker.marginTrade)
                    {
                        if (temp_ordersize_bid + cumBidSize > this.taker.baseBalance.available)
                        {
                            bid_price = 0;
                        }
                        if (ask_price * temp_ordersize_ask + cumAskAmount > this.taker.quoteBalance.available)
                        {
                            ask_price = 0;
                        }
                    }
                    else
                    {
                        decimal marginAvailability = this.taker_exchange.getMarginAvailability() - this.taker_exchange.marginLocked;
                        if (this.taker.marginLong)//Both margin
                        {
                            //Buying on the LP side.
                            if(this.taker.shortPosition.total - this.taker.shortPosition.inuse < totalSize_Ask && marginAvailability < (temp_ordersize_ask * ask_price + cumAskAmount) / this.taker.leverage)
                            {
                                ask_price = 0; 
                            }
                            //Selling on the LP
                            if(this.taker.longPosition.total - this.taker.longPosition.inuse < totalSize_Bid && marginAvailability < (temp_ordersize_bid * bid_price + cumBidAmount) / this.taker.leverage)
                            {
                                bid_price = 0;
                            }
                        }
                        else
                        {
                            //Buying on the LP side.
                            if (this.taker.shortPosition.total - this.taker.shortPosition.inuse < totalSize_Ask && this.taker.quoteBalance.available < temp_ordersize_ask * ask_price + cumAskAmount)
                            {
                                ask_price = 0;
                            }
                            //Selling on the LP
                            if (this.taker.baseBalance.total - this.taker.baseBalance.inuse < totalSize_Bid && marginAvailability < (temp_ordersize_bid * bid_price + cumBidAmount) / this.taker.leverage)
                            {
                                bid_price = 0;
                            }
                        }
                    }

                    //if (this.taker.marginTrade == false && temp_ordersize_bid * (1 + this.taker.taker_fee) * 2 + cumBidSize > this.taker.baseBalance.available)
                    //{
                    //    bid_price = 0;
                    //}
                    //if (this.taker.marginTrade == false && this.asks[i] * temp_ordersize_ask * (1 + this.taker.taker_fee) * 2 + cumAskAmount > this.taker.quoteBalance.available)
                    //{
                    //    ask_price = 0;
                    //}

                    //if (this.maker.marginTrade && this.maker.shortPosition.total - this.maker.shortPosition.inuse < temp_ordersize_bid + cumBidSize)
                    //{
                    //    bid_price = 0;
                    //}
                    //if (this.maker.shortPosition.total * ask_price + temp_ordersize_ask * ask_price + cumAskAmount > this.maker.quoteBalance.total * max_maker_levarage)
                    //{
                    //    ask_price = 0;
                    //}

                    cumAskSize += temp_ordersize_ask;
                    cumBidSize += temp_ordersize_bid;
                    cumAskAmount += temp_ordersize_ask * ask_price;
                    cumBidAmount += temp_ordersize_bid * bid_price;

                    if (bid_price < 0)
                    {
                        addLog($"Invalid bid price. bid:{bid_price} skew:{this.skew_point} base markup:{this.base_markup}", logType.WARNING);
                        bid_price = 0;
                    }
                    else if (bid_price > 0 && maker_bid > 0 && (bid_price / maker_bid > (decimal)1.1 || bid_price / maker_bid < (decimal)0.9))
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
                    //this.bids[i] = bid_price;
                    //this.asks[i] = ask_price;
                    this.bid_orders[i].price = bid_price;
                    this.ask_orders[i].price = ask_price;
                }

                using (var ulock = this.updating.getlock())
                {
                    string ordid;
                    DataSpotOrderUpdate ord;
                    i = 0;
                    for(i = 0;i < this.layers;++i)
                    {
                        ordid = this.live_buyorders[i];
                        if (ordid != "")
                        {
                            using(var olock = this.oManager.order_lock.getlock())
                            {
                                if (this.oManager.orders.ContainsKey(ordid))
                                {
                                    ord = this.oManager.orders[ordid];
                                    switch (ord.status)
                                    {
                                        case orderStatus.NONE:
                                            addLog("Something wrong. " + ord.ToString(), logType.WARNING);
                                            this.live_buyorders[i] = "";
                                            this.current_bids[i] = 0;
                                            break;
                                        case orderStatus.Filled:
                                        case orderStatus.Canceled:
                                        case orderStatus.INVALID:
                                        case orderStatus.WaitCancel:
                                            this.live_buyorders[i] = "";
                                            this.current_bids[i] = 0;
                                            break;
                                    }
                                    if (ord.status == orderStatus.Open && !this.oManager.live_orders.ContainsKey(ord.internal_order_id))
                                    {
                                        addLog("The order status is open but doesn't exist in liveorders", logType.WARNING);
                                        addLog(ord.ToString(), logType.WARNING);
                                    }
                                }
                                else if (current - this.live_buyorder_time > TimeSpan.FromSeconds(10))
                                {
                                    addLog("Strategy buy order not found. order_id:" + ordid);
                                    //await RefreshLiveOrders();
                                    ret = false;
                                    this.live_buyorders[i] = "";
                                    this.current_bids[i] = 0;
                                }
                            }
                            
                        }
                        else
                        {
                            this.current_bids[i] = 0;
                        }

                        ordid = this.live_sellorders[i];
                        if (ordid != "")
                        {
                            using (var olock = this.oManager.order_lock.getlock())
                            {
                                if (this.oManager.orders.ContainsKey(ordid))
                                {
                                    ord = this.oManager.orders[ordid];
                                    switch (ord.status)
                                    {
                                        case orderStatus.NONE:
                                            addLog("Something wrong. " + ord.ToString(), logType.WARNING);
                                            this.live_sellorders[i] = "";
                                            this.current_asks[i] = 0;
                                            break;
                                        case orderStatus.Filled:
                                        case orderStatus.Canceled:
                                        case orderStatus.INVALID:
                                        case orderStatus.WaitCancel:
                                            this.live_sellorders[i] = "";
                                            this.current_asks[i] = 0;
                                            break;
                                    }
                                    if (ord.status == orderStatus.Open && !this.oManager.live_orders.ContainsKey(ord.internal_order_id))
                                    {
                                        addLog("The order status is open but doesn't exist in liveorders", logType.WARNING);
                                        addLog(ord.ToString(), logType.WARNING);
                                    }
                                }
                                else if (current - this.live_sellorder_time > TimeSpan.FromSeconds(10))
                                {
                                    addLog("Strategy sell order not found. order_id:" + ordid);
                                    //await RefreshLiveOrders();
                                    ret = false;
                                    this.live_sellorders[i] = "";
                                    this.current_asks[i] = 0;
                                }
                            }
                        }
                        else
                        {
                            this.current_asks[i] = 0;
                        }
                    }
                    
                    await this.checkLiveOrders();

                    int newBuyOrder = 0;
                    int newSellOrder = 0;

                    List<string> cancelling_ord = new List<string>();


                    for(i = 0;i <this.layers;++i)
                    {
                        decimal bid_price = this.bid_orders[i].price;
                        decimal ask_price = this.ask_orders[i].price;


                        bool askChanged = false;
                        bool bidChanged = false;

                        if (bid_price > 0 && (this.current_bids[i] == 0 || bid_price / this.current_bids[i] > 1 + this.modThreshold + modTh_buffer || bid_price / this.current_bids[i] < 1 - this.modThreshold - modTh_buffer))
                        {
                            bidChanged = true;
                        }
                        if (ask_price > 0 && (this.current_asks[i] == 0 || ask_price / this.current_asks[i] > 1 + this.modThreshold + modTh_buffer || ask_price / this.current_asks[i] < 1 - this.modThreshold - modTh_buffer))
                        {
                            askChanged = true;
                        }
                        using (var olock = this.oManager.order_lock.getlock())
                        {
                            if (this.oManager.orders.ContainsKey(this.live_buyorders[i]))
                            {
                                ord = this.oManager.orders[this.live_buyorders[i]];
                                //if (ord.status == orderStatus.Open && (bid_price == 0 || ((this.maxMakerPosition + this.maker.net_pos) > this.maxMakerPosition * ((decimal)0.5 + this.oneSideThreshold / 200))))
                                if (ord.status == orderStatus.Open && (bid_price == 0 || ((this.maker.net_pos - this.targetMakerPosition) > this.maxMakerPosition * this.oneSideThreshold / 100)))
                                {
                                    cancelling_ord.Add(this.live_buyorders[i]);
                                    this.live_buyorders[i] = "";
                                    this.current_bids[i] = 0;
                                    //this.bids[i] = 0;
                                    this.bid_orders[i].price = 0;
                                }
                                else if (((isPriceChanged && bidChanged) || bid_price > this.current_bids[i]) && ord.status == orderStatus.Open/*this.current_bids[i] != bid_price*/)
                                {
                                    cancelling_ord.Add(this.live_buyorders[i]);
                                    this.live_buyorders[i] = "";
                                    this.current_bids[i] = 0;
                                    //this.bids[i] = 0;
                                    this.bid_orders[i].price = 0;
                                    newBuyOrder |= (1 << i);
                                }
                            }
                            else if (this.live_buyorders[i] == "")
                            {
                                //if (bid_price == 0 || ((this.maxMakerPosition + this.maker.net_pos) > this.maxMakerPosition * ((decimal)0.5 + this.oneSideThreshold / 200)))
                                if (bid_price == 0 || ((this.maker.net_pos - this.targetMakerPosition) > this.maxMakerPosition * this.oneSideThreshold / 100))
                                {
                                    //this.bids[i] = 0;
                                    this.bid_orders[i].price = 0;
                                }
                                else if (this.last_filled_time_buy == null || (decimal)(current - this.last_filled_time_buy).Value.TotalSeconds > this.intervalAfterFill)
                                {
                                    newBuyOrder |= (1 << i);
                                }
                            }
                            if (this.oManager.orders.ContainsKey(this.live_sellorders[i]))
                            {
                                ord = this.oManager.orders[this.live_sellorders[i]];
                                //if (ord.status == orderStatus.Open && (ask_price == 0 || ((this.maxMakerPosition + this.maker.net_pos) < this.maxMakerPosition * ((decimal)0.5 - this.oneSideThreshold / 200))))
                                if (ord.status == orderStatus.Open && (ask_price == 0 || (-(this.maker.net_pos - this.targetMakerPosition) > this.maxMakerPosition * this.oneSideThreshold / 100)))
                                {
                                    cancelling_ord.Add(this.live_sellorders[i]);
                                    this.live_sellorders[i] = "";
                                    this.current_asks[i] = 0;
                                    //this.asks[i] = 0;
                                    this.ask_orders[i].price = 0;
                                }
                                else if (((isPriceChanged && askChanged) || ask_price < this.current_asks[i]) && ord.status == orderStatus.Open/*this.current_asks[i] != ask_price*/)
                                {
                                    cancelling_ord.Add(this.live_sellorders[i]);
                                    this.live_sellorders[i] = "";
                                    this.current_asks[i] = 0;
                                    //this.asks[i] = 0;
                                    this.ask_orders[i].price = 0;
                                    newSellOrder |= (1 << i);
                                }
                            }
                            else if (this.live_sellorders[i] == "")
                            {
                                //if (ask_price == 0 || ((this.maxMakerPosition + this.maker.net_pos) < this.maxMakerPosition * ((decimal)0.5 - this.oneSideThreshold / 200)))
                                if (ask_price == 0 || (-(this.maker.net_pos - this.targetMakerPosition) > this.maxMakerPosition * this.oneSideThreshold / 100))
                                {
                                    //this.asks[i] = 0;
                                    this.ask_orders[i].price = 0;
                                }
                                else if (this.last_filled_time_sell == null || (decimal)(current - this.last_filled_time_sell).Value.TotalSeconds > this.intervalAfterFill)
                                {
                                    newSellOrder |= (1 << i);
                                }
                            }
                        }
                    }

                    //this.live_askprice = this.asks[0];
                    //this.live_bidprice = this.bids[0];
                    this.live_askprice = this.ask_orders[0].price;
                    this.live_bidprice = this.bid_orders[0].price;

                    this.oManager.placeCancelSpotOrders(this.maker, cancelling_ord);

                    if ((current - this.live_buyorder_time).TotalMilliseconds < this.order_throttle || (current - this.live_sellorder_time).TotalMilliseconds < this.order_throttle)
                    {
                        //Only do cancelling orders to avoid excessive orders
                        return ret;
                    }
                    if (!this.checkLatency(this.taker, 1000))
                    {
                        return ret;
                    }
                    if (newBuyOrder > 0 && newSellOrder > 0)//Both orders exist
                    {
                        if (this.ask_orders[0].price - maker_ask > maker_bid - this.bid_orders[0].price)
                        {
                            buyFirst = true;
                        }
                        else
                        {
                            buyFirst = false;
                        }
                    }
                    else if (newBuyOrder > 0)//Only buy order exists
                    {
                        buyFirst = true;
                    }
                    else//Only sell order exists or no order exists
                    {
                        buyFirst = false;
                    }
                    if(buyFirst)
                    {
                        for(i = 0;i < this.layers;++i)
                        {
                            if (this.bid_orders[i].price > 0 && this.bid_orders[i].quantity > 0 && (newBuyOrder & (1 << i)) > 0)
                            {
                                this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.bid_orders[i].quantity, this.bid_orders[i].price, this.bid_orders[i].pos_side, null, true, false, vr_markup.ToString("F2"));
                                using (var tslock = this.tradeSummary_lock.getlock())
                                {
                                    tradeSummary ts = this.oManager.TS_stack.pop();
                                    if (ts == null)
                                    {
                                        ts = new tradeSummary();
                                    }
                                    ts.id = this.live_buyorders[i];
                                    ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                    //ts.maker_orderid = this.live_buyorders[i];
                                    this.tempTradeSummaries[ts.id] = ts;
                                }
                                //if (this.maker.marginTrade)
                                //{
                                //    this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.bid_orders[i].quantity, this.bid_orders[i].price, buyOrdersSide, null, true, false, vr_markup.ToString("F2"));
                                //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    {
                                //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //        if (ts == null)
                                //        {
                                //            ts = new tradeSummary();
                                //        }
                                //        ts.id = this.live_buyorders[i];
                                //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //        //ts.maker_orderid = this.live_buyorders[i];
                                //        this.tempTradeSummaries[ts.id] = ts;
                                //    }
                                //    //if (this.maker.marginLong)
                                //    //{
                                //    //    if (buyOrdersSide != positionSide.NONE)
                                //    //    {
                                //    //        //Expect the position side be configured correctly for both spot and margin
                                //    //        this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.ordersize_bid[i], this.bids[i], buyOrdersSide, null, true, false, vr_markup.ToString("F2"));
                                //    //        using (var tslock = this.tradeSummary_lock.getlock())
                                //    //        {
                                //    //            tradeSummary ts = this.oManager.TS_stack.pop();
                                //    //            if (ts == null)
                                //    //            {
                                //    //                ts = new tradeSummary();
                                //    //            }
                                //    //            ts.id = this.live_buyorders[i];
                                //    //            ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //    //            //ts.maker_orderid = this.live_buyorders[i];
                                //    //            this.tempTradeSummaries[ts.id] = ts;
                                //    //        }
                                //    //    }
                                //    //    else
                                //    //    {
                                //    //        addLog("Invalid position side", logType.WARNING);
                                //    //    }
                                //    //}
                                //    //else
                                //    //{
                                //    //    this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.ordersize_bid[i], this.bids[i], buyOrdersSide, null, true, false, vr_markup.ToString("F2"));
                                //    //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    //    {
                                //    //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //    //        if (ts == null)
                                //    //        {
                                //    //            ts = new tradeSummary();
                                //    //        }
                                //    //        ts.id = this.live_buyorders[i];
                                //    //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //    //        //ts.maker_orderid = this.live_buyorders[i];
                                //    //        this.tempTradeSummaries[ts.id] = ts;
                                //    //    }
                                //    //}
                                //}
                                //else
                                //{
                                //    this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.bid_orders[i].quantity, this.bid_orders[i].price, positionSide.NONE, null, true, false, vr_markup.ToString("F2"));
                                //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    {
                                //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //        if (ts == null)
                                //        {
                                //            ts = new tradeSummary();
                                //        }
                                //        ts.id = this.live_buyorders[i];
                                //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //        //ts.maker_orderid = this.live_buyorders[i];
                                //        this.tempTradeSummaries[ts.id] = ts;
                                //    }
                                //}
                                
                                //if(this.maker.marginTrade)
                                //{
                                //    if(this.maker.shortPosition.available >= this.ordersize_bid[i])
                                //    {
                                //        this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.ordersize_bid[i], this.bids[i], positionSide.Short, null, true, false, vr_markup.ToString("F2"));
                                //        using(var tslock = this.tradeSummary_lock.getlock())
                                //        {
                                //            tradeSummary ts = this.oManager.TS_stack.pop();
                                //            if (ts == null)
                                //            {
                                //                ts = new tradeSummary();
                                //            }
                                //            ts.id = this.live_buyorders[i];
                                //            ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //            //ts.maker_orderid = this.live_buyorders[i];
                                //            this.tempTradeSummaries[ts.id] = ts;
                                //        }
                                //    }
                                //    else
                                //    {
                                //        this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.ordersize_bid[i], this.bids[i], positionSide.Long, null, true, false, vr_markup.ToString("F2"));
                                //        using (var tslock = this.tradeSummary_lock.getlock())
                                //        {
                                //            tradeSummary ts = this.oManager.TS_stack.pop();
                                //            if (ts == null)
                                //            {
                                //                ts = new tradeSummary();
                                //            }
                                //            ts.id = this.live_buyorders[i];
                                //            ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //            //ts.maker_orderid = this.live_buyorders[i];
                                //            this.tempTradeSummaries[ts.id] = ts;
                                //        }
                                //    }
                                //}
                                //else
                                //{
                                //    this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.ordersize_bid[i], this.bids[i], positionSide.NONE, null, true, false, vr_markup.ToString("F2"));
                                //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    {
                                //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //        if (ts == null)
                                //        {
                                //            ts = new tradeSummary();
                                //        }
                                //        ts.id = this.live_buyorders[i];
                                //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //        //ts.maker_orderid = this.live_buyorders[i];
                                //        this.tempTradeSummaries[ts.id] = ts;
                                //    }
                                //}

                                this.current_bids[i] = this.bid_orders[i].price;
                                this.stg_orders.Add(this.live_buyorders[i]);
                                this.stg_orders_dict[this.live_buyorders[i]] = this.bid_orders[i].quantity;
                                this.live_buyorder_time = current;
                            }
                            if (this.ask_orders[i].price > 0 && this.ask_orders[i].quantity > 0 && (newSellOrder & (1 << i)) > 0)
                            {
                                this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ask_orders[i].quantity, this.ask_orders[i].price, this.ask_orders[i].pos_side, null, true, false, vr_markup.ToString("F2"));
                                using (var tslock = this.tradeSummary_lock.getlock())
                                {
                                    tradeSummary ts = this.oManager.TS_stack.pop();
                                    if (ts == null)
                                    {
                                        ts = new tradeSummary();
                                    }
                                    ts.id = this.live_sellorders[i];
                                    ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                    //ts.maker_orderid = this.live_sellorders[i];
                                    this.tempTradeSummaries[ts.id] = ts;
                                }
                                //if (this.maker.marginTrade)
                                //{
                                //    this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ask_orders[i].quantity, this.ask_orders[i].price, sellOrdersSide, null, true, false, vr_markup.ToString("F2"));
                                //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    {
                                //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //        if (ts == null)
                                //        {
                                //            ts = new tradeSummary();
                                //        }
                                //        ts.id = this.live_sellorders[i];
                                //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //        //ts.maker_orderid = this.live_sellorders[i];
                                //        this.tempTradeSummaries[ts.id] = ts;
                                //    }
                                //    //if (this.maker.marginLong)
                                //    //{
                                //    //    this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ordersize_ask[i], this.asks[i], sellOrdersSide, null, true, false, vr_markup.ToString("F2"));
                                //    //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    //    {
                                //    //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //    //        if (ts == null)
                                //    //        {
                                //    //            ts = new tradeSummary();
                                //    //        }
                                //    //        ts.id = this.live_sellorders[i];
                                //    //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //    //        //ts.maker_orderid = this.live_sellorders[i];
                                //    //        this.tempTradeSummaries[ts.id] = ts;
                                //    //    }
                                //    //}
                                //    //else
                                //    //{
                                //    //    if (this.maker.baseBalance.available >= this.ordersize_ask[i])
                                //    //    {
                                //    //        this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ordersize_ask[i], this.asks[i], sellOrdersSide, null, true, false, vr_markup.ToString("F2"));
                                //    //        using (var tslock = this.tradeSummary_lock.getlock())
                                //    //        {
                                //    //            tradeSummary ts = this.oManager.TS_stack.pop();
                                //    //            if (ts == null)
                                //    //            {
                                //    //                ts = new tradeSummary();
                                //    //            }
                                //    //            ts.id = this.live_sellorders[i];
                                //    //            ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //    //            //ts.maker_orderid = this.live_sellorders[i];
                                //    //            this.tempTradeSummaries[ts.id] = ts;
                                //    //        }
                                //    //    }
                                //    //    else
                                //    //    {
                                //    //        this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ordersize_ask[i], this.asks[i], sellOrdersSide, null, true, false, vr_markup.ToString("F2"));
                                //    //        using (var tslock = this.tradeSummary_lock.getlock())
                                //    //        {
                                //    //            tradeSummary ts = this.oManager.TS_stack.pop();
                                //    //            if (ts == null)
                                //    //            {
                                //    //                ts = new tradeSummary();
                                //    //            }
                                //    //            ts.id = this.live_sellorders[i];
                                //    //            ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //    //            //ts.maker_orderid = this.live_sellorders[i];
                                //    //            this.tempTradeSummaries[ts.id] = ts;
                                //    //        }
                                //    //    }
                                //    //}
                                //}
                                //else
                                //{
                                //    this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ask_orders[i].quantity, this.ask_orders[i].price, positionSide.NONE, null, true, false, vr_markup.ToString("F2"));
                                //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    {
                                //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //        if (ts == null)
                                //        {
                                //            ts = new tradeSummary();
                                //        }
                                //        ts.id = this.live_sellorders[i];
                                //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //        //ts.maker_orderid = this.live_sellorders[i];
                                //        this.tempTradeSummaries[ts.id] = ts;
                                //    }
                                //}
                                //if (this.maker.marginTrade)
                                //{
                                //    if (this.maker.longPosition.available >= this.ordersize_ask[i])
                                //    {
                                //        this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ordersize_ask[i], this.asks[i], positionSide.Long, null, true, false, vr_markup.ToString("F2"));
                                //        using (var tslock = this.tradeSummary_lock.getlock())
                                //        {
                                //            tradeSummary ts = this.oManager.TS_stack.pop();
                                //            if (ts == null)
                                //            {
                                //                ts = new tradeSummary();
                                //            }
                                //            ts.id = this.live_sellorders[i];
                                //            ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //            //ts.maker_orderid = this.live_sellorders[i];
                                //            this.tempTradeSummaries[ts.id] = ts;
                                //        }
                                //    }
                                //    else
                                //    {
                                //        this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ordersize_ask[i], this.asks[i], positionSide.Short, null, true, false, vr_markup.ToString("F2"));
                                //        using (var tslock = this.tradeSummary_lock.getlock())
                                //        {
                                //            tradeSummary ts = this.oManager.TS_stack.pop();
                                //            if (ts == null)
                                //            {
                                //                ts = new tradeSummary();
                                //            }
                                //            ts.id = this.live_sellorders[i];
                                //            ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //            //ts.maker_orderid = this.live_sellorders[i];
                                //            this.tempTradeSummaries[ts.id] = ts;
                                //        }
                                //    }
                                //}
                                //else
                                //{
                                //    this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ordersize_ask[i], this.asks[i], positionSide.NONE, null, true, false, vr_markup.ToString("F2"));
                                //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    {
                                //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //        if (ts == null)
                                //        {
                                //            ts = new tradeSummary();
                                //        }
                                //        ts.id = this.live_sellorders[i];
                                //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //        //ts.maker_orderid = this.live_sellorders[i];
                                //        this.tempTradeSummaries[ts.id] = ts;
                                //    }
                                //}

                                this.current_asks[i] = this.ask_orders[i].price;
                                this.stg_orders.Add(this.live_sellorders[i]);
                                this.stg_orders_dict[this.live_sellorders[i]] = this.ask_orders[i].quantity;
                                this.live_sellorder_time = current;
                            }
                        }
                    }
                    else
                    {
                        for (i = 0; i < this.layers; ++i)
                        {
                            if (this.ask_orders[i].price > 0 && this.ask_orders[i].quantity > 0 && (newSellOrder & (1 << i)) > 0)
                            {
                                this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ask_orders[i].quantity, this.ask_orders[i].price, this.ask_orders[i].pos_side, null, true, false, vr_markup.ToString("F2"));
                                using (var tslock = this.tradeSummary_lock.getlock())
                                {
                                    tradeSummary ts = this.oManager.TS_stack.pop();
                                    if (ts == null)
                                    {
                                        ts = new tradeSummary();
                                    }
                                    ts.id = this.live_sellorders[i];
                                    ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                    //ts.maker_orderid = this.live_sellorders[i];
                                    this.tempTradeSummaries[ts.id] = ts;
                                }
                                //if (this.maker.marginTrade)
                                //{
                                //    this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ask_orders[i].quantity, this.ask_orders[i].price, sellOrdersSide, null, true, false, vr_markup.ToString("F2"));
                                //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    {
                                //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //        if (ts == null)
                                //        {
                                //            ts = new tradeSummary();
                                //        }
                                //        ts.id = this.live_sellorders[i];
                                //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //        //ts.maker_orderid = this.live_sellorders[i];
                                //        this.tempTradeSummaries[ts.id] = ts;
                                //    }
                                //    //if (this.maker.marginLong)
                                //    //{
                                //    //    this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ordersize_ask[i], this.asks[i], sellOrdersSide, null, true, false, vr_markup.ToString("F2"));
                                //    //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    //    {
                                //    //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //    //        if (ts == null)
                                //    //        {
                                //    //            ts = new tradeSummary();
                                //    //        }
                                //    //        ts.id = this.live_sellorders[i];
                                //    //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //    //        //ts.maker_orderid = this.live_sellorders[i];
                                //    //        this.tempTradeSummaries[ts.id] = ts;
                                //    //    }
                                //    //}
                                //    //else
                                //    //{
                                //    //    if (this.maker.baseBalance.available >= this.ordersize_ask[i])
                                //    //    {
                                //    //        this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ordersize_ask[i], this.asks[i], sellOrdersSide, null, true, false, vr_markup.ToString("F2"));
                                //    //        using (var tslock = this.tradeSummary_lock.getlock())
                                //    //        {
                                //    //            tradeSummary ts = this.oManager.TS_stack.pop();
                                //    //            if (ts == null)
                                //    //            {
                                //    //                ts = new tradeSummary();
                                //    //            }
                                //    //            ts.id = this.live_sellorders[i];
                                //    //            ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //    //            //ts.maker_orderid = this.live_sellorders[i];
                                //    //            this.tempTradeSummaries[ts.id] = ts;
                                //    //        }
                                //    //    }
                                //    //    else
                                //    //    {
                                //    //        this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ordersize_ask[i], this.asks[i], sellOrdersSide, null, true, false, vr_markup.ToString("F2"));
                                //    //        using (var tslock = this.tradeSummary_lock.getlock())
                                //    //        {
                                //    //            tradeSummary ts = this.oManager.TS_stack.pop();
                                //    //            if (ts == null)
                                //    //            {
                                //    //                ts = new tradeSummary();
                                //    //            }
                                //    //            ts.id = this.live_sellorders[i];
                                //    //            ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //    //            //ts.maker_orderid = this.live_sellorders[i];
                                //    //            this.tempTradeSummaries[ts.id] = ts;
                                //    //        }
                                //    //    }
                                //    //}
                                //}
                                //else
                                //{
                                //    this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ask_orders[i].quantity, this.ask_orders[i].price, positionSide.NONE, null, true, false, vr_markup.ToString("F2"));
                                //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    {
                                //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //        if (ts == null)
                                //        {
                                //            ts = new tradeSummary();
                                //        }
                                //        ts.id = this.live_sellorders[i];
                                //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //        //ts.maker_orderid = this.live_sellorders[i];
                                //        this.tempTradeSummaries[ts.id] = ts;
                                //    }
                                //}
                                //if (this.maker.marginTrade)
                                //{
                                //    if (this.maker.longPosition.available >= this.ordersize_ask[i])
                                //    {
                                //        this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ordersize_ask[i], this.asks[i], positionSide.Long, null, true, false, vr_markup.ToString("F2"));
                                //        using (var tslock = this.tradeSummary_lock.getlock())
                                //        {
                                //            tradeSummary ts = this.oManager.TS_stack.pop();
                                //            if (ts == null)
                                //            {
                                //                ts = new tradeSummary();
                                //            }
                                //            ts.id = this.live_sellorders[i];
                                //            ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //            //ts.maker_orderid = this.live_sellorders[i];
                                //            this.tempTradeSummaries[ts.id] = ts;
                                //        }
                                //    }
                                //    else
                                //    {
                                //        this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ordersize_ask[i], this.asks[i], positionSide.Short, null, true, false, vr_markup.ToString("F2"));
                                //        using (var tslock = this.tradeSummary_lock.getlock())
                                //        {
                                //            tradeSummary ts = this.oManager.TS_stack.pop();
                                //            if (ts == null)
                                //            {
                                //                ts = new tradeSummary();
                                //            }
                                //            ts.id = this.live_sellorders[i];
                                //            ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //            //ts.maker_orderid = this.live_sellorders[i];
                                //            this.tempTradeSummaries[ts.id] = ts;
                                //        }
                                //    }
                                //}
                                //else
                                //{
                                //    this.live_sellorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Sell, orderType.Limit, this.ordersize_ask[i], this.asks[i], positionSide.NONE, null, true, false, vr_markup.ToString("F2"));
                                //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    {
                                //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //        if (ts == null)
                                //        {
                                //            ts = new tradeSummary();
                                //        }
                                //        ts.id = this.live_sellorders[i];
                                //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //        //ts.maker_orderid = this.live_sellorders[i];
                                //        this.tempTradeSummaries[ts.id] = ts;
                                //    }
                                //}
                                this.current_asks[i] = this.ask_orders[i].price;
                                this.stg_orders.Add(this.live_sellorders[i]);
                                this.stg_orders_dict[this.live_sellorders[i]] = this.ask_orders[i].quantity;
                                this.live_sellorder_time = current;
                            }
                            if (this.bid_orders[i].price > 0 && this.bid_orders[i].quantity > 0 && (newBuyOrder & (1 << i)) > 0)
                            {
                                this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.bid_orders[i].quantity, this.bid_orders[i].price, this.bid_orders[i].pos_side, null, true, false, vr_markup.ToString("F2"));
                                using (var tslock = this.tradeSummary_lock.getlock())
                                {
                                    tradeSummary ts = this.oManager.TS_stack.pop();
                                    if (ts == null)
                                    {
                                        ts = new tradeSummary();
                                    }
                                    ts.id = this.live_buyorders[i];
                                    ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                    //ts.maker_orderid = this.live_buyorders[i];
                                    this.tempTradeSummaries[ts.id] = ts;
                                }
                                //if (this.maker.marginTrade)
                                //{
                                //    this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.bid_orders[i].quantity, this.bid_orders[i].price, buyOrdersSide, null, true, false, vr_markup.ToString("F2"));
                                //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    {
                                //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //        if (ts == null)
                                //        {
                                //            ts = new tradeSummary();
                                //        }
                                //        ts.id = this.live_buyorders[i];
                                //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //        //ts.maker_orderid = this.live_buyorders[i];
                                //        this.tempTradeSummaries[ts.id] = ts;
                                //    }
                                //    //if (this.maker.marginLong)
                                //    //{
                                //    //    if (buyOrdersSide != positionSide.NONE)
                                //    //    {
                                //    //        //Expect the position side be configured correctly for both spot and margin
                                //    //        this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.bid_orders[i].quantity, this.bid_orders[i].price, buyOrdersSide, null, true, false, vr_markup.ToString("F2"));
                                //    //        using (var tslock = this.tradeSummary_lock.getlock())
                                //    //        {
                                //    //            tradeSummary ts = this.oManager.TS_stack.pop();
                                //    //            if (ts == null)
                                //    //            {
                                //    //                ts = new tradeSummary();
                                //    //            }
                                //    //            ts.id = this.live_buyorders[i];
                                //    //            ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //    //            //ts.maker_orderid = this.live_buyorders[i];
                                //    //            this.tempTradeSummaries[ts.id] = ts;
                                //    //        }
                                //    //    }
                                //    //    else
                                //    //    {
                                //    //        addLog("Invalid position side", logType.WARNING);
                                //    //    }
                                //    //}
                                //    //else
                                //    //{
                                //    //    this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.bid_orders[i].quantity, this.bid_orders[i].price, buyOrdersSide, null, true, false, vr_markup.ToString("F2"));
                                //    //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    //    {
                                //    //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //    //        if (ts == null)
                                //    //        {
                                //    //            ts = new tradeSummary();
                                //    //        }
                                //    //        ts.id = this.live_buyorders[i];
                                //    //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //    //        //ts.maker_orderid = this.live_buyorders[i];
                                //    //        this.tempTradeSummaries[ts.id] = ts;
                                //    //    }
                                //    //}
                                //}
                                //else
                                //{
                                //    this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.bid_orders[i].quantity, this.bid_orders[i].price, positionSide.NONE, null, true, false, vr_markup.ToString("F2"));
                                //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    {
                                //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //        if (ts == null)
                                //        {
                                //            ts = new tradeSummary();
                                //        }
                                //        ts.id = this.live_buyorders[i];
                                //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //        //ts.maker_orderid = this.live_buyorders[i];
                                //        this.tempTradeSummaries[ts.id] = ts;
                                //    }
                                //}
                                //if (this.maker.marginTrade)
                                //{
                                //    if (this.maker.shortPosition.available >= this.bid_orders[i].quantity)
                                //    {
                                //        this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.bid_orders[i].quantity, this.bid_orders[i].price, positionSide.Short, null, true, false, vr_markup.ToString("F2"));
                                //        using (var tslock = this.tradeSummary_lock.getlock())
                                //        {
                                //            tradeSummary ts = this.oManager.TS_stack.pop();
                                //            if (ts == null)
                                //            {
                                //                ts = new tradeSummary();
                                //            }
                                //            ts.id = this.live_buyorders[i];
                                //            ++this.tradeid_num;
                                //            ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //            //ts.maker_orderid = this.live_buyorders[i];
                                //            this.tempTradeSummaries[ts.id] = ts;
                                //        }
                                //    }
                                //    else
                                //    {
                                //        this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.bid_orders[i].quantity, this.bid_orders[i].price, positionSide.Long, null, true, false, vr_markup.ToString("F2"));
                                //        using (var tslock = this.tradeSummary_lock.getlock())
                                //        {
                                //            tradeSummary ts = this.oManager.TS_stack.pop();
                                //            if (ts == null)
                                //            {
                                //                ts = new tradeSummary();
                                //            }
                                //            ts.id = this.live_buyorders[i];
                                //            ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //            //ts.maker_orderid = this.live_buyorders[i];
                                //            this.tempTradeSummaries[ts.id] = ts;
                                //        }
                                //    }
                                //}
                                //else
                                //{
                                //    this.live_buyorders[i] = await this.oManager.placeNewSpotOrder(this.maker, orderSide.Buy, orderType.Limit, this.bid_orders[i].quantity, this.bid_orders[i].price, positionSide.NONE, null, true, false, vr_markup.ToString("F2"));
                                //    using (var tslock = this.tradeSummary_lock.getlock())
                                //    {
                                //        tradeSummary ts = this.oManager.TS_stack.pop();
                                //        if (ts == null)
                                //        {
                                //            ts = new tradeSummary();
                                //        }
                                //        ts.id = this.live_buyorders[i];
                                //        ts.setPricingInfo(this.name, this.maker.symbol_market, this.taker.symbol_market, this.markups[i] + vr_markup, 0, taker_VR / Math.Sqrt(this.taker.RV_minute * 60) * 1_000_000, this.skew_point, this.skewWidening, this.maker.marginDiscount, this.taker.marginDiscount);
                                //        //ts.maker_orderid = this.live_buyorders[i];
                                //        this.tempTradeSummaries[ts.id] = ts;
                                //    }
                                //}
                                this.current_bids[i] = this.bid_orders[i].price;
                                this.stg_orders.Add(this.live_buyorders[i]);
                                this.stg_orders_dict[this.live_buyorders[i]] = this.bid_orders[i].quantity;
                                this.live_buyorder_time = current;
                            }
                        }
                    }
                }
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

            if(ins.market == market.coincheck)
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
            //decimal diff_amount = this.maker.baseBalance.total + this.taker.baseBalance.total - this.baseCcyQuantity;
            decimal diff_amount = this.maker.net_pos + this.taker.net_pos;
            addLog("Strategy[" + this.name + "] Adjusting the position diff_amount:" + diff_amount.ToString("N" + this.taker.quantity_scale));
            orderSide side = orderSide.Sell;
            if(diff_amount < 0)
            {
                diff_amount *= -1;
                side = orderSide.Buy;
            }
            diff_amount = Math.Round(diff_amount / this.taker.quantity_unit) * this.taker.quantity_unit; 

            if(diff_amount >= this.taker.quantity_unit)
            {
                if (this.taker.marginTrade)
                {
                    if (side == orderSide.Buy)
                    {
                        if (this.taker.shortPosition.available >= diff_amount)
                        {
                            this.oManager.placeNewSpotOrder(this.taker, side, orderType.Market, diff_amount, 0, positionSide.Short, null, true);
                        }
                        else
                        {
                            this.oManager.placeNewSpotOrder(this.taker, side, orderType.Market, diff_amount, 0, positionSide.Long, null, true);
                        }
                    }
                    else if(side == orderSide.Sell)
                    {
                        if (this.taker.longPosition.available >= diff_amount)
                        {
                            this.oManager.placeNewSpotOrder(this.taker, side, orderType.Market, diff_amount, 0, positionSide.Long, null, true);
                        }
                        else
                        {
                            this.oManager.placeNewSpotOrder(this.taker, side, orderType.Market, diff_amount, 0, positionSide.Short, null, true);
                        }
                    }
                }
                else
                {
                    if ((side == orderSide.Buy && this.taker.quoteBalance.available > diff_amount * this.taker.bestask.Item1 * (decimal)1.05) || (side == orderSide.Buy && this.taker.baseBalance.available > diff_amount))
                    {
                        this.oManager.placeNewSpotOrder(this.taker, side, orderType.Market, diff_amount, 0, positionSide.NONE, null, true);
                    }
                    else
                    {
                        addLog("Insufficient availability. Symbol:" + this.taker.symbol_market, logType.WARNING);
                    }
                }
            }
        }

        public async Task checkLiveOrders()
        {
            List<string> maker_orders = new List<string>();
            List<string> taker_orders = new List<string>();

            using (var olock = this.oManager.order_lock.getlock())
            {
                foreach (var ord in this.oManager.live_orders)
                {
                    if (this.live_buyorders.Contains(ord.Key) == false && this.live_sellorders.Contains(ord.Key) == false && ord.Value.status == orderStatus.Open)
                    {
                        if (this.maker.symbol_market == ord.Value.symbol_market)
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
            }
            
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
            if (this.maker.net_pos - this.targetMakerPosition > this.maxMakerPosition * this.skewThreshold / 100)
            {
                skew_point = - this.maxSkew * ((this.maker.net_pos - this.targetMakerPosition) - this.maxMakerPosition * this.skewThreshold / 100) / (this.maxMakerPosition * this.oneSideThreshold / 100 - this.maxMakerPosition * this.skewThreshold / 100);
                if (skew_point < -this.maxSkew)
                {
                    skew_point = -this.maxSkew;
                }
            }
            else if (this.maker.net_pos - this.targetMakerPosition < - this.maxMakerPosition * this.skewThreshold / 100)
            {
                skew_point = this.maxSkew * (- (this.maker.net_pos - this.targetMakerPosition) - this.maxMakerPosition * this.skewThreshold / 100) / (this.maxMakerPosition * this.oneSideThreshold / 100 - this.maxMakerPosition * this.skewThreshold / 100);
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
            if (this.enabled && this.abook && trade.symbol + "@" + trade.market.ToString() == this.maker.symbol_market)
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
                using(var ulock = this.updating.getlock())
                {
                    string ord_id;
                    DataSpotOrderUpdate ord = null;
                    decimal quantity = 0;
                    switch (trade.side)
                    {
                        case orderSide.Buy:
                            using(var flock = this.fill_lock.getlock())
                            {
                                for (int i = 0; i < this.layers; ++i)
                                {
                                    ord_id = this.live_sellorders[i];
                                    using (var olock = this.oManager.order_lock.getlock())
                                    {
                                        if (this.oManager.orders.ContainsKey(ord_id))
                                        {
                                            ord = this.oManager.orders[ord_id];
                                        }
                                        else
                                        {
                                            ord = null;
                                        }
                                    }

                                    if (ord != null && ord.status == orderStatus.Open)
                                    {
                                        if (ord.order_price < trade.price && ord.update_time + this.onTrade_timeBuf < trade.filled_time)//Assuming those 2 times are from same clock. If the buy trade price is higher than our ask
                                        {
                                            if (this.executed_OrderIds.ContainsKey(ord.internal_order_id))
                                            {
                                                //Do nothing
                                            }
                                            else
                                            {
                                                this.live_sellorders[i] = "";
                                                decimal filled_quantity = ord.order_quantity - ord.filled_quantity;
                                                if (this.stg_orders_dict.ContainsKey(ord_id))
                                                {
                                                    filled_quantity = this.stg_orders_dict[ord_id];
                                                    this.stg_orders_dict[ord_id] = 0;
                                                }
                                                //decimal diff_amount = this.maker.baseBalance.total + this.taker.baseBalance.total - this.baseCcyQuantity;
                                                decimal diff_amount = this.maker.net_pos + this.taker.net_pos;
                                                if (DateTime.UtcNow - this.lastPosAdjustment > TimeSpan.FromSeconds(10))
                                                {
                                                    this.lastPosAdjustment = DateTime.UtcNow;
                                                    filled_quantity -= diff_amount;
                                                }

                                                if (filled_quantity > 0)
                                                {
                                                    //if (this.taker.marginTrade)
                                                    //{
                                                    //    if(this.taker.marginLong)
                                                    //    {
                                                    //        if (this.taker.shortPosition.available >= filled_quantity)
                                                    //        {
                                                    //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.Short, null, true, true, ord.internal_order_id);
                                                    //        }
                                                    //        else
                                                    //        {
                                                    //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.Long, null, true, true, ord.internal_order_id);
                                                    //        }
                                                    //    }
                                                    //    else
                                                    //    {
                                                    //        if (this.taker.shortPosition.available >= filled_quantity)
                                                    //        {
                                                    //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.Short, null, true, true, ord.internal_order_id);
                                                    //        }
                                                    //        else
                                                    //        {
                                                    //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true, true, ord.internal_order_id);
                                                    //        }
                                                    //    }
                                                    //}
                                                    //else
                                                    //{
                                                    //    this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true, true, ord.internal_order_id);
                                                    //}
                                                    this.placeHedgeOrder(this.taker, orderSide.Buy, filled_quantity, ord.internal_order_id);
                                                }
                                                this.last_filled_time_sell = DateTime.UtcNow;
                                                this.last_filled_time = this.last_filled_time_sell;
                                                //this.executed_Orders_old[ord.internal_order_id] = ord;
                                                this.executed_OrderIds[ord.internal_order_id] = fillType.onTrade;
                                                //ord.msg += "  onTrades at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                                                addLog(ord.ToString() + "  onTrades at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat));
                                            }
                                        }
                                    }
                                }
                            }
                            break;
                        case orderSide.Sell:
                            using(var flock = this.fill_lock.getlock())
                            {
                                for (int i = 0; i < this.layers; ++i)
                                {
                                    ord_id = this.live_buyorders[i];
                                    using (var olock = this.oManager.order_lock.getlock())
                                    {
                                        if (this.oManager.orders.ContainsKey(ord_id))
                                        {
                                            ord = this.oManager.orders[ord_id];
                                        }
                                        else
                                        {
                                            ord = null;
                                        }
                                    }
                                    if (ord != null && ord.status == orderStatus.Open)
                                    {
                                        if (ord.order_price > trade.price && ord.update_time + this.onTrade_timeBuf < trade.filled_time)//Assuming those 2 times are from same clock. If the sell trade price is lower than our bid
                                        {
                                            //if (this.executed_Orders_old.ContainsKey(ord.internal_order_id))
                                            if (this.executed_OrderIds.ContainsKey(ord.internal_order_id))
                                            {
                                                //Do nothing
                                            }
                                            else
                                            {
                                                this.live_buyorders[i] = "";
                                                decimal filled_quantity = ord.order_quantity - ord.filled_quantity;
                                                if (this.stg_orders_dict.ContainsKey(ord_id))
                                                {
                                                    filled_quantity = this.stg_orders_dict[ord_id];
                                                    this.stg_orders_dict[ord_id] = 0;
                                                }
                                                //decimal diff_amount = this.maker.baseBalance.total + this.taker.baseBalance.total - this.baseCcyQuantity;
                                                decimal diff_amount = this.maker.net_pos + this.taker.net_pos;
                                                if (DateTime.UtcNow - this.lastPosAdjustment > TimeSpan.FromSeconds(10))
                                                {
                                                    this.lastPosAdjustment = DateTime.UtcNow;
                                                    filled_quantity += diff_amount;
                                                }
                                                if (filled_quantity > 0)
                                                {
                                                    //if (this.taker.marginTrade)
                                                    //{
                                                    //    if(this.taker.marginLong)
                                                    //    {
                                                    //        if (this.taker.longPosition.available >= filled_quantity)
                                                    //        {
                                                    //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.Long, null, true, true, ord.internal_order_id);
                                                    //        }
                                                    //        else
                                                    //        {
                                                    //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.Short, null, true, true, ord.internal_order_id);
                                                    //        }
                                                    //    }
                                                    //    else
                                                    //    {
                                                    //        if (this.taker.baseBalance.available >= filled_quantity)
                                                    //        {
                                                    //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true, true, ord.internal_order_id);
                                                    //        }
                                                    //        else
                                                    //        {
                                                    //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.Short, null, true, true, ord.internal_order_id);
                                                    //        }
                                                    //    }
                                                    //}
                                                    //else
                                                    //{
                                                    //    this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true, true, ord.internal_order_id);
                                                    //}
                                                    this.placeHedgeOrder(this.taker, orderSide.Sell, filled_quantity, ord.internal_order_id);
                                                }
                                                this.last_filled_time_buy = DateTime.UtcNow;
                                                this.last_filled_time = this.last_filled_time_buy;
                                                //this.executed_Orders_old[ord.internal_order_id] = ord;
                                                this.executed_OrderIds[ord.internal_order_id] = fillType.onTrade;
                                                //ord.msg += "  onTrades at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                                                addLog(ord.ToString() + "  onTrades at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat));
                                            }
                                        }
                                    }
                                }
                            }
                            break;
                    }
                }
            }
        }
        public void onMakerQuotes(DataOrderBook quote)
        {
            if (this.enabled && this.abook && quote.symbol + "@" + quote.market.ToString() == this.maker.symbol_market)
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
                //decimal diff_amount = this.maker.baseBalance.total + this.taker.baseBalance.total - this.baseCcyQuantity;
                decimal diff_amount = this.maker.net_pos + this.taker.net_pos;
                using (var ulock = this.updating.getlock())
                {
                    string ord_id;
                    DataSpotOrderUpdate ord;
                    for (int i = 0; i < this.layers; ++i)
                    {
                        bool exit = true;
                        ord_id = this.live_sellorders[i];
                        using (var olock = this.oManager.order_lock.getlock())
                        {
                            if (this.oManager.orders.ContainsKey(ord_id))
                            {
                                ord = this.oManager.orders[ord_id];
                            }
                            else
                            {
                                ord = null;
                            }
                        }

                        
                        if (ord != null && ord.status == orderStatus.Open)
                        {
                            if (ord.update_time < quote.orderbookTime && quote.asks.ContainsKey(ord.order_price) && quote.asks[ord.order_price] == 0)
                            {
                                using(var flock = this.fill_lock.getlock())
                                {
                                    //if (this.executed_Orders_old.ContainsKey(ord.internal_order_id))
                                    if (this.executed_OrderIds.ContainsKey(ord.internal_order_id))
                                    {
                                        //Do nothing
                                    }
                                    else
                                    {
                                        decimal filled_quantity = ord.order_quantity - ord.filled_quantity;
                                        if (this.stg_orders_dict.ContainsKey(ord_id))
                                        {
                                            filled_quantity = this.stg_orders_dict[ord_id];
                                            this.stg_orders_dict[ord_id] = 0;
                                        }
                                        if (DateTime.UtcNow - this.lastPosAdjustment > TimeSpan.FromSeconds(10))
                                        {
                                            this.lastPosAdjustment = DateTime.UtcNow;
                                            filled_quantity -= diff_amount;
                                        }

                                        if (filled_quantity > 0)
                                        {
                                            //if (this.taker.marginTrade)
                                            //{
                                            //    if(this.taker.marginLong)
                                            //    {
                                            //        if (this.taker.shortPosition.available >= filled_quantity)
                                            //        {
                                            //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.Short, null, true);
                                            //        }
                                            //        else
                                            //        {
                                            //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.Long, null, true);
                                            //        }
                                            //    }
                                            //    else
                                            //    {
                                            //        if (this.taker.shortPosition.available >= filled_quantity)
                                            //        {
                                            //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.Short, null, true);
                                            //        }
                                            //        else
                                            //        {
                                            //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true);
                                            //        }
                                            //    }
                                            //}
                                            //else
                                            //{
                                            //    this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true);
                                            //}
                                            this.placeHedgeOrder(this.taker, orderSide.Buy, filled_quantity, ord.internal_order_id);

                                        }
                                        this.live_sellorders[i] = "";
                                        this.last_filled_time_sell = DateTime.UtcNow;
                                        this.last_filled_time = this.last_filled_time_sell;
                                        //this.executed_Orders_old[ord.internal_order_id] = ord;
                                        this.executed_OrderIds[ord.internal_order_id] = fillType.onQuotes;
                                        //ord.msg += "  onMakerQuotes at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                                        addLog(ord.ToString() + "  onMakerQuotes at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat));
                                    }
                                }
                                
                                exit = false;
                            }
                        }
                        ord_id = this.live_buyorders[i];
                        using (var olock = this.oManager.order_lock.getlock())
                        {
                            if (this.oManager.orders.ContainsKey(ord_id))
                            {
                                ord = this.oManager.orders[ord_id];
                            }
                            else
                            {
                                ord = null;
                            }
                        }
                        if (ord != null && ord.status == orderStatus.Open)
                        {
                            if (ord.update_time < quote.orderbookTime && quote.bids.ContainsKey(ord.order_price) && quote.bids[ord.order_price] == 0)
                            {
                                using(var flock = this.fill_lock.getlock())
                                {
                                    //if (this.executed_Orders_old.ContainsKey(ord.internal_order_id))
                                    if (this.executed_OrderIds.ContainsKey(ord.internal_order_id))
                                    {
                                        //Do nothing
                                    }
                                    else
                                    {
                                        decimal filled_quantity = ord.order_quantity - ord.filled_quantity;
                                        if (this.stg_orders_dict.ContainsKey(ord_id))
                                        {
                                            filled_quantity = this.stg_orders_dict[ord_id];
                                            this.stg_orders_dict[ord_id] = 0;
                                        }
                                        if (DateTime.UtcNow - this.lastPosAdjustment > TimeSpan.FromSeconds(10))
                                        {
                                            this.lastPosAdjustment = DateTime.UtcNow;
                                            filled_quantity += diff_amount;
                                        }
                                        if (filled_quantity > 0)
                                        {
                                            //if (this.taker.marginTrade)
                                            //{
                                            //    if(this.taker.marginLong)
                                            //    {
                                            //        if (this.taker.longPosition.available >= filled_quantity)
                                            //        {
                                            //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.Long, null, true);
                                            //        }
                                            //        else
                                            //        {
                                            //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.Short, null, true);
                                            //        }
                                            //    }
                                            //    else
                                            //    {
                                            //        if (this.taker.baseBalance.available >= filled_quantity)
                                            //        {
                                            //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true);
                                            //        }
                                            //        else
                                            //        {
                                            //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.Short, null, true);
                                            //        }
                                            //    }
                                            //}
                                            //else
                                            //{
                                            //    this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true);
                                            //}
                                            this.placeHedgeOrder(this.taker, orderSide.Sell, filled_quantity, ord.internal_order_id);
                                        }
                                        this.live_buyorders[i] = "";
                                        this.last_filled_time_buy = DateTime.UtcNow;
                                        this.last_filled_time = this.last_filled_time_buy;
                                        //this.executed_Orders_old[ord.internal_order_id] = ord;
                                        this.executed_OrderIds[ord.internal_order_id] = fillType.onQuotes;
                                        //ord.msg += "  onMakerQuotes at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                                        addLog(ord.ToString() + "  onMakerQuotes at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat));
                                    }
                                }
                                exit = false;
                            }
                        }
                        if(exit)
                        {
                            break;
                        }
                    }
                }
                
            }
        }
        public void onOrdUpdate(DataSpotOrderUpdate ord, DataSpotOrderUpdate prev)
        {
            if (this.enabled && this.abook)
            {
                if(ord.status == orderStatus.Filled)
                {
                    if (this.stg_orders.Contains(ord.internal_order_id) == false && this.stg_orders.Contains(ord.market + ord.order_id))
                    {
                        if(ord.symbol_market == this.maker.symbol_market)
                        {
                            this.stg_orders.Add(ord.market + ord.order_id);
                            this.stg_orders_dict[ord.market + ord.order_id] = ord.order_quantity - ord.filled_quantity;
                        }
                        else
                        {
                            return;
                        }
                    }
                    //decimal diff_amount = this.maker.baseBalance.total + this.taker.baseBalance.total - this.baseCcyQuantity;
                    decimal diff_amount = this.maker.net_pos + this.taker.net_pos;

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

                    using(var flock = this.fill_lock.getlock())
                    {
                        //if (this.executed_Orders_old.ContainsKey(ord.internal_order_id) || this.executed_Orders_old.ContainsKey(ord.market + ord.order_id))
                        if (this.executed_OrderIds.ContainsKey(ord.internal_order_id) || this.executed_OrderIds.ContainsKey(ord.market + ord.order_id))
                        {
                            //Do nothing
                        }
                        else
                        {
                            decimal filled_quantity;
                            if (ord == prev)
                            {
                                filled_quantity = ord.filled_quantity;
                            }
                            else
                            {
                                filled_quantity = ord.filled_quantity - prev.filled_quantity;
                            }

                            if (this.stg_orders_dict.ContainsKey(ord.internal_order_id))
                            {
                                this.stg_orders_dict[ord.internal_order_id] -= filled_quantity;
                            }

                            if (DateTime.UtcNow - this.lastPosAdjustment > TimeSpan.FromSeconds(10))
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

                            filled_quantity = Math.Round(filled_quantity / this.taker.quantity_unit) * this.taker.quantity_unit;

                            switch (ord.side)
                            {
                                case orderSide.Buy:
                                    if (filled_quantity > 0)
                                    {
                                        //if (this.taker.marginTrade)
                                        //{
                                        //    if(this.taker.marginLong)
                                        //    {
                                        //        if (this.taker.longPosition.available >= filled_quantity)
                                        //        {
                                        //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.Long, null, true, false, ord.internal_order_id);
                                        //        }
                                        //        else
                                        //        {
                                        //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.Short, null, true, false, ord.internal_order_id);
                                        //        }
                                        //    }
                                        //    else
                                        //    {
                                        //        if (this.taker.baseBalance.available >= filled_quantity)
                                        //        {
                                        //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true, false, ord.internal_order_id);
                                        //        }
                                        //        else
                                        //        {
                                        //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.Short, null, true, false, ord.internal_order_id);
                                        //        }
                                        //    }
                                        //}
                                        //else
                                        //{
                                        //    this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true, false, ord.internal_order_id);
                                        //}
                                        this.placeHedgeOrder(this.taker, orderSide.Sell, filled_quantity, ord.internal_order_id);
                                    }
                                    else
                                    {
                                        addLog("Filled quantity is 0.");
                                        addLog(ord.ToString());
                                        addLog(prev.ToString());
                                        addLog((ord.filled_quantity - prev.filled_quantity).ToString());
                                        addLog("diff amount:" + (diff_amount).ToString());
                                        addLog(this.taker.quantity_unit.ToString());
                                    }
                                    this.last_filled_time_buy = DateTime.UtcNow;
                                    this.last_filled_time = this.last_filled_time_buy;
                                    break;
                                case orderSide.Sell:

                                    if (filled_quantity > 0)
                                    {
                                        //if (this.taker.marginTrade)
                                        //{
                                        //    if(this.taker.marginLong)
                                        //    {
                                        //        if (this.taker.shortPosition.available >= filled_quantity)
                                        //        {
                                        //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.Short, null, true, false, ord.internal_order_id);
                                        //        }
                                        //        else
                                        //        {
                                        //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.Long, null, true, false, ord.internal_order_id);
                                        //        }
                                        //    }
                                        //    else
                                        //    {
                                        //        if (this.taker.shortPosition.available >= filled_quantity)
                                        //        {
                                        //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.Short, null, true, false, ord.internal_order_id);
                                        //        }
                                        //        else
                                        //        {
                                        //            this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true, false, ord.internal_order_id);
                                        //        }
                                        //    }
                                        //}
                                        //else
                                        //{
                                        //    this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true, false, ord.internal_order_id);
                                        //}
                                        this.placeHedgeOrder(this.taker, orderSide.Buy, filled_quantity, ord.internal_order_id);
                                    }
                                    else
                                    {
                                        addLog("Filled quantity is 0.");
                                        addLog(ord.ToString());
                                        addLog(prev.ToString());
                                        addLog((ord.filled_quantity - prev.filled_quantity).ToString());
                                        addLog(this.taker.quantity_unit.ToString());
                                    }
                                    this.last_filled_time_sell = DateTime.UtcNow;
                                    this.last_filled_time = this.last_filled_time_sell;
                                    break;
                            }
                            this.executed_OrderIds[ord.internal_order_id] = fillType.onOrderUpdate;
                            //ord.msg += "  onOrdUpdate at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat);
                            addLog(ord.ToString() + "  onOrdUpdate at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat));
                        }
                    }
                }
                else if(ord.status == orderStatus.Canceled)
                {
                    if(this.stg_orders_dict.ContainsKey(ord.internal_order_id))
                    {
                        this.stg_orders_dict[ord.internal_order_id] = 0;
                    }
                    using (var tslock = this.tradeSummary_lock.getlock())
                    {
                        if (this.tempTradeSummaries.ContainsKey(ord.internal_order_id))
                        {
                            tradeSummary ts = this.tempTradeSummaries[ord.internal_order_id];
                            this.tempTradeSummaries.Remove(ord.internal_order_id);
                            ts.init();
                            this.oManager.TS_stack.push(ts);
                        }
                    }

                }
            }
        }

        public async Task onFill(DataFill fill)
        {
            if (this.enabled && this.abook)//onFill should work even if the strategy disabled
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
                if (fill.market == this.maker.market)
                {
                    decimal diff_amount = this.maker.net_pos + this.taker.net_pos;
                    decimal filled_quantity = fill.quantity;
                    DataSpotOrderUpdate ord;

                    if (this.stg_orders.Contains(fill.internal_order_id) == false && this.stg_orders.Contains(fill.market + fill.order_id))
                    {
                        //Even if the order id is not registered, if the market and symbol meets the strategy handle the fill.
                        //Register the market + order_id
                        if (fill.symbol_market == this.maker.symbol_market)
                        {
                            this.stg_orders.Add(fill.market + fill.order_id);
                        }
                        else
                        {
                            return;
                        }
                    }

                    using(var flock = this.fill_lock.getlock())
                    {
                        if ((this.executed_OrderIds.ContainsKey(fill.internal_order_id) && this.executed_OrderIds[fill.internal_order_id] != fillType.onFill)
                        || (this.executed_OrderIds.ContainsKey(fill.market + fill.order_id) && this.executed_OrderIds[fill.market + fill.order_id] != fillType.onFill))
                        {
                            //Do nothing
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

                            if (this.stg_orders_dict.ContainsKey(fill.internal_order_id))
                            {
                                this.stg_orders_dict[fill.internal_order_id] -= fill.quantity;
                            }

                            filled_quantity = Math.Round(filled_quantity / this.taker.quantity_unit) * this.taker.quantity_unit;

                            if (filled_quantity > 0)
                            {
                                switch (fill.side)
                                {
                                    case orderSide.Buy:
                                        //if (this.taker.marginTrade)
                                        //{
                                        //    if(this.taker.marginLong)
                                        //    {
                                        //        //if (this.taker.longPosition.available >= filled_quantity)
                                        //        if (this.taker.longPosition.available > 0)
                                        //        {
                                        //            decimal closing = filled_quantity;
                                        //            decimal opening = 0;
                                        //            if(closing > this.taker.longPosition.available)
                                        //            {
                                        //                closing = this.taker.longPosition.available;
                                        //                opening = filled_quantity - closing; 

                                        //            }
                                        //            await this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, closing, 0, positionSide.Long, null, true, false, fill.internal_order_id);
                                        //            await this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, opening, 0, positionSide.Short, null, true, false, fill.internal_order_id);
                                        //        }
                                        //        else
                                        //        {
                                        //            await this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.Short, null, true, false, fill.internal_order_id);
                                        //        }
                                        //    }
                                        //    else
                                        //    {
                                        //        //if (this.taker.baseBalance.available >= filled_quantity)
                                        //        //{
                                        //        //    await this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true, false, fill.internal_order_id);
                                        //        //}
                                        //        if (this.taker.baseBalance.available > 0)
                                        //        {
                                        //            decimal closing = filled_quantity;
                                        //            decimal opening = 0;
                                        //            if (closing > this.taker.baseBalance.available)
                                        //            {
                                        //                closing = this.taker.baseBalance.available;
                                        //                opening = filled_quantity - closing;

                                        //            }
                                        //            await this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, closing, 0, positionSide.NONE, null, true, false, fill.internal_order_id);
                                        //            await this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, opening, 0, positionSide.Short, null, true, false, fill.internal_order_id);
                                        //        }
                                        //        else
                                        //        {
                                        //            await this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.Short, null, true, false, fill.internal_order_id);
                                        //        }
                                        //    }
                                        //}
                                        //else
                                        //{
                                        //    await this.oManager.placeNewSpotOrder(this.taker, orderSide.Sell, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true, false, fill.internal_order_id);
                                        //}
                                        await this.placeHedgeOrder(this.taker, orderSide.Sell, filled_quantity, fill.internal_order_id);
                                        using (var olock = this.oManager.order_lock.getlock())
                                        {
                                            if (this.oManager.orders.ContainsKey(fill.internal_order_id))
                                            {
                                                ord = this.oManager.orders[fill.internal_order_id];
                                                if (ord.order_quantity - ord.filled_quantity <= fill.quantity || ord.status == orderStatus.Filled)
                                                {
                                                    addLog(ord.ToString() + ",  onFill at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat) + fill.internal_order_id + " " + fill.msg);
                                                    this.last_filled_time_buy = DateTime.UtcNow;
                                                    this.last_filled_time = this.last_filled_time_buy;
                                                }
                                            }
                                            else
                                            {
                                                this.addLog("[OnFill]Order Not Found:" + fill.ToString(), Enums.logType.WARNING);
                                                if (fill.quantity == this.ToBsize)
                                                {
                                                    this.last_filled_time_buy = DateTime.UtcNow;
                                                    this.last_filled_time = this.last_filled_time_buy;
                                                }
                                            }
                                        }

                                        break;
                                    case orderSide.Sell:
                                        //if (this.taker.marginTrade)
                                        //{
                                        //    if(this.taker.marginLong)
                                        //    {
                                        //        //if (this.taker.shortPosition.available >= filled_quantity)
                                        //        //{
                                        //        //    await this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.Short, null, true, false, fill.internal_order_id);
                                        //        //}
                                        //        if (this.taker.shortPosition.available > 0)
                                        //        {
                                        //            decimal closing = filled_quantity;
                                        //            decimal opening = 0;
                                        //            if (closing > this.taker.shortPosition.available)
                                        //            {
                                        //                closing = this.taker.shortPosition.available;
                                        //                opening = filled_quantity - closing;

                                        //            }
                                        //            await this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, closing, 0, positionSide.Short, null, true, false, fill.internal_order_id);
                                        //            await this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, opening, 0, positionSide.Long, null, true, false, fill.internal_order_id);
                                        //        }
                                        //        else
                                        //        {
                                        //            await this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.Long, null, true, false, fill.internal_order_id);
                                        //        }
                                        //    }
                                        //    else
                                        //    {
                                        //        if (this.taker.shortPosition.available > 0)
                                        //        {
                                        //            decimal closing = filled_quantity;
                                        //            decimal opening = 0;
                                        //            if (closing > this.taker.shortPosition.available)
                                        //            {
                                        //                closing = this.taker.shortPosition.available;
                                        //                opening = filled_quantity - closing;

                                        //            }
                                        //            await this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, closing, 0, positionSide.Short, null, true, false, fill.internal_order_id);
                                        //            await this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, opening, 0, positionSide.NONE, null, true, false, fill.internal_order_id);
                                        //        }
                                        //        else
                                        //        {
                                        //            await this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true, false, fill.internal_order_id);
                                        //        }
                                        //    }

                                        //}
                                        //else
                                        //{
                                        //    await this.oManager.placeNewSpotOrder(this.taker, orderSide.Buy, orderType.Market, filled_quantity, 0, positionSide.NONE, null, true, false, fill.internal_order_id);
                                        //}

                                        await this.placeHedgeOrder(this.taker, orderSide.Buy, filled_quantity, fill.internal_order_id);
                                        using (var olock = this.oManager.order_lock.getlock())
                                        {
                                            if (this.oManager.orders.ContainsKey(fill.internal_order_id))
                                            {
                                                ord = this.oManager.orders[fill.internal_order_id];

                                                if (ord.order_quantity - ord.filled_quantity <= fill.quantity || ord.status == orderStatus.Filled)
                                                {
                                                    this.last_filled_time_sell = DateTime.UtcNow;
                                                    this.last_filled_time = this.last_filled_time_sell;
                                                    addLog(ord.ToString() + ",  onFill at " + DateTime.UtcNow.ToString(GlobalVariables.tmMsecFormat) + fill.internal_order_id + " " + fill.msg);
                                                }
                                            }
                                            else
                                            {
                                                this.addLog("[OnFill]Order Not Found:" + fill.ToString(), Enums.logType.WARNING);
                                                if (fill.quantity == this.ToBsize)
                                                {
                                                    this.last_filled_time_sell = DateTime.UtcNow;
                                                    this.last_filled_time = this.last_filled_time_sell;
                                                }
                                            }
                                        }

                                        break;
                                }
                                //Once onFill triggered, onFill has the responsibility to hedge the entire order.
                                this.executed_OrderIds[fill.internal_order_id] = fillType.onFill;
                            }
                        }
                    }

                    using (var tslock = this.tradeSummary_lock.getlock())
                    {
                        if (this.tempTradeSummaries.ContainsKey(fill.internal_order_id))
                        {
                            tradeSummary ts = this.tempTradeSummaries[fill.internal_order_id];
                            this.tempTradeSummaries.Remove(fill.internal_order_id);
                            ts.setMakerFill(fill, this.maker.getAdjustedTimeStamp(fill.filled_time.Value));
                            this.tradeSummaries[ts.id] = ts;
                        }
                        else if (this.tradeSummaries.ContainsKey(fill.internal_order_id))
                        {
                            tradeSummary ts = this.tradeSummaries[fill.internal_order_id];
                            ts.setMakerFill(fill, this.maker.getAdjustedTimeStamp(fill.filled_time.Value));
                            this.tradeSummaries[ts.id] = ts;
                        }
                    }
                }
                else if(fill.symbol_market == this.taker.symbol_market)
                {
                    using (var tslock = this.tradeSummary_lock.getlock())
                    {
                        if (this.tempTradeSummaries.ContainsKey(fill.msg))
                        {
                            tradeSummary ts = this.tempTradeSummaries[fill.msg];
                            this.tempTradeSummaries.Remove(fill.msg);
                            ts.setTakerFill(fill, this.taker.getAdjustedTimeStamp(fill.filled_time.Value));
                            ts.calcPnL();
                            this.tradeSummaries[ts.id] = ts;
                        }
                        else if (this.tradeSummaries.ContainsKey(fill.msg))
                        {
                            tradeSummary ts = this.tradeSummaries[fill.msg];
                            ts.setTakerFill(fill, this.taker.getAdjustedTimeStamp(fill.filled_time.Value));
                            ts.calcPnL();
                            this.tradeSummaries[ts.id] = ts;
                        }
                    }
                }
            }
        }
        public async Task placeHedgeOrder(Instrument ins,orderSide side, decimal quantity,string msg)
        {
            decimal closing = quantity;
            decimal availability ;
            decimal opening = 0;

            if (ins.marginTrade)
            {
                if (ins.marginLong)
                {
                    switch (side)
                    {
                        case orderSide.Buy:
                            using(var plock = ins.shortPosition.balance_lock.getlock())
                            {
                                availability = ins.shortPosition.available;
                                if (availability > 0)
                                {
                                    if (closing > availability)
                                    {
                                        closing = availability;
                                        opening = quantity - closing;
                                    }
                                    if(closing <= 0)
                                    {
                                        addLog($"shortPosition closing is 0 quantity:{quantity} available:{availability}");
                                    }
                                    if(opening < 0)
                                    {
                                        addLog($"shortPosition opening is 0 quantity:{quantity} available:{availability}");
                                    }
                                }
                            }
                            if (ins.shortPosition.available > 0)
                            {
                                if(closing > 0)
                                {
                                    await this.oManager.placeNewSpotOrder(ins, side, orderType.Market, closing, 0, positionSide.Short, null, true, false, msg);
                                }
                                if(opening > 0)
                                {
                                    await this.oManager.placeNewSpotOrder(ins, side, orderType.Market, opening, 0, positionSide.Long, null, true, false, msg);
                                }
                            }
                            else
                            {
                                await this.oManager.placeNewSpotOrder(ins, side, orderType.Market, quantity, 0, positionSide.Long, null, true, false, msg);
                            }
                            break;
                        case orderSide.Sell:
                            using (var plock = ins.longPosition.balance_lock.getlock())
                            {
                                availability = ins.longPosition.available;
                                if (availability > 0)
                                {
                                    if (closing > availability)
                                    {
                                        closing = availability;
                                        opening = quantity - closing;
                                    }
                                    if (closing <= 0)
                                    {
                                        addLog($"longPosition closing is 0 quantity:{quantity} available:{availability}");
                                    }
                                    if (opening < 0)
                                    {
                                        addLog($"longPosition opening is 0 quantity:{quantity} available:{availability}");
                                    }
                                }
                            }
                            if (ins.longPosition.available > 0)
                            {
                                if(closing > 0)
                                {
                                    await this.oManager.placeNewSpotOrder(ins, side, orderType.Market, closing, 0, positionSide.Long, null, true, false, msg);
                                }
                                if(opening > 0)
                                {
                                    await this.oManager.placeNewSpotOrder(ins, side, orderType.Market, opening, 0, positionSide.Short, null, true, false, msg);
                                }
                            }
                            else
                            {
                                await this.oManager.placeNewSpotOrder(ins, side, orderType.Market, quantity, 0, positionSide.Short, null, true, false, msg);
                            }
                            break;
                    }
                }
                else
                {
                    switch (side)
                    {
                        case orderSide.Buy:
                            using (var plock = ins.shortPosition.balance_lock.getlock())
                            {
                                availability = ins.shortPosition.available;
                                if (availability > 0)
                                {
                                    if (closing > availability)
                                    {
                                        closing = availability;
                                        opening = quantity - closing;
                                    }
                                    if (closing <= 0)
                                    {
                                        addLog($"shortPosition closing is 0 quantity:{quantity} available:{availability}");
                                    }
                                    if (opening < 0)
                                    {
                                        addLog($"shortPosition opening is 0 quantity:{quantity} available:{availability}");
                                    }
                                }
                            }
                            if (ins.shortPosition.available > 0)
                            {
                                if(closing > 0)
                                {
                                    await this.oManager.placeNewSpotOrder(ins, side, orderType.Market, closing, 0, positionSide.Short, null, true, false, msg);
                                }
                                if(opening > 0)
                                {
                                    await this.oManager.placeNewSpotOrder(ins, side, orderType.Market, opening, 0, positionSide.NONE, null, true, false, msg);
                                }
                            }
                            else
                            {
                                await this.oManager.placeNewSpotOrder(ins, side, orderType.Market, quantity, 0, positionSide.NONE, null, true, false, msg);
                            }
                            break;
                        case orderSide.Sell:
                            using (var plock = ins.baseBalance.balance_lock.getlock())
                            {
                                availability = ins.baseBalance.available;
                                if (availability > 0)
                                {
                                    if (closing > availability)
                                    {
                                        closing = availability;
                                        opening = quantity - closing;
                                    }

                                    if (closing <= 0)
                                    {
                                        addLog($"baseBalance closing is 0 quantity:{quantity} available:{availability}");
                                    }
                                    if (opening < 0)
                                    {
                                        addLog($"baseBalance opening is 0 quantity:{quantity} available:{availability}");
                                    }
                                }
                            }
                            if (ins.baseBalance.available > 0)
                            {
                                if(closing > 0)
                                {
                                    await this.oManager.placeNewSpotOrder(ins, side, orderType.Market, closing, 0, positionSide.NONE, null, true, false, msg);
                                }
                                if(opening > 0)
                                {
                                    await this.oManager.placeNewSpotOrder(ins, side, orderType.Market, opening, 0, positionSide.Short, null, true, false, msg);
                                }
                            }
                            else
                            {
                                await this.oManager.placeNewSpotOrder(ins, side, orderType.Market, quantity, 0, positionSide.Short, null, true, false, msg);
                            }
                            break;
                    }
                }
            }
            else
            {
                await this.oManager.placeNewSpotOrder(ins, side, orderType.Market, quantity, 0, positionSide.NONE, null, true, false, msg);
            }
        }
        public async Task placeHedgeOrders(orderSide side, Dictionary<string,decimal> quantities)
        {
            Instrument ins = null;
            foreach(var qt in quantities)
            {
                if(oManager.Instruments.ContainsKey(qt.Key))
                {
                    ins = oManager.Instruments[qt.Key];
                    this.oManager.placeNewSpotOrder(ins, side, orderType.Market, qt.Value, 0, positionSide.NONE, null, true);
                }
                else
                {
                    addLog("[placeHedgeOrders]Instrument not found. symbol market:" + qt.Value, logType.WARNING);
                }
            }
        }

        public bool getWeightedAvgPrice(orderSide side, List<decimal> quantities, List<decimal> prices,Dictionary<string,decimal> qty_symbolmarket)
        {
            bool ret = false;
            int layer = 0;
            decimal quantity;
            decimal cumQuantity = 0;
            decimal weightedPrice = 0;
            if (quantities.Count > layer)
            {
                quantity = quantities[layer];
            }
            else
            {
                return ret;
            }

            decimal currentAsk = 0;
            decimal currentBid = 0;
            decimal currentQty = 0;

            //Get All locks
            foreach (var ins in this.takers.Values)
            {
                using(ins.quotes_lock.getlock())
                {
                    if (currentAsk == 0 || currentAsk > ins.bestask.Item1)
                    {
                        currentAsk = ins.bestask.Item1;
                    }
                    if (currentBid == 0 || currentBid < ins.bestbid.Item1)
                    {
                        currentBid = ins.bestbid.Item1;
                    }
                }
            }
            switch (side)
            {
                case orderSide.Buy:
                    break;
                case orderSide.Sell:
                    break;
            }
            return ret;
        }
        public void updateAggregatedQuotes(int depth = 5)
        {
            decimal currentAsk = 0;
            decimal currentBid = 0;

            int currentBidDepth = 0;
            int currentAskDepth = 0;

            //Get All locks
            foreach(var ins in this.takers.Values)
            {
                using(var qlock = ins.quotes_lock.getlock())
                {
                    if (currentAsk == 0 || currentAsk > ins.bestask.Item1)
                    {
                        currentAsk = ins.bestask.Item1;
                    }
                    if (currentBid == 0 || currentBid < ins.bestbid.Item1)
                    {
                        currentBid = ins.bestbid.Item1;
                    }
                }
            }
            //this.bids.Clear();
            //this.asks.Clear();
            bool exit = true;
            while(true)
            {
                exit = true;
                foreach (var ins in this.takers.Values)
                {
                    using (var qlock = ins.quotes_lock.getlock())
                    {
                        if (currentAskDepth < depth)
                        {
                            if (currentAsk <= ins.asks.Keys.Max())
                            {
                                exit = false;
                                if (ins.asks.ContainsKey(currentAsk) && ins.asks[currentAsk] > 0)
                                {
                                    if (this.agg_asks.ContainsKey(currentAsk))
                                    {
                                        this.agg_asks[currentAsk] += ins.asks[currentAsk];
                                    }
                                    else
                                    {
                                        this.agg_asks[currentAsk] = ins.asks[currentAsk];
                                    }
                                }
                            }
                        }
                        if (currentBidDepth < depth)
                        {
                            if (currentBid >= ins.bids.Keys.Min())
                            {
                                exit = false;
                                if (ins.bids.ContainsKey(currentBid) && ins.bids[currentBid] > 0)
                                {
                                    if (this.agg_bids.ContainsKey(currentBid))
                                    {
                                        this.agg_bids[currentBid] += ins.bids[currentBid];
                                    }
                                    else
                                    {
                                        this.agg_bids[currentBid] = ins.bids[currentBid];
                                    }
                                }
                            }
                        }
                    }
                }
                if(this.agg_asks.ContainsKey(currentAsk))
                {
                    ++currentAskDepth;
                }
                if (this.agg_bids.ContainsKey(currentBid))
                {
                    ++currentBidDepth;
                }
                if(exit || (currentAskDepth >= depth && currentBidDepth >= depth))
                {
                    break;
                }
                currentAsk += this.taker_min_quote_unit;
                currentBid -= this.taker_min_quote_unit;
            }
        }

        public void update_micurve(MarketImpact mi)
        {
            int sign = 1;
            if (mi.fill_side == orderSide.Sell)
            {
                sign = -1;
            }
            foreach (var item in mi.prices)
            {
                this.market_impact_curve[item.Key] = (this.market_impact_curve[item.Key] * this.mi_volume + sign * (item.Value - mi.filled_price) / mi.filled_price * 1_000_000 * mi.filled_quantity) / (this.mi_volume + mi.filled_quantity);
            }
            this.mi_volume += mi.filled_quantity;
        }

        public void addLog(string line, Enums.logType logtype = Enums.logType.INFO)
        {
            this._addLog("[Strategy]" + line, logtype);
        }
    }
}
