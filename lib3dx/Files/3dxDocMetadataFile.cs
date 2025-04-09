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
using Newtonsoft.Json;

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
                var metadataJsonObj = _3dxServer.GetMetadataJSON(DocumentObjectId, secContext);

                var docInfoObj = _3dxServer.GetDocument(DocumentObjectId);

                var obj = new JObject
                {
                    ["resources/v1/collabServices/attributes/op/read"] = metadataJsonObj,
                    ["resources/v1/modeler/documents/ids"] = docInfoObj
                };

                var resultStr = JsonConvert.SerializeObject(obj, Formatting.Indented);
                var result = new MemoryStream(Encoding.UTF8.GetBytes(resultStr));
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
