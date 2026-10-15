using lib3dx;
using libCommon;
using libCommon.Events;
using libSMB;
using libVFS.WebDAV.Stores;
using libWebDAV;
using NWebDav.Server.Stores;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
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
        SmbHost? smbHost;
        public string ComputedUNC { get; protected set; }

        //The transport the drive is served over once started (the log says why, when it isn't the
        //one that was asked for)
        public TransportMode ActiveTransport { get; private set; } = TransportMode.WebDAV;

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

            //Before the documents are fetched, so that a transport that can't work is known (and
            //fallen back from) without a fetch being wasted on it
            StartTransport();

            try
            {
                Log.WriteLine($"Initialising {nameof(_3dxStore)}.");

                //SMB has no listing size limit, so the tree keeps its own shape there
                var listingLimit = ActiveTransport == TransportMode.SMB ? 0 : MaxMetadataSizeInBytes;

                _3dxStore = new _3dxStore(
                    _3dxServer,
                    Settings.Vfs.WebDavServerUrl,
                    Settings._3dx.QueryThreads,
                    listingLimit,
                    InitialisationProgress);

                Log.WriteLine($"{nameof(_3dxStore)} initialised.");

                if (Settings._3dx.RefreshIntervalMinutes > 0)
                {
                    _3dxStore.RefreshStatus += (sender, args) => SessionStatus?.Invoke(this, args);
                    _3dxStore.StartRefresh(Settings._3dx.RefreshIntervalMinutes);
                }

                if (smbHost != null)
                {
                    //the share has been up (and empty) since StartTransport; now it has the tree
                    smbHost.Source = _3dxStore;
                    Log.WriteLine($"Serving the document tree at {ComputedUNC} over SMB.");
                }
                else
                {
                    InitialisationProgress?.Invoke(this, new ProgressEventArgs()
                    {
                        Nature = ProgressEventArgs.EnumNature.Neutral,
                        Message = "Initialising WebDAV server"
                    });

                    Log.WriteLine("Initialising WebDAV server");
                    webdavHost = new WebdavHost(Settings.Vfs.WebDavServerUrl, _3dxStore);

                    //Start synchronously so we know the server is actually listening (and surface a
                    //bind failure such as the port already being in use) before opening Explorer.
                    Log.WriteLine("Starting WebDAV server");
                    webdavHost.Start();
                    Log.WriteLine("WebDAV server started.");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while starting the file server:{Environment.NewLine}{ex}");

                Stop();

                InitialisationFinished?.Invoke(this, new FinishedEventArgs()
                {
                    Success = false,
                    Message = $"Error while starting the file server: {ex.Message}"
                });

                return;
            }

            //The server is now serving the tree, so it is safe to open the virtual drive in Explorer
            Process.Start("explorer.exe", ComputedUNC);

            InitialisationFinished?.Invoke(this, new FinishedEventArgs()
            {
                Success = true,
            });
        }

        //SMB where it is asked for and possible, otherwise WebDAV. Auto falls back quietly (the
        //log says why); SMB asked for outright is not silently something else.
        void StartTransport()
        {
            var mode = Settings.Vfs.Transport;

            ActiveTransport = TransportMode.WebDAV;
            ComputedUNC = Settings.Vfs.GetComputedUNC();

            if (mode == TransportMode.WebDAV)
            {
                Log.WriteLine("Transport: WebDAV (as configured).");
                return;
            }

            string reason;
            if (!SmbConnection.IsSupported)
            {
                reason = $"Windows build {Environment.OSVersion.Version.Build} can't connect to an SMB server on a port other than 445 (Windows 11 24H2 or later is needed)";
            }
            else if (!SmbServerBinary.IsAvailable(out var missing))
            {
                reason = missing;
            }
            else
            {
                InitialisationProgress?.Invoke(this, new ProgressEventArgs()
                {
                    Nature = ProgressEventArgs.EnumNature.Neutral,
                    Message = "Starting SMB server..."
                });

                try
                {
                    var smb = Settings.Vfs.Smb;
                    smbHost = SmbHost.Start(IPAddress.Parse(smb.Address), smb.Port, smb.ShareName);
                    smbHost.ServerDied += message => SessionError?.Invoke(this, new ProgressEventArgs()
                    {
                        Message = message,
                        Nature = ProgressEventArgs.EnumNature.Bad
                    });

                    ActiveTransport = TransportMode.SMB;
                    ComputedUNC = smbHost.UncPath;
                    Log.WriteLine($"Transport: SMB at {ComputedUNC}.");
                    return;
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"Could not start the SMB transport:{Environment.NewLine}{ex}");
                    reason = ex.Message;
                }
            }

            if (mode == TransportMode.SMB)
            {
                throw new Exception($"SMB is not available: {reason}");
            }

            Log.WriteLine($"Transport: WebDAV, because SMB is not available: {reason}");
        }

        public void Stop()
        {
            try
            {
                //the client connection goes first, then the server and its endpoint
                smbHost?.Dispose();
                smbHost = null;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while stopping the SMB host: {ex.Message}");
            }

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
