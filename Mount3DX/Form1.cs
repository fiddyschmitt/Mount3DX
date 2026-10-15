using libCommon;
using lib3dxVFS.WebDAV.Stores;
using System.Data;
using System.Text;
using libCommon.Events;
using lib3dx;
using System.Net;
using System.Diagnostics;
using libCommon.Utilities;
using System.Collections.Concurrent;
using Microsoft.Win32;
using libSMB;

namespace Mount3DX
{
    public partial class Form1 : Form
    {
        _3dxServer? _3dxServer;

        public Form1()
        {
            InitializeComponent();
        }

        public static readonly string PROGRAM_NAME = "Mount 3DX";
        public static readonly string PROGRAM_VERSION = "1.7.0";

        private void Form1_Load(object sender, EventArgs e)
        {
            Text = $"{PROGRAM_NAME}";
            lblVersion.Text = $"v{PROGRAM_VERSION}";
            lblVersion.Left = grp3dx.Right - lblVersion.Width;
            MinimumSize = Size;

            lblRunningStatus.Text = string.Empty;

            InitLogging();
            LoadSettings();

            Log.WriteLine($"Program started ({PROGRAM_NAME} {PROGRAM_VERSION})");
            Log.WriteLine($"Windows {Environment.OSVersion.Version}; SMB on a port other than 445 is {(SmbConnection.IsSupported ? "supported" : "not supported (Windows 11 24H2 or later is needed)")}");
            LogWebClientSettings();
            LoadWebclientSettings();

            ApplyClipboardMonitoring();
        }

        public static uint FileAttributesLimitInBytes = 1_000_000;

        private static void LoadWebclientSettings()
        {
            var webclientSettingsKey = @"SYSTEM\CurrentControlSet\Services\WebClient\Parameters";

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(webclientSettingsKey);
                var valueData = key?.GetValue("FileAttributesLimitInBytes");

                //if the value is absent (or an unexpected kind), keep the default
                if (valueData is int intValue)
                {
                    FileAttributesLimitInBytes = unchecked((uint)intValue);
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while reading WebClient settings from {webclientSettingsKey}: {ex}");
            }
        }

        private static void LogWebClientSettings()
        {
            var webclientSettingsKey = @"SYSTEM\CurrentControlSet\Services\WebClient\Parameters";

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(webclientSettingsKey);
                if (key != null)
                {
                    var lines = key
                                    .GetValueNames()
                                    .ToList()
                                    .Select(valueName =>
                                    {
                                        var valueKind = key.GetValueKind(valueName);

                                        object valueData = key.GetValue(valueName) ?? "";
                                        if (valueKind == RegistryValueKind.DWord)
                                        {
                                            valueData = unchecked((uint)(int)valueData);
                                        }
                                        else
                                        {
                                            valueData = valueData?.ToString() ?? "";
                                        }

                                        var line = ($@"    {valueName} ({valueKind}) = {valueData}");
                                        return line;
                                    })
                                    .ToString(Environment.NewLine);

                    Log.WriteLine($@"WebClient settings:{Environment.NewLine}HKLM\{webclientSettingsKey}{Environment.NewLine}{lines}");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while reading WebClient settings from {webclientSettingsKey}: {ex}");
            }
        }

        private static void InitLogging()
        {
            //each run starts a fresh log
            try
            {
                if (Log.Filename != null)
                {
                    File.Delete(Log.Filename);
                }
            }
            catch (Exception ex)
            {
                //a log that can't be cleared (read-only folder, file in use) mustn't stop the app
                System.Diagnostics.Debug.WriteLine($"Could not delete log file {Log.Filename}: {ex.Message}");
            }
        }

        readonly string settingsFilename = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        Settings settings = new();

        private void LoadSettings()
        {
            settings = new Settings();
            if (File.Exists(settingsFilename))
            {
                try
                {
                    var settingsJson = File.ReadAllText(settingsFilename);
                    settings = settingsJson?.DeserializeJson<Settings>() ?? new Settings();
                }
                catch (Exception ex)
                {
                    //a hand-edited file with a typo must not stop the app from starting
                    Log.WriteLine($"Could not read {settingsFilename}; using default settings. {ex.Message}");
                    ShowStatus(ProgressEventArgs.EnumNature.Warning, $"Could not read settings.json; using defaults. {ex.Message}");
                    settings = new Settings();
                }
            }

            txt3dxServerUrl.Text = settings._3dx.ServerUrl;

            txtRefreshIntervalMinutes.Value = settings._3dx.RefreshIntervalMinutes;

            chkExtraFiles.Checked = settings._3dx.IncludeMetadataFiles;

            chkMonitorClipboard.Checked = settings._3dx.MonitorClipboard;
        }

        private void SaveSettings()
        {
            try
            {
                settings._3dx.ServerUrl = txt3dxServerUrl.Text;

                settings._3dx.RefreshIntervalMinutes = (int)txtRefreshIntervalMinutes.Value;

                settings._3dx.IncludeMetadataFiles = chkExtraFiles.Checked;

                settings._3dx.MonitorClipboard = chkMonitorClipboard.Checked;

                var settingsJson = settings.SerializeToJson();

                var existingSettingsFileContent = string.Empty;
                if (File.Exists(settingsFilename))
                {
                    existingSettingsFileContent = File.ReadAllText(settingsFilename);
                }

                if (!string.IsNullOrEmpty(settingsJson) && !settingsJson.Equals(existingSettingsFileContent))
                {
                    File.WriteAllText(settingsFilename, settingsJson);
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while saving settings: {ex.Message}");
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            //Nothing to stop, or Windows is shutting down (don't hold it up; process exit releases
            //the port anyway)
            if (session == null || e.CloseReason == CloseReason.WindowsShutDown) return;

            //Stopping waits for the WebDAV host and the background loops. Doing that here on the
            //UI thread locked the form up, so cancel this close, stop in the background with the
            //status showing "Stopping...", and close again once the session is gone.
            e.Cancel = true;
            StopSession(session, ProgressEventArgs.EnumNature.Neutral, "Stopped", whenStopped: Close);
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            SaveSettings();

            clipboardMonitor?.Dispose();
            clipboardMonitor = null;
            linkPopup?.Dispose();
            linkPopup = null;
        }

        Session? session = null;

        private void BtnStart_Click(object sender, EventArgs e)
        {
            ShowStatus(ProgressEventArgs.EnumNature.Neutral, "");

            SetSettingsEditable(false);

            if (btnStart.Text.Equals("Start"))
            {
                Log.WriteLine($"Start button clicked");
                var startTime = DateTime.Now;

                btnStart.Enabled = false;
                Cursor = Cursors.WaitCursor;

                SaveSettings();
                LoadSettings();

                if (_3dxServer == null || _3dxServer.ServerUrl != settings._3dx.ServerUrl)
                {
                    _3dxServer = new _3dxServer(settings._3dx.ServerUrl, settings._3dx.IncludeMetadataFiles);
                }

                //The server object is kept between sessions so its cookies survive, so apply any
                //settings that may have changed since it was created
                _3dxServer.ServeMetadataFiles = settings._3dx.IncludeMetadataFiles;

                var newSession = new Session(_3dxServer, settings, FileAttributesLimitInBytes);
                session = newSession;

                //A stopped session may still have a refresh in flight (it cannot be interrupted
                //mid-way), so its late events must not be applied to a newer session
                bool IsCurrent() => ReferenceEquals(newSession, session);

                newSession.InitialisationProgress += (sender, args) => SafeInvoke(() =>
                {
                    if (!IsCurrent()) return;

                    ShowStatus(args.Nature, args.Message);
                });

                newSession.InitialisationFinished += (sender, args) => SafeInvoke(() =>
                {
                    if (!IsCurrent()) return;

                    if (args.Success)
                    {
                        Log.WriteLine("Session started successfully.");

                        btnStart.Text = "Stop";
                        ShowStatus(ProgressEventArgs.EnumNature.Good, "Running");
                        btnOpenVirtualDrive.Visible = true;
                    }
                    else
                    {
                        Log.WriteLine($"Session failed to start: {args.Message}");

                        ShowStatus(ProgressEventArgs.EnumNature.Bad, args.Message);
                        btnOpenVirtualDrive.Visible = false;
                        SetSettingsEditable(true);
                    }

                    btnStart.Enabled = true;
                    Cursor = Cursors.Default;
                });

                newSession.SessionStatus += (sender, args) => SafeInvoke(() =>
                {
                    if (!IsCurrent()) return;

                    if (args.Nature == ProgressEventArgs.EnumNature.Good)
                    {
                        //recovered; back to the normal running state
                        ShowStatus(ProgressEventArgs.EnumNature.Good, "Running");
                    }
                    else
                    {
                        ShowStatus(args.Nature, args.Message);
                    }
                });

                newSession.SessionError += (sender, args) => SafeInvoke(() =>
                {
                    if (!IsCurrent()) return;

                    var sessionDuration = DateTime.Now - startTime;
                    var message = $"Session finished after {sessionDuration.FormatTimeSpan()}. Reason: {args.Message}";
                    Log.WriteLine(message);

                    StopSession(newSession, ProgressEventArgs.EnumNature.Bad, message);
                });

                Task.Factory.StartNew(newSession.Start);
            }
            else
            {
                Log.WriteLine($"Stop button clicked");

                if (session != null)
                {
                    StopSession(session, ProgressEventArgs.EnumNature.Neutral, "Stopped");
                }
            }
        }

        //Session events arrive on background threads, possibly after the form has closed
        void SafeInvoke(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                Invoke(action);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }   //handle destroyed between the check and the call
        }

        //Stopping waits for the WebDAV host and the background loops (several seconds in the worst
        //case), so do it off the UI thread and keep the button disabled meanwhile
        void StopSession(Session stoppingSession, ProgressEventArgs.EnumNature finalNature, string finalMessage, Action? whenStopped = null)
        {
            btnStart.Enabled = false;
            btnOpenVirtualDrive.Visible = false;
            linkPopup?.Dismiss();
            ShowStatus(ProgressEventArgs.EnumNature.Neutral, "Stopping...");

            Task.Run(() =>
            {
                stoppingSession.Stop();

                SafeInvoke(() =>
                {
                    //no session is running any more (late events from it are ignored via IsCurrent)
                    if (ReferenceEquals(session, stoppingSession))
                    {
                        session = null;
                    }

                    btnStart.Text = "Start";
                    btnStart.Enabled = true;
                    SetSettingsEditable(true);
                    ShowStatus(finalNature, finalMessage);

                    whenStopped?.Invoke();
                });
            });
        }

        //The server URL, refresh interval and transport only apply at Start, so they are locked
        //while a session runs. The two checkboxes stay enabled: they take effect immediately.
        void SetSettingsEditable(bool editable)
        {
            txt3dxServerUrl.Enabled = editable;
            txtRefreshIntervalMinutes.Enabled = editable;
        }

        void ShowStatus(ProgressEventArgs.EnumNature nature, string? message)
        {
            (lblRunningStatus.BackColor, lblRunningStatus.ForeColor) = nature switch
            {
                ProgressEventArgs.EnumNature.Good => (Color.LimeGreen, Color.Black),
                ProgressEventArgs.EnumNature.Warning => (Color.Orange, Color.Black),
                ProgressEventArgs.EnumNature.Bad => (Color.Red, Color.White),
                _ => (DefaultBackColor, Color.Black),
            };

            lblRunningStatus.Text = message;
        }

        private void ChkExtraFiles_CheckedChanged(object sender, EventArgs e)
        {
            //The generated files are always in the tree; the WebDAV layer checks these flags on
            //every listing, so this applies to the running session without a rebuild. (Explorer
            //may show its cached listing for up to a minute; F5 refreshes it.)
            settings._3dx.IncludeMetadataFiles = chkExtraFiles.Checked;

            if (_3dxServer != null)
            {
                _3dxServer.ServeMetadataFiles = chkExtraFiles.Checked;
            }
        }

        private void BtnOpenVirtualDrive_Click(object sender, EventArgs e)
        {
            if (session != null)
            {
                Process.Start("explorer.exe", session.ComputedUNC);
            }
        }

        ClipboardMonitor? clipboardMonitor;
        LinkPopup? linkPopup;

        //Some programs change the clipboard twice for one copy; the same link again straight away is
        //ignored. Only links are kept here, never other text.
        string? lastCopiedLink;
        long lastCopiedLinkAt;

        //Counts the copies looked at, so that a slow lookup is dropped once a later copy has overtaken it
        int clipboardCopies;

        private void ChkMonitorClipboard_CheckedChanged(object sender, EventArgs e)
        {
            settings._3dx.MonitorClipboard = chkMonitorClipboard.Checked;
            ApplyClipboardMonitoring();
        }

        //Listens to the clipboard while the checkbox is ticked
        void ApplyClipboardMonitoring()
        {
            if (chkMonitorClipboard.Checked)
            {
                if (clipboardMonitor != null) return;

                try
                {
                    clipboardMonitor = new ClipboardMonitor();
                    clipboardMonitor.Changed += ClipboardMonitor_Changed;
                    Log.WriteLine("Watching the clipboard for 3DX links.");
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"Could not watch the clipboard: {ex.Message}");
                }
            }
            else
            {
                if (clipboardMonitor == null) return;

                clipboardMonitor.Dispose();
                clipboardMonitor = null;
                linkPopup?.Dismiss();
                Log.WriteLine("Stopped watching the clipboard.");
            }
        }

        void ClipboardMonitor_Changed(object? sender, EventArgs e)
        {
            //Only while the drive is up, which is where the document would be opened from. The text
            //isn't even read otherwise.
            var current = session;
            if (current == null || !current.IsServing || clipboardMonitor == null) return;

            var text = clipboardMonitor.ReadText();
            var objectIds = _3dxLink.GetObjectIds(text);

            if (objectIds.Count == 0)
            {
                //whatever was copied instead overtakes a lookup still under way
                clipboardCopies++;
                lastCopiedLink = null;
                return;
            }

            var now = Environment.TickCount64;
            if (text == lastCopiedLink && now - lastCopiedLinkAt < 1000) return;
            lastCopiedLink = text;
            lastCopiedLinkAt = now;

            var copy = ++clipboardCopies;

            //a dotted id is looked up in 3DX, so this happens in the background
            Task.Run(() => current.FindLinkedDocument(objectIds))
                .ContinueWith(lookup =>
                {
                    if (lookup.Exception != null)
                    {
                        Log.WriteLine($"Error while looking up a copied 3DX link:{Environment.NewLine}{lookup.Exception}");
                        return;
                    }

                    if (lookup.Result is not LinkedDocument document) return;

                    SafeInvoke(() =>
                    {
                        //a later copy, a stop or the checkbox overtook this one
                        if (copy != clipboardCopies || !ReferenceEquals(session, current) || !current.IsServing || clipboardMonitor == null) return;

                        ShowLinkPopup(document);
                    });
                }, TaskScheduler.Default);
        }

        void ShowLinkPopup(LinkedDocument document)
        {
            if (linkPopup == null || linkPopup.IsDisposed)
            {
                linkPopup = new LinkPopup();
                linkPopup.OpenRequested += objectId => OpenLinkedDocument(objectId, inFolder: false);
                linkPopup.OpenFolderRequested += objectId => OpenLinkedDocument(objectId, inFolder: true);
            }

            linkPopup.ShowFor(document);
        }

        //A document with one file: Open opens the file, Open folder shows it selected in its folder.
        //Several files, or none: both open the document's folder.
        void OpenLinkedDocument(string objectId, bool inFolder)
        {
            var current = session;
            if (current == null || !current.IsServing) return;

            //The click was the last input, which lets this process hand the foreground to whatever it
            //opens (the popup never activates, so the window the link was copied in is still in front)
            ShellActions.AllowAnyProcessToTakeForeground();

            //Its own thread: anything that touches the drive can take a while, and the shell wants STA
            var thread = new Thread(() =>
            {
                try
                {
                    var location = current.Locate(objectId) ?? throw new InvalidOperationException("The document is no longer on the drive. It may have left 3DX's document list at the last refresh.");

                    ShellActions.AllowAnyProcessToTakeForeground();

                    if (location.Files.Count != 1)
                    {
                        Log.WriteLine($"Opening {location.Folder}");
                        ShellActions.Open(location.Folder);
                    }
                    else if (inFolder)
                    {
                        Log.WriteLine($"Showing {location.Files[0]} in its folder");
                        ShellActions.ShowInFolder(location.Files[0]);
                    }
                    else
                    {
                        Log.WriteLine($"Opening {location.Files[0]}");
                        ShellActions.Open(location.Files[0]);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"Could not open the document of a copied 3DX link:{Environment.NewLine}{ex}");
                    MessageBox.Show(ex.Message, PROGRAM_NAME, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            })
            {
                IsBackground = true,
                Name = "Open copied 3DX link",
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }
}