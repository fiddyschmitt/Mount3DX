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

            var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (cookieString != null)
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookieString);
            }

            var response = httpClient.Send(request);
            var responseStr = response.Content.ReadAsStringAsync().Result;

            return responseStr;
        }

        public static string HttpPost(string url, StringContent? formData, string? cookieString = null)
        {
            var httpClient = new HttpClient();

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = formData
            };

            if (cookieString != null)
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookieString);
            }

            var response = httpClient.Send(request);
            var responseStr = response.Content.ReadAsStringAsync().Result;

            return responseStr;
        }
    }
}
