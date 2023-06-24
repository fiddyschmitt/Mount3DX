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
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace lib3dx
{
    public class _3dxFolder : _3dxItem
    {

        public override string ToString()
        {
            return $"{Name}";
        }

        public List<_3dxFolder> Subfolders = new();

        public void PopulateSubfoldersRecursively(string serverUrl, string cookies, int queryThreads)
        {
            var folderQueue = new ConcurrentQueue<_3dxFolder>();
            folderQueue.Enqueue(this);

            var recurseTask = QueueUtility
                    .Process(folderQueue, folder =>
                    {
                        folder.Subfolders = GetSubFolders(folder, serverUrl, cookies);

                        folder.Subfolders.ForEach(subfolder => subfolder.Parent = folder);

                        return folder.Subfolders;
                    }, queryThreads, new CancellationToken());
            recurseTask.Wait();
        }

        public List<_3dxFolder> GetSubFolders(_3dxFolder folder, string serverUrl, string cookies)
        {
            var objectUrl = serverUrl.UrlCombine(@$"common/emxUIStructureFancyTreeGetData.jsp?objectId={folder.ObjectId}&commandName=TMCProjectStructure&reinit=");

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(objectUrl),
                Method = HttpMethod.Get
            };

            request.Headers.Add("Accept-Language", "en-US,en;q=0.5");   //required for some reason

            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Cookie", cookies);

            var jsonString = client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;

            var result = JArray
                            .Parse(jsonString)
                            .Select(o => new
                            {
                                ObjectId = o["objectId"].ToString(),
                                Title = o["title"].ToString()
                            })
                            .Select(o => new
                            {
                                o.ObjectId,
                                Title = o.Title.Substring(0, o.Title.LastIndexOf("(")),
                            })
                            .Select(o => new _3dxFolder()
                            {
                                ObjectId = o.ObjectId,
                                Name = o.Title,
                            })
                            .ToList();

            return result;
        }
    }
}
