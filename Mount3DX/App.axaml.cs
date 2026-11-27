using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Mount3DX
{
    public partial class App : Application
    {
        //The main window, or the message that Mount 3DX is already running
        readonly Func<Window> createMainWindow;

        public App(Func<Window> createMainWindow)
        {
            this.createMainWindow = createMainWindow;
        }

        //For the XAML previewer
        public App() : this(() => new MainWindow(startOnLaunch: false))
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
                desktop.MainWindow = createMainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
