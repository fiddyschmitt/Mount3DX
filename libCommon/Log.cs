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

        public static string? Filename { get; set; } = Path.ChangeExtension(AppDomain.CurrentDomain.FriendlyName, ".log");

        public static void WriteLine(string message)
        {
            var filename = Filename;
            if (filename == null) return;

            var logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
            lock (logLock)
            {
                //AppendAllText creates the file if needed. The file is deliberately
                //opened per line so logs survive a crash.
                File.AppendAllText(filename, logLine);
            }
        }
    }
}
