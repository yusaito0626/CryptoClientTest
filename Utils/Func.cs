using Enums;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utils
{
    public class funcContainer : IDisposable
    {
        Action f;

        public funcContainer(Func<Action> starter)
        {
            f = starter();
        }
        public void Dispose()
        {
            var action = Interlocked.Exchange(ref f, null);
            action?.Invoke();
        }
    };

    public static class Functions
    {
        public static DateTime? convertToDateTime(string str, market m)
        {
            DateTime temp;
            switch (m)
            {
                case market.gmocoin:

                    if (DateTime.TryParseExact(str, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out temp))
                    {
                        return temp;
                    }
                    else if (DateTime.TryParseExact(str, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out temp))
                    {
                        return temp;
                    }
                    else
                    {
                        return null;
                    }
                    break;
                case market.coincheck:
                    return DateTime.Parse(str, null, System.Globalization.DateTimeStyles.RoundtripKind);
                    break;
                default:
                    return null;
                    break;
            }
        }
        public static DateTime? convertToDateTime(Int64 itime, market m)
        {
            switch (m)
            {
                case market.bitbank:
                    return DateTimeOffset.FromUnixTimeMilliseconds(itime).UtcDateTime;
                    break;
                case market.coincheck:
                    return DateTimeOffset.FromUnixTimeMilliseconds(itime).UtcDateTime;
                    break;
                default:
                    return null;
                    break;
            }
        }
    }
}
