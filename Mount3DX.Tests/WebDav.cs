using System.Net;
using System.Xml.Linq;

namespace Mount3DX.Tests
{
    //Reading the drive over HTTP, as any WebDAV client does
    public static class WebDav
    {
        public sealed record Entry(string Name, bool IsCollection, long? Length);

        //A URL of the drive, each segment escaped
        public static string UrlOf(params string[] segments) => "/" + string.Join("/", segments.Select(Uri.EscapeDataString));

        //A listing of a folder (PROPFIND with Depth: 1)
        public static HttpRequestMessage PropFind(string url)
        {
            var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            request.Headers.Add("Depth", "1");
            return request;
        }

        public static async Task<List<Entry>> ListAsync(HttpClient http, string url)
        {
            using var request = PropFind(url);
            using var response = await http.SendAsync(request);
            return EntriesOf(response, await response.Content.ReadAsStringAsync(), url);
        }

        public static List<Entry> List(HttpClient http, string url)
        {
            using var request = PropFind(url);
            using var response = http.Send(request);
            return EntriesOf(response, new StreamReader(response.Content.ReadAsStream()).ReadToEnd(), url);
        }

        //The entries of the folder at url, without the folder itself
        static List<Entry> EntriesOf(HttpResponseMessage response, string body, string url)
        {
            Assert.AreEqual(HttpStatusCode.MultiStatus, response.StatusCode, $"PROPFIND {url}");

            XNamespace dav = "DAV:";
            var folder = UnescapedPath(url);

            return XDocument.Parse(body)
                        .Descendants(dav + "response")
                        .Select(entry =>
                        {
                            var href = entry.Element(dav + "href")!.Value;
                            var path = UnescapedPath(Uri.TryCreate(href, UriKind.Absolute, out var absolute) ? absolute.AbsolutePath : href);
                            var length = entry.Descendants(dav + "getcontentlength").FirstOrDefault()?.Value;

                            return (Path: path, Entry: new Entry(
                                                            path.Split('/').Last(),
                                                            entry.Descendants(dav + "collection").Any(),
                                                            long.TryParse(length, out var n) ? n : null));
                        })
                        .Where(item => item.Path != folder)
                        .Select(item => item.Entry)
                        .ToList();
        }

        //An unescaped path without the slashes at its ends
        static string UnescapedPath(string url) => Uri.UnescapeDataString(url).Trim('/');
    }
}
