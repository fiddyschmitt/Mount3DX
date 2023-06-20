using libCommon;
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

namespace lib3dx
{
    public class _3DXFolder : _3dxItem
    {

        public override string ToString()
        {
            return $"{Title}";
        }

        public List<_3DXFolder> Subfolders = new();
        public List<_3dxDocument> Documents = new();

        public void PopulateDocumentsRecursively(string serverUrl, string cookies, int queryThreads)
        {
            var firstPage = GetDocuments(serverUrl, cookies, 1, 10).Result;

            var totalDocsStr = firstPage.Split(new string[] { "\\\"nhits\\\":", ",\\\"facets\\\"" }, StringSplitOptions.None)[1];
            var totalDocs = int.Parse(totalDocsStr);

            var pageSize = 100;
            var totalPages = (int)(totalDocs / (double)pageSize);
            var pages = Enumerable
                .Range(1, totalPages + 1)
                .ToList();

            Int32 pagesRetrieved = 0;

            var result = pages
                            .AsParallel()
                            .AsOrdered()
                            .WithDegreeOfParallelism(queryThreads)
                            .Select(page =>
                            {
                                var pageStr = GetDocuments(serverUrl, cookies, page, pageSize).Result;

                                Interlocked.Increment(ref pagesRetrieved);
                                Console.WriteLine($"Retrieved {pagesRetrieved:N0}/{totalPages:N0} pages. Page number {page}");

                                return pageStr;
                            })
                            .ToList();
        }

        public static async Task<string> GetDocuments(string serverUrl, string cookies, int pageNumber, int pageSize)
        {
            using var httpClient = new HttpClient();

            //httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookies);

            var url = serverUrl.UrlCombine($"resources/v1/modeler/controldocuments/myctrldocs/alldocuments?pageNumber={pageNumber}&pageSize={pageSize}&tenant=OnPremise&xrequestedwith=xmlhttprequest");

            using var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public void PopulateSubfoldersRecursively(string serverUrl, string cookies, int queryThreads)
        {
            var folderQueue = new ConcurrentQueue<_3DXFolder>();
            folderQueue.Enqueue(this);

            var recurseTask = QueueUtility
                    .Process(folderQueue, folder =>
                    {
                        folder.Subfolders = GetSubFolders(folder, serverUrl, cookies);

                        folder.Subfolders.ForEach(subfolder => subfolder.Parent = folder);

                        Debug.WriteLine($"{folder.Title}: {folder.Subfolders.Count:N0}");
                        return folder.Subfolders;
                    }, queryThreads, new CancellationToken());
            recurseTask.Wait();
        }

        public List<_3DXFolder> GetSubFolders(_3DXFolder folder, string serverUrl, string cookies)
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
                            .Select(o => new _3DXFolder()
                            {
                                ObjectId = o.ObjectId,
                                Title = o.Title,
                                FullPath = Path.Combine(folder.FullPath, o.Title)
                            })
                            .ToList();

            return result;
        }
    }
}
