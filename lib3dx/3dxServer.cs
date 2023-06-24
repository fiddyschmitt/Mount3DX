using libCommon;
using libCommon.Events;
using libCommon.Utilities;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web;
using WebUtility = libCommon.Utilities.WebUtility;

namespace lib3dx
{
    public class _3dxServer
    {
        CancellationTokenSource CancellationTokenSource = new();
        Task? PingTask;

        public _3dxServer(string serverUrl, string cookies, bool keepAlive, int keepAliveIntervalMinutes)
        {
            ServerUrl = serverUrl;
            Cookies = cookies;
            KeepAlive = keepAlive;
            KeepAliveInterval = TimeSpan.FromMinutes(keepAliveIntervalMinutes);

            if (keepAlive)
            {
                PingTask = Task.Factory.StartNew(() =>
                {
                    while (!CancellationTokenSource.IsCancellationRequested)
                    {
                        Ping();
                        Task.Delay(KeepAliveInterval, CancellationTokenSource.Token).Wait();
                    }
                });
            }
        }

        public bool Ping()
        {
            var pingUrl = ServerUrl.UrlCombine(@"resources/v1/modeler/documents/ABC123AABEC11256");

            var success = false;

            try
            {
                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", Cookies);
                var request = new HttpRequestMessage(HttpMethod.Get, pingUrl);

                try
                {
                    var response = httpClient.Send(request);
                    var responseStr = response.Content.ReadAsStringAsync().Result;
                    if (responseStr.Contains("Object Does Not Exist"))
                    {
                        success = true;
                    }
                }
                catch { }
            }
            catch
            { }

            return success;
        }

        public void Close()
        {
            CancellationTokenSource.Cancel();
            PingTask?.Wait();
        }

        public string GetSecurityContext()
        {
            var securityContextUrl = ServerUrl.UrlCombine("resources/modeler/pno/person?current=true&select=preferredcredentials");
            var securityContextJsonStr = WebUtility.HttpGet(securityContextUrl, Cookies);
            var securityContextJson = JObject.Parse(securityContextJsonStr);

            var role = securityContextJson["preferredcredentials"]?["role"]?["name"].ToString();
            var org = securityContextJson["preferredcredentials"]?["organization"]?["name"].ToString();
            var collabspace = securityContextJson["preferredcredentials"]?["collabspace"]?["name"].ToString();

            var result = $"{role}.{org}.{collabspace}";
            return result;
        }

        public List<_3dxFolder> GetRootFolders()
        {
            var securityContext = GetSecurityContext();

            var getRootFoldersUrl = ServerUrl.UrlCombine("resources/v1/FolderManagement/Folder/roots?tenant=OnPremise&xrequestedwith=xmlhttprequest");

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(getRootFoldersUrl),
                Method = HttpMethod.Post,
                Content = new StringContent(@"{""Type"":""FolderSection_AllFolders"",""select_predicate"":[]}", Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("SecurityContext", securityContext);

            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Cookie", Cookies);

            var rootFolderJsonStr = client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
            var result = JObject
                            .Parse(rootFolderJsonStr)["folders"]
                            .Select(folder =>
                            {
                                var newFolder = new _3dxFolder(
                                        folder["id"].ToString(),
                                        folder["name"].ToString(),
                                        null,
                                        DateTime.Parse(folder["created"].ToString()),
                                        DateTime.Parse(folder["modified"].ToString()),
                                        DateTime.Parse(folder["modified"].ToString())
                                        );

                                return newFolder;
                            })
                            .ToList();

            return result;
        }

        public static List<string> itemTypes = new List<string>();

        //Note - if a document is in a folder, this method doesn't return all the revisions of that document. Only the revisions that the user attached
        public List<_3dxItem> GetItemsInFolder(_3dxFolder folder, string securityContext)
        {
            var getContentUrl = ServerUrl.UrlCombine($"resources/v1/FolderManagement/Folder/{folder.ObjectId}/getContent?tenant=OnPremise&xrequestedwith=xmlhttprequest");

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(getContentUrl),
                Method = HttpMethod.Post,
                Content = new StringContent(@"{""Content"":""All"",""select_predicate"":[]}", Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("SecurityContext", securityContext);

            var httpClient = WebUtility.NewHttpClientWithCompression();

            httpClient.DefaultRequestHeaders.Add("Cookie", Cookies);

            var documentsToRetrieve = new List<string>();

            var rootFolderJsonStr = httpClient.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
            var result = JObject
                            .Parse(rootFolderJsonStr)["content"]
                            .Select(item =>
                            {
                                var itemType = item["type"].ToString();
                                itemTypes.Add(itemType);

                                _3dxItem? newItem = null;
                                if (itemType.Equals("Document"))
                                {
                                    documentsToRetrieve.Add(item["id"].ToString());
                                    return null;
                                }

                                if (itemType.Equals("Workspace Vault"))
                                {
                                    newItem = new _3dxFolder(
                                                    item["id"].ToString(),
                                                    item["name"].ToString(),
                                                    folder,
                                                    DateTime.Parse(item["created"].ToString()),
                                                    DateTime.Parse(item["modified"].ToString()),
                                                    DateTime.Parse(item["modified"].ToString()));
                                }

                                if (newItem == null)
                                {
                                    //Debugger.Break();
                                    return null;
                                }


                                return newItem;
                            })
                            .Where(item => item != null)
                            .ToList();

            //get all documents, because they are effectively folders
            var documentDetails = GetDocuments(documentsToRetrieve, folder);

            result.AddRange(documentDetails);

            return result;
        }

        public List<_3dxDocument> GetDocuments(List<string> documentIds, _3dxFolder? parent)
        {
            var getDocumentDetails = ServerUrl.UrlCombine($"resources/v1/modeler/documents/ids");

            var idsCombined = documentIds.ToString(",");

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(getDocumentDetails),
                Method = HttpMethod.Post,
                Content = new StringContent($"$ids={idsCombined}", Encoding.UTF8, "application/x-www-form-urlencoded"),
            };

            var httpClient = WebUtility.NewHttpClientWithCompression();

            httpClient.DefaultRequestHeaders.Add("Cookie", Cookies);

            var documentDetailsJsonStr = httpClient.SendAsync(request).Result.Content.ReadAsStringAsync().Result;

            var documents = JObject.Parse(documentDetailsJsonStr)["data"]
                                                .Select(o =>
                                                {
                                                    var newDoc = JTokenToDocument(o, parent);
                                                    newDoc.Parent = parent;
                                                    return newDoc;
                                                })
                                                .Where(doc => doc != null)
                                                .ToList();

            return documents;
        }


        public List<_3dxDocument> GetAllDocuments(string serverUrl, string cookies, int queryThreads, EventHandler<ProgressEventArgs>? progress)
        {
            var firstPage = GetDocumentPage(serverUrl, cookies, 1, 1).Result;

            var totalDocsStr = firstPage.Split(new string[] { "\\\"nhits\\\":", ",\\\"facets\\\"" }, StringSplitOptions.None)[1];
            var totalDocs = int.Parse(totalDocsStr);

            var pageSize = 100;
            var totalPages = (int)(totalDocs / (double)pageSize);
            var pages = Enumerable
                .Range(1, totalPages + 1)
                .Take(1)
                .ToList();

            int pagesRetrieved = 0;
            int documentsDiscovered = 0;

            var result = pages
                            .AsParallel()
                            .AsOrdered()
                            .WithDegreeOfParallelism(queryThreads)
                            .SelectMany(page =>
                            {
                                var pageJson = GetDocumentPage(serverUrl, cookies, page, pageSize).Result;

                                var documents = JObject.Parse(pageJson)["data"]
                                                .Select(o => JTokenToDocument(o, null))
                                                .Where(doc => doc != null)
                                                .ToList();

                                Interlocked.Increment(ref pagesRetrieved);
                                Interlocked.Add(ref documentsDiscovered, documents.Count());

                                progress?.Invoke(this, new ProgressEventArgs()
                                {
                                    Message = $"Retrieved {pagesRetrieved:N0}/{totalPages:N0} pages. {documentsDiscovered:N0} documents discovered.",
                                    Nature = ProgressEventArgs.EnumNature.Neutral
                                });

                                return documents;
                            })
                            .ToList();

            return result;
        }

        public _3dxDocument JTokenToDocument(JToken o, _3dxFolder parent)
        {
            if (o["dataelements"]?["title"] == null)
            {
                return null;
            }

            var documentObjectId = o["id"].ToString();
            var name = o["dataelements"]?["name"]?.ToString();
            var revision = o["dataelements"]?["revision"]?.ToString();
            var title = o["dataelements"]?["title"]?.ToString();
            var documentType = o["dataelements"]?["typeNLS"]?.ToString();
            var description = o["dataelements"]?["description"]?.ToString();
            var originalName = o["dataelements"]?["name"]?.ToString();

            var created = DateTime.Parse(o["dataelements"]?["originated"]?.ToString());
            var modified = DateTime.Parse(o["dataelements"]?["modified"]?.ToString());
            var accessed = modified;

            var derivedName = $"{name} Rev {revision}";
            if (!name.Equals(title, StringComparison.CurrentCultureIgnoreCase))
            {
                derivedName += $" ({title})";
            }

            derivedName = FileUtility.ReplaceInvalidChars(derivedName);

            var newDocument = new _3dxDocument(
                                    documentObjectId,
                                    derivedName,
                                    parent,
                                    created,
                                    modified,
                                    accessed,
                                    originalName,
                                    revision,
                                    documentType
                                    );

            var files = o["relateddata"]?["files"].Select(file =>
            {
                var fileObjectId = file["id"].ToString();
                var name = file["dataelements"]?["title"].ToString();
                var fileRevision = file["dataelements"]?["revision"].ToString();

                var created = DateTime.Parse(o["dataelements"]?["originated"].ToString());
                var modified = DateTime.Parse(o["dataelements"]?["modified"].ToString());
                var accessed = modified;
                var size = 0UL;
                if (file["dataelements"]["fileSize"] != null)
                {
                    size = ulong.Parse(file["dataelements"]?["fileSize"].ToString());
                }

                return new _3dxFile(
                            fileObjectId,
                            name,
                            newDocument,
                            created,
                            modified,
                            accessed,
                            documentObjectId,
                            fileRevision,
                            size);
            })
            .ToList();


            newDocument.Files = files;

            return newDocument;
        }

        public static async Task<string> GetDocumentPage(string serverUrl, string cookies, int pageNumber, int pageSize)
        {
            var httpClient = WebUtility.NewHttpClientWithCompression();

            //httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookies);

            var url = serverUrl.UrlCombine($"resources/v1/modeler/controldocuments/myctrldocs/alldocuments?pageNumber={pageNumber}&pageSize={pageSize}&tenant=OnPremise&xrequestedwith=xmlhttprequest");

            using var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public string ServerUrl { get; }
        public string Cookies { get; }
        public bool KeepAlive { get; }
        public int QueryThreads { get; }
        public TimeSpan KeepAliveInterval { get; }
    }
}