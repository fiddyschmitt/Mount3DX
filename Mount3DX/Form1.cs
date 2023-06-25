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
        public static readonly string PROGRAM_VERSION = "0.1";

        private void Form1_Load(object sender, EventArgs e)
        {
            Text = $"{PROGRAM_NAME} {PROGRAM_VERSION}";

            LoadSettings();

            //Scratch();

            //todo: The Windows 10 WebDAV client needs several settings tweaked:
            //HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WebClient\Parameters
            //      FileAttributesLimitInBytes = 4294967295     //Allows explorer to open folders which contain many items
            //      FileSizeLimitInBytes = 4294967295           //Allows large files to be opened
            //      InternetServerTimeoutInSec = 180            //Give the server a longer time to respond
            //      LocalServerTimeoutInSec = 180               //Give the server a longer time to respond
            //      SendReceiveTimeoutInSec = 180               //Give the server a longer time to respond
            //After changing the settings, restart the 'WebClient' service

            lblRunningStatus.Text = string.Empty;
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
            txt3dxCookieString.Text = settings._3dx.CookiesString;
            chkKeepAlive.Checked = settings._3dx.KeepAlive;
            txtKeepAliveIntervalMinutes.Value = settings._3dx.KeepAliveIntervalMinutes;

            txtMapToDriveLetter.Text = settings.Vfs.MapToDriveLetter;
        }

        private void SaveSettings()
        {
            try
            {
                settings._3dx.ServerUrl = txt3dxServerUrl.Text;
                settings._3dx.CookiesString = txt3dxCookieString.Text;
                settings._3dx.KeepAlive = chkKeepAlive.Checked;
                settings._3dx.KeepAliveIntervalMinutes = (int)txtKeepAliveIntervalMinutes.Value;

                settings.Vfs.MapToDriveLetter = txtMapToDriveLetter.Text;

                var settingsJson = settings.SerializeToJson();

                var existingSettingsFileContent = File.ReadAllText(settingsJson);
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
            grpVfs.Enabled = false;

            if (btnStart.Text.Equals("Start"))
            {
                btnStart.Enabled = false;

                SaveSettings();
                LoadSettings();

                session = new Session(settings);

                session.Progress += (sender, args) => Invoke(new MethodInvoker(() =>
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

                session.Finished += (sender, args) => Invoke(new MethodInvoker(() =>
                {
                    if (args.Success)
                    {
                        btnStart.Text = "Stop";

                        lblRunningStatus.BackColor = Color.LimeGreen;
                        lblRunningStatus.ForeColor = Color.Black;
                        lblRunningStatus.Text = "Running";
                    }
                    else
                    {
                        grp3dx.Enabled = true;
                        grpVfs.Enabled = true;
                    }

                    btnStart.Enabled = true;
                }));

                Task.Factory.StartNew(session.Start);
            }
            else
            {
                session?.Stop();
                btnStart.Text = "Start";

                grp3dx.Enabled = true;
                grpVfs.Enabled = true;
            }
        }

#pragma warning disable IDE0051 // Remove unused private members
        private void Scratch()
#pragma warning restore IDE0051 // Remove unused private members
        {
            var _3dxServer = new _3dxServer(settings._3dx.ServerUrl, settings._3dx.CookiesString, false, 5);

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

            var recurseTask = QueueUtility
                    .Process(folderQueue, folder =>
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

                    }, settings._3dx.QueryThreads, new CancellationToken());
            recurseTask.Wait();

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
    }
}