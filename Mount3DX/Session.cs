using lib3dx;
using libVFS;
using libWebDAV;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mount3DX
{
    public class Session
    {
        private Settings settings;

        _3dxServer? _3dxServer;
        WebdavHost? webdavHost;

        public Session(Settings settings)
        {
            this.settings = settings;
        }

        public (bool StartedSuccessfully, string Message) Start()
        {
            /*
            var _3dxServer = new _3dxServer(
                settings._3dx.ServerUrl,
                settings._3dx.CookiesString,
                settings._3dx.KeepAlive,
                settings._3dx.KeepAliveIntervalMinutes);

            var pingSuccessful = _3dxServer.Ping();

            if (!pingSuccessful)
            {
                _3dxServer.Close();
                return (false, "The 3DX server could not be contacted. Please check the URL and Cookies.");
            }
            */

            webdavHost = new WebdavHost(settings.Vfs.WebDavServerUrl);

            Task.Factory.StartNew(() =>
            {
                webdavHost.Start();
            });

            var computedUNC = @"\\localhost@SSL@11000\DavWWWRoot";

            NetworkDriveUtility.MapNetworkDrive(settings.Vfs.MapToDriveLetter, computedUNC);
            Process.Start("explorer.exe", settings.Vfs.MapToDriveLetter);


            return (true, string.Empty);
        }

        public void Stop()
        {
            webdavHost?.Stop();
            _3dxServer?.Close();
        }
    }
}
