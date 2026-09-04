using lib3dx.Files;
using libCommon;
using libCommon.Events;
using libCommon.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web;

namespace lib3dx
{
    public class _3dxServer
    {
        public HttpClient HttpClient;
        public HttpClientHandler ClientHandler;

        CancellationTokenSource CancelPingTask = new();
        Task? PingTask;

        public string ServerUrl { get; protected set; }

        //Settable because the server object outlives a session (to keep its cookies), while these
        //settings may change between sessions
        public bool GenerateDocumentLinkFile { get; set; }

        public bool GenerateDocumentMetadataFile { get; set; }

        string? SearchServiceUrl;

        public event EventHandler<ProgressEventArgs>? KeepAliveFailed;

        public _3dxServer(string serverUrl, bool generateDocumentLinkFile, bool generateDocumentMetadataFile)
        {
            ServerUrl = serverUrl;
            GenerateDocumentLinkFile = generateDocumentLinkFile;
            GenerateDocumentMetadataFile = generateDocumentMetadataFile;
            (HttpClient, ClientHandler) = CreateHttpClient();
        }

        static (HttpClient Client, HttpClientHandler Handler) CreateHttpClient()
        {
            var clientHandler = new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };

            var client = new HttpClient(clientHandler);

            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

            //settings to allow large files to be downloaded
            client.Timeout = TimeSpan.FromMinutes(30);  //default is 1 minute, 40 seconds

            return (client, clientHandler);
        }

        public bool LogIn()
        {
            //We need to clear the cookies here otherwise the previous call to Ping() interferes with the login process
            (HttpClient, ClientHandler) = CreateHttpClient();

            //a new login may carry different preferred credentials
            lock (securityContextLock) { securityContext = null; }

            //Try to log in using Single Sign-On
            var result = _3dxLogin.LogInUsingHttpClient(ServerUrl, HttpClient, ClientHandler);

            //Fall back to Selenium
            if (!result)
            {
                //We need to clear the cookies here otherwise the previous call to LogInUsingHttpClient() interferes with the login process
                (HttpClient, ClientHandler) = CreateHttpClient();

                result = _3dxLogin.LogInUsingSelenium(ServerUrl, HttpClient, ClientHandler.CookieContainer);
            }

            if (result)
            {
                //find the Search Service
                SearchServiceUrl = _3dxLogin.DiscoverSearchServiceUrl(ServerUrl, HttpClient);

                //Pinging the server retrieves the Search Service cookies
                var pingSuccessful = Ping(CancellationToken.None);

                result &= pingSuccessful;
            }

            return result;
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
                        pingSuccessful = Ping(CancelPingTask.Token);

                        if (pingSuccessful) break;

                        try { Task.Delay(TimeSpan.FromSeconds(60), CancelPingTask.Token).Wait(); } catch { }
                    }

                    if (CancelPingTask.IsCancellationRequested) break;

                    if (!pingSuccessful)
                    {
                        Log.WriteLine($"Server can no longer be pinged. Attempted {maxAttempts} {"time".Pluralize(maxAttempts)}.");

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


        public bool Ping(CancellationToken cancellationToken)
        {
            var tests = new[] {
                            ServerUrl.UrlCombine("resources/v1/modeler/documents/ids"),
                            SearchServiceUrl?.UrlCombine("search") ?? ""
            };

            var allCookiesWork = tests
                                    .All(testUrl =>
                                    {
                                        var cookieWorks = false;

                                        try
                                        {
                                            var request = new HttpRequestMessage(HttpMethod.Get, testUrl);

                                            try
                                            {
                                                var response = HttpClient.Send(request, cancellationToken);
                                                var responseStr = response.Content.ReadAsStringAsync(cancellationToken).Result;
                                                if (responseStr.Contains("An unexpected error has occurred") || responseStr.Contains("FS_INVALID_QUERY__NO_QUERY"))
                                                {
                                                    cookieWorks = true;
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.WriteLine($"Error while pinging sever:{Environment.NewLine}{ex.Message}");
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

        //The security context is fixed for a logged-in user, so fetch it once per login rather than
        //on every metadata request
        readonly object securityContextLock = new();
        string? securityContext;

        public string GetSecurityContext()
        {
            lock (securityContextLock)
            {
                if (securityContext == null)
                {
                    //could also use: resources/pno/person/getsecuritycontext
                    var securityContextUrl = ServerUrl.UrlCombine("resources/modeler/pno/person?current=true&select=preferredcredentials");

                    var request = new HttpRequestMessage(HttpMethod.Get, securityContextUrl);
                    var response = HttpClient.Send(request);
                    response.EnsureSuccessStatusCode();
                    var securityContextJsonStr = response.Content.ReadAsStringAsync().Result;
                    var securityContextJson = JObject.Parse(securityContextJsonStr);

                    var role = securityContextJson["preferredcredentials"]?["role"]?["name"]?.ToString();
                    var org = securityContextJson["preferredcredentials"]?["organization"]?["name"]?.ToString();
                    var collabspace = securityContextJson["preferredcredentials"]?["collabspace"]?["name"]?.ToString();

                    securityContext = $"{role}.{org}.{collabspace}";
                }

                return securityContext;
            }
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


            var response = HttpClient.SendAsync(request).Result;
            response.EnsureSuccessStatusCode();
            var rootFolderJsonStr = response.Content.ReadAsStringAsync().Result;
            var result = JObject
                            .Parse(rootFolderJsonStr)["folders"]
                            ?.Select(folder =>
                            {
                                var id = folder["id"]?.ToString();


                                var newFolder = new _3dxFolder(
                                        folder["id"]?.ToString() ?? throw new Exception("id could not be retrieved"),
                                        folder["name"]?.ToString() ?? throw new Exception("name could not be retrieved"),
                                        null,
                                        DateTime.Parse(folder["created"]?.ToString() ?? throw new Exception("created could not be retrieved"), CultureInfo.CurrentCulture),
                                        DateTime.Parse(folder["modified"]?.ToString() ?? throw new Exception("modified could not be retrieved"), CultureInfo.CurrentCulture),
                                        DateTime.Parse(folder["modified"]?.ToString() ?? throw new Exception("modified could not be retrieved"), CultureInfo.CurrentCulture)
                                        );

                                return newFolder;
                            })
                            ?.ToList() ?? [];

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

            var documentsToRetrieve = new List<string>();

            var response = HttpClient.SendAsync(request).Result;
            response.EnsureSuccessStatusCode();
            var rootFolderJsonStr = response.Content.ReadAsStringAsync().Result;
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
                                                    DateTime.Parse(item["created"]?.ToString() ?? throw new Exception("created could not be retrieved"), CultureInfo.CurrentCulture),
                                                    DateTime.Parse(item["modified"]?.ToString() ?? throw new Exception("modified could not be retrieved"), CultureInfo.CurrentCulture),
                                                    DateTime.Parse(item["modified"]?.ToString() ?? throw new Exception("modified could not be retrieved"), CultureInfo.CurrentCulture));
                                }

                                return newItem;
                            })
                            .Where(item => item != null)
                            .ToList() ?? [];

            //get all documents, because they are effectively folders
            var documentDetails = GetDocuments(documentsToRetrieve, folder);

            result.AddRange(documentDetails);

            return result!;
        }

        public string GetMetadata(string documentId, string securityContext)
        {
            var jsonObj = GetMetadataJSON(documentId, securityContext);

            var result = JsonConvert.SerializeObject(jsonObj, Formatting.Indented);

            return result;
        }


        public JObject GetMetadataJSON(string documentId, string securityContext)
        {
            var metadataUrl = ServerUrl.UrlCombine("resources/v1/collabServices/attributes/op/read");
            var request = new HttpRequestMessage(HttpMethod.Post, metadataUrl);
            request.Headers.Add("SecurityContext", securityContext);

            var reqContentJson = $$"""
                {"lIds":[],"relIDs":[],"busIDs":["{{documentId}}"],"plmparameters":"false","attributes":"true","navigateToMain":"true","readonly":"true","debug_properties":""}
                """;
            request.Content = new StringContent(reqContentJson, Encoding.UTF8, "application/json");

            var response = HttpClient.Send(request);
            response.EnsureSuccessStatusCode();

            var jsonStr = response.Content.ReadAsStringAsync().Result;

            var result = JObject.Parse(jsonStr);
            return result;
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


            var response = HttpClient.Send(request);
            response.EnsureSuccessStatusCode();
            var documentDetailsJsonStr = response.Content.ReadAsStringAsync().Result;

            var dataField = JObject.Parse(documentDetailsJsonStr)?["data"] ?? throw new Exception("data could not be retrieved");

            var documents = dataField
                                .Select(o => JTokenToDocument(o, parent))
                                .ToList();

            return documents;
        }

        public JToken GetDocument(string documentId)
        {
            var getDocumentDetails = ServerUrl.UrlCombine($"resources/v1/modeler/documents/ids");

            var idsCombined = documentId;

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(getDocumentDetails),
                Method = HttpMethod.Post,
                Content = new StringContent($"$ids={idsCombined}", Encoding.UTF8, "application/x-www-form-urlencoded"),
            };


            var response = HttpClient.Send(request);
            response.EnsureSuccessStatusCode();
            var documentDetailsJsonStr = response.Content.ReadAsStringAsync().Result;

            var dataField = JObject.Parse(documentDetailsJsonStr)?["data"] ?? throw new Exception("data could not be retrieved");

            return dataField;
        }


        public List<_3dxDocument> GetAllDocuments(_3dxFolder parent, int queryThreads, EventHandler<ProgressEventArgs>? progress)
        {
            var firstPageStr = GetDocumentPage(0, 1, null);
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
                                try
                                {
                                    string docPageStr;
                                    JToken? resultObj = null;

                                    int attempt;
                                    int maxAttempts = 5;

                                    for (attempt = 1; attempt <= maxAttempts; attempt++)
                                    {
                                        try
                                        {
                                            docPageStr = GetDocumentPage(page, pageSize, searchId);
                                            var docPageJson = JObject.Parse(docPageStr);
                                            resultObj = docPageJson["results"];

                                            if (resultObj != null)
                                            {
                                                break;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Log.WriteLine($"Could not retrieve results for page {page}, during attempt {attempt}. {ex.Message}".Trim());
                                        }

                                        //give a struggling server some room rather than hammering it
                                        if (attempt < maxAttempts)
                                        {
                                            Thread.Sleep(TimeSpan.FromSeconds(2 * attempt));
                                        }
                                    }

                                    if (resultObj == null)
                                    {
                                        var couldNotRetrievePage = $"Could not retrieve results for page {page}. Attempted {maxAttempts} {"time".Pluralize(maxAttempts)}.";
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
                                }
                                catch (Exception ex)
                                {
                                    var pageError = new StringBuilder();
                                    pageError.AppendLine($"Error on page {page:N0}:");

                                    var currentException = ex;

                                    while (currentException != null)
                                    {
                                        pageError.AppendLine($"{currentException.Message.Trim()}");
                                        currentException = currentException.InnerException;
                                    }

                                    Log.WriteLine(pageError.ToString().Trim());

                                    throw;
                                }
                            })
                            .ToList();

            return result!;
        }

        //3DX returns timestamps in a locale-specific format, and the shipped version parsed them with
        //the current culture and was correct. Parse with the current culture first to preserve that,
        //then fall back to invariant culture for ISO-8601 style values. (Parsing invariant-first
        //transposed month and day for ambiguous day-first dates such as "03/07/2018": the US MM/DD
        //interpretation parses successfully and so the current culture was never tried.) A value we
        //still can't parse stays as default(DateTime) and is clamped to a valid range when served.
        static DateTime ParseServerDate(string? value)
        {
            if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var result)
                || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            {
                return result;
            }

            return default;
        }

        //Path segments are limited to 255 characters; leave headroom for the " (n)" suffix that
        //duplicate names receive, and keep the overall UNC path within reach of MAX_PATH
        public const int MaxDocumentNameLength = 120;
        public const int MaxFileNameLength = 150;

        public _3dxDocument? JTokenToDocument(JToken o, _3dxFolder parent)
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

            var created = ParseServerDate(o["dataelements"]?["originated"]?.ToString());
            var modified = ParseServerDate(o["dataelements"]?["modified"]?.ToString());
            var accessed = modified;

            var derivedName = $"{name} Rev {revision}";
            if (!name.Equals(title, StringComparison.CurrentCultureIgnoreCase))
            {
                derivedName += $" ({title})";
            }

            derivedName = FileUtility.ReplaceInvalidChars(derivedName);

            derivedName = derivedName.TrimEnd('.').Trim();
            derivedName = derivedName[..Math.Min(MaxDocumentNameLength, derivedName.Length)];
            derivedName = derivedName.TrimEnd('.').Trim();

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
                var fileObjectId = file["id"]?.ToString() ?? throw new Exception("id could not be retrieved");
                var rawName = file["dataelements"]?["title"]?.ToString() ?? throw new Exception("title could not be retrieved");
                var fileRevision = file["dataelements"]?["revision"]?.ToString() ?? throw new Exception("revision could not be retrieved");

                //the title is whatever the uploader called it; it may contain characters that are
                //invalid in a Windows path, or be longer than a path segment allows
                var name = FileUtility.MakeSafeFileName(rawName, MaxFileNameLength);

                var created = ParseServerDate(file["dataelements"]?["originated"]?.ToString());
                var modified = ParseServerDate(file["dataelements"]?["modified"]?.ToString());
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
            .OfType<_3dxDownloadableFile>()
            .ToList() ?? [];

            if (GenerateDocumentLinkFile)
            {
                _3dxDownloadableFile docLinkFile = new _3dxDocUrlFile(
                                            Guid.NewGuid().ToString(),
                                            $"_link.url",
                                            newDocument,
                                            newDocument.CreationTimeUtc,
                                            newDocument.LastWriteTimeUtc,
                                            newDocument.LastAccessTimeUtc,
                                            newDocument.ObjectId,
                                            ServerUrl);
                files.Add(docLinkFile);
            }

            if (GenerateDocumentMetadataFile)
            {
                _3dxDownloadableFile docMetadataFile = new _3dxDocMetadataFile(
                                            Guid.NewGuid().ToString(),
                                            "_metadata.json",
                                            newDocument,
                                            newDocument.CreationTimeUtc,
                                            newDocument.LastWriteTimeUtc,
                                            newDocument.LastAccessTimeUtc,
                                            newDocument.ObjectId,
                                            this);

                files.Add(docMetadataFile);
            }


            newDocument.Files = files
                                    .OfType<_3dxDownloadableFile>()
                                    .ToList();

            return newDocument;
        }

        public string GetDocumentPage(int pageNumber, int pageSize, string? searchId)
        {
            if (SearchServiceUrl == null)
            {
                //InvalidOperationException: a programming/state error that retrying won't fix
                throw new InvalidOperationException($"Search Service URL not yet retrieved.");
            }

            var searchUrl = SearchServiceUrl.UrlCombine("search?xrequestedwith=xmlhttprequest");

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

            var response = HttpClient.Send(request);
            response.EnsureSuccessStatusCode();
            var searchResultsJsonStr = response.Content.ReadAsStringAsync().Result;

            return searchResultsJsonStr;
        }
    }
}