using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCommon
{
    public static class Log
    {
        public static string? Filename { get; set; } = Path.ChangeExtension(AppDomain.CurrentDomain.FriendlyName, ".log");

        public static void WriteLine(string message)
        {
            if (Filename == null) return;

            var logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
            lock (Filename)
            {
                if (File.Exists(Filename))
                {
                    File.AppendAllText(Filename, logLine);
                }
                else
                {
                    File.WriteAllText(Filename, logLine);
                }
            }
        }
    }
}
