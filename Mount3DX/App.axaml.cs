using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Mount3DX
{
    public partial class App : Application
    {
        //Run with --start, as the startup shortcut does
        readonly bool startOnLaunch;

        public App(bool startOnLaunch)
        {
            this.startOnLaunch = startOnLaunch;
        }

        //For the XAML previewer
        public App() : this(startOnLaunch: false)
        {
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                //The link popup is a window of its own, hidden between links; it mustn't keep the
                //program running once the main window has closed
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                desktop.MainWindow = new MainWindow(startOnLaunch);
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
