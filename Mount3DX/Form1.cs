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
        public Form1()
        {
            InitializeComponent();
        }

        public static readonly string PROGRAM_NAME = "Mount 3DX";
        public static readonly string PROGRAM_VERSION = "1.4.0";

        private void Form1_Load(object sender, EventArgs e)
        {
            Text = $"{PROGRAM_NAME}";
            lblVersion.Text = $"v{PROGRAM_VERSION}";
            lblVersion.Left = grp3dx.Right - lblVersion.Width;
            MinimumSize = Size;

            InitLogging();
            LoadSettings();

            Scratch();

            lblRunningStatus.Text = string.Empty;

            Log.WriteLine($"Program started ({PROGRAM_NAME} {PROGRAM_VERSION})");
            LogWebClientSetrtings();
            LoadWebclientSettings();
        }

        public static uint FileAttributesLimitInBytes = 1_000_000;

        private void LoadWebclientSettings()
        {
            var webclientSettingsKey = @"SYSTEM\CurrentControlSet\Services\WebClient\Parameters";

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(webclientSettingsKey);
                if (key != null)
                {
                    var valueData = key.GetValue("FileAttributesLimitInBytes") ?? $"{FileAttributesLimitInBytes}";
                    FileAttributesLimitInBytes = unchecked((uint)(int)valueData);
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while reading WebClient settings from {webclientSettingsKey}: {ex}");
            }
        }

        private void LogWebClientSetrtings()
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
        }

        private void SaveSettings()
        {
            try
            {
                settings._3dx.ServerUrl = txt3dxServerUrl.Text;

                settings._3dx.RefreshIntervalMinutes = (int)txtRefreshIntervalMinutes.Value;

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
            catch { }
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            SaveSettings();
        }

        Session? session = null;

        private void BtnStart_Click(object sender, EventArgs e)
        {
            lblRunningStatus.BackColor = DefaultBackColor;
            lblRunningStatus.ForeColor = Color.Black;
            lblRunningStatus.Text = "";

            grp3dx.Enabled = false;

            if (btnStart.Text.Equals("Start"))
            {
                Log.WriteLine($"Start button clicked");
                var startTime = DateTime.Now;

                btnStart.Enabled = false;

                SaveSettings();
                LoadSettings();

                session = new Session(settings, FileAttributesLimitInBytes);

                session.InitialisationProgress += (sender, args) => Invoke(new MethodInvoker(() =>
                {
                    switch (args.Nature)
                    {
                        case ProgressEventArgs.EnumNature.Good:
                            lblRunningStatus.BackColor = Color.LimeGreen;
                            lblRunningStatus.ForeColor = Color.Black;
                            break;

                        case ProgressEventArgs.EnumNature.Neutral:
                            lblRunningStatus.BackColor = DefaultBackColor;
                            lblRunningStatus.ForeColor = Color.Black;
                            break;

                        case ProgressEventArgs.EnumNature.Bad:
                            lblRunningStatus.BackColor = Color.Red;
                            lblRunningStatus.ForeColor = Color.White;
                            break;
                    }

                    lblRunningStatus.Text = args.Message;
                }));

                session.InitialisationFinished += (sender, args) => Invoke(new MethodInvoker(() =>
                {
                    if (args.Success)
                    {
                        Log.WriteLine("Session started successfully.");

                        btnStart.Text = "Stop";

                        lblRunningStatus.BackColor = Color.LimeGreen;
                        lblRunningStatus.ForeColor = Color.Black;
                        lblRunningStatus.Text = "Running";

                        btnOpenVirtualDrive.Visible = true;
                    }
                    else
                    {
                        Log.WriteLine($"Session failed to start: {args.Message}");

                        lblRunningStatus.BackColor = Color.Red;
                        lblRunningStatus.ForeColor = Color.White;
                        lblRunningStatus.Text = args.Message;

                        btnOpenVirtualDrive.Visible = false;

                        grp3dx.Enabled = true;
                    }

                    btnStart.Enabled = true;
                }));

                session.SessionError += (sender, args) => Invoke(new MethodInvoker(() =>
                {
                    session?.Stop();

                    btnStart.Text = "Start";
                    grp3dx.Enabled = true;

                    var sessionDuration = DateTime.Now - startTime;

                    lblRunningStatus.BackColor = Color.Red;
                    lblRunningStatus.ForeColor = Color.White;
                    lblRunningStatus.Text = $"Session finished after {sessionDuration.FormatTimeSpan()}. Reason: {args.Message}";

                    btnOpenVirtualDrive.Visible = false;

                    Log.WriteLine(lblRunningStatus.Text);
                }));

                Task.Factory.StartNew(session.Start);
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

#pragma warning disable IDE0051 // Remove unused private members
        private void Scratch()
#pragma warning restore IDE0051 // Remove unused private members
        {
            _3dxCookies? cookies = null;

            var cookiesFile = @"C:\Temp\cookies.txt";
            if (File.Exists(cookiesFile))
            {
                cookies = File.ReadAllText(cookiesFile).DeserializeJson<_3dxCookies>();

                if (!lib3dx._3dxServer.Ping(cookies, CancellationToken.None))
                {
                    //the cache cookies are invalid
                    cookies = null;
                }
            }

            if (cookies == null)
            {
                var (Success, Cookies) = _3dxLogin.GetSessionCookies(settings._3dx.ServerUrl);
                if (Cookies == null) return;
                cookies = Cookies;
                var cookiesStr = Cookies.SerializeToJson();
                File.WriteAllText(cookiesFile, cookiesStr);
            }

            var _3dxServer = new _3dxServer(settings._3dx.ServerUrl, cookies);


            //1. Get all the collectors
            var collectorsFolder = new _3dxFolder(Guid.NewGuid().ToString(), "Collectors", null, DateTime.Now, DateTime.Now, DateTime.Now);
            var collectors = _3dxServer.GetAllCollectors(collectorsFolder, cookies, 8, null);

            //2. Establish all the parents & children (not working yet)
            _3dxServer.PopulateParentsAndChildren(collectors, cookies, 8, null);

            //3. Get the documents for each collector or Part
            var specs = _3dxServer.GetSpecificationDocuments("E2268A56C908000065C062670001FABC", collectorsFolder);
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