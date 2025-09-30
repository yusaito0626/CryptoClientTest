using Crypto_Clients;
using CryptoClients.Net.Enums;
using Crypto_Trading;

using System.Threading;

Console.WriteLine("Hello, World!");

Crypto_Clients.Crypto_Clients cl = Crypto_Clients.Crypto_Clients.GetInstance();

string baseCcy = "BTC";
string quoteCcy = "USDT";
string[] markets = [Exchange.Binance];

Dictionary<string, Instrument> instruments = new Dictionary<string, Instrument>();

Instrument ins = new Instrument();
ins.setSymbolMarket(baseCcy + quoteCcy, Exchange.Bybit);
instruments[ins.symbol_market] = ins;
ins = new Instrument();
ins.setSymbolMarket(baseCcy + "-" + quoteCcy, Exchange.Coinbase);
instruments[ins.symbol_market] = ins;
ins = new Instrument();
ins.setSymbolMarket(baseCcy + quoteCcy, Exchange.Binance);
instruments[ins.symbol_market] = ins;

//await cl.subscribeTrades(markets,baseCcy, quoteCcy);
//await cl.subscribeOrderBook(markets, baseCcy, quoteCcy);
//cl.readCredentials(Exchange.Coinbase, "C:\\Users\\yusai\\coinbase_viewonly.json");
//cl.readCredentials(Exchange.Bybit, "C:\\Users\\yusai\\bybit_viewonly.json");
//await cl.subscribeBybitOrderBook(baseCcy, quoteCcy);
//await cl.subscribeCoinbaseOrderBook(baseCcy, quoteCcy);
//await cl.subscribeSpotOrderUpdates([Exchange.Bybit]);
//await cl.getBalance(markets);
Console.WriteLine("Done");
void logging()
{
    string filepath = "C:\\Users\\yusai\\log.csv";
    string symbol_market = "";
    using (FileStream f = new FileStream(filepath, FileMode.Create, FileAccess.Write))
    {
        using (StreamWriter s = new StreamWriter(f))
        {
            while (true)
            {
                DataOrderBook msg;
                string strMsg;
                while (cl.ordBookQueue.TryDequeue(out msg))
                {
                    symbol_market = msg.symbol.ToUpper() + "@" + msg.market;
                    if (instruments.ContainsKey(symbol_market))
                    {
                        ins = instruments[symbol_market];
                        ins.updateQuotes(msg);
                        strMsg = ins.ToString("Quote5");

                    }
                    else
                    {
                        strMsg = "[ERROR] The symbol market did not found. " + symbol_market;
                    }
                    Console.WriteLine(strMsg);
                    s.WriteLine(strMsg);
                    cl.pushToOrderBookStack(msg);
                }
                while (cl.strQueue.TryDequeue(out strMsg))
                {
                    Console.WriteLine(strMsg);
                    s.WriteLine(strMsg);
                    s.Flush();
                }
            }
        }
    }
}

System.Threading.Thread th = new Thread(logging);

Crypto_Trading.QuoteManager qManager = QuoteManager.GetInstance();

if(!qManager.initializeInstruments("c:\\users\\yusai\\crypto_master.csv"))
{
    Console.WriteLine("The file doesn't exist");
}

foreach(var item in qManager.instruments.Values)
{
    Console.WriteLine("Calling ToString");
    Console.WriteLine(item.ToString());
}

th.Start();
Thread.Sleep(2000);
Console.WriteLine("Now sending an order to Bybit");
Thread.Sleep(5000);
DataSpotOrderUpdate ordId = await cl.placeNewSpotOrder(Exchange.Bybit, "ETH", "USDT", orderSide.Buy, orderType.Limit, (decimal)0.001, 4000);
Console.WriteLine("Order ID:" + ordId.order_id);
Thread.Sleep(5000);
Console.WriteLine("Now cancelling an order to Bybit");
DataSpotOrderUpdate cancelling = await cl.placeCancelSpotOrder(Exchange.Bybit, "ETH", "USDT", ordId.order_id);
Console.WriteLine("Done Canceled ID:" + cancelling.order_id);
