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

        public _3dxServer(string serverUrl, _3dxCookies cookies)
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
                    int attempt;
                    int maxAttempts = 5;

                    var pingSuccessful = false;

                    for (attempt = 1; attempt <= maxAttempts && !CancelPingTask.IsCancellationRequested; attempt++)
                    {
                        pingSuccessful = Ping(Cookies, CancelPingTask.Token);

                        if (pingSuccessful) break;

                        try { Task.Delay(TimeSpan.FromSeconds(60), CancelPingTask.Token).Wait(); } catch { }
                    }

                    if (CancelPingTask.IsCancellationRequested) break;

                    if (!pingSuccessful)
                    {
                        Log.WriteLine($"Server can no longer be pinged. Attempted {attempt} {"time".Pluralize(attempt)}.");

                        KeepAliveFailed?.Invoke(this, new ProgressEventArgs()
                        {
                            Message = "Server can no longer be pinged.",
                            Nature = ProgressEventArgs.EnumNature.Bad
                        });

                        break;
                    }

                    if (attempt > 1)
                    {
                        //Debugger.Break();
                    }

                    try { Task.Delay(keepAliveInterval, CancelPingTask.Token).Wait(); } catch { }

                    if (CancelPingTask.IsCancellationRequested) break;
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


        public static bool Ping(_3dxCookies cookies, CancellationToken cancellationToken)
        {
            var tests = new[] {
                new
                {
                    TestUrl = cookies._3DSpace.BaseUrl.UrlCombine("resources/v1/modeler/documents/ids"),
                    Cookies = cookies._3DSpace
                },
                new
                {
                    TestUrl = cookies._3DSearch.BaseUrl.UrlCombine("search"),
                    Cookies = cookies._3DSearch
                }
            };

            var allCookiesWork = tests
                                    .All(test =>
                                    {
                                        var cookieWorks = false;

                                        try
                                        {
                                            var httpClient = new HttpClient();
                                            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", test.Cookies.Cookie);
                                            var request = new HttpRequestMessage(HttpMethod.Get, test.TestUrl);

                                            try
                                            {
                                                var response = httpClient.Send(request, cancellationToken);
                                                var responseStr = response.Content.ReadAsStringAsync(cancellationToken).Result;
                                                if (responseStr.Contains("An unexpected error has occurred") || responseStr.Contains("FS_INVALID_QUERY__NO_QUERY"))
                                                {
                                                    cookieWorks = true;
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

                                        return cookieWorks;
                                    });

            return allCookiesWork;
        }

        public string GetSecurityContext()
        {
            var securityContextUrl = ServerUrl.UrlCombine("resources/modeler/pno/person?current=true&select=preferredcredentials");
            var securityContextJsonStr = WebUtility.HttpGet(securityContextUrl, Cookies._3DSpace.Cookie);
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
            client.DefaultRequestHeaders.Add("Cookie", Cookies._3DSpace.Cookie);

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

            httpClient.DefaultRequestHeaders.Add("Cookie", Cookies._3DSpace.Cookie);

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

            httpClient.DefaultRequestHeaders.Add("Cookie", Cookies._3DSpace.Cookie);

            var documentDetailsJsonStr = httpClient.SendAsync(request).Result.Content.ReadAsStringAsync().Result;

            var dataField = JObject.Parse(documentDetailsJsonStr)?["data"] ?? throw new Exception("data could not be retrieved");

            var documents = dataField
                                .Select(o => JTokenToDocument(o, parent))
                                .ToList();

            return documents;
        }


        public List<_3dxDocument> GetAllDocuments(_3dxFolder parent, _3dxCookies cookies, int queryThreads, EventHandler<ProgressEventArgs>? progress)
        {
            var firstPageStr = GetDocumentPage(cookies, 0, 1, null);
            var firstPageJson = JObject.Parse(firstPageStr) ?? throw new Exception("Could not parse JSON retrieved from first search");

            var totalDocsStr = (firstPageJson["infos"]?["nmatches"]?.ToString()) ?? throw new Exception("Could not find nmatches field in JSON");
            var totalDocs = int.Parse(totalDocsStr);

            var nextStartStr = (firstPageJson["infos"]?["next_start"]?.ToString()) ?? throw new Exception("Could not find next_start field in JSON");
            var searchId = nextStartStr.Split(" ", StringSplitOptions.None).Last();



            var pageSize = 100;
            var totalPages = (int)Math.Ceiling(totalDocs / (double)pageSize);
            var pages = Enumerable
                .Range(0, totalPages)
                //.Take(10)
                .ToList();

            var pagesRetrieved = 0;
            var documentsDiscovered = 0;

            var result = pages
                            .AsParallel()
                            .AsOrdered()
                            .WithDegreeOfParallelism(queryThreads)
                            .SelectMany(page =>
                            {
                                string docPageStr;
                                JToken? resultObj = null;

                                int attempt;
                                int maxAttempts = 5;

                                for (attempt = 1; attempt <= maxAttempts; attempt++)
                                {
                                    try
                                    {
                                        docPageStr = GetDocumentPage(cookies, page, pageSize, searchId);
                                        var docPageJson = JObject.Parse(docPageStr);
                                        resultObj = docPageJson["results"];

                                        if (resultObj != null)
                                        {
                                            break;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.WriteLine($"Could not retrieve results for page {page}, during attempt {attempt}. {ex.Message}");
                                    }
                                }

                                if (resultObj == null)
                                {
                                    var couldNotRetrievePage = $"Could not retrieve results for page {page}. Attempted {attempt} {"time".Pluralize(attempt)}.";
                                    Log.WriteLine(couldNotRetrievePage);

                                    /*
                                    progress?.Invoke(null, new ProgressEventArgs()
                                    {
                                        Message = couldNotRetrievePage,
                                        Nature = ProgressEventArgs.EnumNature.Bad
                                    });
                                    */

                                    throw new Exception(couldNotRetrievePage);
                                }

                                if (attempt > 1)
                                {
                                    Log.WriteLine($"Successfully retrieved page {page} after {attempt} {"attempt".Pluralize(attempt)}");
                                }


                                var documentIds = resultObj
                                                    .Select(result =>
                                                    {
                                                        var attributes = result["attributes"];

                                                        var resourceIdAttribute = attributes?.FirstOrDefault(attr => attr["name"]?.ToString().Equals("resourceid") ?? false);

                                                        var resourceId = resourceIdAttribute?["value"]?.ToString();

                                                        return resourceId ?? "";
                                                    })
                                                    .Where(documentId => !string.IsNullOrEmpty(documentId))
                                                    .ToList();

                                var documents = GetDocuments(documentIds, parent)
                                                    .Where(doc => doc != null)  //todo - find which are null and why
                                                    .ToList();

                                Interlocked.Increment(ref pagesRetrieved);
                                Interlocked.Add(ref documentsDiscovered, documents.Count);

                                progress?.Invoke(null, new ProgressEventArgs()
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

            derivedName = FileUtility.WebdavCompatibleFilename(derivedName);

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

        public static string GetDocumentPage(_3dxCookies cookies, int pageNumber, int pageSize, string? searchId)
        {
            var searchUrl = cookies._3DSearch.BaseUrl.UrlCombine("search?xrequestedwith=xmlhttprequest");

            var requestJson = $$"""
                    {
                        "label": "preview-1c00d81706698017698",
                        "locale": "en",
                        "nresults": {{pageSize}},
                        "query": "((flattenedtaxonomies:types/Document) OR (flattenedtaxonomies:\"types/CONTROLLED DOCUMENTS\")) NOT ((policy:\"Controlled Document Template\") OR (policy:\"Rendition Document Template\") OR (policy:Version) OR (policy:ProxyItem))",
                        "refine": {
                        },
                        "select_predicate": [
                            "ds6w:label",
                            "ds6w:status",
                            "ds6w:type"
                        ],
                        "source": [
                            "3dspace"
                        ],
                        "start": 0,
                        "tenant": "OnPremise",
                        "with_synthesis": true
                    }
                    """;

            if (pageNumber > 0)
            {
                var start = pageNumber * pageSize;
                var nextStart = $"{start} 0 {pageNumber} {searchId}";

                requestJson = requestJson.Replace("""
                        "start": 0,
                        """,

                    $$"""
                        "next_start": "{{nextStart}}",
                        """);
            }

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(searchUrl),
                Method = HttpMethod.Post,
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            var httpClient = WebUtility.NewHttpClientWithCompression();

            httpClient.DefaultRequestHeaders.Add("Cookie", cookies._3DSearch.Cookie);

            var response = httpClient.SendAsync(request).Result;
            response.EnsureSuccessStatusCode();
            var searchResultsJsonStr = response.Content.ReadAsStringAsync().Result;

            return searchResultsJsonStr;
        }

        public List<_3dxCollector> GetAllCollectors(_3dxFolder parent, _3dxCookies cookies, int queryThreads, EventHandler<ProgressEventArgs>? progress)
        {
            var firstPageStr = GetCollectorsPage(cookies, 0, 1, null);
            var firstPageJson = JObject.Parse(firstPageStr) ?? throw new Exception("Could not parse JSON retrieved from first search");

            var totalCollectorsStr = (firstPageJson["infos"]?["nmatches"]?.ToString()) ?? throw new Exception("Could not find nmatches field in JSON");
            var totalCollectors = int.Parse(totalCollectorsStr);

            var nextStartStr = (firstPageJson["infos"]?["next_start"]?.ToString()) ?? throw new Exception("Could not find next_start field in JSON");
            var searchId = nextStartStr.Split(" ", StringSplitOptions.None).Last();



            var pageSize = 100;
            var totalPages = (int)Math.Ceiling(totalCollectors / (double)pageSize);
            var pages = Enumerable
                .Range(0, totalPages)
                //.Take(10)
                .ToList();

            var pagesRetrieved = 0;
            var collectorsDiscovered = 0;

            var result = pages
                            .AsParallel()
                            .AsOrdered()
                            .WithDegreeOfParallelism(queryThreads)
                            .SelectMany(page =>
                            {
                                string collectorsPageStr;
                                JToken? resultObj = null;

                                int attempt;
                                int maxAttempts = 5;

                                for (attempt = 1; attempt <= maxAttempts; attempt++)
                                {
                                    try
                                    {
                                        collectorsPageStr = GetCollectorsPage(cookies, page, pageSize, searchId);
                                        var docPageJson = JObject.Parse(collectorsPageStr);
                                        resultObj = docPageJson["results"];

                                        if (resultObj != null)
                                        {
                                            break;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.WriteLine($"Could not retrieve Collectors results for page {page}, during attempt {attempt}. {ex.Message}");
                                    }
                                }

                                if (resultObj == null)
                                {
                                    var couldNotRetrievePage = $"Could not retrieve Collectors results for page {page}. Attempted {attempt} {"time".Pluralize(attempt)}.";
                                    Log.WriteLine(couldNotRetrievePage);

                                    throw new Exception(couldNotRetrievePage);
                                }

                                if (attempt > 1)
                                {
                                    Log.WriteLine($"Successfully retrieved Collectors page {page} after {attempt} {"attempt".Pluralize(attempt)}");
                                }


                                var collectors = resultObj
                                                    .Select(result =>
                                                    {
                                                        var attributes = result["attributes"];

                                                        var resourceIdAttribute = attributes?.FirstOrDefault(attr => attr["name"]?.ToString().Equals("resourceid") ?? false);
                                                        var resourceId = resourceIdAttribute?["value"]?.ToString();

                                                        var labelAttribute = attributes?.FirstOrDefault(attr => attr["name"]?.ToString().Equals("ds6w:label") ?? false);
                                                        var label = labelAttribute?["value"]?.ToString();
                                                        label = FileUtility.WebdavCompatibleFilename(label);

                                                        var createdAttribute = attributes?.FirstOrDefault(attr => attr["name"]?.ToString().Equals("ds6w:when/ds6w:created") ?? false);
                                                        var created = DateTime.Parse(createdAttribute?["value"]?.ToString());

                                                        var modifiedAttribute = attributes?.FirstOrDefault(attr => attr["name"]?.ToString().Equals("ds6w:when/ds6w:modified") ?? false);
                                                        var modified = DateTime.Parse(modifiedAttribute?["value"]?.ToString());

                                                        var newCollector = new _3dxCollector(resourceId, label, null, created, modified, modified);
                                                        return newCollector;
                                                    })
                                                    .Where(collector => collector.Name.EndsWith("Root Software _ Firmware Collector"))
                                                    .ToList();

                                Interlocked.Increment(ref pagesRetrieved);
                                Interlocked.Add(ref collectorsDiscovered, collectors.Count);


                                //Does perform some level of recursion but doesn't go all the way
                                //PopulateChildren_UsingServerSideRecursion(collectors, cookies, 8, null);

                                PopulateChildren_UsingLocalRecursion(collectors, cookies, 8, null);

                                //progress?.Invoke(null, new ProgressEventArgs()
                                //{
                                //    Message = $"Retrieved {pagesRetrieved:N0}/{totalPages:N0} pages. {collectorsDiscovered:N0} documents discovered.",
                                //    Nature = ProgressEventArgs.EnumNature.Neutral
                                //});

                                return collectors;
                            })
                            .ToList();

            //Populate the files
            result
                .OfType<_3dxFolder>()
                .Recurse2(folder =>
                {
                    var specDocs = GetSpecificationDocuments(folder.ObjectId, folder);
                    folder.Subfolders.AddRange(specDocs);

                    return folder.Subfolders;
                }, queryThreads, CancellationToken.None);

            return result!;
        }

        //Warning: Does perform some level of recursion but doesn't go all the way
        public void PopulateChildren_UsingServerSideRecursion(List<_3dxCollector> collectors, _3dxCookies cookies, int queryThreads, EventHandler<ProgressEventArgs>? progress)
        {
            collectors
                .OfType<_3dxFolder>()
                .AsParallel()
                .WithDegreeOfParallelism(queryThreads)
                .ForAll(collector =>
                {
                    Debug.WriteLine($"{collector.ObjectId} {collector.FullPath}");

                    //var relationsStr = GetRelationships_UsingProgressiveExpand(cookies, collector.ObjectId);

                    var relationsStr = GetRelationships_UsingExpand(cookies, collector.ObjectId, true);
                    var resultsObj = JObject
                                        .Parse(relationsStr)["results"];

                    var allSubfolders = resultsObj
                                        .Select(res => res["attributes"])
                                        .Where(attr => attr != null)
                                        .Where(attr => attr.Any(a => a["name"]?.ToString() == "physicalid"))
                                        ?.Select(attr =>
                                        {
                                            var did = attr.FirstOrDefault(a => a["name"]?.ToString() == "did")?["value"]?.ToString() ?? throw new Exception();
                                            var physicalId = attr.FirstOrDefault(a => a["name"]?.ToString() == "physicalid")?["value"]?.ToString() ?? throw new Exception();
                                            var label = attr.FirstOrDefault(a => a["name"]?.ToString() == "ds6w:label")?["value"]?.ToString() ?? throw new Exception();
                                            label = FileUtility.WebdavCompatibleFilename(label);
                                            var created = DateTime.Parse(attr.FirstOrDefault(a => a["name"]?.ToString() == "ds6w:created")?["value"]?.ToString() ?? throw new Exception());
                                            var modified = DateTime.Parse(attr.FirstOrDefault(a => a["name"]?.ToString() == "ds6w:modified")?["value"]?.ToString() ?? throw new Exception());
                                            var ds6wType = attr.FirstOrDefault(a => a["name"]?.ToString() == "ds6w:type")?["value"]?.ToString() ?? throw new Exception();
                                            var itemType = attr.FirstOrDefault(a => a["name"]?.ToString() == "type")?["value"]?.ToString() ?? throw new Exception();

                                            var newFolder = new _3dxFolder(physicalId, label, collector, created, modified, modified);
                                            return new
                                            {
                                                Folder = newFolder,
                                                did
                                            };
                                        })
                                        .OrderBy(folder => folder.Folder.Name)
                                        .ToList();

                    //the 'path' arrays tell us the folder structure
                    resultsObj
                        .Select(res => res["path"])
                        .OfType<JArray>()
                        .ToList()
                        .ForEach(path =>
                        {
                            var currentParentFolder = collector;

                            var didsToExamine = path
                                                    .Skip(1)        //we don't want the first one, because it's just the collector
                                                    .SkipLast(1)    //we don't go to the final level because we retrieve that level using GetSpecificationDocuments()
                                                    .ToList();

                            foreach (var folderDid in didsToExamine)
                            {
                                var thisLevelFolder = allSubfolders.FirstOrDefault(f => f.did == folderDid.ToString())?.Folder;

                                if (thisLevelFolder != null)
                                {
                                    var alreadyAdded = currentParentFolder.Subfolders.Any(sf => sf.ObjectId == thisLevelFolder.ObjectId);
                                    if (!alreadyAdded)
                                    {
                                        var specDocs = GetSpecificationDocuments(thisLevelFolder.ObjectId, thisLevelFolder);    //todo: We might only need to do this at the leaf nodes
                                        thisLevelFolder.Subfolders.AddRange(specDocs);

                                        currentParentFolder.Subfolders.Add(thisLevelFolder);
                                        thisLevelFolder.Parent = currentParentFolder;
                                    }

                                    currentParentFolder = thisLevelFolder;
                                }
                            }
                        });
                });
        }

        public void PopulateChildren_UsingLocalRecursion(List<_3dxCollector> collectors, _3dxCookies cookies, int queryThreads, EventHandler<ProgressEventArgs>? progress)
        {
            collectors
                .OfType<_3dxFolder>()
                .AsParallel()
                .WithDegreeOfParallelism(queryThreads)
                .Recurse2(folder =>
                {
                    //Debug.WriteLine($"{folder.ObjectId} {folder.FullPath}");

                    //var relationsStr = GetRelationships_UsingProgressiveExpand(cookies, collector.ObjectId);

                    var relationsStr = GetRelationships_UsingExpand(cookies, folder.ObjectId, false);

                    var subfolders = JObject
                                        .Parse(relationsStr)["results"]
                                        .Select(res => res["attributes"])
                                        .Where(attr => attr != null)
                                        .Where(attr => attr.Any(a => a["name"]?.ToString() == "physicalid"))
                                        ?.Select(attr =>
                                        {
                                            var physicalId = attr.FirstOrDefault(a => a["name"]?.ToString() == "physicalid")?["value"]?.ToString() ?? throw new Exception();
                                            var label = attr.FirstOrDefault(a => a["name"]?.ToString() == "ds6w:label")?["value"]?.ToString() ?? throw new Exception();
                                            label = FileUtility.WebdavCompatibleFilename(label);
                                            var created = DateTime.Parse(attr.FirstOrDefault(a => a["name"]?.ToString() == "ds6w:created")?["value"]?.ToString() ?? throw new Exception());
                                            var modified = DateTime.Parse(attr.FirstOrDefault(a => a["name"]?.ToString() == "ds6w:modified")?["value"]?.ToString() ?? throw new Exception());
                                            var ds6wType = attr.FirstOrDefault(a => a["name"]?.ToString() == "ds6w:type")?["value"]?.ToString() ?? throw new Exception();
                                            var itemType = attr.FirstOrDefault(a => a["name"]?.ToString() == "type")?["value"]?.ToString() ?? throw new Exception();

                                            var newFolder = new _3dxFolder(physicalId, label, folder, created, modified, modified);
                                            return newFolder;
                                        })
                                        .Where(fol => folder.ObjectId != fol.ObjectId)
                                        .OrderBy(folder => folder.Name)
                                        .ToList();

                    folder.Subfolders = subfolders;

                    return subfolders;
                }, queryThreads, CancellationToken.None);
        }

        //Warning: entireHierarchy does perform some level of recursion but doesn't go all the way
        public static string GetRelationships_UsingExpand(_3dxCookies cookies, string itemId, bool entireHierarchy)
        {
            var searchUrl = cookies._3DSpace.BaseUrl.UrlCombine("cvservlet/expand");

            var requestJson = $$"""
                    {
                        %RECURSION_PLACEHOLDER%
                        "label": "xEngineer-3473903-1710239530055",
                        "q.iterative_filter_query_bo": "[ds6w:globalType]:\"ds6w:Document\" OR [ds6w:globalType]:\"ds6w:Part\"",
                        "root_path_physicalid": [
                            [
                                "{{itemId}}"
                            ]
                        ],
                        "select_bo": [
                            "ds6w:label",
                            "ds6w:modified",
                            "ds6w:created",
                            "ds6w:description",
                            "ds6wg:revision",
                            "ds6w:responsible",
                            "owner",
                            "ds6w:status",
                            "ds6w:type",
                            "ds6wg:EnterpriseExtension.V_PartNumber",
                            "ds6wg:MaterialUsageExtension.DeclaredQuantity",
                            "ds6wg:DELFmiContQuantity_Mass.V_ContQuantity",
                            "ds6wg:DELFmiContQuantity_Volume.V_ContQuantity",
                            "ds6wg:raw_material.v_dimensiontype",
                            "type",
                            "physicalid",
                            "ds6w:policy",
                            "ds6w:reservedBy",
                            "ds6w:globalType",
                            "ds6wg:PLMReference.V_isLastVersion",
                            "ds6w:reserved",
                            "ds6w:identifier"
                        ]
                    }
                    """;

            if (entireHierarchy)
            {
                requestJson = requestJson.Replace("%RECURSION_PLACEHOLDER%", "");
            }
            else
            {
                requestJson = requestJson.Replace("%RECURSION_PLACEHOLDER%", @"""expand_iter"": ""1"",");
            }

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(searchUrl),
                Method = HttpMethod.Post,
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            var httpClient = WebUtility.NewHttpClientWithCompression();

            httpClient.DefaultRequestHeaders.Add("Cookie", cookies._3DSpace.Cookie);

            var response = httpClient.SendAsync(request).Result;
            response.EnsureSuccessStatusCode();
            var searchResultsJsonStr = response.Content.ReadAsStringAsync().Result;

            return searchResultsJsonStr;
        }

        public static string GetRelationships_UsingProgressiveExpand(_3dxCookies cookies, string itemId)
        {
            var searchUrl = cookies._3DSpace.BaseUrl.UrlCombine("cvservlet/progressiveexpand/v2");

            var requestJson = $$"""
                    {
                        "batch": {
                            "expands": [
                                {
                                    "aggregation_processors": [
                                        {
                                            "truncate": {
                                                "max_distance_from_prefix": 1,
                                                "prefix_filter": {
                                                    "prefix_path": [
                                                        {
                                                            "physical_id_path": [
                                                                "{{itemId}}"
                                                            ]
                                                        }
                                                    ]
                                                }
                                            }
                                        }
                                    ],
                                    "filter": {
                                        "or": {
                                            "filters": [
                                                {
                                                    "and_not": {
                                                        "left": {
                                                            "and": {
                                                                "filters": [
                                                                    {
                                                                        "prefix_filter": {
                                                                            "prefix_path": [
                                                                                {
                                                                                    "physical_id_path": [
                                                                                        "{{itemId}}"
                                                                                    ]
                                                                                }
                                                                            ]
                                                                        }
                                                                    }
                                                                ]
                                                            }
                                                        },
                                                        "right": {
                                                            "or": {
                                                                "filters": [
                                                                    {
                                                                        "sequence_filter": {
                                                                            "sequence": [
                                                                                {
                                                                                    "uql": "NOT (((typecode:0 OR typecode:1) AND (ds6w_58_globaltype:\"ds6w:Document\" OR ds6w_58_globaltype:\"ds6w:Part\"))  OR (typecode:2 AND NOT (flattenedtaxonomies:\"reltypes/XCADBaseDependency\")))"
                                                                                }
                                                                            ]
                                                                        }
                                                                    }
                                                                ]
                                                            }
                                                        }
                                                    }
                                                }
                                            ]
                                        }
                                    },
                                    "label": "Expand-Level-1-3473903-ENOSCEN_AP-1710399986368",
                                    "root": {
                                        "physical_id": "{{itemId}}"
                                    }
                                }
                            ]
                        },
                        "outputs": {
                            "hits": {
                                "predefined_computation": [
                                    "urlstream|thumbnail|2dthb",
                                    "icons"
                                ]
                            },
                            "select_object": [
                                "physicalid",
                                "ds6w:globalType",
                                "ds6w:label",
                                "ds6wg:revision",
                                "ds6w:type",
                                "ds6w:description",
                                "ds6w:responsible",
                                "ds6w:cadMaster",
                                "ds6w:status",
                                "owner",
                                "ds6w:reservedBy",
                                "bo.pgpshowextension.V_PGP_Show",
                                "ds6w:modified",
                                "ds6w:project",
                                "ds6w:identifier"
                            ],
                            "select_pgp": [
                                "show",
                                "hide"
                            ],
                            "select_relation": [
                                "physicalid",
                                "ds6w:globalType",
                                "ds6w:label",
                                "matrixtxt",
                                "ro.pgpshowextension.V_PGP_Show",
                                "ro.plminstance.v_treeorder"
                            ]
                        }
                    }
                    """;

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(searchUrl),
                Method = HttpMethod.Post,
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            var httpClient = WebUtility.NewHttpClientWithCompression();

            httpClient.DefaultRequestHeaders.Add("Cookie", cookies._3DSpace.Cookie);

            var response = httpClient.SendAsync(request).Result;
            response.EnsureSuccessStatusCode();
            var searchResultsJsonStr = response.Content.ReadAsStringAsync().Result;

            return searchResultsJsonStr;
        }

        //Warning: For some reason, this only works if the cookie is for 3dspace1
        public static string GetRelationships_UsingGetEcosystem(_3dxCookies cookies, string itemId)
        {
            var searchUrl = cookies._3DSpace.BaseUrl.UrlCombine("resources/enorelnav/navigate/getecosystem");

            var requestJson = $$"""
                    {
                        "ecoSystemWithDetail":true,
                        "debug":false,
                        "id":"{{itemId}}"}
                    """;

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(searchUrl),
                Method = HttpMethod.Post,
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            var httpClient = WebUtility.NewHttpClientWithCompression();

            httpClient.DefaultRequestHeaders.Add("Cookie", cookies._3DSpace.Cookie);

            var response = httpClient.SendAsync(request).Result;
            response.EnsureSuccessStatusCode();
            var searchResultsJsonStr = response.Content.ReadAsStringAsync().Result;

            return searchResultsJsonStr;
        }

        public static string GetCollectorsPage(_3dxCookies cookies, int pageNumber, int pageSize, string? searchId)
        {
            var searchUrl = cookies._3DSearch.BaseUrl.UrlCombine("search?xrequestedwith=xmlhttprequest");

            var requestJson = $$"""
                    {
                        "label": "preview-1c00d81706698017698",
                        "locale": "en",
                        "nresults": {{pageSize}},
                        "query": "((flattenedtaxonomies:types/BOE_Collector))",
                        "refine": {
                        },
                        "select_predicate": [
                            "ds6w:label",
                            "ds6w:status",
                            "ds6w:type"
                        ],
                        "source": [
                            "3dspace"
                        ],
                        "start": 0,
                        "tenant": "OnPremise",
                        "with_synthesis": true
                    }
                    """;

            if (pageNumber > 0)
            {
                var start = pageNumber * pageSize;
                var nextStart = $"{start} 0 {pageNumber} {searchId}";

                requestJson = requestJson.Replace("""
                        "start": 0,
                        """,

                    $$"""
                        "next_start": "{{nextStart}}",
                        """);
            }

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(searchUrl),
                Method = HttpMethod.Post,
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            var httpClient = WebUtility.NewHttpClientWithCompression();

            httpClient.DefaultRequestHeaders.Add("Cookie", cookies._3DSearch.Cookie);

            var response = httpClient.SendAsync(request).Result;
            response.EnsureSuccessStatusCode();
            var searchResultsJsonStr = response.Content.ReadAsStringAsync().Result;

            return searchResultsJsonStr;
        }

        public List<_3dxDocument?> GetSpecificationDocuments(string parentId, _3dxFolder parent)
        {
            var getDocumentDetails = ServerUrl.UrlCombine($"resources/v1/modeler/documents/parentId/{parentId}?parentRelName=SpecificationDocument&parentDirection=from&$fields=indexedImage,indexedTypeicon,isDocumentType&$include=versions");

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(getDocumentDetails),
                Method = HttpMethod.Get,
            };

            var httpClient = WebUtility.NewHttpClientWithCompression();

            httpClient.DefaultRequestHeaders.Add("Cookie", Cookies._3DSpace.Cookie);

            var documentDetailsJsonStr = httpClient.SendAsync(request).Result.Content.ReadAsStringAsync().Result;

            var dataField = JObject.Parse(documentDetailsJsonStr)?["data"] ?? throw new Exception("data could not be retrieved");

            var documents = dataField
                                .Select(o => JTokenToDocument(o, parent))
                                .ToList();

            return documents;
        }

        public string ServerUrl { get; }
        public _3dxCookies Cookies { get; }
    }
}