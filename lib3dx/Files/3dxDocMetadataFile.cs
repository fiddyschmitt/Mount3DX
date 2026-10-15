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

        //The SMB client, on the other hand, reads exactly as many bytes as the size it was given,
        //so an open of this file (a stat, as opposed to a listing) builds the content and reports
        //its real length. The content is kept for a while so the reads that follow the open, and a
        //reopen soon after, don't fetch it again.
        static readonly TimeSpan ContentLifetime = TimeSpan.FromMinutes(1);
        readonly object contentLock = new();
        byte[]? content;
        DateTime contentBuiltAt;

        public _3dxDocMetadataFile(string objectId, string name, _3dxItem? parent, DateTime creationTimeUtc, DateTime lastWriteTimeUtc, DateTime lastAccessTimeUtc, string documentObjectId) : base(objectId, name, parent, creationTimeUtc, lastWriteTimeUtc, lastAccessTimeUtc, PlaceholderSize)
        {
            DocumentObjectId = documentObjectId;
        }

        public ulong GetContentLength(_3dxServer _3dxServer)
        {
            return (ulong)GetContent(_3dxServer).Length;
        }

        public override Stream Download(_3dxServer _3dxServer)
        {
            return new MemoryStream(GetContent(_3dxServer), writable: false);
        }

        byte[] GetContent(_3dxServer _3dxServer)
        {
            lock (contentLock)
            {
                if (content != null && DateTime.UtcNow - contentBuiltAt < ContentLifetime)
                {
                    return content;
                }

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
                    content = Encoding.UTF8.GetBytes(resultStr);
                    contentBuiltAt = DateTime.UtcNow;
                    return content;
                }
                catch (Exception ex)
                {
                    //rethrow so the file server returns an error, rather than serving an empty file
                    Log.WriteLine($"Error while generating metadata file:{Environment.NewLine}{ex}");
                    throw;
                }
            }
        }
    }
}
