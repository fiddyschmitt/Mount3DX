using libCommon;
using libVFS.WebDAV.Stores;
using System.Data;
using System.Text;

namespace Mount3DX
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        public static string PROGRAM_NAME = "Mount 3DX";
        public static string PROGRAM_VERSION = "0.1";

        private void Form1_Load(object sender, EventArgs e)
        {
            Text = $"{PROGRAM_NAME} {PROGRAM_VERSION}";

            LoadSettings();

            //todo: The Windows 10 WebDAV client sometimes restricts access to files larger than 50MB.
            //This can be changed in: HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WebClient\Parameters\FileSizeLimitInBytes = 4294967295
            //After which, the 'WebClient' service needs to be restarted.

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
                if (!string.IsNullOrEmpty(settingsJson))
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

        private void btnStart_Click(object sender, EventArgs e)
        {
            lblRunningStatus.BackColor = Control.DefaultBackColor;
            lblRunningStatus.ForeColor = Color.Black;
            lblRunningStatus.Text = "";

            grp3dx.Enabled = false;
            grpVfs.Enabled = false;

            if (btnStart.Text.Equals("Start"))
            {
                SaveSettings();
                LoadSettings();

                session = new Session(settings);

                session.Progress += (sender, args) =>
                {
                    switch (args.Nature)
                    {
                        case Session.ProgressEventArgs.EnumNature.Good:
                            lblRunningStatus.BackColor = Color.LimeGreen;
                            lblRunningStatus.ForeColor = Color.Black;
                            break;

                        case Session.ProgressEventArgs.EnumNature.Neutral:
                            lblRunningStatus.BackColor = Control.DefaultBackColor;
                            lblRunningStatus.ForeColor = Color.Black;
                            break;

                        case Session.ProgressEventArgs.EnumNature.Bad:
                            lblRunningStatus.BackColor = Color.Red;
                            lblRunningStatus.ForeColor = Color.White;
                            break;
                    }

                    lblRunningStatus.Text = args.Message;
                };

                var started = session.Start();

                if (started)
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
            }
            else
            {
                session?.Stop();
                btnStart.Text = "Start";

                grp3dx.Enabled = true;
                grpVfs.Enabled = true;
            }
        }
    }
}