namespace Mount3DX.Tests.Fake3dx
{
    //A document as 3DX holds it. The dotted id is the other id classic 3DSpace pages use for it.
    public sealed record FakeDocument(string Id, string Name, string Revision, string Title, IReadOnlyList<FakeFile> Files, string Type = "Document", string? DottedId = null)
    {
        public const string Originated = "2024-01-02T03:04:05";
        public const string Modified = "2024-02-03T04:05:06";
    }

    public sealed record FakeFile(string Id, string Title, FakeContent Content);

    //What a test sees of a request to the fake 3DX. RangeFrom and RangeTo are the Range header's
    //first range, if it had one.
    public sealed record FakeRequest(string Method, string Path, string Query, long? RangeFrom, long? RangeTo, string CsrfToken, string SecurityContext);

    //A file's bytes: given, or made up from their position so a large file costs no memory
    public abstract class FakeContent
    {
        public abstract long Length { get; }

        public abstract void Read(long offset, Span<byte> buffer);

        public byte[] Read(long offset, int count)
        {
            var result = new byte[count];
            Read(offset, result);
            return result;
        }

        public byte[] ReadAll() => Read(0, checked((int)Length));

        public static FakeContent Of(byte[] bytes) => new BytesContent(bytes);

        public static FakeContent Of(string text) => new BytesContent(System.Text.Encoding.UTF8.GetBytes(text));

        public static FakeContent Generated(long length, int seed) => new GeneratedContent(length, seed);

        sealed class BytesContent(byte[] bytes) : FakeContent
        {
            public override long Length => bytes.Length;

            public override void Read(long offset, Span<byte> buffer) => bytes.AsSpan(checked((int)offset), buffer.Length).CopyTo(buffer);
        }

        sealed class GeneratedContent(long length, int seed) : FakeContent
        {
            public override long Length => length;

            public override void Read(long offset, Span<byte> buffer)
            {
                for (var i = 0; i < buffer.Length; i++)
                {
                    var p = offset + i;
                    buffer[i] = (byte)((p * 31 + (p >> 10) * 7 + seed * 13) % 251);
                }
            }
        }
    }
}
