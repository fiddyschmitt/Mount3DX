using libCommon.Events;
using libVFS.WebDAV.Stores;
using libWebDAV;
using NWebDav.Server.Stores;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Mount3DX
{
    public class Session
    {
        private Settings settings;

        _3dxStore? _3dxStore;
        WebdavHost? webdavHost;

        public event EventHandler<ProgressEventArgs>? Progress;
        public event EventHandler<FinishedEventArgs>? Finished;

        public Session(Settings settings)
        {
            this.settings = settings;
        }

        public void Start()
        {
            Progress?.Invoke(this, new ProgressEventArgs()
            {
                Nature = ProgressEventArgs.EnumNature.Neutral,
                Message = "Connecting to 3dx"
            });

            var _3dxStore = new _3dxStore(
                settings._3dx.ServerUrl,
                settings._3dx.CookiesString,
                settings._3dx.KeepAlive,
                settings._3dx.KeepAliveIntervalMinutes,
                settings._3dx.QueryThreads,
                Progress);

            /*
            var pingSuccessful = _3dxStore.Ping();

            if (!pingSuccessful)
            {
                Stop();

                Progress?.Invoke(this, new ProgressEventArgs()
                {
                    Nature = ProgressEventArgs.EnumNature.Bad,
                    Message = "The 3DX server could not be contacted. Please check the URL and Cookies."
                });

                return false;
            }
            */

            Progress?.Invoke(this, new ProgressEventArgs()
            {
                Nature = ProgressEventArgs.EnumNature.Neutral,
                Message = "Initiating WebDAV server"
            });

            try
            {
                //var store = new libVFS.WebDAV.Stores.DiskStore(@"C:\", false);
                webdavHost = new WebdavHost(settings.Vfs.WebDavServerUrl, _3dxStore);
            }
            catch (Exception ex)
            {
                Stop();

                Finished?.Invoke(this, new FinishedEventArgs()
                {
                    Success = false,
                    Message = $"Error while initialising WebDAV server: {ex.Message}"
                });

                return;
            }

            Task.Factory.StartNew(() =>
            {
                webdavHost.Start();
            });

            var computedUNC = @"\\localhost@SSL@11000\DavWWWRoot";
            computedUNC = @"\\localhost@11000\DavWWWRoot";

            //NetworkDriveUtility.MapNetworkDrive(settings.Vfs.MapToDriveLetter, computedUNC);

            //Process.Start("explorer.exe", settings.Vfs.MapToDriveLetter);
            Process.Start("explorer.exe", computedUNC);

            Finished?.Invoke(this, new FinishedEventArgs()
            {
                Success = true,
            });
        }

        public void Stop()
        {
            try
            {
                webdavHost?.Stop();
            }
            catch { }

            try
            {
                _3dxStore?.Close();
            }
            catch { }
        }
    }
}
