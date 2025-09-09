using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Crypto_Trading
{
    public class Strategy
    {
        public decimal markup;
        public decimal baseCcyQuantity;
        public decimal ToBsize;

        public decimal intervalAfterFill;
        public decimal modThreshold;

        public decimal skewThreshold;
        public decimal oneSideThreshold;

        public bool abook;
        public bool includeFee;

        public string taker_symbol_market;
        public string maker_symbol_market;

        public Instrument? taker;
        public Instrument? maker;
        public Strategy() 
        {
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

        }

        public void readStrategyFile(string jsonfilename)
        {
            string fileContent = File.ReadAllText(jsonfilename);
            using JsonDocument doc = JsonDocument.Parse(fileContent);
            var root = doc.RootElement;

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
    }
}
