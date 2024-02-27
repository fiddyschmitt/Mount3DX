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
        private readonly uint MaxMetadataSizeInBytes;
        _3dxServer? _3dxServer;
        _3dxStore? _3dxStore;
        WebdavHost? webdavHost;
        public string ComputedUNC { get; protected set; }

        public event EventHandler<ProgressEventArgs>? InitialisationProgress;
        public event EventHandler<FinishedEventArgs>? InitialisationFinished;
        public event EventHandler<ProgressEventArgs>? SessionError;

        public static _3dxCookies? Cookies { get; set; }

        public Session(Settings settings, uint maxMetadataSizeInBytes)
        {
            Settings = settings;
            MaxMetadataSizeInBytes = maxMetadataSizeInBytes;
            ComputedUNC = settings.Vfs.GetComputedUNC();
        }

        public void Start()
        {
            Log.WriteLine("Session starting");

            if (Cookies == null)
            {
                InitialisationProgress?.Invoke(this, new ProgressEventArgs()
                {
                    Nature = ProgressEventArgs.EnumNature.Neutral,
                    Message = "Please log in to 3DX using the browser that was opened..."
                });

                Log.WriteLine("Acquiring cookies.");
                var cookiesResult = _3dxLogin.GetSessionCookies(Settings._3dx.ServerUrl);

                if (cookiesResult.Success)
                {
                    Log.WriteLine("Cookies acquired successfully.");
                    Cookies = cookiesResult.Cookies;
                }
                else
                {
                    Log.WriteLine("Cookies not acquired successfully.");
                }
            }
            else
            {
                Log.WriteLine("Cookies present. Pinging server.");

                //see if the cookies work
                _3dxServer = new _3dxServer(Settings._3dx.ServerUrl, Cookies);
                var currentCookiesWork = _3dxServer.Ping(Cookies, CancellationToken.None);

                if (currentCookiesWork)
                {
                    Log.WriteLine("Cookies are still valid.");
                }
                else
                {
                    //the cookies didn't work. Let's log in again
                    InitialisationProgress?.Invoke(this, new ProgressEventArgs()
                    {
                        Nature = ProgressEventArgs.EnumNature.Neutral,
                        Message = "Please log in to 3DX using the browser that was opened..."
                    });

                    Log.WriteLine("Server did not respond to ping. Acquiring new cookies.");
                    var cookiesResult = _3dxLogin.GetSessionCookies(Settings._3dx.ServerUrl);

                    if (cookiesResult.Success)
                    {
                        Log.WriteLine("Cookies acquired successfully.");
                        Cookies = cookiesResult.Cookies;
                    }
                    else
                    {
                        Log.WriteLine("Cookies not acquired successfully.");
                    }
                }
            }

            if (Cookies == null)
            {
                Log.WriteLine("Could not acquire cookies. Displaying error message.");

                Stop();

                InitialisationFinished?.Invoke(this, new FinishedEventArgs()
                {
                    Success = false,
                    Message = "Cookies could not be acquired. Please check the URL."
                });

                return;
            }

            Log.WriteLine("Pinging server.");
            _3dxServer = new _3dxServer(Settings._3dx.ServerUrl, Cookies);
            var pingSuccessful = _3dxServer.Ping(Cookies, CancellationToken.None);

            if (pingSuccessful)
            {
                Log.WriteLine("Server responded to ping.");
            }
            else
            {
                Log.WriteLine("Server did not respond to ping. Displaying error message.");

                Stop();

                InitialisationFinished?.Invoke(this, new FinishedEventArgs()
                {
                    Success = false,
                    Message = "The 3DX server could not be contacted. Please check the URL."
                });

                return;
            }

            if (Settings._3dx.KeepAliveIntervalMinutes > 0)
            {
                _3dxServer.KeepAliveFailed += SessionError;
                _3dxServer.StartKeepAlive(Settings._3dx.KeepAliveIntervalMinutes);
            }

            InitialisationProgress?.Invoke(this, new ProgressEventArgs()
            {
                Nature = ProgressEventArgs.EnumNature.Neutral,
                Message = "Initialising WebDAV server"
            });



            try
            {
                Log.WriteLine($"Initialising {nameof(_3dxStore)}.");

                _3dxStore = new _3dxStore(
                    Settings._3dx.ServerUrl,
                    Settings.Vfs.WebDavServerUrl,
                    Cookies,
                    Settings._3dx.QueryThreads,
                    MaxMetadataSizeInBytes,
                    InitialisationProgress);

                Log.WriteLine($"{nameof(_3dxStore)} initialised.");

                if (Settings._3dx.RefreshIntervalMinutes > 0)
                {
                    _3dxStore.RefreshFailed += SessionError;
                    _3dxStore.StartRefresh(Settings._3dx.RefreshIntervalMinutes);
                }

                Log.WriteLine("Initialising WebDAV server");
                webdavHost = new WebdavHost(Settings.Vfs.WebDavServerUrl, _3dxStore);
                Log.WriteLine("WebDAV server initialised.");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while initialising WebDAV server:{Environment.NewLine}{ex}");

                Stop();

                InitialisationFinished?.Invoke(this, new FinishedEventArgs()
                {
                    Success = false,
                    Message = $"Error while initialising WebDAV server: {ex.Message}"
                });

                return;
            }

            Task.Factory.StartNew(() =>
            {
                Log.WriteLine("Starting WebDAV server");

                try
                {
                    webdavHost.Start();
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"Error while starting {nameof(WebdavHost)}:{Environment.NewLine}{ex}");

                    SessionError?.Invoke(this, new ProgressEventArgs()
                    {
                        Message = $"Error while starting {nameof(WebdavHost)}: {ex.Message}",
                        Nature = ProgressEventArgs.EnumNature.Bad
                    });
                }

                Log.WriteLine("WebDAV server finished");
            });

            //NetworkDriveUtility.MapNetworkDrive(settings.Vfs.MapToDriveLetter, computedUNC);

            //Process.Start("explorer.exe", settings.Vfs.MapToDriveLetter);
            Process.Start("explorer.exe", ComputedUNC);

            InitialisationFinished?.Invoke(this, new FinishedEventArgs()
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

            Log.WriteLine($"Session stopped.");
        }
    }
}
