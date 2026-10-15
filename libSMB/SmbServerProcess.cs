using libCommon;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace libSMB
{
    //One portable-smb-server process serving one share from the VFS endpoint. It is tied
    //to this process twice over (a kill-on-close job object, and its own -exit-with-parent), so a
    //crash of Mount3DX never leaves a server holding the port.
    public sealed class SmbServerProcess : IDisposable
    {
        readonly Process process;
        readonly IPAddress address;
        readonly int port;
        readonly Queue<string> recentOutput = new();
        volatile bool stopping;

        //Raised when the server exits on its own; the message says what it printed last
        public event Action<string>? Died;

        SmbServerProcess(Process process, IPAddress address, int port)
        {
            this.process = process;
            this.address = address;
            this.port = port;
        }

        public bool IsRunning => !process.HasExited;

        public static SmbServerProcess Start(string binary, IPAddress address, int port, string user, string password, string vfsUrl, string shareName, string vfsToken)
        {
            var info = new ProcessStartInfo(binary)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var argument in new[]
            {
                "-ip", address.ToString(),
                "-port", port.ToString(),
                "-user", user,
                "-pass", password,
                "-vfs-token", vfsToken,
                //quit when Mount3DX is gone (a crash, a kill): no orphan holding the port on the next start
                "-exit-with-parent",
                "-vfs", vfsUrl,
                "-share", shareName,
            })
            {
                info.ArgumentList.Add(argument);
            }

            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            var server = new SmbServerProcess(process, address, port);
            process.OutputDataReceived += (_, e) => server.Remember(e.Data);
            process.ErrorDataReceived += (_, e) => server.Remember(e.Data);
            process.Exited += (_, _) => server.OnExited();

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                process.Dispose();
                throw new InvalidOperationException($"Could not start the SMB server {binary}: {ex.Message}", ex);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            ChildJob.Attach(process);

            try
            {
                server.WaitUntilListening(TimeSpan.FromSeconds(15));
            }
            catch
            {
                server.Dispose();
                throw;
            }

            Log.WriteLine($"SMB server listening on {address}:{port} (pid {process.Id})");
            return server;
        }

        void WaitUntilListening(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException($"The SMB server exited with code {process.ExitCode}: {RecentOutput()}");
                }

                if (CanConnect())
                {
                    //A connection proves something listens on the port, not that it is us: on a port
                    //another server holds (445 is Windows' own), ours exits a moment after starting.
                    Thread.Sleep(250);
                    if (process.HasExited)
                    {
                        throw new InvalidOperationException($"The SMB server exited with code {process.ExitCode}: {RecentOutput()}");
                    }

                    return;
                }

                Thread.Sleep(100);
            }

            throw new InvalidOperationException($"The SMB server did not start listening on {address}:{port} within {timeout.TotalSeconds:N0} seconds: {RecentOutput()}");
        }

        bool CanConnect()
        {
            try
            {
                using var client = new TcpClient(address.AddressFamily);
                return client.ConnectAsync(address, port).Wait(500);
            }
            catch (Exception ex) when (ex is SocketException or AggregateException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            stopping = true;

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                    Log.WriteLine("SMB server stopped.");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Log.WriteLine($"Error while stopping the SMB server: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        void OnExited()
        {
            if (stopping) return;

            var message = $"The SMB server exited with code {process.ExitCode}: {RecentOutput()}";
            Log.WriteLine(message);
            Died?.Invoke(message);
        }

        void Remember(string? line)
        {
            if (line == null) return;

            //the server only prints its start-up lines and errors (no per-request logging), so keep them all
            Log.WriteLine($"portable-smb-server: {line}");

            lock (recentOutput)
            {
                recentOutput.Enqueue(line);
                while (recentOutput.Count > 20)
                {
                    recentOutput.Dequeue();
                }
            }
        }

        string RecentOutput()
        {
            lock (recentOutput)
            {
                return recentOutput.Count == 0 ? "(no output)" : string.Join(" | ", recentOutput);
            }
        }
    }

    //A job object with kill-on-close: when this process ends in any way, the server goes with it
    //instead of lingering on the port
    static partial class ChildJob
    {
        const int JobObjectExtendedLimitInformationClass = 9;
        const uint JobObjectLimitKillOnJobClose = 0x2000;

        static readonly object Gate = new();
        static IntPtr job;

        public static void Attach(Process process)
        {
            try
            {
                lock (Gate)
                {
                    if (job == IntPtr.Zero)
                    {
                        job = CreateJobObjectW(IntPtr.Zero, null);
                        if (job == IntPtr.Zero) return;

                        var info = new JobObjectExtendedLimitInformation
                        {
                            BasicLimitInformation = { LimitFlags = JobObjectLimitKillOnJobClose }
                        };

                        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
                        var buffer = Marshal.AllocHGlobal(size);
                        try
                        {
                            Marshal.StructureToPtr(info, buffer, false);
                            SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, buffer, (uint)size);
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(buffer);
                        }
                    }

                    AssignProcessToJobObject(job, process.Handle);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                //-exit-with-parent still covers the crash case
                Log.WriteLine($"Could not attach the SMB server to a job object: {ex.Message}");
            }
        }

        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr CreateJobObjectW(IntPtr attributes, string? name);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [StructLayout(LayoutKind.Sequential)]
        struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }
    }
}
