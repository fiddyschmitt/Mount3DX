using libCommon;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mount3DX
{
    public class Settings
    {
        public _3dxSettings _3dx = new();

        public VfsSettings Vfs = new();
    };

    public class _3dxSettings
    {
        public string ServerUrl = "https://server/3dspace";
        public int RefreshIntervalMinutes = 30;
        public int QueryThreads = 8;
        public int KeepAliveIntervalMinutes = 5;

        //Whether each document folder includes the generated _link.url and _metadata.json files.
        //Applies immediately to a running session.
        public bool IncludeMetadataFiles = true;
    }

    //How the drive is served. Auto takes SMB where Windows can connect to it (11 24H2 or later)
    //and WebDAV otherwise; the other two insist. Each is the name of the settings group that
    //configures it, and settings.json may spell it in any case ("smb", "Smb" and "SMB" all do).
    public enum TransportMode
    {
        Auto,
        Smb,
        WebDAV,
    }

    public class VfsSettings
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public TransportMode Transport = TransportMode.Auto;

        public SmbSettings Smb = new();

        public WebDavSettings WebDAV = new();

        //Where a settings.json from before the WebDAV group (1.7.0 and earlier) kept the URL.
        //Read only; it is written back in its new place.
        [JsonProperty]
        string WebDavServerUrl { set => WebDAV.ServerUrl = value; }
    }

    public class SmbSettings
    {
        //The Windows client ties the port to the server name for every later connection to that
        //name, so the share gets a name of its own rather than "localhost", which other tools
        //(AnyFS among them) connect to on their own ports.
        public string Address = "127.0.0.1";

        public int Port = 1445;

        public string ShareName = "3DX";

        //How much of what has been read over SMB is kept on disk (the cache folder under the
        //Mount3DX folder in LocalAppData) for rereads. Files larger than this are still read in
        //full, a piece at a time.
        public int CacheSizeMB = 2048;

        public string GetComputedUNC()
        {
            return $@"\\{Address}\{ShareName}";
        }
    }

    public class WebDavSettings
    {
        public string ServerUrl = "http://localhost:11000";

        public string GetComputedUNC()
        {
            //http://localhost:11000   ->  \\localhost@11000\DavWWWRoot
            //https://localhost:11000  ->  \\localhost@SSL@11000\DavWWWRoot
            var uri = new Uri(ServerUrl);
            var ssl = uri.Scheme == Uri.UriSchemeHttps ? "@SSL" : "";
            var port = uri.IsDefaultPort ? "" : $"@{uri.Port}";
            var result = $@"\\{uri.Host}{ssl}{port}\DavWWWRoot";
            return result;
        }
    }
}
