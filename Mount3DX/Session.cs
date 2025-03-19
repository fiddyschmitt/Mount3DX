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
        _3dxServer _3dxServer;
        _3dxStore? _3dxStore;
        WebdavHost? webdavHost;
        public string ComputedUNC { get; protected set; }

        public event EventHandler<ProgressEventArgs>? InitialisationProgress;
        public event EventHandler<FinishedEventArgs>? InitialisationFinished;
        public event EventHandler<ProgressEventArgs>? SessionError;

        public Session(_3dxServer _3dxServer, Settings settings, uint maxMetadataSizeInBytes)
        {
            this._3dxServer = _3dxServer;
            Settings = settings;
            MaxMetadataSizeInBytes = maxMetadataSizeInBytes;
            ComputedUNC = settings.Vfs.GetComputedUNC();
        }

        public void Start()
        {
            Log.WriteLine("Session starting");

            var isLoggedIn = _3dxServer.Ping(CancellationToken.None);

            if (isLoggedIn)
            {
                Log.WriteLine("Currently logged in.");
            }
            else
            {
                //the cookies didn't work. Let's log in again
                InitialisationProgress?.Invoke(this, new ProgressEventArgs()
                {
                    Nature = ProgressEventArgs.EnumNature.Neutral,
                    Message = "Signing into 3DX..."
                });

                Log.WriteLine("Server did not respond to ping. Logging in.");
                var loginResult = _3dxServer.LogIn();

                if (loginResult)
                {
                    Log.WriteLine("Logged in successfully.");
                    isLoggedIn = true;
                }
                else
                {
                    Log.WriteLine("Login unsuccessful.");
                }

            }

            if (!isLoggedIn)
            {
                Log.WriteLine("Could not acquire cookies. Displaying error message.");

                Stop();

                InitialisationFinished?.Invoke(this, new FinishedEventArgs()
                {
                    Success = false,
                    Message = "Login unsuccessful. Please check the URL."
                });

                return;
            }

            Log.WriteLine("Pinging server.");

            var pingSuccessful = _3dxServer.Ping(CancellationToken.None);

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
                    _3dxServer,
                    Settings.Vfs.WebDavServerUrl,
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
