namespace libCommon.Vfs
{
    //A read-only view of a file tree, in the shape the SMB server (LibSmb) asks for it: paths are
    //relative to the share root, '/'-separated, with no leading slash; "" is the root itself.
    public interface IVfsSource
    {
        //null when nothing is at the path. A file's size is what a listing shows unless forOpen, which
        //is for reading the content: then it is the real size, which a generated file (the metadata
        //file) only knows once its content has been made, at the cost of a call to 3DX. The SMB
        //client reads exactly as many bytes as an open reports.
        VfsEntry? Stat(string path, bool forOpen);

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
