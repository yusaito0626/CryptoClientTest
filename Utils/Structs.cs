using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utils
{
    public struct logEntry
    {
        public string logtype {  get; set; }
        public string msg { get; set; }
    }

    public struct masterInfo
    {
        public string symbol { get; set; }
        public string baseCcy { get; set; }
        public string quoteCcy { get; set; }
        public string market {  get; set; }
        public decimal taker_fee { get; set; }
        public decimal maker_fee { get; set; }
        public decimal price_unit { get; set; }
        public decimal quantity_unit { get; set; }
    }
    public struct strategySetting
    {
        public string name { get; set; }
        public string baseCcy { get; set; }
        public string quoteCcy { get; set; }
        public string taker_market { get; set; }
        public string maker_market { get; set; }
        public decimal markup { get; set; }
        public decimal min_markup { get; set; }
        public decimal max_skew { get; set; }
        public decimal skew_widening { get; set; }
        public decimal baseCcy_quantity { get; set; }
        public decimal ToBsize { get; set; }
        public decimal intervalAfterFill { get; set; }
        public decimal modThreshold { get; set; }
        public decimal skewThreshold { get; set; }
        public decimal oneSideThreshold { get; set; }
        public Boolean predictFill { get; set; }
    }
    public struct fillInfo
    {
        public string timestamp { get; set; }
        public string market { get; set; }
        public string symbol { get; set; }
        public string side { get; set; }
        public string fill_price { get; set; }
        public string quantity { get; set; }
        public string fee { get; set; }
    }
    public struct strategyInfo
    {
        public string name { get; set; }
        public string baseCcy { get; set; }
        public string quoteCcy { get; set; }
        public string maker_market { get; set; }
        public string taker_market { get; set; }
        public string maker_symbol_market { get; set; }
        public string taker_symbol_market { get; set; }

        public decimal spread { get; set; }
        public decimal skew { get; set; }

        public decimal ask { get; set; }
        public decimal askSize { get; set; }
        public decimal bid { get; set; }
        public decimal bidSize { get; set; }

        public decimal liquidity_ask { get; set; }
        public decimal liquidity_bid { get; set; }

        public decimal notionalVolume { get; set; }
        public decimal tradingPnL { get; set; }
        public decimal totalFee { get; set; }
        public decimal totalPnL { get; set; }
    }

    public struct instrumentInfo
    {
        public string symbol { get; set; }
        public string market { get; set; }
        public string symbol_market { get; set; }
        public string baseCcy { get; set; }
        public string quoteCcy { get; set; }

        public decimal last_price { get; set; }
        public decimal notional_buy { get; set; }
        public decimal notional_sell { get; set; }
        public decimal quantity_buy { get; set; }
        public decimal quantity_sell { get; set; }

        //balance
        public decimal baseCcy_total { get; set; }
        public decimal baseCcy_inuse { get; set; }
        public decimal quoteCcy_total { get; set; }
        public decimal quoteCcy_inuse { get; set; }

        //Execution
        public decimal my_quantity_buy { get; set; }
        public decimal my_notional_buy { get; set; }
        public decimal my_quantity_sell { get; set; }
        public decimal my_notional_sell { get; set; }

        public decimal quoteFee_total { get; set; }
        public decimal baseFee_total { get; set; }
    }

    public struct connecitonStatus
    {
        public string market { get; set; }
        public string publicState { get; set; }
        public string privateState { get; set; }
        public double avgRTT { get; set; }
    }

    public struct threadStatus
    {
        public string name { get; set; }
        public bool isRunning { get; set; }
        public double avgProcessingTime { get; set; }
    }
}
