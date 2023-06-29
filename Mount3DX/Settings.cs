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
        public string Cookies = "SERVERID=abc; JSESSIONID=def";
        public bool KeepAlive = true;
        public int KeepAliveIntervalMinutes = 5;
        public int QueryThreads = 8;
        public int RefreshIntervalMinutes = 30;
    }

    public class VfsSettings
    {
        public string WebDavServerUrl = "http://localhost:11000";
        //public string MapToDriveLetter = libCommon.Utilities.FileUtility.GetAvailableDriveLetter();

        public string GetComputedUNC()
        {
            //http://localhost:11000   ->  \\localhost@11000\DavWWWRoot 
            var result = $@"\\{WebDavServerUrl.Replace("http://", "").Replace(":", "@")}\DavWWWRoot";
            return result;
        }
    }

}
