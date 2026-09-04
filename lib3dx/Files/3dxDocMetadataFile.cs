using libCommon;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace lib3dx.Files
{
    public class _3dxDocMetadataFile : _3dxDownloadableFile
    {
        readonly string DocumentObjectId;

        //The content comes from two 3DX calls, so its real size isn't known until it is fetched.
        //Fetching it just to answer a folder listing made every PROPFIND on a document folder cost
        //two upstream round trips, which a tool traversing the whole tree turned into two per
        //document. So listings report this placeholder and the content is only fetched when the
        //file is actually read. The redirector serves whatever bytes the GET returns, so the
        //placeholder is cosmetic; it is what every released version reported.
        public const ulong PlaceholderSize = 1;

        public _3dxDocMetadataFile(string objectId, string name, _3dxItem? parent, DateTime creationTimeUtc, DateTime lastWriteTimeUtc, DateTime lastAccessTimeUtc, string documentObjectId) : base(objectId, name, parent, creationTimeUtc, lastWriteTimeUtc, lastAccessTimeUtc, PlaceholderSize)
        {
            DocumentObjectId = documentObjectId;
        }

        public override Stream Download(_3dxServer _3dxServer)
        {
            try
            {
                Log.WriteLine($"Downloading metadata file for document {DocumentObjectId}");

                var secContext = _3dxServer.GetSecurityContext();
                var metadataJsonObj = _3dxServer.GetMetadataJSON(DocumentObjectId, secContext);
                var docInfoObj = _3dxServer.GetDocument(DocumentObjectId);

                var obj = new JObject
                {
                    ["resources/v1/collabServices/attributes/op/read"] = metadataJsonObj,
                    ["resources/v1/modeler/documents/ids"] = docInfoObj
                };

                var resultStr = JsonConvert.SerializeObject(obj, Formatting.Indented);
                return new MemoryStream(Encoding.UTF8.GetBytes(resultStr), writable: false);
            }
            catch (Exception ex)
            {
                //rethrow so the WebDAV layer returns an error, rather than serving an empty file
                Log.WriteLine($"Error while generating metadata file:{Environment.NewLine}{ex}");
                throw;
            }
        }
    }
}
