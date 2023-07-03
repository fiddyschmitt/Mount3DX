using lib3dx;
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
        private readonly Settings Settings;

        _3dxServer? _3dxServer;
        WebdavHost? webdavHost;
        public string ComputedUNC { get; protected set; }

        public event EventHandler<ProgressEventArgs>? Progress;
        public event EventHandler<FinishedEventArgs>? Finished;

        public Session(Settings settings)
        {
            Settings = settings;
            ComputedUNC = settings.Vfs.GetComputedUNC();
        }

        public void Start()
        {
            Progress?.Invoke(this, new ProgressEventArgs()
            {
                Nature = ProgressEventArgs.EnumNature.Neutral,
                Message = "Connecting to 3DX"
            });

            _3dxServer = new _3dxServer(Settings._3dx.ServerUrl, Settings._3dx.Cookies);
            var pingSuccessful = _3dxServer.Ping();

            if (!pingSuccessful)
            {
                Stop();

                Finished?.Invoke(this, new FinishedEventArgs()
                {
                    Success = false,
                    Message = "The 3DX server could not be contacted. Please check the URL and Cookies."
                });

                return;
            }

            if (Settings._3dx.KeepAlive)
            {
                _3dxServer.StartKeepAlive(Settings._3dx.KeepAliveIntervalMinutes);
            }

            Progress?.Invoke(this, new ProgressEventArgs()
            {
                Nature = ProgressEventArgs.EnumNature.Neutral,
                Message = "Initiating WebDAV server"
            });

            try
            {
                var _3dxStore = new _3dxStore(
                    Settings._3dx.ServerUrl,
                    Settings._3dx.Cookies,
                    Settings._3dx.QueryThreads,
                    Progress);

                webdavHost = new WebdavHost(Settings.Vfs.WebDavServerUrl, _3dxStore);
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

            //NetworkDriveUtility.MapNetworkDrive(settings.Vfs.MapToDriveLetter, computedUNC);

            //Process.Start("explorer.exe", settings.Vfs.MapToDriveLetter);
            Process.Start("explorer.exe", ComputedUNC);

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
                _3dxServer?.StopKeepAlive();
            }
            catch { }
        }
    }
}
