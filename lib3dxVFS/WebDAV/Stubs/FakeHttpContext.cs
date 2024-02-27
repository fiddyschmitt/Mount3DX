using NWebDav.Server.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace lib3dxVFS.WebDAV.Stubs
{
    public class FakeHttpContext : IHttpContext
    {
        public FakeHttpContext(Uri requestUrl, int depth)
        {
            Request = new FakeHttpRequest(requestUrl, depth);
        }

        public IHttpRequest Request { get; }

        public IHttpResponse Response { get; } = new FakeHttpResponse();

        public IHttpSession? Session => null;

        public Task CloseAsync()
        {
            return Task.FromResult(true);
        }
    }
}
