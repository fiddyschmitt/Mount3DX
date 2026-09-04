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
        public static readonly string PROGRAM_VERSION = "1.6.3";

        private void Form1_Load(object sender, EventArgs e)
        {
            Text = $"{PROGRAM_NAME}";
            lblVersion.Text = $"v{PROGRAM_VERSION}";
            lblVersion.Left = grp3dx.Right - lblVersion.Width;
            MinimumSize = Size;

            InitLogging();
            LoadSettings();

            lblRunningStatus.Text = string.Empty;

            Log.WriteLine($"Program started ({PROGRAM_NAME} {PROGRAM_VERSION})");
            LogWebClientSettings();
            LoadWebclientSettings();
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
            if (Log.Filename != null)
            {
                File.Delete(Log.Filename);
            }
        }

        readonly string settingsFilename = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        Settings settings = new();

        private void LoadSettings()
        {
            settings = new Settings();
            if (File.Exists(settingsFilename))
            {
                var settingsJson = File.ReadAllText(settingsFilename);
                settings = settingsJson?.DeserializeJson<Settings>() ?? new Settings();
            }

            txt3dxServerUrl.Text = settings._3dx.ServerUrl;

            txtRefreshIntervalMinutes.Value = settings._3dx.RefreshIntervalMinutes;

            //one checkbox drives both generated files; settings.json keeps the two separate flags
            chkExtraFiles.Checked = settings._3dx.GenerateExtraFiles.DocumentLink || settings._3dx.GenerateExtraFiles.DocumentMetadata;
        }

        private void SaveSettings()
        {
            try
            {
                settings._3dx.ServerUrl = txt3dxServerUrl.Text;

                settings._3dx.RefreshIntervalMinutes = (int)txtRefreshIntervalMinutes.Value;

                settings._3dx.GenerateExtraFiles.DocumentLink = chkExtraFiles.Checked;
                settings._3dx.GenerateExtraFiles.DocumentMetadata = chkExtraFiles.Checked;

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

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            SaveSettings();
        }

        Session? session = null;

        private void BtnStart_Click(object sender, EventArgs e)
        {
            ShowStatus(ProgressEventArgs.EnumNature.Neutral, "");

            grp3dx.Enabled = false;

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
                    _3dxServer = new _3dxServer(
                        settings._3dx.ServerUrl,
                        settings._3dx.GenerateExtraFiles.DocumentLink,
                        settings._3dx.GenerateExtraFiles.DocumentMetadata);
                }

                //The server object is kept between sessions so its cookies survive, so apply any
                //settings that may have changed since it was created
                _3dxServer.GenerateDocumentLinkFile = settings._3dx.GenerateExtraFiles.DocumentLink;
                _3dxServer.GenerateDocumentMetadataFile = settings._3dx.GenerateExtraFiles.DocumentMetadata;

                var newSession = new Session(_3dxServer, settings, FileAttributesLimitInBytes);
                session = newSession;

                //A stopped session may still have a refresh in flight (it cannot be interrupted
                //mid-way), so its late events must not be applied to a newer session
                bool IsCurrent() => ReferenceEquals(newSession, session);

                newSession.InitialisationProgress += (sender, args) => Invoke(new MethodInvoker(() =>
                {
                    if (!IsCurrent()) return;

                    ShowStatus(args.Nature, args.Message);
                }));

                newSession.InitialisationFinished += (sender, args) => Invoke(new MethodInvoker(() =>
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
                        grp3dx.Enabled = true;
                    }

                    btnStart.Enabled = true;
                    Cursor = Cursors.Default;
                }));

                newSession.SessionStatus += (sender, args) => Invoke(new MethodInvoker(() =>
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
                }));

                newSession.SessionError += (sender, args) => Invoke(new MethodInvoker(() =>
                {
                    if (!IsCurrent()) return;

                    newSession.Stop();

                    btnStart.Text = "Start";
                    grp3dx.Enabled = true;

                    var sessionDuration = DateTime.Now - startTime;

                    ShowStatus(ProgressEventArgs.EnumNature.Bad, $"Session finished after {sessionDuration.FormatTimeSpan()}. Reason: {args.Message}");
                    btnOpenVirtualDrive.Visible = false;

                    Log.WriteLine(lblRunningStatus.Text);
                }));

                Task.Factory.StartNew(newSession.Start);
            }
            else
            {
                Log.WriteLine($"Stop button clicked");
                session?.Stop();

                btnStart.Text = "Start";
                grp3dx.Enabled = true;
                btnOpenVirtualDrive.Visible = false;
            }
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

        private void BtnOpenVirtualDrive_Click(object sender, EventArgs e)
        {
            if (session != null)
            {
                Process.Start("explorer.exe", session.ComputedUNC);
            }
        }
    }
}