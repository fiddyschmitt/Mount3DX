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

namespace Mount3DX
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        public static readonly string PROGRAM_NAME = "Mount 3DX";
        public static readonly string PROGRAM_VERSION = "1.1.0";

        private void Form1_Load(object sender, EventArgs e)
        {
            Text = $"{PROGRAM_NAME}";
            lblVersion.Text = $"v{PROGRAM_VERSION}";
            lblVersion.Left = grp3dx.Right - lblVersion.Width;
            MinimumSize = Size;

            InitLogging();
            LoadSettings();

            //Scratch();

            lblRunningStatus.Text = string.Empty;

            Log.WriteLine($"Program started ({PROGRAM_NAME} {PROGRAM_VERSION})");
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

                session = new Session(settings);

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
                        btnStart.Text = "Stop";

                        lblRunningStatus.BackColor = Color.LimeGreen;
                        lblRunningStatus.ForeColor = Color.Black;
                        lblRunningStatus.Text = "Running";

                        btnOpenVirtualDrive.Visible = true;
                    }
                    else
                    {
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
            var loginUrl = settings._3dx.ServerUrl.UrlCombine("common/emxNavigator.jsp");
            var cookies = _3dxLogin.GetSessionCookies(loginUrl);
            var _3dxServer = new _3dxServer(settings._3dx.ServerUrl, cookies.Cookies);

            var root = new _3dxFolder(
                                "root",
                                "",
                                null,
                                DateTime.Now,
                                DateTime.Now,
                                DateTime.Now)
            {
                Subfolders = _3dxServer.GetRootFolders()
            };

            var securityContext = _3dxServer.GetSecurityContext();

            var folderQueue = new ConcurrentQueue<_3dxFolder>();
            root.Subfolders.ForEach(rt => folderQueue.Enqueue(rt));

            int totalFolders = 0;
            int totalDocs = 0;
            int totalFiles = 0;

            QueueUtility
                    .Recurse2(folderQueue, folder =>
                    {
                        var itemsInFolder = _3dxServer.GetItemsInFolder(folder, securityContext);

                        var documents = itemsInFolder
                                            .OfType<_3dxDocument>()
                                            .ToList();

                        var files = documents
                                        .Sum(doc => doc.Files.Count);

                        folder.Subfolders = itemsInFolder
                                            .Except(documents)
                                            .OfType<_3dxFolder>()
                                            .ToList();

                        Interlocked.Add(ref totalFolders, folder.Subfolders.Count);
                        Interlocked.Add(ref totalDocs, documents.Count);
                        Interlocked.Add(ref totalFiles, files);

                        //Debug.WriteLine($"{folder.FullPath}\tSubfolders: {folder.Subfolders.Count:N0}\tDocs: {documents.Count:N0}");
                        Debug.WriteLine($"Total folders: {totalFolders:N0}\tTotal docs: {totalDocs:N0}\tTotal files: {totalFiles:N0}");

                        return folder.Subfolders;

                    }, settings._3dx.QueryThreads, CancellationToken.None);

            /*
            var itemTypes = _3dxServer
                                .itemTypes
                                .GroupBy(
                                    itemType => itemType,
                                    itemType => itemType,
                                    (key, grp) => new
                                    {
                                        ItemType = key,
                                        Count = grp.Count()
                                    })
                                .OrderByDescending(grp => grp.Count)
                                .Select(grp => $"{grp.ItemType},{grp.Count}")
                                .ToString(Environment.NewLine);
            */
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