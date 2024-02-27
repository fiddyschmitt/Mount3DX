using NWebDav.Server.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lib3dxVFS.WebDAV.Stubs
{
    public class FakeHttpResponse : IHttpResponse
    {
        public int Status { get; set; }
        public string StatusDescription { get; set; } = string.Empty;

        public Stream Stream { get; } = new MemoryStream();

        public void SetHeaderValue(string header, string value)
        {
        }
    }
}
