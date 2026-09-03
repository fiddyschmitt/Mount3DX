using libCommon;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lib3dx.Files
{
    public class _3dxDocUrlFile : _3dxDownloadableFile
    {
        //The content depends only on the server URL and the document id, so it is built up front.
        //That way Size (served as getcontentlength) always matches what Download() returns.
        readonly byte[] Content;

        public _3dxDocUrlFile(string objectId, string name, _3dxItem? parent, DateTime creationTimeUtc, DateTime lastWriteTimeUtc, DateTime lastAccessTimeUtc, string documentObjectId, string serverUrl) : base(objectId, name, parent, creationTimeUtc, lastWriteTimeUtc, lastAccessTimeUtc, 0)
        {
            var urlFileContents = $"""
                [InternetShortcut]
                URL={serverUrl.UrlCombine($"common/emxTree.jsp?objectId={documentObjectId}")}
                """;

            Content = Encoding.UTF8.GetBytes(urlFileContents);
        }

        public override ulong Size => (ulong)Content.Length;

        public override Stream Download(_3dxServer _3dxServer)
        {
            Log.WriteLine("Downloading Doc URL file");
            return new MemoryStream(Content, writable: false);
        }
    }
}
