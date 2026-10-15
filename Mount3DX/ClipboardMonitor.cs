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
        readonly System.Windows.Forms.Timer settle = new() { Interval = 150 };
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

            settle.Dispose();
            window.Dispose();
        }

        //A message-only window: never shown, it only receives the notifications
        sealed class ListenerWindow : NativeWindow, IDisposable
        {
            readonly Action onUpdate;

            public ListenerWindow(Action onUpdate)
            {
                this.onUpdate = onUpdate;

                CreateHandle(new CreateParams { Parent = HWND_MESSAGE });

                if (!AddClipboardFormatListener(Handle))
                {
                    var error = new Win32Exception(Marshal.GetLastPInvokeError());
                    DestroyHandle();
                    throw error;
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_CLIPBOARDUPDATE)
                {
                    onUpdate();
                    m.Result = IntPtr.Zero;
                    return;
                }

                base.WndProc(ref m);
            }

            public void Dispose()
            {
                if (Handle == IntPtr.Zero) return;

                RemoveClipboardFormatListener(Handle);
                DestroyHandle();
            }
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
