namespace libCommon.Vfs
{
    //A read-only view of a file tree, in the shape the SMB server's VFS contract asks for: paths are
    //relative to the share root, '/'-separated, with no leading slash; "" is the root itself.
    public interface IVfsSource
    {
        //null when nothing is at the path
        VfsEntry? Stat(string path);

        //The entries directly under a directory; null when the path isn't a directory
        IReadOnlyList<VfsEntry>? List(string path);

        //The content of a file, readable from the start and, where the source can, seekable: a read
        //deep inside a large file then doesn't have to transfer everything before it
        Stream OpenRead(string path);
    }

    public sealed class VfsEntry
    {
        public required string Name { get; init; }
        public required bool IsDirectory { get; init; }
        public ulong Size { get; init; }
        public DateTime LastWriteTimeUtc { get; init; }

        //Changes whenever the content does, so cached bytes are never served for a newer version
        public string? ContentKey { get; init; }
    }
}
