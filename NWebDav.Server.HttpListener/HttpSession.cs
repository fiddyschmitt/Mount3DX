using System.Security.Principal;

using NWebDav.Server.Http;

namespace NWebDav.Server.HttpListener
{
    public class HttpSession : IHttpSession
    {
        public HttpSession(IPrincipal principal)
        {
            Principal = principal;
        }

        public IPrincipal Principal { get; }
    }
}
