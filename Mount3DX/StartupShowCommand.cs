using Avalonia.Controls;
using System.Runtime.InteropServices;

namespace Mount3DX
{
    //How whatever started the program asked for its window to be shown: a shortcut's Run box asks for
    //Normal, Minimized or Maximized (the startup shortcut asks for Minimized, without taking the
    //focus). Windows Forms applied this to the first window by itself; Avalonia doesn't.
    public static partial class StartupShowCommand
    {
        const int STARTF_USESHOWWINDOW = 0x1;

        const short SW_SHOWMINIMIZED = 2;
        const short SW_SHOWMAXIMIZED = 3;
        const short SW_SHOWNOACTIVATE = 4;
        const short SW_MINIMIZE = 6;
        const short SW_SHOWMINNOACTIVE = 7;
        const short SW_SHOWNA = 8;
        const short SW_FORCEMINIMIZE = 11;

        public static (WindowState State, bool Activate) Get()
        {
            GetStartupInfoW(out var info);
            if ((info.dwFlags & STARTF_USESHOWWINDOW) == 0)
            {
                return (WindowState.Normal, true);
            }

            return info.wShowWindow switch
            {
                SW_SHOWMINIMIZED => (WindowState.Minimized, true),
                SW_MINIMIZE or SW_SHOWMINNOACTIVE or SW_FORCEMINIMIZE => (WindowState.Minimized, false),
                SW_SHOWMAXIMIZED => (WindowState.Maximized, true),
                SW_SHOWNOACTIVATE or SW_SHOWNA => (WindowState.Normal, false),
                _ => (WindowState.Normal, true),
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        struct STARTUPINFOW
        {
            public int cb;
            public IntPtr lpReserved;
            public IntPtr lpDesktop;
            public IntPtr lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [LibraryImport("kernel32.dll")]
        private static partial void GetStartupInfoW(out STARTUPINFOW info);
    }
}
