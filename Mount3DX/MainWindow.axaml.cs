using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using lib3dx;
using lib3dxVFS.SMB;
using libCommon;
using libCommon.Events;
using libCommon.Utilities;
using Microsoft.Win32;
using System.Diagnostics;

namespace Mount3DX
{
    public partial class MainWindow : Window
    {
        _3dxServer? _3dxServer;

        //Run with --start, as the startup shortcut does: the session starts as soon as the program is up
        readonly bool startOnLaunch;

        //Session events can arrive once the window has closed
        bool closed;

        //In the tray while the window is minimised, in place of the window's button on the taskbar.
        //Clicking it shows the window again.
        readonly TrayIcon? trayIcon;

        //Normal or maximised: how the window was before it was minimised, and how it comes back
        WindowState shownState = WindowState.Normal;

        //Started minimised, as the startup shortcut starts it: it is only in the tray, and the window
        //isn't shown until the icon there is clicked
        public bool StartsInTray { get; }

        public static readonly string PROGRAM_NAME = "Mount 3DX";
        public static readonly string PROGRAM_VERSION = "1.8.1";

        //For the XAML previewer
        public MainWindow() : this(startOnLaunch: false)
        {
        }

        public MainWindow(bool startOnLaunch)
        {
            InitializeComponent();
            this.startOnLaunch = startOnLaunch;

            Title = PROGRAM_NAME;
            lblVersion.Text = $"v{PROGRAM_VERSION}";

            //the previewer shows the window, and nothing else happens
            if (Design.IsDesignMode) return;

            //As the shortcut that started it asks. Minimised, as the startup shortcut asks, means in the
            //tray only.
            var (state, activate) = StartupShowCommand.Get();
            StartsInTray = state == WindowState.Minimized;
            if (!StartsInTray)
            {
                (WindowState, ShowActivated) = (state, activate);
            }

            trayIcon = new TrayIcon
            {
                Icon = Icon,
                ToolTipText = PROGRAM_NAME,
                IsVisible = StartsInTray,
            };
            trayIcon.Clicked += (sender, e) => ShowFromTray();
            TrayIcon.SetIcons(Application.Current!, new TrayIcons { trayIcon });

            //Attached once the window has its defaults, as the designer did. Loading the settings
            //below changes a box only where the settings differ from them.
            chkExtraFiles.IsCheckedChanged += ChkExtraFiles_CheckedChanged;
            chkMonitorClipboard.IsCheckedChanged += ChkMonitorClipboard_CheckedChanged;
            chkRunAtStartup.IsCheckedChanged += ChkRunAtStartup_CheckedChanged;

            InitLogging();
            LoadSettings();

            Log.WriteLine($"Program started ({PROGRAM_NAME} {PROGRAM_VERSION})");
            Log.WriteLine($"Windows {Environment.OSVersion.Version}; SMB on a port other than 445 is {(WindowsSmbClient.IsSupported ? "supported" : "not supported (Windows 11 24H2 or later is needed)")}");
            LogWebClientSettings();
            LoadWebclientSettings();

            ApplyClipboardMonitoring();

            //ticked when the shortcut is there, which this leaves as it is
            chkRunAtStartup.IsChecked = StartupShortcut.Exists;

            if (startOnLaunch)
            {
                //Once the program is running, with the window up or only in the tray. The startup
                //shortcut runs it this way at sign-in, when nobody has asked to see the drive, so it
                //isn't opened in Explorer (Open virtual drive is there for that).
                Dispatcher.UIThread.Post(() =>
                {
                    Log.WriteLine($"Run with {StartupShortcut.StartArgument}, so starting straight away, without opening Explorer");
                    StartSession(openInExplorer: false);
                });
            }
        }

        //Shown again from the tray, the window keeps its focus where it was
        bool openedBefore;

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            //back from the tray, with a status that came meanwhile
            if (fitToStatusWhenRestored)
            {
                FitToStatus();
            }

            if (openedBefore) return;
            openedBefore = true;

            //as it was first in the tab order: Enter or Space starts the drive straight away
            btnStart.Focus();
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
                Debug.WriteLine($"Could not delete log file {Log.Filename}: {ex.Message}");
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

            chkExtraFiles.IsChecked = settings._3dx.IncludeMetadataFiles;

            chkMonitorClipboard.IsChecked = settings._3dx.MonitorClipboard;
        }

        private void SaveSettings()
        {
            try
            {
                settings._3dx.ServerUrl = txt3dxServerUrl.Text ?? "";

                //an emptied box keeps the interval it had
                if (txtRefreshIntervalMinutes.Value is decimal minutes)
                {
                    settings._3dx.RefreshIntervalMinutes = (int)minutes;
                }

                settings._3dx.IncludeMetadataFiles = chkExtraFiles.IsChecked == true;

                settings._3dx.MonitorClipboard = chkMonitorClipboard.IsChecked == true;

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

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            //Nothing to stop, or Windows is shutting down (don't hold it up; process exit releases
            //the port anyway)
            if (session == null || e.CloseReason == WindowCloseReason.OSShutdown) return;

            //Stopping waits for the WebDAV host and the background loops. Doing that here on the
            //UI thread would lock the window up, so cancel this close, stop in the background with
            //the status showing "Stopping...", and close again once the session is gone.
            e.Cancel = true;
            StopSession(session, ProgressEventArgs.EnumNature.Neutral, "Stopped", whenStopped: Close);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            closed = true;

            if (Design.IsDesignMode) return;

            SaveSettings();

            clipboardMonitor?.Dispose();
            clipboardMonitor = null;
            linkPopup?.Close();
            linkPopup = null;
            trayIcon?.Dispose();
        }

        Session? session = null;

        private void BtnStart_Click(object? sender, RoutedEventArgs e)
        {
            if (btnStart.Content is "Start")
            {
                Log.WriteLine($"Start button clicked");
                StartSession(openInExplorer: true);
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

        void StartSession(bool openInExplorer)
        {
            ShowStatus(ProgressEventArgs.EnumNature.Neutral, "");

            SetSettingsEditable(false);

            var startTime = DateTime.Now;

            btnStart.IsEnabled = false;
            Cursor = new Cursor(StandardCursorType.Wait);

            SaveSettings();
            LoadSettings();

            if (_3dxServer == null || _3dxServer.ServerUrl != settings._3dx.ServerUrl)
            {
                _3dxServer = new _3dxServer(settings._3dx.ServerUrl, settings._3dx.IncludeMetadataFiles);
            }

            //The server object is kept between sessions so its cookies survive, so apply any
            //settings that may have changed since it was created
            _3dxServer.ServeMetadataFiles = settings._3dx.IncludeMetadataFiles;

            var newSession = new Session(_3dxServer, settings, FileAttributesLimitInBytes, openInExplorer);
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

                    btnStart.Content = "Stop";
                    ShowStatus(ProgressEventArgs.EnumNature.Good, "Running");
                    btnOpenVirtualDrive.IsVisible = true;
                }
                else
                {
                    Log.WriteLine($"Session failed to start: {args.Message}");

                    ShowStatus(ProgressEventArgs.EnumNature.Bad, args.Message);
                    btnOpenVirtualDrive.IsVisible = false;
                    SetSettingsEditable(true);
                }

                btnStart.IsEnabled = true;
                Cursor = Cursor.Default;
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

        //Session events arrive on background threads, possibly after the window has closed
        void SafeInvoke(Action action)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (closed) return;

                action();
            });
        }

        //Stopping waits for the WebDAV host and the background loops (several seconds in the worst
        //case), so do it off the UI thread and keep the button disabled meanwhile
        void StopSession(Session stoppingSession, ProgressEventArgs.EnumNature finalNature, string finalMessage, Action? whenStopped = null)
        {
            btnStart.IsEnabled = false;
            btnOpenVirtualDrive.IsVisible = false;
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

                    btnStart.Content = "Start";
                    btnStart.IsEnabled = true;
                    SetSettingsEditable(true);
                    ShowStatus(finalNature, finalMessage);

                    whenStopped?.Invoke();
                });
            });
        }

        //The server URL and refresh interval only apply at Start, so they are locked while a
        //session runs. The checkboxes stay enabled: they take effect immediately.
        void SetSettingsEditable(bool editable)
        {
            txt3dxServerUrl.IsEnabled = editable;
            txtRefreshIntervalMinutes.IsEnabled = editable;
        }

        void ShowStatus(ProgressEventArgs.EnumNature nature, string? message)
        {
            switch (nature)
            {
                case ProgressEventArgs.EnumNature.Good:
                    statusBadge.Background = Brushes.LimeGreen;
                    lblRunningStatus.Foreground = Brushes.Black;
                    break;

                case ProgressEventArgs.EnumNature.Warning:
                    statusBadge.Background = Brushes.Orange;
                    lblRunningStatus.Foreground = Brushes.Black;
                    break;

                case ProgressEventArgs.EnumNature.Bad:
                    statusBadge.Background = Brushes.Red;
                    lblRunningStatus.Foreground = Brushes.White;
                    break;

                default:
                    //plain text, in the theme's colours
                    statusBadge.Background = null;
                    lblRunningStatus.ClearValue(TextBlock.ForegroundProperty);
                    break;
            }

            lblRunningStatus.Text = message;
            FitToStatus();
        }

        //The window is as tall as its content (SizeToContent), so it grows for a long status and
        //shrinks back for a short one. Dragging the window's edge stops that until the next status,
        //which asks for it again. A window that is minimised or in the tray is fitted once it is back
        //on screen: fitted while hidden, it would come back at its old height.
        bool fitToStatusWhenRestored;

        void FitToStatus()
        {
            if (WindowState != WindowState.Normal || !IsVisible)
            {
                fitToStatusWhenRestored = true;
                return;
            }

            fitToStatusWhenRestored = false;

            //via Manual, so that the window is measured again even when it was fitting already
            SizeToContent = SizeToContent.Manual;
            SizeToContent = SizeToContent.Height;

            //and measured again even when nothing in it has changed since its last layout, as when it
            //comes back from the tray (hidden, it was measured as nothing)
            InvalidateMeasure();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property != WindowStateProperty) return;

            var state = change.GetNewValue<WindowState>();
            if (state == WindowState.Minimized)
            {
                //once the minimising is done
                Dispatcher.UIThread.Post(MinimiseToTray);
                return;
            }

            shownState = state;

            if (fitToStatusWhenRestored && state == WindowState.Normal)
            {
                FitToStatus();
            }
        }

        //The window leaves the taskbar, and its icon waits in the tray
        void MinimiseToTray()
        {
            //Not once it is back or closed, nor while a message is open over it: hiding the window would
            //hide the message too, and the window would come back still waiting on it
            if (trayIcon == null || closed || WindowState != WindowState.Minimized || OwnedWindows.Count > 0) return;

            Hide();
            trayIcon.IsVisible = true;
        }

        //The tray icon was clicked: the window comes back as it was, or is shown for the first time if it
        //started in the tray, and comes to the front
        void ShowFromTray()
        {
            if (trayIcon == null || closed) return;

            trayIcon.IsVisible = false;
            WindowState = shownState;
            ShowActivated = true;
            Show();
            Activate();
        }

        private void ChkExtraFiles_CheckedChanged(object? sender, RoutedEventArgs e)
        {
            //The generated files are always in the tree; the WebDAV layer checks these flags on
            //every listing, so this applies to the running session without a rebuild. (Explorer
            //may show its cached listing for up to a minute; F5 refreshes it.)
            settings._3dx.IncludeMetadataFiles = chkExtraFiles.IsChecked == true;

            if (_3dxServer != null)
            {
                _3dxServer.ServeMetadataFiles = chkExtraFiles.IsChecked == true;
            }
        }

        //Ticking makes the startup shortcut and unticking deletes it, there and then. Opening the
        //window ticks the box when the shortcut is already there, which leaves the shortcut as it is.
        private void ChkRunAtStartup_CheckedChanged(object? sender, RoutedEventArgs e)
        {
            var runAtStartup = chkRunAtStartup.IsChecked == true;
            if (runAtStartup == StartupShortcut.Exists) return;

            try
            {
                if (runAtStartup)
                {
                    StartupShortcut.Create();
                    Log.WriteLine($"Run at startup: created {StartupShortcut.FilePath}");
                }
                else
                {
                    StartupShortcut.Delete();
                    Log.WriteLine($"Run at startup: deleted {StartupShortcut.FilePath}");
                }
            }
            catch (Exception ex)
            {
                var action = runAtStartup ? "create" : "delete";
                Log.WriteLine($"Could not {action} {StartupShortcut.FilePath}:{Environment.NewLine}{ex}");
                MessageBox.Show($"Could not {action} the startup shortcut. {ex.Message}", MessageBoxIcon.Warning, owner: this);

                //back to what is there, which comes back here and stops at the check above
                chkRunAtStartup.IsChecked = StartupShortcut.Exists;
            }
        }

        private void BtnOpenVirtualDrive_Click(object? sender, RoutedEventArgs e)
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

        private void ChkMonitorClipboard_CheckedChanged(object? sender, RoutedEventArgs e)
        {
            settings._3dx.MonitorClipboard = chkMonitorClipboard.IsChecked == true;
            ApplyClipboardMonitoring();
        }

        //Listens to the clipboard while the checkbox is ticked
        void ApplyClipboardMonitoring()
        {
            if (chkMonitorClipboard.IsChecked == true)
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
            if (linkPopup == null)
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
                    SafeInvoke(() => MessageBox.Show(ex.Message, MessageBoxIcon.Warning));
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
