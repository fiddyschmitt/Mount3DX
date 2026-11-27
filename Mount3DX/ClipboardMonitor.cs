using Avalonia.Threading;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Mount3DX
{
    //Raises Changed on the UI thread after the clipboard's content has changed. Windows notifies the
    //listener of every change, so nothing polls. Notifications are coalesced for a moment, because
    //browsers change the clipboard more than once for a single copy.
    public sealed partial class ClipboardMonitor : IDisposable
    {
        const int WM_CLIPBOARDUPDATE = 0x031D;
        const uint CF_UNICODETEXT = 13;
        static readonly IntPtr HWND_MESSAGE = new(-3);

        //What password managers mark their copies with, asking clipboard monitors to look away
        static readonly uint[] PrivateFormats =
        [
            RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"),
            RegisterClipboardFormat("Clipboard Viewer Ignore"),
        ];

        //Text longer than this isn't read at all (a copied link is far shorter)
        public const int MaxTextLength = 16 * 1024;

        readonly ListenerWindow window;
        readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(150) };
        bool disposed;

        public event EventHandler? Changed;

        //On the UI thread: the notifications arrive through its message loop
        public ClipboardMonitor()
        {
            settle.Tick += (sender, e) =>
            {
                settle.Stop();
                Changed?.Invoke(this, EventArgs.Empty);
            };

            window = new ListenerWindow(() =>
            {
                //each notification restarts the wait
                settle.Stop();
                settle.Start();
            });
        }

        //The clipboard's text, or null when it holds none, holds too much, is marked private, or
        //stays locked by the program that is writing it
        public string? ReadText()
        {
            if (disposed || PrivateFormats.Any(format => format != 0 && IsClipboardFormatAvailable(format)))
            {
                return null;
            }

            if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
            {
                return null;
            }

            //only ever held open briefly
            for (var attempt = 1; !OpenClipboard(window.Handle); attempt++)
            {
                if (attempt == 5) return null;
                Thread.Sleep(20);
            }

            try
            {
                var data = GetClipboardData(CF_UNICODETEXT);
                if (data == IntPtr.Zero) return null;

                //The text ends at its terminator, and the block holding it may be a little larger.
                //Nothing past the limit is copied, so a large copy costs nothing here.
                var readable = (int)Math.Min((long)GlobalSize(data) / 2, MaxTextLength + 1);
                if (readable == 0) return null;

                var text = GlobalLock(data);
                if (text == IntPtr.Zero) return null;

                try
                {
                    var chars = Marshal.PtrToStringUni(text, readable);
                    var end = chars.IndexOf('\0');
                    if (end >= 0) return chars[..end];

                    //no terminator: all of it, unless it goes on past the limit
                    return readable <= MaxTextLength ? chars : null;
                }
                finally
                {
                    GlobalUnlock(data);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            settle.Stop();
            window.Dispose();
        }

        //A message-only window: never shown, it only receives the notifications. Its messages come
        //through the message loop of the thread that made it, the UI thread.
        sealed unsafe partial class ListenerWindow : IDisposable
        {
            const string ClassName = "Mount3DX.ClipboardListener";

            //The class is registered once, for every listener there will be
            static readonly Lazy<bool> Registered = new(Register);

            //Which listener a handle is, for the window procedure; only the UI thread uses it
            static readonly Dictionary<IntPtr, ListenerWindow> Listeners = [];

            readonly Action onUpdate;

            public IntPtr Handle { get; private set; }

            public ListenerWindow(Action onUpdate)
            {
                this.onUpdate = onUpdate;

                _ = Registered.Value;
                Handle = CreateWindowEx(0, ClassName, null, 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
                if (Handle == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                Listeners[Handle] = this;

                if (!AddClipboardFormatListener(Handle))
                {
                    var error = new Win32Exception(Marshal.GetLastPInvokeError());
                    Dispose();
                    throw error;
                }
            }

            public void Dispose()
            {
                if (Handle == IntPtr.Zero) return;

                RemoveClipboardFormatListener(Handle);
                Listeners.Remove(Handle);
                DestroyWindow(Handle);
                Handle = IntPtr.Zero;
            }

            static bool Register()
            {
                fixed (char* className = ClassName)
                {
                    var windowClass = new WNDCLASSEXW
                    {
                        cbSize = (uint)sizeof(WNDCLASSEXW),
                        lpfnWndProc = &WindowProcedure,
                        hInstance = GetModuleHandle(null),
                        lpszClassName = className,
                    };

                    if (RegisterClassExW(&windowClass) == 0)
                    {
                        throw new Win32Exception(Marshal.GetLastPInvokeError());
                    }
                }

                return true;
            }

            [UnmanagedCallersOnly]
            static IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
            {
                if (message == WM_CLIPBOARDUPDATE && Listeners.TryGetValue(hwnd, out var listener))
                {
                    //nothing may escape into the message loop
                    try
                    {
                        listener.onUpdate();
                    }
                    catch (Exception ex)
                    {
                        libCommon.Log.WriteLine($"Error while handling a clipboard change: {ex}");
                    }

                    return IntPtr.Zero;
                }

                return DefWindowProcW(hwnd, message, wParam, lParam);
            }

            [StructLayout(LayoutKind.Sequential)]
            struct WNDCLASSEXW
            {
                public uint cbSize;
                public uint style;
                public delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> lpfnWndProc;
                public int cbClsExtra;
                public int cbWndExtra;
                public IntPtr hInstance;
                public IntPtr hIcon;
                public IntPtr hCursor;
                public IntPtr hbrBackground;
                public char* lpszMenuName;
                public char* lpszClassName;
                public IntPtr hIconSm;
            }

            [LibraryImport("user32.dll", SetLastError = true)]
            private static partial ushort RegisterClassExW(WNDCLASSEXW* windowClass);

            [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
            private static partial IntPtr CreateWindowEx(uint exStyle, string className, string? windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

            [LibraryImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static partial bool DestroyWindow(IntPtr hwnd);

            [LibraryImport("user32.dll")]
            private static partial IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

            [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
            private static partial IntPtr GetModuleHandle(string? moduleName);
        }

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AddClipboardFormatListener(IntPtr hwnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool RemoveClipboardFormatListener(IntPtr hwnd);

        [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial uint RegisterClipboardFormat(string format);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsClipboardFormatAvailable(uint format);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool OpenClipboard(IntPtr newOwner);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseClipboard();

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetClipboardData(uint format);

        [LibraryImport("kernel32.dll")]
        private static partial nuint GlobalSize(IntPtr memory);

        [LibraryImport("kernel32.dll")]
        private static partial IntPtr GlobalLock(IntPtr memory);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GlobalUnlock(IntPtr memory);
    }
}
