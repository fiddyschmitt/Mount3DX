using lib3dxVFS.SMB;
using Mount3DX.Tests.Fake3dx;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Security.Cryptography;
using static Mount3DX.Tests.TestTree;

namespace Mount3DX.Tests
{
    //A session over SMB against the fake 3DX, read through the Windows SMB client the way any
    //program reads the drive. Needs Windows 11 24H2 or later (an SMB server on a port other than 445).
    [TestClass]
    public sealed class SmbTests
    {
        static Fake3dxServer fake = null!;
        static StartedSession started = null!;

        static string Root => TestSessions.SmbUnc;

        static string PathTo(FakeDocument document, FakeFile file) => Path.Combine(Root, PathOf(document, file, '\\'));

        public TestContext TestContext { get; set; } = null!;

        [ClassInitialize]
        public static async Task StartAsync(TestContext context)
        {
            if (!WindowsSmbClient.IsSupported)
            {
                Assert.Inconclusive("This Windows can't connect to an SMB server on a port other than 445 (Windows 11 24H2 or later is needed).");
            }

            fake = await Fake3dxServer.StartAsync(Documents);
            started = TestSessions.Start(fake, settings => settings.Vfs.Transport = TransportMode.Smb);

            Assert.IsTrue(started.Finished.Success, started.Finished.Message);
        }

        [ClassCleanup]
        public static async Task StopAsync()
        {
            started?.Session.Stop();

            if (fake != null)
            {
                await fake.DisposeAsync();
            }
        }

        [TestMethod]
        public void The_session_serves_over_SMB_where_its_settings_say()
        {
            Assert.AreEqual(TransportMode.Smb, started.Session.ActiveTransport);
            Assert.AreEqual(Root, started.Session.ComputedUNC);
        }

        [TestMethod]
        public void The_root_lists_every_document()
        {
            var folders = Directory.GetDirectories(Root).Select(Path.GetFileName).ToList();

            CollectionAssert.AreEquivalent(FolderNames.Values.ToList(), folders);
        }

        [TestMethod]
        public void A_document_folder_lists_its_files_with_their_sizes()
        {
            var files = new DirectoryInfo(Path.Combine(Root, FolderOf(Pump))).GetFiles();

            CollectionAssert.AreEquivalent(new[] { "drawing.pdf", "notes.txt", "data.bin", LinkFile, MetadataFile }, files.Select(file => file.Name).ToArray());
            Assert.AreEqual(Notes.Content.Length, files.Single(file => file.Name == "notes.txt").Length);
            Assert.AreEqual(1, files.Single(file => file.Name == MetadataFile).Length, "the metadata file's placeholder size");
            Assert.IsTrue(files.All(file => !file.Attributes.HasFlag(FileAttributes.ReadOnly)), "files are shown read-only (copies of them would be too)");
        }

        [TestMethod]
        public void Files_read_back_exactly()
        {
            foreach (var (document, file) in new[] { (Pump, Drawing), (Pump, Notes), (Pump, Data), (HeatExchanger, Overview), (Odd, Spec), (Odd, Readme1), (Odd, Readme2), (Escapes, Offer) })
            {
                CollectionAssert.AreEqual(file.Content.ReadAll(), File.ReadAllBytes(PathTo(document, file)), PathOf(document, file, '\\'));
            }
        }

        [TestMethod]
        public void Names_are_found_in_any_case()
        {
            Assert.IsTrue(File.Exists(Path.Combine(Root, FolderOf(Pump).ToUpperInvariant(), "NOTES.TXT")));
        }

        [TestMethod]
        public void Missing_files_and_folders_are_missing()
        {
            Assert.IsFalse(File.Exists(Path.Combine(Root, FolderOf(Pump), "nope.txt")));
            Assert.IsFalse(Directory.Exists(Path.Combine(Root, "no such document")));
        }

        [TestMethod]
        public void The_metadata_file_is_made_only_when_it_is_opened()
        {
            var path = Path.Combine(Root, FolderOf(HeatExchanger), MetadataFile);
            var before = fake.AttributeReads;

            Assert.HasCount(1, Directory.GetFiles(Path.Combine(Root, FolderOf(HeatExchanger)), MetadataFile));
            Assert.AreEqual(1, new FileInfo(path).Length, "the placeholder size for a look at the file");
            Assert.AreEqual(before, fake.AttributeReads, "listing or looking at the file asked 3DX for the attributes");

            var json = JObject.Parse(File.ReadAllText(path));

            Assert.AreEqual(before + 1, fake.AttributeReads);
            Assert.AreEqual(HeatExchanger.Title, StoreTests.TitleIn(json));
            Assert.AreEqual(HeatExchanger.Id, json["resources/v1/modeler/documents/ids"]?[0]?["id"]?.ToString());
        }

        [TestMethod]
        public void The_link_file_opens_the_document_in_3DX()
        {
            var text = File.ReadAllText(Path.Combine(Root, FolderOf(Pump), LinkFile));

            StringAssert.StartsWith(text, "[InternetShortcut]");
            StringAssert.Contains(text, $"URL={fake.ServerUrl}/common/emxTree.jsp?objectId={Pump.Id}");
        }

        [TestMethod]
        public void A_large_file_reads_back_whole_from_few_downloads()
        {
            string hash;
            using (var stream = new FileStream(PathTo(Big, Model), FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
            {
                hash = Convert.ToHexString(SHA256.HashData(stream));
            }

            Assert.AreEqual(Sha256Of(Model), hash);

            //Read in order, the file comes from one download. As a file is opened something else
            //reads its end too (seen here: a download of the last chunk; Defender, which scans files
            //on network shares, is the likely reader), which costs a second one. A download per
            //read would show as dozens.
            var downloads = fake.DownloadsOf(Model);
            TestContext.WriteLine($"downloads of {FileNames[Model]}: {string.Join(", ", downloads.Select(d => d.RangeFrom?.ToString() ?? "start"))}");
            Assert.IsLessThanOrEqualTo(upperBound: 2, value: downloads.Count, "downloads of a file read in order");
        }

        //A machine's programs all share its one connection to the share, so a read waiting on 3DX (a
        //fetch of something not cached) must hold up nothing else there. LibSmb 0.1.0 worked on a
        //connection's requests one at a time, and a listing behind such a read waited for it.
        [TestMethod]
        public void A_read_waiting_on_3DX_holds_up_nothing_else_on_the_share()
        {
            fake.DownloadDelay = TimeSpan.FromSeconds(3);

            try
            {
                var read = Task.Run(() => File.ReadAllBytes(PathTo(Late, Slow)));

                var waiting = Stopwatch.StartNew();
                while (fake.DownloadsOf(Slow).Count == 0 && !read.IsCompleted && waiting.Elapsed < TimeSpan.FromSeconds(10))
                {
                    Thread.Sleep(10);
                }

                Assert.IsNotEmpty(fake.DownloadsOf(Slow), "the read never asked 3DX for the file");

                //a folder nothing else lists, so Windows' directory cache can't answer for the server
                var listing = Stopwatch.StartNew();
                var names = TestSessions.NamesIn(Path.Combine(Root, FolderOf(Fillers[99])));
                listing.Stop();
                TestContext.WriteLine($"a listing while a read waited on 3DX: {listing.Elapsed.TotalMilliseconds:N0} ms");

                Assert.IsFalse(read.IsCompleted, "the read didn't wait on 3DX, so the listing proves nothing");
                CollectionAssert.AreEquivalent(new[] { "filler.txt", LinkFile, MetadataFile }, names.ToList());
                Assert.IsLessThan(upperBound: TimeSpan.FromSeconds(1), value: listing.Elapsed, $"the listing took {listing.Elapsed.TotalSeconds:N1} s while a read waited on 3DX");
                CollectionAssert.AreEqual(Slow.Content.ReadAll(), read.Result);
            }
            finally
            {
                fake.DownloadDelay = TimeSpan.Zero;
            }
        }

        [TestMethod]
        public void A_large_file_copies_byte_for_byte()
        {
            var copy = Path.Combine(Path.GetTempPath(), "Mount3DX.Tests", $"copy-{Guid.NewGuid():N}.bin");

            try
            {
                File.Copy(PathTo(Big, Model), copy);

                using var stream = File.OpenRead(copy);
                Assert.AreEqual(Sha256Of(Model), Convert.ToHexString(SHA256.HashData(stream)));
                Assert.IsFalse(File.GetAttributes(copy).HasFlag(FileAttributes.ReadOnly), "the copy is read-only");
            }
            finally
            {
                File.Delete(copy);
            }
        }

        [TestMethod]
        public void A_read_deep_inside_a_large_file_asks_3DX_for_that_part()
        {
            var at = 70 * MiB + 5;
            var buffer = new byte[64 * 1024];

            using (var stream = File.OpenRead(PathTo(Big, Scan)))
            {
                stream.Seek(at, SeekOrigin.Begin);
                stream.ReadExactly(buffer);
            }

            CollectionAssert.AreEqual(Scan.Content.Read(at, buffer.Length), buffer);

            //the cache fetches whole 8 MiB chunks: this one starts at 64 MiB
            var downloads = fake.DownloadsOf(Scan);
            TestContext.WriteLine($"downloads of {FileNames[Scan]}: {string.Join(", ", downloads.Select(d => d.RangeFrom?.ToString() ?? "start"))}");
            Assert.IsTrue(downloads.Any(download => download.RangeFrom == 64 * MiB), "3DX wasn't asked for the part that was read");
        }

        [TestMethod]
        public void Changes_are_refused()
        {
            var folder = Path.Combine(Root, FolderOf(Pump));
            var notes = Path.Combine(folder, "notes.txt");

            Assert.Throws<IOException>(() => File.WriteAllText(Path.Combine(folder, "new.txt"), "new"), "creating a file");
            Assert.Throws<IOException>(() => File.Delete(notes), "deleting a file");
            Assert.Throws<IOException>(() => Directory.CreateDirectory(Path.Combine(Root, "New folder")), "making a folder");
            Assert.Throws<IOException>(() => File.Move(notes, Path.Combine(folder, "renamed.txt")), "renaming a file");
            Assert.Throws<IOException>(() =>
            {
                using var stream = new FileStream(notes, FileMode.Open, FileAccess.ReadWrite);
                stream.Write("changed"u8);
            }, "writing to a file");

            CollectionAssert.AreEqual(Notes.Content.ReadAll(), File.ReadAllBytes(notes));
            Assert.IsFalse(File.Exists(Path.Combine(folder, "new.txt")));
        }
    }
}
