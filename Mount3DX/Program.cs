using Avalonia;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Mount3DX
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            //Only allow one instance; a second instance would fail to bind the WebDAV port with a confusing error.
            //Global, because the port is machine-wide even across user sessions.
            using var singleInstanceMutex = CreateSingleInstanceMutex(out var isFirstInstance);
            if (!isFirstInstance)
            {
                MessageBox.Show($"{MainWindow.PROGRAM_NAME} is already running.", MessageBoxIcon.Information);
                return;
            }

            //the startup shortcut passes --start
            var startOnLaunch = args.Contains(StartupShortcut.StartArgument, StringComparer.OrdinalIgnoreCase);
            BuildAvaloniaApp(() => new App(startOnLaunch)).StartWithClassicDesktopLifetime(args);
        }

        //What the XAML previewer starts
        public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(() => new App(startOnLaunch: false));

        static AppBuilder BuildAvaloniaApp(Func<App> app) => AppBuilder
            .Configure(app)
            .UseWin32()
            .UseSkia()
            .UseHarfBuzz()
            //A window of settings has no need of the GPU. Drawn in software, it works the same on any
            //machine, over Remote Desktop too, and ANGLE (the GPU renderer's library) isn't shipped.
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });

        static Mutex? CreateSingleInstanceMutex(out bool isFirstInstance)
        {
            //A Global\ mutex gets a DACL that only its creator can open, so an instance started by
            //another user in another session would throw UnauthorizedAccessException rather than
            //discover the running one. Grant everyone the rights needed to open and wait on it.
            var security = new MutexSecurity();
            security.AddAccessRule(new MutexAccessRule(
                                        new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                                        MutexRights.Synchronize | MutexRights.Modify,
                                        AccessControlType.Allow));

            try
            {
                return MutexAcl.Create(initiallyOwned: true, @"Global\Mount3DX", out isFirstInstance, security);
            }
            catch (UnauthorizedAccessException)
            {
                //the mutex exists but was created without the permissive DACL (an older version
                //running in another user's session): treat it as an instance that is already running
                isFirstInstance = false;
                return null;
            }
        }
    }
}
