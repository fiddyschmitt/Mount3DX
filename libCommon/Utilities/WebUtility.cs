using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace libCommon.Utilities
{
    public static class WebUtility
    {
        public static HttpClient NewHttpClientWithCompression()
        {
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };
            var httpClient = new HttpClient(handler);

            httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
            httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

            return httpClient;
        }

        public static string HttpGet(string url, string? cookieString = null)
        {
            var httpClient = new HttpClient();

            if (cookieString != null)
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookieString);
            }

            var content = httpClient.GetStringAsync(url).Result;
            return content;
        }

        public static string HttpPost(string url, StringContent? formData, string? cookieString = null)
        {
            var httpClient = new HttpClient();

            if (cookieString != null)
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookieString);
            }

            var response = httpClient.PostAsync(url, formData).Result.Content.ReadAsStringAsync().Result;

            return response;
        }
    }
}
