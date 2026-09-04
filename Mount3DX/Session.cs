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
        //Fatal: the session has to stop
        public event EventHandler<ProgressEventArgs>? SessionError;

        //Non-fatal: a background refresh failed (the previous document list is still being served)
        //or has since recovered
        public event EventHandler<ProgressEventArgs>? SessionStatus;

        public Session(_3dxServer _3dxServer, Settings settings, uint maxMetadataSizeInBytes)
        {
            this._3dxServer = _3dxServer;
            Settings = settings;
            MaxMetadataSizeInBytes = maxMetadataSizeInBytes;
            ComputedUNC = settings.Vfs.GetComputedUNC();
        }

        public void Start()
        {
            //Start runs on a bare background task, so anything that escapes here would be an
            //unobserved exception and the UI would wait forever for InitialisationFinished
            try
            {
                StartCore();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while starting the session:{Environment.NewLine}{ex}");

                Stop();

                InitialisationFinished?.Invoke(this, new FinishedEventArgs()
                {
                    Success = false,
                    Message = $"Error while starting: {ex.Message}"
                });
            }
        }

        void StartCore()
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

            //Being logged in already implies a successful ping: either the one above, or the one
            //LogIn() finishes with. No need for a third round trip.

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
                    _3dxStore.RefreshStatus += (sender, args) => SessionStatus?.Invoke(this, args);
                    _3dxStore.StartRefresh(Settings._3dx.RefreshIntervalMinutes);
                }

                Log.WriteLine("Initialising WebDAV server");
                webdavHost = new WebdavHost(Settings.Vfs.WebDavServerUrl, _3dxStore);

                //Start synchronously so we know the server is actually listening (and surface a
                //bind failure such as the port already being in use) before opening Explorer.
                Log.WriteLine("Starting WebDAV server");
                webdavHost.Start();
                Log.WriteLine("WebDAV server started.");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while starting WebDAV server:{Environment.NewLine}{ex}");

                Stop();

                InitialisationFinished?.Invoke(this, new FinishedEventArgs()
                {
                    Success = false,
                    Message = $"Error while starting WebDAV server: {ex.Message}"
                });

                return;
            }

            //The server is now listening, so it is safe to open the virtual drive in Explorer
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
            catch (Exception ex)
            {
                Log.WriteLine($"Error while stopping the WebDAV host: {ex.Message}");
            }

            try
            {
                _3dxServer?.StopKeepAlive();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while stopping the keep-alive task: {ex.Message}");
            }

            try
            {
                _3dxStore?.StopRefresh();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while stopping the refresh task: {ex.Message}");
            }

            Log.WriteLine($"Session stopped.");
        }
    }
}
