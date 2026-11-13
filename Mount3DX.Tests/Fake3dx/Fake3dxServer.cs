using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Net;

namespace Mount3DX.Tests.Fake3dx
{
    //3DX on this machine, over plain HTTP, answering what Mount3DX asks of a real one:
    //  - the services list (where the search service is) and the two pings
    //  - search, a page at a time
    //  - document details, by physical or dotted id
    //  - the security context and the attributes that go into _metadata.json
    //  - the three-step download: a CSRF token, a download ticket (a PUT carrying the token), then
    //    the file from the file service, with Range
    //The sign-in isn't faked (single sign-on with a smart card, or a browser): a session is started
    //the way one is after a sign-in, with _3dxServer.FindServices. Every request is recorded, so a
    //test can check what was asked of 3DX.
    public sealed class Fake3dxServer : IAsyncDisposable
    {
        public const string CsrfToken = "fake-csrf-token";
        public const string SecurityContext = "Reader.Org.Space";
        const string SearchId = "SEARCH-1";
        const int BlockSize = 256 * 1024;

        readonly WebApplication app;
        readonly ConcurrentQueue<FakeRequest> requests = new();
        readonly Dictionary<string, FakeDocument> documentsById = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, (FakeDocument Document, FakeFile File)> filesById = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<FakeDocument> Documents { get; }

        //http://127.0.0.1:{port}
        public string BaseUrl { get; private set; } = "";

        //What Mount3DX's settings call the server URL
        public string ServerUrl => $"{BaseUrl}/3dspace";

        public string SearchServiceUrl => $"{BaseUrl}/federated";

        public IReadOnlyList<FakeRequest> Requests => [.. requests];

        Fake3dxServer(IReadOnlyList<FakeDocument> documents)
        {
            Documents = documents;

            foreach (var document in documents)
            {
                documentsById.Add(document.Id, document);
                if (document.DottedId != null)
                {
                    documentsById.Add(document.DottedId, document);
                }

                foreach (var file in document.Files)
                {
                    filesById.Add(file.Id, (document, file));
                }
            }

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.AddServerHeader = false;
                kestrel.Listen(IPAddress.Loopback, 0);
            });

            app = builder.Build();
            app.Run(HandleAsync);
        }

        public static async Task<Fake3dxServer> StartAsync(IReadOnlyList<FakeDocument> documents)
        {
            var server = new Fake3dxServer(documents);
            await server.app.StartAsync();

            var address = server.app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
                            ?? throw new InvalidOperationException("Kestrel reported no address for the fake 3DX server.");
            server.BaseUrl = address.TrimEnd('/');

            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }

        //The requests whose path is this one, in any case
        public IReadOnlyList<FakeRequest> RequestsTo(string path) => [.. requests.Where(request => request.Path.Equals(path, StringComparison.OrdinalIgnoreCase))];

        //The file service's answers for a file: one per download (a seek makes another, with Range)
        public IReadOnlyList<FakeRequest> DownloadsOf(FakeFile file) => RequestsTo($"/fcs/{file.Id}");

        //What _metadata.json costs: one attribute read per document whose metadata was made
        public int AttributeReads => RequestsTo("/3dspace/resources/v1/collabServices/attributes/op/read").Count;

        async Task HandleAsync(HttpContext context)
        {
            var request = context.Request;
            var path = request.Path.Value ?? "";
            var (rangeFrom, rangeTo) = RangeOf(request);

            requests.Enqueue(new FakeRequest(
                                    request.Method,
                                    path,
                                    request.QueryString.Value ?? "",
                                    rangeFrom,
                                    rangeTo,
                                    request.Headers["ENO_CSRF_TOKEN"].ToString(),
                                    request.Headers["SecurityContext"].ToString()));

            var get = HttpMethods.IsGet(request.Method);
            var post = HttpMethods.IsPost(request.Method);

            try
            {
                switch (path)
                {
                    case "/3dspace/resources/AppsMngt/api/v1/services" when get:
                        await JsonAsync(context, Services());
                        return;

                    //how a signed-in session answers the pings: the endpoint is there and wants input
                    case "/3dspace/resources/v1/modeler/documents/ids" when get:
                        await TextAsync(context, StatusCodes.Status200OK, "An unexpected error has occurred");
                        return;

                    case "/federated/search" when get:
                        await TextAsync(context, StatusCodes.Status200OK, "FS_INVALID_QUERY__NO_QUERY");
                        return;

                    case "/federated/search" when post:
                        await SearchAsync(context);
                        return;

                    case "/3dspace/resources/v1/modeler/documents/ids" when post:
                        await DocumentsAsync(context);
                        return;

                    case "/3dspace/resources/modeler/pno/person" when get:
                        await JsonAsync(context, JObject.Parse("""{"preferredcredentials":{"role":{"name":"Reader"},"organization":{"name":"Org"},"collabspace":{"name":"Space"}}}"""));
                        return;

                    case "/3dspace/resources/v1/collabServices/attributes/op/read" when post:
                        await AttributesAsync(context);
                        return;

                    case "/3dspace/resources/v1/application/CSRF" when get:
                        await JsonAsync(context, new JObject { ["success"] = true, ["csrf"] = new JObject { ["name"] = "ENO_CSRF_TOKEN", ["value"] = CsrfToken } });
                        return;
                }

                if (HttpMethods.IsPut(request.Method) && TicketFor(path) is { } ticket)
                {
                    await TicketAsync(context, ticket.DocumentId, ticket.FileId);
                    return;
                }

                if (get && path.StartsWith("/fcs/", StringComparison.OrdinalIgnoreCase))
                {
                    await DownloadAsync(context, path["/fcs/".Length..], rangeFrom, rangeTo);
                    return;
                }

                await TextAsync(context, StatusCodes.Status404NotFound, $"The fake 3DX has no {request.Method} {path}");
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException && context.RequestAborted.IsCancellationRequested)
            {
                //the client went away mid-answer (a download it no longer needs)
            }
        }

        JObject Services() => new()
        {
            ["platforms"] = new JArray(new JObject
            {
                ["id"] = "OnPremise",
                ["services"] = new JArray(
                    new JObject { ["id"] = "3dspace", ["name"] = "3DSpace", ["url"] = ServerUrl },
                    new JObject { ["id"] = "3dsearch", ["name"] = "3DSearch", ["url"] = SearchServiceUrl }),
            }),
        };

        //A page of the document list. The first request asks for one result to learn the total and
        //the search id; the rest name their page with next_start ("start 0 page searchId").
        async Task SearchAsync(HttpContext context)
        {
            var query = JObject.Parse(await new StreamReader(context.Request.Body).ReadToEndAsync());
            var count = query["nresults"]?.Value<int>() ?? 10;
            var start = query["start"]?.Value<int>() ?? 0;

            if (query["next_start"]?.ToString() is { } nextStart)
            {
                var parts = nextStart.Split(' ');
                if (parts.Length != 4 || parts[3] != SearchId)
                {
                    await TextAsync(context, StatusCodes.Status400BadRequest, $"bad next_start: {nextStart}");
                    return;
                }

                start = int.Parse(parts[0]);
            }

            var page = Documents.Skip(start).Take(count).ToList();

            await JsonAsync(context, new JObject
            {
                ["infos"] = new JObject
                {
                    ["nmatches"] = Documents.Count,
                    ["nresults"] = page.Count,
                    ["next_start"] = $"{start + page.Count} 0 {start / Math.Max(count, 1) + 1} {SearchId}",
                },
                ["results"] = new JArray(page.Select(document => new JObject
                {
                    ["attributes"] = new JArray(
                        new JObject { ["name"] = "resourceid", ["value"] = document.Id },
                        new JObject { ["name"] = "ds6w:label", ["value"] = document.Title }),
                })),
            });
        }

        //Details of the documents with the given ids ("$ids=a,b,c"); ids that aren't documents are
        //left out, and a dotted id is answered with the document's physical id
        async Task DocumentsAsync(HttpContext context)
        {
            var form = await context.Request.ReadFormAsync();
            var ids = form["$ids"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var data = new JArray();
            foreach (var id in ids)
            {
                if (documentsById.TryGetValue(id, out var document))
                {
                    data.Add(DocumentJson(document));
                }
            }

            await JsonAsync(context, new JObject { ["success"] = true, ["data"] = data });
        }

        static JObject DocumentJson(FakeDocument document) => new()
        {
            ["id"] = document.Id,
            ["type"] = document.Type,
            ["dataelements"] = new JObject
            {
                ["title"] = document.Title,
                ["name"] = document.Name,
                ["revision"] = document.Revision,
                ["typeNLS"] = document.Type,
                ["originated"] = FakeDocument.Originated,
                ["modified"] = FakeDocument.Modified,
            },
            ["relateddata"] = new JObject
            {
                ["files"] = new JArray(document.Files.Select(file => new JObject
                {
                    ["id"] = file.Id,
                    ["dataelements"] = new JObject
                    {
                        ["title"] = file.Title,
                        ["fileSize"] = file.Content.Length.ToString(),
                        ["originated"] = FakeDocument.Originated,
                        ["modified"] = FakeDocument.Modified,
                    },
                })),
            },
        };

        //The attributes behind _metadata.json, which need the security context of the person signed in
        async Task AttributesAsync(HttpContext context)
        {
            if (context.Request.Headers["SecurityContext"] != SecurityContext)
            {
                await TextAsync(context, StatusCodes.Status403Forbidden, "no or wrong SecurityContext");
                return;
            }

            var body = JObject.Parse(await new StreamReader(context.Request.Body).ReadToEndAsync());
            var id = body["busIDs"]?.FirstOrDefault()?.ToString();

            if (id == null || !documentsById.TryGetValue(id, out var document))
            {
                await TextAsync(context, StatusCodes.Status404NotFound, $"no document {id}");
                return;
            }

            static JObject Attribute(string name, string value) => new() { ["name"] = name, ["value"] = value };

            await JsonAsync(context, new JObject
            {
                ["success"] = true,
                ["results"] = new JArray(new JObject
                {
                    ["busID"] = document.Id,
                    ["attributes"] = new JArray(
                        Attribute("Title", document.Title),
                        Attribute("Name", document.Name),
                        Attribute("Revision", document.Revision),
                        Attribute("Description", $"Description of {document.Name}")),
                }),
            });
        }

        static (string DocumentId, string FileId)? TicketFor(string path)
        {
            //  /3dspace/resources/v1/modeler/documents/{document}/files/{file}/DownloadTicket
            var parts = path.Split('/');
            return parts is ["", "3dspace", "resources", "v1", "modeler", "documents", var documentId, "files", var fileId, "DownloadTicket"]
                        ? (documentId, fileId)
                        : null;
        }

        async Task TicketAsync(HttpContext context, string documentId, string fileId)
        {
            if (context.Request.Headers["ENO_CSRF_TOKEN"] != CsrfToken)
            {
                await TextAsync(context, StatusCodes.Status403Forbidden, "no or wrong ENO_CSRF_TOKEN");
                return;
            }

            if (!filesById.TryGetValue(fileId, out var owner) || !owner.Document.Id.Equals(documentId, StringComparison.OrdinalIgnoreCase))
            {
                await TextAsync(context, StatusCodes.Status404NotFound, $"no file {fileId} in document {documentId}");
                return;
            }

            await JsonAsync(context, new JObject
            {
                ["success"] = true,
                ["data"] = new JArray(new JObject
                {
                    ["dataelements"] = new JObject
                    {
                        ["ticketURL"] = $"{BaseUrl}/fcs/{fileId}?ticket={Guid.NewGuid():N}",
                        ["fileName"] = owner.File.Title,
                    },
                }),
            });
        }

        async Task DownloadAsync(HttpContext context, string fileId, long? rangeFrom, long? rangeTo)
        {
            if (!filesById.TryGetValue(fileId, out var owner))
            {
                await TextAsync(context, StatusCodes.Status404NotFound, $"no file {fileId}");
                return;
            }

            var content = owner.File.Content;
            var length = content.Length;
            long start = 0;
            var end = length - 1;
            var response = context.Response;

            if (rangeFrom != null || rangeTo != null)
            {
                if (rangeFrom == null)
                {
                    //the last N bytes
                    start = Math.Max(0, length - rangeTo!.Value);
                }
                else
                {
                    start = rangeFrom.Value;
                    end = Math.Min(rangeTo ?? end, end);
                }

                if (start >= length)
                {
                    response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                    response.Headers.ContentRange = $"bytes */{length}";
                    return;
                }

                response.StatusCode = StatusCodes.Status206PartialContent;
                response.Headers.ContentRange = $"bytes {start}-{end}/{length}";
            }

            response.ContentType = "application/octet-stream";
            response.ContentLength = end - start + 1;

            var buffer = new byte[BlockSize];
            for (var position = start; position <= end;)
            {
                var count = (int)Math.Min(BlockSize, end - position + 1);
                content.Read(position, buffer.AsSpan(0, count));
                await response.Body.WriteAsync(buffer.AsMemory(0, count), context.RequestAborted);
                position += count;
            }
        }

        static (long? From, long? To) RangeOf(HttpRequest request)
        {
            if (!RangeHeaderValue.TryParse(request.Headers.Range.ToString(), out var range) || range.Ranges.Count != 1)
            {
                return (null, null);
            }

            var item = range.Ranges.First();
            return (item.From, item.To);
        }

        static Task JsonAsync(HttpContext context, JToken json)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            return context.Response.WriteAsync(json.ToString());
        }

        static Task TextAsync(HttpContext context, int status, string text)
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "text/plain; charset=utf-8";
            return context.Response.WriteAsync(text);
        }
    }
}
