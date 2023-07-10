using lib3dx;
using libCommon;
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
        _3dxStore? _3dxStore;
        WebdavHost? webdavHost;
        public string ComputedUNC { get; protected set; }

        public event EventHandler<ProgressEventArgs>? Progress;
        public event EventHandler<FinishedEventArgs>? Finished;

        public static string? cookies;

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
                Message = "Please log in to 3DX using the Firefox browser that was opened..."
            });

            Progress?.Invoke(this, new ProgressEventArgs()
            {
                Nature = ProgressEventArgs.EnumNature.Neutral,
                Message = "Connecting to 3DX"
            });

            var loginUrl = Settings._3dx.ServerUrl.UrlCombine("common/emxNavigator.jsp");

            if (cookies == null)
            {
                cookies = _3dxLogin.GetSessionCookies(loginUrl);
            }
            else
            {
                //see if the cookies work
                _3dxServer = new _3dxServer(Settings._3dx.ServerUrl, cookies);

                var currentCookiesWork = _3dxServer.Ping();
                if (!currentCookiesWork)
                {
                    //the cookies didn't work. Let's log in again
                    cookies = _3dxLogin.GetSessionCookies(loginUrl);
                }
            }

            _3dxServer = new _3dxServer(Settings._3dx.ServerUrl, cookies);
            var pingSuccessful = _3dxServer.Ping();

            if (!pingSuccessful)
            {
                Stop();

                Finished?.Invoke(this, new FinishedEventArgs()
                {
                    Success = false,
                    Message = "The 3DX server could not be contacted. Please check the URL."
                });

                return;
            }

            if (Settings._3dx.KeepAliveIntervalMinutes > 0)
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
                _3dxStore = new _3dxStore(
                    Settings._3dx.ServerUrl,
                    cookies,
                    Settings._3dx.QueryThreads,
                    Progress);

                if (Settings._3dx.RefreshIntervalMinutes > 0)
                {
                    _3dxStore.StartRefresh(Settings._3dx.RefreshIntervalMinutes);
                }

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

            try
            {
                _3dxStore?.StopRefresh();
            }
            catch { }
        }
    }
}
