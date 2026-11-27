using Avalonia.Controls;
using System.Runtime.InteropServices;

namespace Mount3DX
{
    public enum MessageBoxIcon : uint
    {
        Warning = 0x30,     //MB_ICONWARNING
        Information = 0x40, //MB_ICONINFORMATION
    }

    //Windows' own message box, with an OK button. The app manifest asks for the common controls that
    //draw it in the current Windows style.
    public static partial class MessageBox
    {
        //Modal to the owner, if there is one. Any thread.
        public static void Show(string text, MessageBoxIcon icon, Window? owner = null)
        {
            var ownerHandle = owner?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            MessageBoxW(ownerHandle, text, MainWindow.PROGRAM_NAME, (uint)icon);
        }

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int MessageBoxW(IntPtr owner, string text, string caption, uint type);
    }
}
