using System.Runtime.InteropServices;
using System.Text;

namespace Mount3DX
{
    //The shortcut in the user's Startup folder (shell:startup), which Windows runs when they sign in.
    //It runs this executable minimised, which puts it in the tray only, with the argument that starts
    //the session straight away.
    //Whether it is there is the setting: nothing about it is kept in settings.json.
    public static class StartupShortcut
    {
        //Starts the session as soon as the program is up, as if Start had been clicked
        public const string StartArgument = "--start";

        //What a shortcut's Run box calls "Minimized": minimised, without taking the focus
        const int SW_SHOWMINNOACTIVE = 7;

        public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), $"{MainWindow.PROGRAM_NAME}.lnk");

        public static bool Exists => File.Exists(FilePath);

        //Pointing at this executable, wherever it is now
        public static void Create()
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Could not tell where this program is.");

            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(executable);
                link.SetArguments(StartArgument);
                link.SetWorkingDirectory(Path.GetDirectoryName(executable) ?? "");
                link.SetShowCmd(SW_SHOWMINNOACTIVE);
                link.SetDescription($"Starts {MainWindow.PROGRAM_NAME} when you sign in to Windows");

                ((IPersistFile)link).Save(FilePath, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }

        public static void Delete() => File.Delete(FilePath);

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink
        {
        }

        //Declared here rather than taken from .NET (System.Runtime.InteropServices.ComTypes). The
        //published exe is trimmed, and through .NET's copy the shortcut was never saved, without an
        //error: trimming leaves out the methods that aren't called, and COM calls a method by its place
        //in the interface. This assembly isn't trimmed, so every method here stays in its place.
        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010B-0000-0000-C000-000000000046")]
        interface IPersistFile
        {
            void GetClassID(out Guid classId);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, int mode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string? fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
        }

        //All of it, in vtable order, though only the setters are used
        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);
            void GetIDList(out IntPtr ids);
            void SetIDList(IntPtr ids);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int size);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int size);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int size);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCmd);
            void SetShowCmd(int showCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int size, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
            void Resolve(IntPtr window, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }
    }
}
