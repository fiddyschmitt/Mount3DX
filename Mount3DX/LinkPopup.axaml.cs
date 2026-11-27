using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Runtime.InteropServices;

namespace Mount3DX
{
    //The small window that offers to open the document whose 3DX link was just copied. It shows the
    //document's whole name, sized to fit it, at the bottom right of the primary screen, above the
    //taskbar. It never takes the focus: the link was probably copied to be pasted somewhere, so the
    //window it was copied in stays active, even when a button here is clicked. It hides when a button
    //is clicked, or after a few seconds; the countdown pauses while the mouse is over it.
    public sealed partial class LinkPopup : Window
    {
        static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

        //Once the mouse has been over it, it stays at least this long after the mouse leaves
        static readonly TimeSpan LingerAfterHover = TimeSpan.FromSeconds(2);

        //From the edges of the screen's working area
        const double ScreenMargin = 12;

        const uint WS_EX_TOPMOST = 0x00000008;
        const uint WS_EX_TOOLWINDOW = 0x00000080;   //not in Alt+Tab
        const uint WS_EX_NOACTIVATE = 0x08000000;   //clicks don't activate it
        const uint WM_MOUSEACTIVATE = 0x0021;
        const int MA_NOACTIVATE = 3;
        const int GCL_STYLE = -26;
        const long CS_DROPSHADOW = 0x00020000;

        readonly DispatcherTimer countdown = new() { Interval = TimeSpan.FromMilliseconds(100) };

        TimeSpan remaining;
        long lastTick;
        bool hovered;

        //Raised with the document's id, so that it is found again wherever it is by then
        public event Action<string>? OpenRequested;
        public event Action<string>? OpenFolderRequested;

        public string? ObjectId { get; private set; }

        public LinkPopup()
        {
            InitializeComponent();

            countdown.Tick += (sender, e) => CountDown();

            Win32Properties.AddWindowStylesCallback(this, (style, exStyle) => (style, exStyle | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
            Win32Properties.AddWndProcHookCallback(this, WndProcHook);
            AddDropShadow();
        }

        //The shadow that sets it apart from what is behind it. The shadow is a window class's style, and
        //Avalonia gives every window a class of its own ("Avalonia-" and a GUID), so this one's is the
        //only shadow it adds. Should a class ever be shared, it is left without.
        void AddDropShadow()
        {
            const string ClassPrefix = "Avalonia-";

            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;

            Span<char> name = stackalloc char[256];
            int length;
            unsafe
            {
                fixed (char* buffer = name)
                {
                    length = GetClassNameW(handle, buffer, name.Length);
                }
            }

            var className = name[..Math.Max(0, length)];
            if (!className.StartsWith(ClassPrefix) || !Guid.TryParse(className[ClassPrefix.Length..], out _)) return;

            SetClassLongPtrW(handle, GCL_STYLE, (IntPtr)((long)GetClassLongPtrW(handle, GCL_STYLE) | CS_DROPSHADOW));
        }

        //Shows the document, or replaces the one showing, and starts the countdown again
        public void ShowFor(LinkedDocument document)
        {
            ObjectId = document.ObjectId;
            lblName.Text = document.Name;
            FitAndPlace();

            remaining = Lifetime;
            hovered = false;
            lastTick = Environment.TickCount64;
            countdown.Start();

            if (!IsVisible)
            {
                Show();
            }
        }

        public void Dismiss()
        {
            countdown.Stop();
            ObjectId = null;
            Hide();
        }

        //The window takes the size of the whole name (SizeToContent): on one line where the screen is
        //wide enough, and over as many lines as it takes where it isn't. The buttons keep their size.
        //It goes in the bottom right corner of the primary screen's working area, which leaves out
        //the taskbar (that screen has the notification area).
        void FitAndPlace()
        {
            var screen = Screens.Primary ?? Screens.All[0];
            var area = screen.WorkingArea;
            var scaling = screen.Scaling;

            //the room on the screen, less the margins and the popup's own edges
            var content = (Control)Content!;
            var edges = content.Margin.Left + content.Margin.Right + ((Border)content).BorderThickness.Left * 2 + ((Border)content).Padding.Left * 2;
            lblName.MaxWidth = Math.Max(0, area.Width / scaling - 2 * ScreenMargin - edges);

            content.Measure(Size.Infinity);
            var size = content.DesiredSize;

            var width = (int)Math.Ceiling(size.Width * scaling);
            var height = (int)Math.Ceiling(size.Height * scaling);
            var margin = (int)Math.Round(ScreenMargin * scaling);
            Position = new PixelPoint(area.Right - width - margin, area.Bottom - height - margin);
        }

        void BtnOpen_Click(object? sender, RoutedEventArgs e) => OnButtonClicked(OpenRequested);

        void BtnOpenFolder_Click(object? sender, RoutedEventArgs e) => OnButtonClicked(OpenFolderRequested);

        void OnButtonClicked(Action<string>? handler)
        {
            var objectId = ObjectId;
            Dismiss();

            if (objectId != null)
            {
                handler?.Invoke(objectId);
            }
        }

        void CountDown()
        {
            var now = Environment.TickCount64;
            var elapsed = TimeSpan.FromMilliseconds(now - lastTick);
            lastTick = now;

            if (IsMouseOver())
            {
                hovered = true;
                return;
            }

            if (hovered)
            {
                hovered = false;
                if (remaining < LingerAfterHover)
                {
                    remaining = LingerAfterHover;
                }
                return;
            }

            remaining -= elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                Dismiss();
            }
        }

        bool IsMouseOver()
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            return handle != IntPtr.Zero
                && GetCursorPos(out var cursor)
                && GetWindowRect(handle, out var bounds)
                && cursor.X >= bounds.Left && cursor.X < bounds.Right
                && cursor.Y >= bounds.Top && cursor.Y < bounds.Bottom;
        }

        //Clicks don't activate it
        static IntPtr WndProcHook(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WM_MOUSEACTIVATE)
            {
                handled = true;
                return MA_NOACTIVATE;
            }

            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetCursorPos(out POINT point);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [LibraryImport("user32.dll")]
        private static unsafe partial int GetClassNameW(IntPtr hwnd, char* className, int maxCount);

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetClassLongPtrW(IntPtr hwnd, int index);

        [LibraryImport("user32.dll")]
        private static partial IntPtr SetClassLongPtrW(IntPtr hwnd, int index, IntPtr value);
    }
}
