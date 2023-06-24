using libCommon;
using libCommon.Utilities;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
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
                using var httpClient = new HttpClient();
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
                                var newFolder = new _3dxFolder()
                                {
                                    Name = folder["name"].ToString(),
                                    ObjectId = folder["id"].ToString(),

                                    CreationTimeUtc = DateTime.Parse(folder["created"].ToString()).ToUniversalTime(),
                                    LastWriteTimeUtc = DateTime.Parse(folder["modified"].ToString()).ToUniversalTime(),
                                    LastAccessTimeUtc = DateTime.Parse(folder["modified"].ToString()).ToUniversalTime(),
                                };

                                return newFolder;
                            })
                            .ToList();

            return result;
        }

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

            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Cookie", Cookies);

            var documentsToRetrieve = new List<string>();

            var rootFolderJsonStr = client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
            var result = JObject
                            .Parse(rootFolderJsonStr)["content"]
                            .Select(item =>
                            {
                                var itemType = item["type"].ToString();

                                _3dxItem? newItem = null;
                                if (itemType.Equals("Document"))
                                {
                                    documentsToRetrieve.Add(item["id"].ToString());
                                    return null;
                                }

                                if (itemType.Equals("Workspace Vault"))
                                {
                                    newItem = new _3dxFolder();
                                }

                                if (newItem == null)
                                {
                                    //Debugger.Break();
                                    return null;
                                }

                                newItem.Name = item["name"].ToString();
                                newItem.ObjectId = item["id"].ToString();
                                newItem.Parent = folder;

                                newItem.CreationTimeUtc = DateTime.Parse(item["created"].ToString()).ToUniversalTime();
                                newItem.LastWriteTimeUtc = DateTime.Parse(item["modified"].ToString()).ToUniversalTime();
                                newItem.LastAccessTimeUtc = DateTime.Parse(item["modified"].ToString()).ToUniversalTime();

                                return newItem;
                            })
                            .Where(item => item != null)
                            .ToList();

            //get all documents, because they are effectively folders
            var documentDetails = GetDocuments(documentsToRetrieve, folder);

            result.AddRange(documentDetails);

            return result;
        }

        public List<_3dxDocument> GetDocuments(List<string> documentIds, _3dxItem? parent)
        {
            var getDocumentDetails = ServerUrl.UrlCombine($"resources/v1/modeler/documents/ids");

            var idsCombined = documentIds.ToString(",");

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(getDocumentDetails),
                Method = HttpMethod.Post,
                Content = new StringContent($"$ids={idsCombined}", Encoding.UTF8, "application/x-www-form-urlencoded"),
            };

            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Cookie", Cookies);

            var documentDetailsJsonStr = client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;

            var documents = JObject.Parse(documentDetailsJsonStr)["data"]
                                                .Select(o =>
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

                                                    var created = DateTime.Parse(o["dataelements"]?["originated"]?.ToString());
                                                    var modified = DateTime.Parse(o["dataelements"]?["modified"]?.ToString());
                                                    var accessed = modified;

                                                    var derivedName = $"{name} Rev {revision}";
                                                    if (name.Equals(title, StringComparison.CurrentCultureIgnoreCase))
                                                    {
                                                        var description = o["dataelements"]?["description"]?.ToString();
                                                        if (!string.IsNullOrEmpty(description))
                                                        {
                                                            //derivedName += $" ({description})";
                                                        }
                                                    }
                                                    else
                                                    {
                                                        derivedName += $" ({title})";
                                                    }

                                                    derivedName = FileUtility.ReplaceInvalidChars(derivedName);

                                                    var newDocument = new _3dxDocument()
                                                    {
                                                        ObjectId = documentObjectId,
                                                        Name = derivedName,
                                                        DocumentType = documentType,
                                                        CreationTimeUtc = created.ToUniversalTime(),
                                                        LastWriteTimeUtc = modified.ToUniversalTime(),
                                                        LastAccessTimeUtc = accessed.ToUniversalTime(),
                                                    };

                                                    var files = o["relateddata"]?["files"].Select(file =>
                                                    {
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

                                                        return new _3dxFile(fileRevision, documentObjectId)
                                                        {
                                                            ObjectId = file["id"].ToString(),
                                                            Name = name,
                                                            Parent = newDocument,

                                                            CreationTimeUtc = created.ToUniversalTime(),
                                                            LastWriteTimeUtc = modified.ToUniversalTime(),
                                                            LastAccessTimeUtc = accessed.ToUniversalTime(),
                                                            Size = size
                                                        };
                                                    })
                                                    .ToList();


                                                    newDocument.Files = files;

                                                    return newDocument;
                                                })
                                                .Where(doc => doc != null)
                                                .ToList();

            return documents;
        }


        public List<_3dxDocument> GetAllDocuments(string serverUrl, string cookies, int queryThreads)
        {
            var firstPage = GetDocumentPage(serverUrl, cookies, 1, 10).Result;

            var totalDocsStr = firstPage.Split(new string[] { "\\\"nhits\\\":", ",\\\"facets\\\"" }, StringSplitOptions.None)[1];
            var totalDocs = int.Parse(totalDocsStr);

            var pageSize = 100;
            var totalPages = (int)(totalDocs / (double)pageSize);
            var pages = Enumerable
                .Range(1, totalPages + 1)
                //.Take(1)
                .ToList();

            Int32 pagesRetrieved = 0;
            Int32 documentsDiscovered = 0;

            var result = pages
                            .AsParallel()
                            .AsOrdered()
                            .WithDegreeOfParallelism(queryThreads)
                            .SelectMany(page =>
                            {
                                var pageJson = GetDocumentPage(serverUrl, cookies, page, pageSize).Result;

                                Interlocked.Increment(ref pagesRetrieved);
                                Console.WriteLine($"Retrieved {pagesRetrieved:N0}/{totalPages:N0} pages. Page number {page}");

                                var documents = JObject.Parse(pageJson)["data"]
                                                .Select(o =>
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

                                                    var created = DateTime.Parse(o["dataelements"]?["originated"]?.ToString());
                                                    var modified = DateTime.Parse(o["dataelements"]?["modified"]?.ToString());
                                                    var accessed = modified;

                                                    var derivedName = $"{name} Rev {revision}";
                                                    if (name.Equals(title, StringComparison.CurrentCultureIgnoreCase))
                                                    {
                                                        var description = o["dataelements"]?["description"]?.ToString();
                                                        if (!string.IsNullOrEmpty(description))
                                                        {
                                                            //derivedName += $" ({description})";
                                                        }
                                                    }
                                                    else
                                                    {
                                                        derivedName += $" ({title})";
                                                    }

                                                    derivedName = FileUtility.ReplaceInvalidChars(derivedName);

                                                    var newDocument = new _3dxDocument()
                                                    {
                                                        ObjectId = documentObjectId,
                                                        Name = derivedName,
                                                        DocumentType = documentType,
                                                        CreationTimeUtc = created.ToUniversalTime(),
                                                        LastWriteTimeUtc = modified.ToUniversalTime(),
                                                        LastAccessTimeUtc = accessed.ToUniversalTime(),
                                                    };

                                                    var files = o["relateddata"]?["files"].Select(file =>
                                                    {
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

                                                        return new _3dxFile(fileRevision, documentObjectId)
                                                        {
                                                            ObjectId = file["id"].ToString(),
                                                            Name = name,
                                                            Parent = newDocument,

                                                            CreationTimeUtc = created.ToUniversalTime(),
                                                            LastWriteTimeUtc = modified.ToUniversalTime(),
                                                            LastAccessTimeUtc = accessed.ToUniversalTime(),
                                                            Size = size
                                                        };
                                                    })
                                                    .ToList();


                                                    newDocument.Files = files;

                                                    return newDocument;
                                                })
                                                .Where(doc => doc != null)
                                                .ToList();

                                return documents;
                            })
                            .ToList();

            return result;
        }

        public static async Task<string> GetDocumentPage(string serverUrl, string cookies, int pageNumber, int pageSize)
        {
            using var httpClient = new HttpClient();

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