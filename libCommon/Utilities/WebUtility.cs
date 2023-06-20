using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCommon.Utilities
{
    public static class WebUtility
    {
        public static string HttpGet(string url, string? cookieString = null)
        {
            using var httpClient = new HttpClient();

            if (cookieString != null)
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookieString);
            }

            var content = httpClient.GetStringAsync(url).Result;
            return content;
        }

        public static string HttpPost(string url, StringContent? formData, string? cookieString = null)
        {
            using var httpClient = new HttpClient();

            if (cookieString != null)
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookieString);
            }

            var response = httpClient.PostAsync(url, formData).Result.Content.ReadAsStringAsync().Result;

            return response;
        }
    }
}
