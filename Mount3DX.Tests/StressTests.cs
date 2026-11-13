using lib3dxVFS.SMB;
using Mount3DX.Tests.Fake3dx;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using static Mount3DX.Tests.TestTree;

namespace Mount3DX.Tests
{
    //48 clients at once: 24 readers, each reading a file of its own (8 MiB) again and again, while
    //24 enumerators each list a folder of their own (the root, with all 150 documents, and 23
    //documents). Every read is checked against the file's hash, every listing against what the
    //folder holds.
    //
    //Over SMB everything goes through the one connection the Windows client keeps, and the
    //enumerators list as fast as they can. The WebDAV server takes one request per connection, and
    //Windows holds the port of each closed connection for minutes, so flat out the enumerators
    //would use up the ports of the whole machine in seconds: over WebDAV they pause between
    //listings and stop at a number.
    [TestClass]
    [TestCategory("Stress")]
    public sealed class StressTests
    {
        const int ReaderCount = 24;
        const int EnumeratorCount = 24;
        static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

        static readonly IReadOnlyList<FakeFile> Parts = [.. Enumerable
                                                                .Range(1, ReaderCount)
                                                                .Select(i => new FakeFile($"F0000000000000000000000000{0x20000 + i:X6}", $"part-{i:D2}.bin", FakeContent.Generated(8 * MiB, seed: 100 + i)))];

        static readonly IReadOnlyList<FakeDocument> PartDocuments = [.. Parts.Select((part, i) => new FakeDocument($"4698020700001DA85B7CC0070020{i + 1:X4}", $"STRESS-{i + 1:D2}", "A", $"Stress {i + 1}", [part]))];

        static string FolderOfPart(int i) => $"STRESS-{i + 1:D2} Rev A (Stress {i + 1})";

        //The enumerators' folders and what each holds
        static readonly IReadOnlyList<(string Folder, string[] Names)> Listings = [
            ("", FolderNames.Values.Concat(Enumerable.Range(0, ReaderCount).Select(FolderOfPart)).ToArray()),
            .. Fillers.Take(EnumeratorCount - 1).Select(filler => (FolderOf(filler), new[] { "filler.txt", LinkFile, MetadataFile })),
        ];

        static Fake3dxServer fake = null!;
        static StartedSession webdav = null!;
        static StartedSession? smb;
        static int webdavPort;
        static string[] partHashes = [];

        public TestContext TestContext { get; set; } = null!;

        [ClassInitialize]
        public static async Task StartAsync(TestContext context)
        {
            fake = await Fake3dxServer.StartAsync([.. Documents, .. PartDocuments]);
            partHashes = [.. Parts.Select(Sha256Of)];

            webdavPort = TestSessions.FreeTcpPort();
            webdav = TestSessions.Start(fake, settings =>
            {
                settings.Vfs.Transport = TransportMode.WebDAV;
                settings.Vfs.WebDAV.ServerUrl = $"http://localhost:{webdavPort}";
            });
            Assert.IsTrue(webdav.Finished.Success, webdav.Finished.Message);

            if (WindowsSmbClient.IsSupported)
            {
                smb = TestSessions.Start(fake, settings => settings.Vfs.Transport = TransportMode.Smb);
                Assert.IsTrue(smb.Finished.Success, smb.Finished.Message);
            }
        }

        [ClassCleanup]
        public static async Task StopAsync()
        {
            smb?.Session.Stop();
            webdav?.Session.Stop();

            if (fake != null)
            {
                await fake.DisposeAsync();
            }
        }

        [TestMethod]
        public void Over_SMB()
        {
            if (smb == null)
            {
                Assert.Inconclusive("This Windows can't connect to an SMB server on a port other than 445 (Windows 11 24H2 or later is needed).");
            }

            const int readsEach = 10;
            var downloadsBefore = PartDownloads();
            var result = RunThroughFileApis(TestSessions.SmbUnc, readsEach, TimeSpan.Zero, int.MaxValue);

            Report("SMB", result, downloadsBefore);
            result.AssertClean(ReaderCount * readsEach);
        }

        [TestMethod]
        public void Over_WebDAV()
        {
            const int readsEach = 4;
            using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{webdavPort}"), Timeout = Timeout };
            var timeWaitBefore = TimeWaitCount();
            var downloadsBefore = PartDownloads();

            var result = Stress.Run(
                            readers: [.. Enumerable.Range(0, ReaderCount).Select(i => (Func<long>)(() =>
                            {
                                using var response = http.Send(new HttpRequestMessage(HttpMethod.Get, WebDav.UrlOf(FolderOfPart(i), Parts[i].Title)), HttpCompletionOption.ResponseHeadersRead);
                                response.EnsureSuccessStatusCode();
                                using var body = response.Content.ReadAsStream();
                                Check(Convert.ToHexString(SHA256.HashData(body)), partHashes[i], Parts[i].Title);
                                return Parts[i].Content.Length;
                            }))],
                            enumerators: [.. Listings.Select(listing => (Action)(() =>
                                Check(WebDav.List(http, WebDav.UrlOf(listing.Folder.Length == 0 ? [] : [listing.Folder])).Select(entry => entry.Name), listing.Names, listing.Folder)))],
                            readsEach,
                            listingPause: TimeSpan.FromMilliseconds(100),
                            listingsEachAtMost: 60,
                            Timeout);

            Report("WebDAV over HTTP", result, downloadsBefore);
            TestContext.WriteLine($"  connections in TIME_WAIT: {timeWaitBefore:N0} before, {TimeWaitCount():N0} after");
            result.AssertClean(ReaderCount * readsEach);
        }

        [TestMethod]
        public void Over_WebDAV_through_the_Windows_WebDAV_client()
        {
            if (!TestSessions.WebClientRunning())
            {
                Assert.Inconclusive("The Windows WebDAV client service (WebClient) isn't running. It starts when Explorer opens any WebDAV location, or with \"sc start WebClient\" as an administrator.");
            }

            const int readsEach = 4;
            var timeWaitBefore = TimeWaitCount();
            var downloadsBefore = PartDownloads();
            var result = RunThroughFileApis(webdav.Session.ComputedUNC, readsEach, TimeSpan.FromMilliseconds(100), 60);

            Report("WebDAV through the Windows WebDAV client", result, downloadsBefore);
            TestContext.WriteLine($"  connections in TIME_WAIT: {timeWaitBefore:N0} before, {TimeWaitCount():N0} after");
            result.AssertClean(ReaderCount * readsEach);
        }

        //Readers and enumerators through the file APIs, as programs use the drive
        StressResult RunThroughFileApis(string root, int readsEach, TimeSpan listingPause, int listingsEachAtMost) => Stress.Run(
            readers: [.. Enumerable.Range(0, ReaderCount).Select(i => (Func<long>)(() =>
            {
                using var stream = new FileStream(Path.Combine(root, FolderOfPart(i), Parts[i].Title), FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
                Check(Convert.ToHexString(SHA256.HashData(stream)), partHashes[i], Parts[i].Title);
                return stream.Length;
            }))],
            enumerators: [.. Listings.Select(listing => (Action)(() =>
                Check(TestSessions.NamesIn(Path.Combine(root, listing.Folder)), listing.Names, listing.Folder)))],
            readsEach,
            listingPause,
            listingsEachAtMost,
            Timeout);

        void Report(string what, StressResult result, int downloadsBefore)
        {
            TestContext.WriteLine(result.Summary(what));
            TestContext.WriteLine($"  3DX downloads of the readers' files: {PartDownloads() - downloadsBefore:N0}");
        }

        //Downloads from 3DX of the readers' files so far; a run reports its own
        static int PartDownloads() => Parts.Sum(part => fake.DownloadsOf(part).Count);

        static void Check(string hash, string expected, string file)
        {
            if (hash != expected)
            {
                throw new InvalidDataException($"{file} read back different bytes");
            }
        }

        static void Check(IEnumerable<string?> names, string[] expected, string folder)
        {
            var listed = names.Order(StringComparer.Ordinal).ToArray();
            var wanted = expected.Order(StringComparer.Ordinal).ToArray();

            if (!listed.SequenceEqual(wanted, StringComparer.Ordinal))
            {
                var missing = wanted.Except(listed).FirstOrDefault();
                var extra = listed.Except(wanted).FirstOrDefault();
                throw new InvalidDataException($"'{folder}' listed {listed.Length} entries, not {wanted.Length} (missing: {missing ?? "none"}, extra: {extra ?? "none"})");
            }
        }

        static int TimeWaitCount() => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Count(connection => connection.State == TcpState.TimeWait);
    }
}
