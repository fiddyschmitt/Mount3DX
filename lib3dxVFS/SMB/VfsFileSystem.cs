using libCommon.Vfs;
using LibSmb;

namespace lib3dxVFS.SMB
{
    //The file system behind the share, for LibSmb: the document tree (an IVfsSource), read-only,
    //with file content read through the chunk cache. Every change is refused here as well, but the
    //share is declared read-only, so the server refuses a write, a delete or a rename before it gets
    //this far. LibSmb calls it from many requests at once (a read waiting on 3DX holds up no other),
    //and a call that times out fails its own request (STATUS_IO_TIMEOUT), not the connection.
    public sealed class VfsFileSystem : ISmbFileSystem
    {
        readonly ChunkCache cache;
        volatile IVfsSource? source;

        public VfsFileSystem(ChunkCache cache)
        {
            this.cache = cache;
        }

        //The tree being served; null until the session has loaded it (an empty root meanwhile)
        public IVfsSource? Source
        {
            get => source;
            set => source = value;
        }

        IVfsSource Tree => source ?? EmptySource.Instance;

        //paths are looked up without regard to case, as Windows expects of a share
        public bool CaseInsensitive => true;

        //Also how a name is looked up, so it reports what a listing does: looking at the metadata file
        //doesn't fetch its content from 3DX, opening it does
        public ValueTask<SmbEntry?> StatAsync(string path, CancellationToken cancellationToken)
        {
            var entry = Tree.Stat(path, forOpen: false);
            return ValueTask.FromResult(entry == null ? null : ToSmbEntry(entry));
        }

        public IAsyncEnumerable<SmbEntry> ListAsync(string path, CancellationToken cancellationToken)
        {
            var entries = Tree.List(path) ?? throw new DirectoryNotFoundException($"{path} is not a folder in the document tree.");
            return entries.Select(ToSmbEntry).ToAsyncEnumerable();
        }

        //The server opens a file only for its content (a look at its attributes needs no open)
        public ValueTask<ISmbFile> OpenAsync(string path, SmbOpenMode mode, CancellationToken cancellationToken)
        {
            if (mode != SmbOpenMode.Read)
            {
                throw ReadOnly();
            }

            var tree = Tree;
            var entry = tree.Stat(path, forOpen: true);

            if (entry == null)
            {
                throw new FileNotFoundException("The file is not in the document tree.", path);
            }

            if (entry.IsDirectory)
            {
                throw new SmbFileSystemException(SmbFileSystemError.IsADirectory, $"{path} is a folder.");
            }

            //the path alone would serve a stale cache file after a refresh changed the document
            var key = $"{path}|{entry.ContentKey ?? ""}";

            return ValueTask.FromResult<ISmbFile>(new CachedFile(cache, key, (long)entry.Size, () => tree.OpenRead(path)));
        }

        public ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken) => throw ReadOnly();

        public ValueTask DeleteAsync(string path, CancellationToken cancellationToken) => throw ReadOnly();

        public ValueTask RenameAsync(string fromPath, string toPath, bool replace, CancellationToken cancellationToken) => throw ReadOnly();

        public ValueTask SetTimesAsync(string path, DateTimeOffset? created, DateTimeOffset? modified, CancellationToken cancellationToken) => throw ReadOnly();

        //unknown, so the share shows LibSmb's stand-in figures
        public ValueTask<SmbSpace?> GetSpaceAsync(CancellationToken cancellationToken) => ValueTask.FromResult<SmbSpace?>(null);

        static SmbEntry ToSmbEntry(VfsEntry entry)
        {
            //the WebDAV layer sends these values unconverted, as if UTC; do the same so both transports
            //show the same timestamps
            var modified = new DateTimeOffset(DateTime.SpecifyKind(entry.LastWriteTimeUtc, DateTimeKind.Utc));

            //ReadOnly here would put the read-only attribute on every file (and onto every copy made of one)
            return new SmbEntry(entry.Name, entry.IsDirectory, entry.IsDirectory ? 0 : (long)entry.Size, modified, ReadOnly: false);
        }

        static SmbFileSystemException ReadOnly() => new(SmbFileSystemError.ReadOnly, "The document tree is read-only.");

        //One open of a file. LibSmb makes one call at a time on it, in the order the client sent them;
        //other opens of the same file read the same cached chunks at the same time.
        sealed class CachedFile(ChunkCache cache, string key, long length, Func<Stream> openUpstream) : ISmbFile
        {
            public long Length => length;

            public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken) =>
                await cache.ReadAsync(key, length, openUpstream, offset, buffer, cancellationToken).ConfigureAwait(false);

            public ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken) => throw ReadOnly();

            public ValueTask SetLengthAsync(long length, CancellationToken cancellationToken) => throw ReadOnly();

            //nothing was written
            public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

            //the chunks stay cached for the next open; the cache drops them when they go unused
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        //Served while the session is still fetching the documents
        sealed class EmptySource : IVfsSource
        {
            public static readonly EmptySource Instance = new();

            static readonly VfsEntry Root = new() { Name = "", IsDirectory = true, LastWriteTimeUtc = DateTime.UtcNow };

            public VfsEntry? Stat(string path, bool forOpen) => path.Length == 0 ? Root : null;
            public IReadOnlyList<VfsEntry>? List(string path) => path.Length == 0 ? [] : null;
            public Stream OpenRead(string path) => throw new FileNotFoundException("The document tree is still being fetched.", path);
        }
    }
}
