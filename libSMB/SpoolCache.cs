using libCommon;
using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;
using System.Text;

namespace libSMB
{
    //portable-smb-server asks for file content in stateless ranged reads (/read?offset&length), with
    //several in flight, while a 3DX download is a three-call affair that streams from the start.
    //Answering each read with its own download would multiply the cost of every file, so the first
    //read of a file starts one download into a file under the cache folder, and a read of any range
    //is served as soon as the download has reached it: parallel and out-of-order reads share the one
    //download, and reopening a file soon afterwards comes from the cache. A download nobody has read
    //from for a while is stopped and its partial file deleted; finished files stay until they go
    //unused or the cache is over its size limit, and everything goes when the session stops.
    public sealed class SpoolCache : IDisposable
    {
        readonly string directory;
        readonly long maxBytes;
        readonly TimeSpan idleTimeout;
        readonly TimeSpan maxUnused;
        readonly Dictionary<string, Spool> spools = new(StringComparer.Ordinal);
        readonly object gate = new();
        readonly Timer evictionTimer;
        bool disposed;

        public SpoolCache(string directory, long maxBytes = 2L << 30, TimeSpan? idleTimeout = null, TimeSpan? maxUnused = null)
        {
            this.directory = directory;
            this.maxBytes = maxBytes;
            this.idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(60);
            this.maxUnused = maxUnused ?? TimeSpan.FromMinutes(15);

            Directory.CreateDirectory(directory);

            //left by a crash
            foreach (var stale in Directory.EnumerateFiles(directory, "*.spool"))
            {
                TryDelete(stale);
            }

            evictionTimer = new Timer(_ => Evict(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        //Fills the buffer from the file identified by key (any string that changes when the content
        //does), starting its download if nobody has it yet. Returns fewer bytes only at the end of
        //the file: the SMB server treats a short answer as end-of-file.
        public async Task<int> ReadAsync(string key, long length, Func<Stream> openUpstream, long offset, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (buffer.Length == 0 || offset >= length)
            {
                return 0;
            }

            var spool = GetOrStart(key, length, openUpstream);
            return await spool.ReadFullyAsync(offset, buffer, cancellationToken).ConfigureAwait(false);
        }

        Spool GetOrStart(string key, long length, Func<Stream> openUpstream)
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);

                if (!spools.TryGetValue(key, out var spool) || spool.IsDead)
                {
                    EvictLocked();

                    var path = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32].ToLowerInvariant() + ".spool");
                    spool = new Spool(key, path, length, openUpstream);
                    spools[key] = spool;
                    spool.Start();
                }

                return spool;
            }
        }

        void Evict()
        {
            lock (gate)
            {
                if (disposed) return;
                EvictLocked();
            }
        }

        void EvictLocked()
        {
            var now = DateTime.UtcNow;

            //a reader waiting for its range is a use, however long the download takes
            foreach (var (key, spool) in spools.ToList())
            {
                if (spool.ActiveReaders > 0) continue;

                var unused = now - spool.LastUsed;
                if (spool.IsDead || (spool.IsComplete ? unused > maxUnused : unused > idleTimeout))
                {
                    spools.Remove(key);
                    spool.Dispose();
                }
            }

            var total = spools.Values.Sum(spool => spool.Available);
            foreach (var spool in spools.Values.Where(spool => spool.IsComplete && spool.ActiveReaders == 0).OrderBy(spool => spool.LastUsed).ToList())
            {
                if (total <= maxBytes) break;

                spools.Remove(spool.Key);
                total -= spool.Available;
                spool.Dispose();
            }
        }

        public void Dispose()
        {
            evictionTimer.Dispose();

            List<Spool> all;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;

                all = spools.Values.ToList();
                spools.Clear();
            }

            foreach (var spool in all)
            {
                spool.Dispose();
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.WriteLine($"Could not delete cache file {path}: {ex.Message}");
            }
        }

        //One shared download: the file on disk plus how far it has got
        sealed class Spool : IDisposable
        {
            readonly Func<Stream> openUpstream;
            readonly CancellationTokenSource cts = new();
            readonly SafeFileHandle writeHandle;
            readonly SafeFileHandle readHandle;
            readonly object gate = new();
            TaskCompletionSource progress = new(TaskCreationOptions.RunContinuationsAsynchronously);
            long available;
            bool complete;
            Exception? error;
            int activeReaders;
            bool disposed;

            public Spool(string key, string path, long length, Func<Stream> openUpstream)
            {
                Key = key;
                Path = path;
                Length = length;
                this.openUpstream = openUpstream;

                writeHandle = File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete, FileOptions.Asynchronous);
                readHandle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                LastUsed = DateTime.UtcNow;
            }

            public string Key { get; }
            public string Path { get; }
            public long Length { get; }
            public DateTime LastUsed { get; private set; }

            public long Available => Interlocked.Read(ref available);
            public bool IsComplete { get { lock (gate) { return complete; } } }
            public bool IsDead { get { lock (gate) { return disposed || error != null; } } }
            public int ActiveReaders => Volatile.Read(ref activeReaders);

            public void Start()
            {
                _ = Task.Run(PumpAsync);
            }

            public async Task<int> ReadFullyAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref activeReaders);
                try
                {
                    LastUsed = DateTime.UtcNow;

                    var want = (int)Math.Min(buffer.Length, Length - offset);
                    var filled = 0;

                    while (filled < want)
                    {
                        Task wait;
                        int readable;
                        lock (gate)
                        {
                            readable = (int)Math.Min(want - filled, available - (offset + filled));

                            if (readable <= 0)
                            {
                                if (error != null)
                                {
                                    throw new IOException($"The download of {Key} failed: {error.Message}", error);
                                }

                                if (complete)
                                {
                                    //the upstream was shorter than announced
                                    break;
                                }

                                wait = progress.Task;
                            }
                            else
                            {
                                wait = Task.CompletedTask;
                            }
                        }

                        if (readable > 0)
                        {
                            //the file only ever grows, so this needs no lock
                            var read = ReadAt(readHandle, buffer.Slice(filled, readable), offset + filled);
                            if (read <= 0)
                            {
                                throw new IOException($"The cache file for {Key} returned no data at byte {offset + filled:N0}.");
                            }

                            filled += read;
                        }
                        else
                        {
                            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }

                    LastUsed = DateTime.UtcNow;
                    return filled;
                }
                finally
                {
                    Interlocked.Decrement(ref activeReaders);
                }
            }

            static int ReadAt(SafeFileHandle handle, Memory<byte> buffer, long offset)
            {
                return RandomAccess.Read(handle, buffer.Span, offset);
            }

            async Task PumpAsync()
            {
                var token = cts.Token;
                try
                {
                    //opening a 3DX download blocks on its HTTP calls; this runs on the thread pool
                    using var upstream = openUpstream();
                    if (upstream.CanSeek)
                    {
                        upstream.Seek(0, SeekOrigin.Begin);
                    }

                    var buffer = new byte[256 * 1024];
                    long written = 0;
                    while (written < Length)
                    {
                        var n = await upstream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, Length - written)), token).ConfigureAwait(false);
                        if (n <= 0) break;

                        await RandomAccess.WriteAsync(writeHandle, buffer.AsMemory(0, n), written, token).ConfigureAwait(false);
                        written += n;
                        Signal(written, complete: false, error: null);
                    }

                    if (written < Length)
                    {
                        Log.WriteLine($"Download of {Key} ended after {written:N0} of {Length:N0} bytes.");
                    }

                    Signal(written, complete: true, error: null);
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested)
                    {
                        //stopped on purpose (idle, evicted, or the session ended)
                        Signal(Available, complete: true, error: new OperationCanceledException("The download was stopped."));
                    }
                    else
                    {
                        Log.WriteLine($"Download of {Key} failed:{Environment.NewLine}{ex}");
                        Signal(Available, complete: true, error: ex);
                    }
                }
            }

            void Signal(long newAvailable, bool complete, Exception? error)
            {
                TaskCompletionSource previous;
                lock (gate)
                {
                    Interlocked.Exchange(ref available, newAvailable);
                    if (complete)
                    {
                        this.complete = true;
                        this.error ??= error;
                    }

                    previous = progress;
                    progress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                previous.TrySetResult();
            }

            public void Dispose()
            {
                lock (gate)
                {
                    if (disposed) return;
                    disposed = true;
                }

                cts.Cancel();

                //readers waiting on progress wake up and find the error
                Signal(Available, complete: true, error: new OperationCanceledException("The download was stopped."));

                readHandle.Dispose();
                writeHandle.Dispose();
                cts.Dispose();
                TryDelete(Path);
            }
        }
    }
}
