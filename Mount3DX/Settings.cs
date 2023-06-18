using libCommon;
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
        public string CookiesString = "SERVERID=abc; JSESSIONID=12345";
        public bool KeepAlive = true;
        public int KeepAliveIntervalMinutes = 5;
    }

    public class VfsSettings
    {
        public string WebDavServerUrl = "https://localhost:11000";
        public string MapToDriveLetter = libVFS.Utility.GetAvailableDriveLetter();
    }

}
