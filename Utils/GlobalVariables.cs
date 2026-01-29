using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Utils
{
    public class GlobalVariables
    {
        public const string ver_major = "2";
        public const string ver_minor = "1";
        public const string ver_patch = "3";

        public static string tmMsecFormat = "yyyy-MM-dd HH:mm:ss.fff";

        public static List<double> MI_period = [0,0.5, 1, 2, 3, 4, 5, 10, 30, 60, 120, 180, 240, 300, 600];
        //public static List<double> MI_period = [0, 0.5, 1, 2, 3, 4, 5, 10, 30, 60, 120, 180, 240, 300, 600, 900, 1200, 1800, 3600];
    }
}
