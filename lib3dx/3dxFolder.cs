using libCommon;
using libCommon.Events;
using libCommon.Utilities;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace lib3dx
{
    public class _3dxFolder : _3dxItem
    {
        public List<_3dxFolder> Subfolders = new();

        public _3dxFolder(string objectId, string name, _3dxItem? parent, DateTime creationTimeUtc, DateTime lastWriteTimeUtc, DateTime lastAccessTimeUtc) : base(objectId, name, parent, creationTimeUtc, lastWriteTimeUtc, lastAccessTimeUtc)
        {
        }

        public override string ToString()
        {
            return $"{Name}";
        }

        public void PopulateSubfoldersRecursively(string serverUrl, string cookies, int queryThreads)
        {
            var folderQueue = new ConcurrentQueue<_3dxFolder>();
            folderQueue.Enqueue(this);

            QueueUtility
                    .Recurse2(folderQueue, folder =>
                    {
                        folder.Subfolders = GetSubFolders(folder, serverUrl, cookies);

                        folder.Subfolders.ForEach(subfolder => subfolder.Parent = folder);

                        return folder.Subfolders;
                    }, queryThreads, CancellationToken.None);
        }

        public static List<_3dxFolder> GetSubFolders(_3dxFolder folder, string serverUrl, string cookies)
        {
            var objectUrl = serverUrl.UrlCombine(@$"common/emxUIStructureFancyTreeGetData.jsp?objectId={folder.ObjectId}&commandName=TMCProjectStructure&reinit=");

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(objectUrl),
                Method = HttpMethod.Get
            };

            request.Headers.Add("Accept-Language", "en-US,en;q=0.5");   //required for some reason

            var httpClient = libCommon.Utilities.WebUtility.NewHttpClientWithCompression();

            httpClient.DefaultRequestHeaders.Add("Cookie", cookies);

            var jsonString = httpClient.SendAsync(request).Result.Content.ReadAsStringAsync().Result;

            var result = JArray
                            .Parse(jsonString)
                            .Select(o => new
                            {
                                ObjectId = o["objectId"]?.ToString() ?? throw new Exception("objectId could not be retrieved"),
                                Title = o["title"]?.ToString() ?? throw new Exception("title could not be retrieved")
                            })
                            .Select(o => new
                            {
                                o.ObjectId,
                                Title = o.Title[..o.Title.LastIndexOf("(")],
                            })
                            .Select(o => new _3dxFolder(o.ObjectId, o.Title, folder, DateTime.Now, DateTime.Now, DateTime.Now))
                            .ToList();

            return result;
        }
    }
}
