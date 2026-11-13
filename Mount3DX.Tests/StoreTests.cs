using lib3dx;
using libVFS.WebDAV.Stores;
using Mount3DX.Tests.Fake3dx;
using Newtonsoft.Json.Linq;
using static Mount3DX.Tests.TestTree;

namespace Mount3DX.Tests
{
    //lib3dx and the document tree against the fake 3DX, without a file server in between: the
    //view of the tree that the SMB transport serves
    [TestClass]
    public sealed class StoreTests
    {
        static Fake3dxServer fake = null!;
        static _3dxServer server = null!;
        static _3dxStore store = null!;

        [ClassInitialize]
        public static async Task StartAsync(TestContext context)
        {
            fake = await Fake3dxServer.StartAsync(Documents);
            server = TestSessions.ServerFor(fake);
            store = new _3dxStore(server, "http://localhost:1", queryThreads: 2, maxMetadataSizeInBytes: 0, progress: null);
        }

        [ClassCleanup]
        public static async Task StopAsync()
        {
            if (fake != null)
            {
                await fake.DisposeAsync();
            }
        }

        [TestMethod]
        public void Finding_the_services_pings_3DSpace_and_the_search_service()
        {
            Assert.HasCount(1, fake.RequestsTo("/3dspace/resources/AppsMngt/api/v1/services"));
            Assert.IsTrue(fake.RequestsTo("/3dspace/resources/v1/modeler/documents/ids").Any(request => request.Method == "GET"));
            Assert.IsTrue(fake.RequestsTo("/federated/search").Any(request => request.Method == "GET"));
        }

        [TestMethod]
        public void Every_document_is_fetched_across_the_search_pages()
        {
            var listed = store.List("")!.Select(entry => entry.Name).ToList();

            CollectionAssert.AreEquivalent(FolderNames.Values.ToList(), listed);

            //one search for the total, then the two pages of 100
            Assert.HasCount(3, fake.RequestsTo("/federated/search").Where(request => request.Method == "POST"));
        }

        [TestMethod]
        public void A_document_lists_its_files_and_the_two_Mount3DX_makes()
        {
            var entries = store.List(FolderOf(Pump))!;

            CollectionAssert.AreEquivalent(new[] { "drawing.pdf", "notes.txt", "data.bin", LinkFile, MetadataFile }, entries.Select(entry => entry.Name).ToArray());
            Assert.AreEqual((ulong)Notes.Content.Length, entries.Single(entry => entry.Name == "notes.txt").Size);
        }

        [TestMethod]
        public void Names_are_made_safe_for_Windows_and_duplicates_are_numbered()
        {
            var names = store.List(FolderOf(Odd))!.Select(entry => entry.Name).ToArray();

            CollectionAssert.AreEquivalent(new[] { "spec_.txt", "readme (1).txt", "readme (2).txt", LinkFile, MetadataFile }, names);
        }

        [TestMethod]
        public void A_file_downloads_with_a_CSRF_token_and_a_ticket()
        {
            using (var stream = store.OpenRead(PathOf(Pump, Notes, '/')))
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                CollectionAssert.AreEqual(Notes.Content.ReadAll(), copy.ToArray());
            }

            var ticket = fake.RequestsTo($"/3dspace/resources/v1/modeler/documents/{Pump.Id}/files/{Notes.Id}/DownloadTicket").Single();
            Assert.AreEqual("PUT", ticket.Method);
            Assert.AreEqual(Fake3dxServer.CsrfToken, ticket.CsrfToken);
            Assert.HasCount(1, fake.DownloadsOf(Notes));
        }

        [TestMethod]
        public void Seeking_a_download_asks_3DX_for_the_rest_from_there()
        {
            var at = 50 * MiB + 7;
            var buffer = new byte[4096];

            using (var stream = store.OpenRead(PathOf(Big, Scan, '/')))
            {
                Assert.IsTrue(stream.CanSeek);
                stream.Seek(at, SeekOrigin.Begin);
                stream.ReadExactly(buffer);
            }

            CollectionAssert.AreEqual(Scan.Content.Read(at, buffer.Length), buffer);

            //one download, from where the read was: nothing before it was fetched
            var download = fake.DownloadsOf(Scan).Single();
            Assert.AreEqual(at, download.RangeFrom);
        }

        [TestMethod]
        public void The_metadata_file_is_made_only_for_an_open()
        {
            var path = $"{FolderOf(Pump)}/{MetadataFile}";
            var before = fake.AttributeReads;

            Assert.AreEqual(1UL, store.List(FolderOf(Pump))!.Single(entry => entry.Name == MetadataFile).Size, "the placeholder size in a listing");
            Assert.AreEqual(1UL, store.Stat(path, forOpen: false)!.Size, "the placeholder size for a lookup");
            Assert.AreEqual(before, fake.AttributeReads, "a listing or a lookup asked 3DX for the attributes");

            var opened = store.Stat(path, forOpen: true)!;
            Assert.AreEqual(before + 1, fake.AttributeReads);

            using var stream = store.OpenRead(path);
            var json = JObject.Parse(new StreamReader(stream).ReadToEnd());

            Assert.AreEqual((ulong)stream.Length, opened.Size, "the size an open reports is the content's");
            Assert.AreEqual(before + 1, fake.AttributeReads, "the content made for the open was made again for the read");
            Assert.AreEqual(Pump.Title, TitleIn(json));
            Assert.AreEqual(Pump.Id, json["resources/v1/modeler/documents/ids"]?[0]?["id"]?.ToString());
        }

        [TestMethod]
        public void The_link_file_opens_the_document_in_3DX()
        {
            using var stream = store.OpenRead($"{FolderOf(Pump)}/{LinkFile}");
            var text = new StreamReader(stream).ReadToEnd();

            StringAssert.StartsWith(text, "[InternetShortcut]");
            StringAssert.Contains(text, $"URL={fake.ServerUrl}/common/emxTree.jsp?objectId={Pump.Id}");
        }

        [TestMethod]
        public void A_dotted_id_is_looked_up_in_3DX()
        {
            var identity = server.LookUpDocument(Pump.DottedId!, CancellationToken.None);

            Assert.AreEqual(Pump.Id, identity?.Id);
            Assert.AreEqual(FolderOf(Pump), store.FindDocument(identity!)?.Name);
        }

        [TestMethod]
        public void Missing_paths_are_not_in_the_tree()
        {
            Assert.IsNull(store.Stat("no such document", forOpen: false));
            Assert.IsNull(store.Stat($"{FolderOf(Pump)}/nope.txt", forOpen: true));
            Assert.ThrowsExactly<FileNotFoundException>(() => store.OpenRead($"{FolderOf(Pump)}/nope.txt"));
        }

        //The document's title among the attributes in a _metadata.json
        public static string? TitleIn(JObject metadata) => metadata["resources/v1/collabServices/attributes/op/read"]?["results"]?[0]?["attributes"]?
                                                                .FirstOrDefault(attribute => attribute["name"]?.ToString() == "Title")?["value"]?
                                                                .ToString();
    }
}
