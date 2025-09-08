using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Crypto_Trading
{
    public class OrderManager
    {
        private OrderManager() { }


        private static OrderManager _instance;
        private static readonly object _lockObject = new object();

        public static OrderManager GetInstance()
        {
            lock (_lockObject)
            {
                if (_instance == null)
                {
                    _instance = new OrderManager();
                }
                return _instance;
            }
        }
    }
}

