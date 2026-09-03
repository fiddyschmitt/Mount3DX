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
        readonly _3dxServer Server;

        //The content has to be fetched from 3DX, so it is built on first use (typically the PROPFIND
        //that lists the document folder, which asks for getcontentlength) and cached for the life of
        //this snapshot. Size therefore always matches what Download() returns. A failed fetch is not
        //cached, so a transient error is retried on the next request.
        readonly object contentLock = new();
        byte[]? content;

        public _3dxDocMetadataFile(string objectId, string name, _3dxItem? parent, DateTime creationTimeUtc, DateTime lastWriteTimeUtc, DateTime lastAccessTimeUtc, string documentObjectId, _3dxServer server) : base(objectId, name, parent, creationTimeUtc, lastWriteTimeUtc, lastAccessTimeUtc, 0)
        {
            DocumentObjectId = documentObjectId;
            Server = server;
        }

        public override ulong Size => (ulong)GetContent().Length;

        byte[] GetContent()
        {
            lock (contentLock)
            {
                if (content == null)
                {
                    try
                    {
                        Log.WriteLine($"Generating metadata file for document {DocumentObjectId}");

                        var secContext = Server.GetSecurityContext();
                        var metadataJsonObj = Server.GetMetadataJSON(DocumentObjectId, secContext);
                        var docInfoObj = Server.GetDocument(DocumentObjectId);

                        var obj = new JObject
                        {
                            ["resources/v1/collabServices/attributes/op/read"] = metadataJsonObj,
                            ["resources/v1/modeler/documents/ids"] = docInfoObj
                        };

                        var resultStr = JsonConvert.SerializeObject(obj, Formatting.Indented);
                        content = Encoding.UTF8.GetBytes(resultStr);
                    }
                    catch (Exception ex)
                    {
                        //rethrow so the WebDAV layer returns an error, rather than serving an empty file
                        Log.WriteLine($"Error while generating metadata file:{Environment.NewLine}{ex}");
                        throw;
                    }
                }

                return content;
            }
        }

        public override Stream Download(_3dxServer _3dxServer)
        {
            Log.WriteLine("Downloading metadata file");
            return new MemoryStream(GetContent(), writable: false);
        }
    }
}
