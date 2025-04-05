using libCommon;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using lib3dx.Files;

namespace lib3dx
{
    public class _3dxDocUrlFile : _3dxDownloadableFile
    {
        readonly string DocumentObjectId;

        public _3dxDocUrlFile(string objectId, string name, _3dxItem? parent, DateTime creationTimeUtc, DateTime lastWriteTimeUtc, DateTime lastAccessTimeUtc, string documentObjectId, ulong size) : base(objectId, name, parent, creationTimeUtc, lastWriteTimeUtc, lastAccessTimeUtc, size)
        {
            DocumentObjectId = documentObjectId;
        }

        public override Stream Download(_3dxServer _3dxServer)
        {
            try
            {
                Log.WriteLine("Downloading Doc URL file");

                var urlFileContents = $"""
                    [InternetShortcut]
                    URL={_3dxServer.ServerUrl.UrlCombine($"common/emxTree.jsp?objectId={DocumentObjectId}")}
                    """;

                var result = new MemoryStream(Encoding.UTF8.GetBytes(urlFileContents));
                return result;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while downloading Doc URL file to MemoryStream:{Environment.NewLine}{ex}");
            }

            return Stream.Null;
        }
    }
}
