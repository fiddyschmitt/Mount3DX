using libCommon;
using System.Diagnostics;

namespace libSMB
{
    //The Windows SMB client's side: Windows 11 24H2 (build 26100) added net use /TCPPORT, which
    //registers a port other than 445 for a server name. The connection is device-less (no drive
    //letter): once made, the UNC path works in Explorer and every program. The port sticks to the
    //server name for every later connection to it, which is why the share lives on its own name.
    public static class SmbConnection
    {
        public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100);

        static readonly TimeSpan NetUseTimeout = TimeSpan.FromSeconds(60);

        public static void Connect(string unc, int port, string user, string password)
        {
            var (code, output) = RunNet(["use", unc, $"/TCPPORT:{port}", $"/USER:{user}", password]);
            if (code != 0)
            {
                throw new InvalidOperationException($"net use {unc} /TCPPORT:{port} failed: {Gist(output, code)}");
            }

            Log.WriteLine($"Connected {unc} on port {port}");
        }

        //Best effort: a connection left by an earlier run (a crash) keeps its old port and credentials,
        //so it must go before a new one is made; at stop, the server is going away anyway, so open
        //files are not a reason to keep it (/y answers the prompt)
        public static void Disconnect(string unc)
        {
            try
            {
                var (code, output) = RunNet(["use", unc, "/delete", "/y"]);
                Log.WriteLine(code == 0 ? $"Disconnected {unc}" : $"No connection to {unc} to remove ({Gist(output, code)})");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Could not run net use {unc} /delete: {ex.Message}");
            }
        }

        //Runs net.exe with stdin closed (a prompt reads end-of-file instead of hanging) and a timeout
        static (int Code, string Output) RunNet(IReadOnlyList<string> arguments)
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "net.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info) ?? throw new InvalidOperationException("net.exe did not start");
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)NetUseTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException) { }

                return (-1, $"net use did not finish within {NetUseTimeout.TotalSeconds:N0} seconds.");
            }

            var output = (stdout.Result + "\n" + stderr.Result).Trim();
            return (process.ExitCode, output);
        }

        //net.exe explains itself on its last line ("The network path was not found."); the first only
        //names the error number
        static string Gist(string output, int code)
        {
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return lines.Length > 0 ? lines[^1] : $"exit code {code}.";
        }
    }
}
