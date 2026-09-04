using libCommon;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace lib3dx.Files
{
    //Presents a remote download as a seekable, read-only stream of known length, which is what the
    //WebDAV GET handler needs in order to send Content-Length and honour Range requests.
    //
    //Nothing is fetched until the first read. The GET handler seeks to the requested start and then
    //reads sequentially, so the upstream request is opened at that position with a Range header and
    //only the bytes that were asked for are transferred. Seeking after reading has begun re-opens
    //the upstream request at the new position. If the server ignores the Range header and answers
    //200, the unwanted prefix is drained so the caller still receives the correct bytes.
    public sealed class RangedDownloadStream : Stream
    {
        readonly Func<long, Task<HttpResponseMessage>> openFrom;
        readonly long length;
        readonly string description;

        long position;

        HttpResponseMessage? response;
        Stream? inner;
        long innerPosition;     //the file offset that the next read from 'inner' will return

        public RangedDownloadStream(Func<long, Task<HttpResponseMessage>> openFrom, long length, string description)
        {
            this.openFrom = openFrom;
            this.length = length;
            this.description = description;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => position + offset,
                SeekOrigin.End => length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            if (target < 0)
            {
                throw new IOException("Cannot seek before the start of the stream.");
            }

            //cheap: the upstream request is (re)opened lazily on the next read
            position = target;
            return position;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0 || position >= length)
            {
                return 0;
            }

            var source = await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

            var toRead = (int)Math.Min(buffer.Length, length - position);
            var bytesRead = await source.ReadAsync(buffer[..toRead], cancellationToken).ConfigureAwait(false);

            if (bytesRead == 0)
            {
                Log.WriteLine($"Download of {description} ended at byte {position:N0}, but the server reported a size of {length:N0} bytes.");
            }

            position += bytesRead;
            innerPosition += bytesRead;
            return bytesRead;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        }

        async ValueTask<Stream> EnsureOpenAsync(CancellationToken cancellationToken)
        {
            if (inner != null && innerPosition == position)
            {
                return inner;
            }

            CloseUpstream();

            var newResponse = await openFrom(position).ConfigureAwait(false);
            try
            {
                newResponse.EnsureSuccessStatusCode();

                long toSkip = 0;
                if (position > 0)
                {
                    if (newResponse.StatusCode == HttpStatusCode.PartialContent)
                    {
                        var from = newResponse.Content.Headers.ContentRange?.From;
                        if (from != null && from != position)
                        {
                            throw new IOException($"Requested {description} from byte {position:N0} but the server returned a range starting at byte {from:N0}.");
                        }
                    }
                    else
                    {
                        //the server ignored the Range header and sent the whole file
                        toSkip = position;
                    }
                }

                var reportedLength = newResponse.Content.Headers.ContentRange?.Length ?? newResponse.Content.Headers.ContentLength;
                if (reportedLength != null && reportedLength != length && position == 0)
                {
                    Log.WriteLine($"Server reports {description} as {reportedLength:N0} bytes, but its metadata said {length:N0} bytes.");
                }

                var newInner = await newResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

                if (toSkip > 0)
                {
                    await DrainAsync(newInner, toSkip, cancellationToken).ConfigureAwait(false);
                }

                response = newResponse;
                inner = newInner;
                innerPosition = position;
                return newInner;
            }
            catch
            {
                newResponse.Dispose();
                throw;
            }
        }

        static async Task DrainAsync(Stream stream, long count, CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024];
            while (count > 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(count, buffer.Length)), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException("The download ended before the requested range was reached.");
                }
                count -= read;
            }
        }

        void CloseUpstream()
        {
            inner?.Dispose();
            inner = null;
            response?.Dispose();
            response = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CloseUpstream();
            }
            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
