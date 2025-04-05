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

namespace lib3dx.Files
{
    public class _3dxDocMetadataFile : _3dxDownloadableFile
    {
        readonly string DocumentObjectId;

        public _3dxDocMetadataFile(string objectId, string name, _3dxItem? parent, DateTime creationTimeUtc, DateTime lastWriteTimeUtc, DateTime lastAccessTimeUtc, string documentObjectId, ulong size) : base(objectId, name, parent, creationTimeUtc, lastWriteTimeUtc, lastAccessTimeUtc, size)
        {
            DocumentObjectId = documentObjectId;
        }

        public override Stream Download(_3dxServer _3dxServer)
        {
            try
            {
                Log.WriteLine("Downloading metadata file");

                var secContext = _3dxServer.GetSecurityContext();
                var metadataJson = _3dxServer.GetMetadata(DocumentObjectId, secContext);
                var result = new MemoryStream(Encoding.UTF8.GetBytes(metadataJson));
                return result;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while downloading metadata file to MemoryStream:{Environment.NewLine}{ex}");
            }

            return Stream.Null;
        }
    }
}
