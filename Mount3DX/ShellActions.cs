using libCommon;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mount3DX
{
    //Opening things the way Explorer would. These touch the drive, which can be slow to answer, and
    //the shell wants COM, so they are called on an STA thread of their own rather than the UI thread.
    public static partial class ShellActions
    {
        const uint ASFW_ANY = uint.MaxValue;

        //Lets whatever opens next come to the front. Only works for a process that may set the
        //foreground window itself, such as one that has just been clicked.
        public static void AllowAnyProcessToTakeForeground()
        {
            AllowSetForegroundWindow(ASFW_ANY);
        }

        //A file with the program it is associated with, or a folder in Explorer, as a double-click
        //would. The shell shows its own messages, including the "How do you want to open this file?"
        //choice for a file that nothing is associated with.
        public static void Open(string path)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    ErrorDialog = true,
                });
            }
            catch (Win32Exception ex)
            {
                //the shell has already shown the problem, or the choice was cancelled
                Log.WriteLine($"The shell could not open {path}: {ex.Message}");
            }
        }

        //Explorer showing the folder that holds the item, with the item selected. Explorer reuses a
        //window that already shows the folder, and nothing is parsed from a command line, so any name
        //works.
        public static void ShowInFolder(string path)
        {
            var ids = ParseDisplayName(path);

            try
            {
                //given no items, the shell takes the id list as the item, and selects it in its folder
                var result = SHOpenFolderAndSelectItems(ids, 0, null, 0);
                if (result < 0)
                {
                    throw new IOException($"Explorer could not show {path}. {Marshal.GetExceptionForHR(result)?.Message}".Trim());
                }
            }
            finally
            {
                ILFree(ids);
            }
        }

        static IntPtr ParseDisplayName(string path)
        {
            var result = SHParseDisplayName(path, IntPtr.Zero, out var ids, 0, out _);
            if (result < 0)
            {
                throw new IOException($"Could not find {path}. {Marshal.GetExceptionForHR(result)?.Message}".Trim());
            }

            return ids;
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AllowSetForegroundWindow(uint processId);

        [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr ids, uint attributesIn, out uint attributesOut);

        [LibraryImport("shell32.dll")]
        private static partial int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr[]? items, uint flags);

        [LibraryImport("shell32.dll")]
        private static partial void ILFree(IntPtr ids);
    }
}
