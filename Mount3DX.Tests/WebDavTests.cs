using Mount3DX.Tests.Fake3dx;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using static Mount3DX.Tests.TestTree;

namespace Mount3DX.Tests
{
    //A session over WebDAV against the fake 3DX: what any WebDAV client gets over HTTP and, when the
    //Windows WebDAV client service is running, what programs get through its redirector
    [TestClass]
    public sealed class WebDavTests
    {
        static Fake3dxServer fake = null!;
        static StartedSession started = null!;
        static HttpClient http = null!;
        static int port;

        [ClassInitialize]
        public static async Task StartAsync(TestContext context)
        {
            fake = await Fake3dxServer.StartAsync(Documents);
            port = TestSessions.FreeTcpPort();

            started = TestSessions.Start(fake, settings =>
            {
                settings.Vfs.Transport = TransportMode.WebDAV;
                settings.Vfs.WebDAV.ServerUrl = $"http://localhost:{port}";
            });

            Assert.IsTrue(started.Finished.Success, started.Finished.Message);
            http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        }

        [ClassCleanup]
        public static async Task StopAsync()
        {
            http?.Dispose();
            started?.Session.Stop();

            if (fake != null)
            {
                await fake.DisposeAsync();
            }
        }

        [TestMethod]
        public void The_session_serves_over_WebDAV_where_its_settings_say()
        {
            Assert.AreEqual(TransportMode.WebDAV, started.Session.ActiveTransport);
            Assert.AreEqual($@"\\localhost@{port}\DavWWWRoot", started.Session.ComputedUNC);
        }

        [TestMethod]
        public async Task The_root_lists_every_document()
        {
            var entries = await PropFindAsync(UrlOf());

            CollectionAssert.AreEquivalent(FolderNames.Values.ToList(), entries.Select(entry => entry.Name).ToList());
            Assert.IsTrue(entries.All(entry => entry.IsCollection));
        }

        [TestMethod]
        public async Task A_document_folder_lists_its_files_with_their_sizes()
        {
            var entries = await PropFindAsync(UrlOf(FolderOf(Pump)));

            CollectionAssert.AreEquivalent(new[] { "drawing.pdf", "notes.txt", "data.bin", LinkFile, MetadataFile }, entries.Select(entry => entry.Name).ToArray());
            Assert.AreEqual(Notes.Content.Length, entries.Single(entry => entry.Name == "notes.txt").Length);
            Assert.AreEqual(1, entries.Single(entry => entry.Name == MetadataFile).Length, "the metadata file's placeholder size");
        }

        [TestMethod]
        public async Task Names_that_URLs_have_to_escape_are_listed_and_served()
        {
            var entries = await PropFindAsync(UrlOf(FolderOf(Escapes)));

            CollectionAssert.Contains(entries.Select(entry => entry.Name).ToList(), FileNames[Offer]);
            CollectionAssert.AreEqual(Offer.Content.ReadAll(), await http.GetByteArrayAsync(UrlOf(FolderOf(Escapes), FileNames[Offer])));
        }

        [TestMethod]
        public async Task Files_read_back_exactly()
        {
            foreach (var (document, file) in new[] { (Pump, Drawing), (Pump, Notes), (Pump, Data), (HeatExchanger, Overview), (Odd, Spec), (Odd, Readme1), (Odd, Readme2) })
            {
                var bytes = await http.GetByteArrayAsync(UrlOf(FolderOf(document), FileNames[file]));
                CollectionAssert.AreEqual(file.Content.ReadAll(), bytes, PathOf(document, file, '/'));
            }
        }

        //The server closes every connection after one request. Its answers must say so, or a client
        //that keeps connections open sends its next request on one that is closing, and has it reset.
        [TestMethod]
        public async Task Every_answer_says_the_connection_closes()
        {
            using var get = await http.GetAsync(UrlOf(FolderOf(Big), "model.bin"), HttpCompletionOption.ResponseHeadersRead);
            using var propfind = new HttpRequestMessage(new HttpMethod("PROPFIND"), UrlOf(FolderOf(Pump)));
            propfind.Headers.Add("Depth", "1");
            using var listing = await http.SendAsync(propfind);

            Assert.IsTrue(get.Headers.ConnectionClose, "GET");
            Assert.IsTrue(listing.Headers.ConnectionClose, "PROPFIND");
        }

        [TestMethod]
        public async Task HEAD_reports_the_length_without_a_download()
        {
            var before = fake.DownloadsOf(Drawing).Count;

            using var response = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, UrlOf(FolderOf(Pump), "drawing.pdf")));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(Drawing.Content.Length, response.Content.Headers.ContentLength);
            Assert.HasCount(before, fake.DownloadsOf(Drawing));
        }

        [TestMethod]
        public async Task A_large_file_reads_back_whole()
        {
            using var response = await http.GetAsync(UrlOf(FolderOf(Big), "model.bin"), HttpCompletionOption.ResponseHeadersRead);

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(Model.Content.Length, response.Content.Headers.ContentLength);

            await using var body = await response.Content.ReadAsStreamAsync();
            Assert.AreEqual(Sha256Of(Model), Convert.ToHexString(await SHA256.HashDataAsync(body)));
        }

        [TestMethod]
        public async Task A_range_is_served_from_a_range_of_the_3DX_download()
        {
            var from = 50 * MiB + 123;
            var to = from + 4095;

            using var request = new HttpRequestMessage(HttpMethod.Get, UrlOf(FolderOf(Big), "scan.bin"));
            request.Headers.Range = new RangeHeaderValue(from, to);
            using var response = await http.SendAsync(request);

            Assert.AreEqual(HttpStatusCode.PartialContent, response.StatusCode);
            CollectionAssert.AreEqual(Scan.Content.Read(from, 4096), await response.Content.ReadAsByteArrayAsync());

            //nothing before the range was fetched from 3DX
            Assert.AreEqual(from, fake.DownloadsOf(Scan).Single().RangeFrom);

            //as RFC 9110 writes it, which strict clients insist on
            Assert.AreEqual($"bytes {from}-{to}/{Scan.Content.Length}", RawHeader(response, "Content-Range"));
        }

        [TestMethod]
        public async Task The_link_file_opens_the_document_in_3DX()
        {
            var text = await http.GetStringAsync(UrlOf(FolderOf(Pump), LinkFile));

            StringAssert.StartsWith(text, "[InternetShortcut]");
            StringAssert.Contains(text, $"URL={fake.ServerUrl}/common/emxTree.jsp?objectId={Pump.Id}");
        }

        [TestMethod]
        public async Task The_metadata_file_is_made_when_it_is_read_not_when_it_is_listed()
        {
            var before = fake.AttributeReads;

            await PropFindAsync(UrlOf(FolderOf(HeatExchanger)));
            Assert.AreEqual(before, fake.AttributeReads, "listing the folder asked 3DX for the attributes");

            var json = JObject.Parse(await http.GetStringAsync(UrlOf(FolderOf(HeatExchanger), MetadataFile)));

            Assert.AreEqual(before + 1, fake.AttributeReads);
            Assert.AreEqual(HeatExchanger.Title, StoreTests.TitleIn(json));
            Assert.AreEqual(HeatExchanger.Id, json["resources/v1/modeler/documents/ids"]?[0]?["id"]?.ToString());
        }

        [TestMethod]
        public async Task Missing_paths_are_not_found()
        {
            using var noDocument = await http.GetAsync(UrlOf("no such document"));
            using var noFile = await http.GetAsync(UrlOf(FolderOf(Pump), "nope.txt"));

            Assert.AreEqual(HttpStatusCode.NotFound, noDocument.StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, noFile.StatusCode);
        }

        [TestMethod]
        public async Task Changes_are_refused()
        {
            using var create = await http.PutAsync(UrlOf(FolderOf(Pump), "new.txt"), new StringContent("new"));
            using var overwrite = await http.PutAsync(UrlOf(FolderOf(Pump), "notes.txt"), new StringContent("changed"));
            using var delete = await http.DeleteAsync(UrlOf(FolderOf(Pump), "notes.txt"));
            using var makeFolder = await http.SendAsync(new HttpRequestMessage(new HttpMethod("MKCOL"), UrlOf("New folder")));

            Assert.AreEqual(HttpStatusCode.Forbidden, create.StatusCode, "PUT of a new file");
            Assert.AreEqual(HttpStatusCode.Forbidden, overwrite.StatusCode, "PUT over a file");
            Assert.AreEqual(HttpStatusCode.Forbidden, delete.StatusCode, "DELETE");
            Assert.AreEqual(HttpStatusCode.Forbidden, makeFolder.StatusCode, "MKCOL");
            CollectionAssert.AreEqual(Notes.Content.ReadAll(), await http.GetByteArrayAsync(UrlOf(FolderOf(Pump), "notes.txt")));
        }

        [TestMethod]
        public void Programs_read_the_drive_through_the_Windows_WebDAV_client()
        {
            if (!TestSessions.WebClientRunning())
            {
                Assert.Inconclusive("The Windows WebDAV client service (WebClient) isn't running. It starts when Explorer opens any WebDAV location, or with \"sc start WebClient\" as an administrator.");
            }

            var root = started.Session.ComputedUNC;
            var folders = TestSessions.NamesIn(root).ToList();

            CollectionAssert.AreEquivalent(FolderNames.Values.ToList(), folders);
            CollectionAssert.AreEquivalent(new[] { "drawing.pdf", "notes.txt", "data.bin", LinkFile, MetadataFile }, TestSessions.NamesIn(Path.Combine(root, FolderOf(Pump))).ToList());
            CollectionAssert.AreEqual(Notes.Content.ReadAll(), File.ReadAllBytes(Path.Combine(root, PathOf(Pump, Notes, '\\'))));
            CollectionAssert.AreEqual(Overview.Content.ReadAll(), File.ReadAllBytes(Path.Combine(root, PathOf(HeatExchanger, Overview, '\\'))));
            CollectionAssert.AreEqual(Offer.Content.ReadAll(), File.ReadAllBytes(Path.Combine(root, PathOf(Escapes, Offer, '\\'))));
            Assert.AreEqual(Pump.Title, StoreTests.TitleIn(JObject.Parse(File.ReadAllText(Path.Combine(root, FolderOf(Pump), MetadataFile)))));
        }

        static string UrlOf(params string[] segments) => WebDav.UrlOf(segments);

        static Task<List<WebDav.Entry>> PropFindAsync(string url) => WebDav.ListAsync(http, url);

        static string? RawHeader(HttpResponseMessage response, string name) =>
            response.Content.Headers.NonValidated.TryGetValues(name, out var values) || response.Headers.NonValidated.TryGetValues(name, out values)
                ? values.ToString()
                : null;
    }
}
