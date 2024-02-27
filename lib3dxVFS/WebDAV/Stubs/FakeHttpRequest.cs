using NWebDav.Server.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lib3dxVFS.WebDAV.Stubs
{
    public class FakeHttpRequest : IHttpRequest
    {
        public FakeHttpRequest(Uri url, int depth)
        {
            Url = url;
            Depth = depth;
        }

        public string HttpMethod => "GET";

        public Uri Url { get; private set; }
        public int Depth { get; }

        public string RemoteEndPoint => "";

        public IEnumerable<string> Headers => Array.Empty<string>();

        public Stream Stream { get; } = new MemoryStream();

        public string GetHeaderValue(string header)
        {
            switch (header)
            {
                case "Depth":
                    return $"{Depth}";
                default:
                    break;
            }
            return string.Empty;
        }
    }
}
