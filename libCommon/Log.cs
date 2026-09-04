using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCommon
{
    public static class Log
    {
        static readonly object logLock = new();

        //Next to the executable (like settings.json), not in the working directory, which depends
        //on how the app was launched
        public static string? Filename { get; set; } = Path.Combine(AppContext.BaseDirectory, Path.ChangeExtension(AppDomain.CurrentDomain.FriendlyName, ".log"));

        public static void WriteLine(string message)
        {
            var filename = Filename;
            if (filename == null) return;

            var logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";

            try
            {
                lock (logLock)
                {
                    //AppendAllText creates the file if needed. The file is deliberately
                    //opened per line so logs survive a crash.
                    File.AppendAllText(filename, logLine);
                }
            }
            catch (Exception ex)
            {
                //Logging is called from every thread in the app and must never take it down
                //(for example when the install folder is read-only)
                System.Diagnostics.Debug.WriteLine($"Could not write to log file {filename}: {ex.Message}");
            }
        }
    }
}
