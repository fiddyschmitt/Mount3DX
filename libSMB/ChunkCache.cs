using libCommon;
using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;
using System.Text;

namespace libSMB
{
    //portable-smb-server asks for file content in stateless ranged reads (/read?offset&length), with
    //several in flight, while a 3DX download is a three-call affair that then streams from wherever
    //it was opened. Files can be tens of GB, so neither "one download per read" nor "download the
    //whole file first" will do.
    //
    //Each file is cached in fixed-size chunks under the cache folder, fetched only when a read needs
    //them. A read demands the chunks it covers; a run (one upstream download, read in order) fetches
    //them and keeps going a few chunks past the latest demand, so a sequential copy is one download
    //however its reads are split or reordered, and a read somewhere else in the file starts a run
    //there (one Range request) instead of waiting for everything before it. Readers are served from
    //a chunk as its bytes arrive. Chunks nobody has used for a while are dropped, the least recently
    //used go first when the cache is over its size limit (during a copy too, so a huge file never
    //needs its whole size on disk), a run with no demand closes its download, and everything goes
    //when the session stops.
    public sealed class ChunkCache : IDisposable
    {
        public const int ChunkSize = 8 * 1024 * 1024;

        //how far past the latest demand a run keeps fetching; the client's own read-ahead rarely
        //asks for more than a few MiB beyond what an application has read
        const int ReadAheadChunks = 3;

        //a demand this far past a run's position is streamed through rather than seeked to: cheaper
        //than the three calls a fresh download costs
        const int StreamThroughChunks = 2 * ReadAheadChunks;

        //more runs than this on one file and an idle one is closed before another is opened
        const int MaxRunsPerFile = 2;

        const int BlockSize = 256 * 1024;

        //the least that leaves room for a run's read-ahead and the reads in flight
        public const long MinimumMaxBytes = 16L * ChunkSize;

        readonly string directory;
        readonly long maxBytes;
        readonly TimeSpan idleTimeout;
        readonly TimeSpan maxUnused;
        readonly Dictionary<string, CachedFile> files = new(StringComparer.Ordinal);
        readonly object gate = new();
        readonly Timer evictionTimer;
        long totalBytes;
        bool disposed;

        public ChunkCache(string directory, long maxBytes = 2L << 30, TimeSpan? idleTimeout = null, TimeSpan? maxUnused = null)
        {
            this.directory = directory;
            this.maxBytes = Math.Max(maxBytes, MinimumMaxBytes);
            this.idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(60);
            this.maxUnused = maxUnused ?? TimeSpan.FromMinutes(15);

            if (this.maxBytes != maxBytes)
            {
                Log.WriteLine($"Cache size limit raised from {maxBytes:N0} to {this.maxBytes:N0} bytes, the least that leaves room for the reads in flight.");
            }

            //whatever a crash left behind
            if (Directory.Exists(directory))
            {
                TryDeleteDirectory(directory);
            }
            Directory.CreateDirectory(directory);

            evictionTimer = new Timer(_ => Evict(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        //Bytes on disk, for diagnostics and tests
        public long TotalBytes
        {
            get { lock (gate) { return totalBytes; } }
        }

        //Fills the buffer from the file identified by key (any string that changes when the content
        //does). Returns fewer bytes only at the end of the file: the SMB server treats a short answer
        //as end-of-file.
        public async Task<int> ReadAsync(string key, long length, Func<Stream> openUpstream, long offset, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (buffer.Length == 0 || offset >= length)
            {
                return 0;
            }

            var want = (int)Math.Min(buffer.Length, length - offset);
            var filled = 0;
            CachedFile? file = null;

            while (filled < want)
            {
                var position = offset + filled;
                var index = (int)(position / ChunkSize);
                var inChunk = (int)(position % ChunkSize);

                Chunk chunk;
                Task? wait = null;
                var readable = 0;
                lock (gate)
                {
                    ObjectDisposedException.ThrowIf(disposed, this);

                    if (file == null || file.Removed)
                    {
                        file = GetOrAddLocked(key, length, openUpstream);
                    }

                    chunk = file.Demand(index);
                    chunk.BeginRead();

                    readable = (int)Math.Min(want - filled, chunk.Available - inChunk);
                    if (readable <= 0)
                    {
                        if (chunk.Error != null)
                        {
                            chunk.EndRead();
                            throw new IOException($"The download of {key} failed: {chunk.Error.Message}", chunk.Error);
                        }

                        if (chunk.Complete)
                        {
                            //the upstream was shorter than announced
                            chunk.EndRead();
                            break;
                        }

                        wait = chunk.Progress;
                    }
                }

                try
                {
                    if (wait != null)
                    {
                        await wait.WaitAsync(cancellationToken).ConfigureAwait(false);

                        //the chunk waited on, not a fresh demand for it (which would replace a failed
                        //chunk and try again): the outcome of this attempt is what the reader gets
                        lock (gate)
                        {
                            if (chunk.Error != null)
                            {
                                throw new IOException($"The download of {key} failed: {chunk.Error.Message}", chunk.Error);
                            }
                        }

                        continue;
                    }

                    //the chunk file only ever grows, so this needs no lock
                    var read = ReadAt(chunk.ReadHandle!, buffer.Slice(filled, readable), inChunk);
                    if (read <= 0)
                    {
                        throw new IOException($"The cache file for {key} returned no data at byte {position:N0}.");
                    }

                    filled += read;
                }
                finally
                {
                    lock (gate) { chunk.EndRead(); }
                }
            }

            return filled;
        }

        CachedFile GetOrAddLocked(string key, long length, Func<Stream> openUpstream)
        {
            if (!files.TryGetValue(key, out var file))
            {
                var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32].ToLowerInvariant();
                file = new CachedFile(this, key, Path.Combine(directory, name), length, openUpstream);
                files[key] = file;
            }

            return file;
        }

        void Evict()
        {
            lock (gate)
            {
                if (disposed) return;
                EvictLocked(0);
            }
        }

        //Drops what nobody has used for a while, then the least recently used until the cache is
        //under its limit with room for 'reserve' more. A chunk being read, waited for or fetched is
        //never touched.
        void EvictLocked(long reserve)
        {
            var now = DateTime.UtcNow;

            foreach (var file in files.Values.ToList())
            {
                foreach (var chunk in file.Chunks)
                {
                    //an unused chunk, or one that never got its bytes (its reader gave up)
                    if (chunk != null && !chunk.Busy && (!chunk.Present || now - chunk.LastUsed > maxUnused))
                    {
                        file.DropChunk(chunk);
                    }
                }

                if (file.IsEmpty)
                {
                    files.Remove(file.Key);
                    file.Remove();
                }
            }

            if (totalBytes + reserve <= maxBytes)
            {
                return;
            }

            var candidates = files.Values
                                .SelectMany(file => file.Chunks
                                                        .Where(chunk => chunk != null && chunk.Present && !chunk.Busy)
                                                        .Select(chunk => (File: file, Chunk: chunk!)))
                                .OrderBy(pair => pair.Chunk.LastUsed)
                                .ToList();

            foreach (var (file, chunk) in candidates)
            {
                if (totalBytes + reserve <= maxBytes) break;
                file.DropChunk(chunk);
            }
        }

        public void Dispose()
        {
            evictionTimer.Dispose();

            List<Run> runs;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;

                var all = files.Values.ToList();
                files.Clear();

                runs = all.SelectMany(file => file.Runs).ToList();
                foreach (var file in all)
                {
                    //readers waiting on a chunk wake up and find the error
                    file.ShutDownLocked();
                }
            }

            foreach (var run in runs)
            {
                run.Cancel();
            }

            TryDeleteDirectory(directory);
        }

        static int ReadAt(SafeFileHandle handle, Memory<byte> buffer, long offset)
        {
            return RandomAccess.Read(handle, buffer.Span, offset);
        }

        static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (DirectoryNotFoundException)
            {
                //never written: its folder was never made
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.WriteLine($"Could not delete cache file {path}: {ex.Message}");
            }
        }

        static void TryDeleteDirectory(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                //a chunk still open by a reader goes when that handle closes; the rest at the next start
                Log.WriteLine($"Could not delete cache folder {path}: {ex.Message}");
            }
        }

        //One file: its chunks (null where nothing has been asked for) and the runs fetching them
        sealed class CachedFile
        {
            readonly ChunkCache cache;
            readonly Func<Stream> openUpstream;
            int chunkObjects;

            public CachedFile(ChunkCache cache, string key, string folder, long length, Func<Stream> openUpstream)
            {
                this.cache = cache;
                this.openUpstream = openUpstream;
                Key = key;
                Folder = folder;
                Length = length;
                Chunks = new Chunk?[(int)((length + ChunkSize - 1) / ChunkSize)];
            }

            public string Key { get; }
            public string Folder { get; }
            public long Length { get; }
            public Chunk?[] Chunks { get; }
            public List<Run> Runs { get; } = [];
            public bool Removed { get; private set; }
            public bool IsEmpty => Runs.Count == 0 && chunkObjects == 0;

            //Called by a run, outside the lock: blocks on the 3DX calls
            public Stream OpenUpstream() => openUpstream();

            //Under the lock. The chunk a read needs, with a run on its way to it if it isn't there
            public Chunk Demand(int index)
            {
                var chunk = GetOrCreateChunk(index);
                chunk.LastUsed = DateTime.UtcNow;

                if (!chunk.Complete)
                {
                    if (chunk.Fetcher != null)
                    {
                        chunk.Fetcher.Extend(index);
                    }
                    else
                    {
                        AssignRun(index);
                    }
                }

                return chunk;
            }

            public Chunk GetOrCreateChunk(int index)
            {
                var chunk = Chunks[index];
                if (chunk != null && chunk.Failed)
                {
                    //tried again on the next demand
                    DropChunk(chunk);
                    chunk = null;
                }

                if (chunk == null)
                {
                    chunk = new Chunk(this, index);
                    Chunks[index] = chunk;
                    chunkObjects++;
                }

                return chunk;
            }

            //A run that will stream through the chunk soon takes the demand; otherwise a run starts there
            void AssignRun(int index)
            {
                Run? best = null;
                foreach (var run in Runs)
                {
                    if (run.Ending || run.Next > index || index - run.Next > StreamThroughChunks) continue;
                    if (best == null || run.Next > best.Next) best = run;
                }

                if (best != null)
                {
                    best.Extend(index);
                    return;
                }

                if (Runs.Count >= MaxRunsPerFile)
                {
                    //an idle one can go; a busy one has readers waiting on it
                    Runs.Where(run => run.Idle && !run.Ending).OrderBy(run => run.LastDemand).FirstOrDefault()?.End();
                }

                var fresh = new Run(cache, this, index);
                Runs.Add(fresh);
                fresh.Start();
            }

            //Under the lock, when a run ends with demands it was going to meet still outstanding
            public void RunEnded(Run run)
            {
                Runs.Remove(run);

                if (Removed || cache.disposed) return;

                for (var index = 0; index < Chunks.Length; index++)
                {
                    var chunk = Chunks[index];
                    if (chunk != null && !chunk.Complete && chunk.Fetcher == null && chunk.Busy)
                    {
                        AssignRun(index);
                    }
                }
            }

            //Under the lock: the chunk's bytes leave the cache (its file goes once nobody is reading it)
            public void DropChunk(Chunk chunk)
            {
                if (chunk.Dropped) return;

                if (Chunks[chunk.Index] == chunk)
                {
                    Chunks[chunk.Index] = null;
                    chunkObjects--;
                }

                cache.totalBytes -= chunk.Available;
                chunk.Retire();
            }

            public void Remove()
            {
                Removed = true;
                TryDeleteDirectory(Folder);
            }

            public void ShutDownLocked()
            {
                Removed = true;

                foreach (var run in Runs)
                {
                    run.End();
                }

                foreach (var chunk in Chunks)
                {
                    if (chunk == null) continue;
                    chunk.Fail(new OperationCanceledException("The session has stopped."));
                    DropChunk(chunk);
                }
            }
        }

        //A fixed-size piece of a file on disk, and how much of it has arrived
        sealed class Chunk
        {
            TaskCompletionSource progress = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int readers;

            public Chunk(CachedFile file, int index)
            {
                Index = index;
                Start = (long)index * ChunkSize;
                Size = (int)Math.Min(ChunkSize, file.Length - Start);
                Path = System.IO.Path.Combine(file.Folder, $"{index}.chunk");
                LastUsed = DateTime.UtcNow;
            }

            public int Index { get; }
            public long Start { get; }
            public int Size { get; }
            public string Path { get; }
            public long Available { get; private set; }
            public bool Complete { get; private set; }
            public Exception? Error { get; private set; }
            public Run? Fetcher { get; set; }
            public SafeFileHandle? ReadHandle { get; private set; }
            public DateTime LastUsed { get; set; }
            public bool Dropped { get; private set; }

            public Task Progress => progress.Task;
            public bool Failed => Complete && Error != null;
            public bool Present => Complete && Error == null;

            //being read or waited for, or being fetched
            public bool Busy => readers > 0 || Fetcher != null;

            //All under the lock
            public void BeginRead() => readers++;

            public void EndRead()
            {
                readers--;
                if (Dropped && readers == 0)
                {
                    DisposeFile();
                }
            }

            public void Attach(SafeFileHandle readHandle)
            {
                if (Dropped)
                {
                    readHandle.Dispose();
                }
                else
                {
                    ReadHandle = readHandle;
                }
            }

            public void Advance(long available)
            {
                Available = available;
                Wake();
            }

            public void Finish()
            {
                if (Complete) return;
                Complete = true;
                Fetcher = null;
                Wake();
            }

            public void Fail(Exception error)
            {
                if (Complete) return;
                Complete = true;
                Error = error;
                Fetcher = null;
                Wake();
            }

            public void Retire()
            {
                Dropped = true;
                if (readers == 0)
                {
                    DisposeFile();
                }
            }

            void Wake()
            {
                var previous = progress;
                progress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                previous.TrySetResult();
            }

            void DisposeFile()
            {
                ReadHandle?.Dispose();
                ReadHandle = null;
                TryDelete(Path);
            }
        }

        //One upstream download, fetching chunks in order from where it started while there is demand
        //within reach of it
        sealed class Run
        {
            readonly ChunkCache cache;
            readonly CachedFile file;
            readonly CancellationTokenSource cts = new();
            TaskCompletionSource demand = new(TaskCreationOptions.RunContinuationsAsynchronously);

            //only the run's own task touches these
            Stream? upstream;
            long upstreamPosition;

            public Run(ChunkCache cache, CachedFile file, int start)
            {
                this.cache = cache;
                this.file = file;
                Next = start;
                DemandedUpTo = start;
                LastDemand = DateTime.UtcNow;
            }

            //the chunk being fetched, or the next one to fetch
            public int Next { get; private set; }
            public int DemandedUpTo { get; private set; }
            public DateTime LastDemand { get; private set; }
            public bool Idle { get; private set; }
            public bool Ending { get; private set; }

            public void Start() => _ = Task.Run(PumpAsync);

            //Under the lock
            public void Extend(int index)
            {
                if (index > DemandedUpTo) DemandedUpTo = index;
                LastDemand = DateTime.UtcNow;
                Wake();
            }

            //Under the lock. Asks the run to finish what it is doing and close its download
            public void End()
            {
                Ending = true;
                Wake();
            }

            //Outside the lock. Stops the run where it is
            public void Cancel()
            {
                cts.Cancel();
            }

            void Wake()
            {
                var previous = demand;
                demand = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                previous.TrySetResult();
            }

            async Task PumpAsync()
            {
                var token = cts.Token;
                var block = new byte[BlockSize];

                try
                {
                    while (true)
                    {
                        Chunk? chunk = null;
                        Task? wait = null;
                        lock (cache.gate)
                        {
                            if (Ending || cache.disposed) break;

                            while (Next < file.Chunks.Length && Next <= DemandedUpTo + ReadAheadChunks)
                            {
                                var candidate = file.GetOrCreateChunk(Next);
                                if (!candidate.Complete && candidate.Fetcher == null)
                                {
                                    candidate.Fetcher = this;
                                    chunk = candidate;
                                    break;
                                }

                                //there already, or another run has it
                                Next++;
                            }

                            if (chunk == null)
                            {
                                if (Next >= file.Chunks.Length)
                                {
                                    //the end of the file; a demand before it starts a run of its own
                                    Ending = true;
                                    break;
                                }

                                Idle = true;
                                wait = demand.Task;
                            }
                            else
                            {
                                Idle = false;
                                cache.EvictLocked(chunk.Size);
                            }
                        }

                        if (chunk == null)
                        {
                            try
                            {
                                await wait!.WaitAsync(cache.idleTimeout, token).ConfigureAwait(false);
                            }
                            catch (TimeoutException)
                            {
                                lock (cache.gate)
                                {
                                    if (DateTime.UtcNow - LastDemand >= cache.idleTimeout) Ending = true;
                                }
                            }

                            continue;
                        }

                        try
                        {
                            await FetchAsync(chunk, block, token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            var stopped = token.IsCancellationRequested;
                            if (!stopped)
                            {
                                Log.WriteLine($"Download of {file.Key} failed at byte {chunk.Start:N0}:{Environment.NewLine}{ex}");
                            }

                            lock (cache.gate)
                            {
                                //the chunk stays, failed, so the readers waiting on it see the error (a
                                //permanently broken download must not be retried without end); the
                                //next demand for it replaces it and tries again
                                chunk.Fail(stopped ? new OperationCanceledException("The download was stopped.") : ex);
                                Ending = true;
                            }

                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    //stopped while waiting for demand
                }
                finally
                {
                    try
                    {
                        upstream?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"Error while closing the download of {file.Key}: {ex.Message}");
                    }

                    lock (cache.gate)
                    {
                        Ending = true;
                        file.RunEnded(this);
                    }
                }
            }

            async Task FetchAsync(Chunk chunk, byte[] block, CancellationToken token)
            {
                if (upstream == null)
                {
                    upstream = file.OpenUpstream();
                    upstreamPosition = 0;
                    if (upstream.CanSeek)
                    {
                        upstream.Seek(0, SeekOrigin.Begin);
                    }
                }

                await PositionUpstreamAsync(chunk.Start, block, token).ConfigureAwait(false);

                Directory.CreateDirectory(file.Folder);
                using var writeHandle = File.OpenHandle(chunk.Path, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete, FileOptions.Asynchronous);
                var readHandle = File.OpenHandle(chunk.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                lock (cache.gate)
                {
                    //readers can now be served as the bytes arrive
                    chunk.Attach(readHandle);
                }

                var written = 0;
                while (written < chunk.Size)
                {
                    var n = await upstream.ReadAsync(block.AsMemory(0, Math.Min(block.Length, chunk.Size - written)), token).ConfigureAwait(false);
                    if (n <= 0) break;

                    await RandomAccess.WriteAsync(writeHandle, block.AsMemory(0, n), written, token).ConfigureAwait(false);
                    written += n;
                    upstreamPosition += n;

                    lock (cache.gate)
                    {
                        cache.totalBytes += n;
                        chunk.Advance(written);
                    }
                }

                if (written < chunk.Size)
                {
                    Log.WriteLine($"Download of {file.Key} ended at byte {chunk.Start + written:N0} of {file.Length:N0}.");
                }

                lock (cache.gate)
                {
                    chunk.Finish();
                    Next = chunk.Index + 1;

                    //the upstream is at its end; whatever is demanded beyond gets a run of its own
                    if (written < chunk.Size) Ending = true;
                }
            }

            //Brings the upstream to 'target': a seek where the stream can, otherwise reading and
            //discarding (from the start again if the target is behind)
            async Task PositionUpstreamAsync(long target, byte[] block, CancellationToken token)
            {
                if (upstreamPosition == target)
                {
                    return;
                }

                if (upstream!.CanSeek)
                {
                    upstream.Seek(target, SeekOrigin.Begin);
                    upstreamPosition = target;
                    return;
                }

                if (target < upstreamPosition)
                {
                    upstream.Dispose();
                    upstream = file.OpenUpstream();
                    upstreamPosition = 0;
                }

                while (upstreamPosition < target)
                {
                    var n = await upstream.ReadAsync(block.AsMemory(0, (int)Math.Min(block.Length, target - upstreamPosition)), token).ConfigureAwait(false);
                    if (n <= 0)
                    {
                        throw new EndOfStreamException($"The download of {file.Key} ended at byte {upstreamPosition:N0}, before byte {target:N0}.");
                    }

                    upstreamPosition += n;
                }
            }
        }
    }
}
