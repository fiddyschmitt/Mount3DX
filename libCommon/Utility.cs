using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCommon
{
    public static class Utility
    {
        public static string DownloadString(string url)
        {
            using var client = new HttpClient();
            var result = client.GetStringAsync(url).Result;

            return result;
        }
    }
}
