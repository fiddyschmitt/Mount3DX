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
        CancellationTokenSource CancelPingTask = new();
        Task? PingTask;

        public event EventHandler<ProgressEventArgs>? KeepAliveFailed;

        public _3dxServer(string serverUrl, string cookies)
        {
            ServerUrl = serverUrl;
            Cookies = cookies;
        }

        public void StartKeepAlive(int keepAliveIntervalMinutes)
        {
            var keepAliveInterval = TimeSpan.FromMinutes(keepAliveIntervalMinutes);
            CancelPingTask = new();

            PingTask = Task.Factory.StartNew(() =>
            {
                var startTime = DateTime.Now;

                while (!CancelPingTask.IsCancellationRequested)
                {
                    var pingSuccessful = Ping();

                    if (!pingSuccessful)
                    {
                        KeepAliveFailed?.Invoke(this, new ProgressEventArgs()
                        {
                            Message = "Server can no longer be pinged.",
                            Nature = ProgressEventArgs.EnumNature.Bad
                        });

                        break;
                    }

                    try
                    {
                        Task.Delay(keepAliveInterval, CancelPingTask.Token).Wait();
                    }
                    catch { }
                }

                if (!CancelPingTask.IsCancellationRequested)
                {
                    var duration = DateTime.Now - startTime;
                    Log.WriteLine($"KeepAlive finished abruptly after {duration.FormatTimeSpan()}");
                }
            });

            Log.WriteLine($"Started KeepAlive at interval of {keepAliveIntervalMinutes:N0} minutes.");
        }

        public void StopKeepAlive()
        {
            CancelPingTask.Cancel();
            PingTask?.Wait(1000);   //a timeout because it might be the PingTask which has told the session to stop
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
                catch (Exception ex)
                {
                    Log.WriteLine($"Error while pinging sever:{Environment.NewLine}{ex}");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while creating HttpClient to ping the sever:{Environment.NewLine}{ex}");
            }

            return success;
        }

        public string GetSecurityContext()
        {
            var securityContextUrl = ServerUrl.UrlCombine("resources/modeler/pno/person?current=true&select=preferredcredentials");
            var securityContextJsonStr = WebUtility.HttpGet(securityContextUrl, Cookies);
            var securityContextJson = JObject.Parse(securityContextJsonStr);

            var role = securityContextJson["preferredcredentials"]?["role"]?["name"]?.ToString();
            var org = securityContextJson["preferredcredentials"]?["organization"]?["name"]?.ToString();
            var collabspace = securityContextJson["preferredcredentials"]?["collabspace"]?["name"]?.ToString();

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
                            ?.Select(folder =>
                            {
                                var id = folder["id"]?.ToString();


                                var newFolder = new _3dxFolder(
                                        folder["id"]?.ToString() ?? throw new Exception("id could not be retrieved"),
                                        folder["name"]?.ToString() ?? throw new Exception("name could not be retrieved"),
                                        null,
                                        DateTime.Parse(folder["created"]?.ToString() ?? throw new Exception("created could not be retrieved")),
                                        DateTime.Parse(folder["modified"]?.ToString() ?? throw new Exception("modified could not be retrieved")),
                                        DateTime.Parse(folder["modified"]?.ToString() ?? throw new Exception("modified could not be retrieved"))
                                        );

                                return newFolder;
                            })
                            ?.ToList() ?? new List<_3dxFolder>();

            return result;
        }

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
                            ?.Select(item =>
                            {
                                var id = item["id"]?.ToString() ?? throw new Exception("id could not be retrieved");
                                var itemType = item["type"]?.ToString() ?? throw new Exception("type could not be retrieved");

                                _3dxItem? newItem = null;
                                if (itemType.Equals("Document"))
                                {
                                    documentsToRetrieve.Add(id);
                                    return null;
                                }

                                if (itemType.Equals("Workspace Vault"))
                                {
                                    newItem = new _3dxFolder(
                                                    id,
                                                    item["name"]?.ToString() ?? throw new Exception("name could not be retrieved"),
                                                    folder,
                                                    DateTime.Parse(item["created"]?.ToString() ?? throw new Exception("created could not be retrieved")),
                                                    DateTime.Parse(item["modified"]?.ToString() ?? throw new Exception("modified could not be retrieved")),
                                                    DateTime.Parse(item["modified"]?.ToString() ?? throw new Exception("modified could not be retrieved")));
                                }

                                return newItem;
                            })
                            .Where(item => item != null)
                            .ToList() ?? new List<_3dxItem?>();

            //get all documents, because they are effectively folders
            var documentDetails = GetDocuments(documentsToRetrieve, folder);

            result.AddRange(documentDetails);

            return result!;
        }

        public List<_3dxDocument?> GetDocuments(List<string> documentIds, _3dxFolder parent)
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

            var dataField = JObject.Parse(documentDetailsJsonStr)?["data"] ?? throw new Exception("data could not be retrieved");

            var documents = dataField
                                .Select(o => JTokenToDocument(o, parent))
                                .ToList();

            return documents;
        }


        public List<_3dxDocument> GetAllDocuments(_3dxFolder parent, string serverUrl, string cookies, int queryThreads, EventHandler<ProgressEventArgs>? progress, CancellationToken cancellationToken)
        {
            var firstPage = GetDocumentPage(serverUrl, cookies, 1, 1).Result;

            var totalDocsStr = firstPage.Split(new string[] { "\\\"nhits\\\":", ",\\\"facets\\\"" }, StringSplitOptions.None)[1];
            var totalDocs = int.Parse(totalDocsStr);

            var pageSize = 100;
            var totalPages = (int)(totalDocs / (double)pageSize);
            var pages = Enumerable
                .Range(1, totalPages + 1)
                //s.Take(10)
                .ToList();

            int pagesRetrieved = 0;
            int documentsDiscovered = 0;

            var result = pages
                            .AsParallel()
                            .AsOrdered()
                            .WithDegreeOfParallelism(queryThreads)
                            .SelectMany(page =>
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    return new List<_3dxDocument>();
                                }

                                var pageJson = GetDocumentPage(serverUrl, cookies, page, pageSize).Result;

                                var dataField = JObject.Parse(pageJson)["data"] ?? throw new Exception("data could not retrieved");

                                var documents = dataField
                                                .Select(o => JTokenToDocument(o, parent))
                                                .Where(doc => doc != null)
                                                .Cast<_3dxDocument>()
                                                .ToList();

                                Interlocked.Increment(ref pagesRetrieved);
                                Interlocked.Add(ref documentsDiscovered, documents.Count);

                                progress?.Invoke(this, new ProgressEventArgs()
                                {
                                    Message = $"Retrieved {pagesRetrieved:N0}/{totalPages:N0} pages. {documentsDiscovered:N0} documents discovered.",
                                    Nature = ProgressEventArgs.EnumNature.Neutral
                                });

                                return documents;
                            })
                            .ToList();

            return result!;
        }

        public static _3dxDocument? JTokenToDocument(JToken o, _3dxFolder parent)
        {
            var title = o["dataelements"]?["title"]?.ToString();

            if (title == null)
            {
                return null;
            }

            var documentObjectId = o["id"]?.ToString() ?? throw new Exception("documentObjectId could not be retrieved");
            var name = o["dataelements"]?["name"]?.ToString() ?? throw new Exception("name could not be retrieved");
            var revision = o["dataelements"]?["revision"]?.ToString() ?? throw new Exception("revision could not be retrieved");

            var documentType = o["dataelements"]?["typeNLS"]?.ToString() ?? throw new Exception("typeNLS could not be retrieved");
            var description = o["dataelements"]?["description"]?.ToString();
            var originalName = o["dataelements"]?["name"]?.ToString() ?? throw new Exception("name could not be retrieved");

            _ = DateTime.TryParse(o["dataelements"]?["originated"]?.ToString(), out DateTime created);
            _ = DateTime.TryParse(o["dataelements"]?["modified"]?.ToString(), out DateTime modified);
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

            var files = o["relateddata"]?["files"]?.Select(file =>
            {
                var fileObjectId = file["id"]?.ToString() ?? throw new Exception("id could not be retrieved"); ;
                var name = file["dataelements"]?["title"]?.ToString() ?? throw new Exception("title could not be retrieved");
                var fileRevision = file["dataelements"]?["revision"]?.ToString() ?? throw new Exception("revision could not be retrieved");

                _ = DateTime.TryParse(file["dataelements"]?["originated"]?.ToString(), out DateTime created);
                _ = DateTime.TryParse(file["dataelements"]?["modified"]?.ToString(), out DateTime modified);
                var accessed = modified;
                var size = 0UL;

                var fileSizeStr = file["dataelements"]?["fileSize"]?.ToString();
                if (!string.IsNullOrEmpty(fileSizeStr))
                {
                    size = ulong.Parse(fileSizeStr);
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
            .ToList() ?? new List<_3dxFile>();


            newDocument.Files = files;

            return newDocument;
        }

        public static async Task<string> GetDocumentPage(string serverUrl, string cookies, int pageNumber, int pageSize)
        {
            try
            {
                var httpClient = WebUtility.NewHttpClientWithCompression();

                //httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookies);

                var url = serverUrl.UrlCombine($"resources/v1/modeler/controldocuments/myctrldocs/alldocuments?pageNumber={pageNumber}&pageSize={pageSize}&tenant=OnPremise&xrequestedwith=xmlhttprequest");

                using var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }

        public string ServerUrl { get; }
        public string Cookies { get; }
    }
}