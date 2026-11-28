using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

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

                var window = createMainWindow();
                if (window is MainWindow { StartsInTray: true })
                {
                    //The main window is shown as the program starts, and this one mustn't be: it waits in
                    //the tray. It becomes the main window once the program is running, so that closing
                    //it, once it has been shown from there, still ends the program.
                    Dispatcher.UIThread.Post(() => desktop.MainWindow = window);
                }
                else
                {
                    desktop.MainWindow = window;
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
