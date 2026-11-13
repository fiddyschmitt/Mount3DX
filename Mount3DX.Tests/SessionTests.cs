using lib3dxVFS.SMB;
using Mount3DX.Tests.Fake3dx;
using System.Net;
using System.Net.Sockets;
using static Mount3DX.Tests.TestTree;

namespace Mount3DX.Tests
{
    //Starting and stopping sessions against the fake 3DX: what a stop leaves behind, and which
    //transport is used when SMB can't start
    [TestClass]
    public sealed class SessionTests
    {
        static Fake3dxServer fake = null!;

        [ClassInitialize]
        public static async Task StartAsync(TestContext context)
        {
            fake = await Fake3dxServer.StartAsync(Documents);
        }

        [ClassCleanup]
        public static async Task StopAsync()
        {
            if (fake != null)
            {
                await fake.DisposeAsync();
            }
        }

        static void SkipUnlessSmbWorksHere()
        {
            if (!WindowsSmbClient.IsSupported)
            {
                Assert.Inconclusive("This Windows can't connect to an SMB server on a port other than 445 (Windows 11 24H2 or later is needed).");
            }
        }

        [TestMethod]
        public void Stopping_a_WebDAV_session_closes_its_port()
        {
            var port = TestSessions.FreeTcpPort();
            var started = TestSessions.Start(fake, settings =>
            {
                settings.Vfs.Transport = TransportMode.WebDAV;
                settings.Vfs.WebDAV.ServerUrl = $"http://localhost:{port}";
            });

            Assert.IsTrue(started.Finished.Success, started.Finished.Message);
            Assert.IsTrue(started.Session.IsServing);
            Assert.IsTrue(TestSessions.PortAnswers(IPAddress.Loopback, port));

            started.Session.Stop();

            Assert.IsFalse(started.Session.IsServing);
            Assert.IsFalse(TestSessions.PortAnswers(IPAddress.Loopback, port), "the WebDAV port still answers");
        }

        [TestMethod]
        public void Stopping_an_SMB_session_disconnects_the_share_and_removes_the_cache()
        {
            SkipUnlessSmbWorksHere();

            var started = TestSessions.Start(fake, settings => settings.Vfs.Transport = TransportMode.Smb);
            var cache = Path.Combine(started.StateDirectory, "cache");

            try
            {
                Assert.IsTrue(started.Finished.Success, started.Finished.Message);
                Assert.AreEqual(TransportMode.Smb, started.Session.ActiveTransport);

                //something in the cache to be removed
                CollectionAssert.AreEqual(Notes.Content.ReadAll(), File.ReadAllBytes(Path.Combine(TestSessions.SmbUnc, PathOf(Pump, Notes, '\\'))));
                Assert.IsTrue(Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories).Any(), "nothing was cached");
            }
            finally
            {
                started.Session.Stop();
            }

            Assert.DoesNotContain(TestSessions.SmbUnc, TestSessions.NetUse(), StringComparison.OrdinalIgnoreCase, "the share is still connected");
            Assert.IsFalse(TestSessions.PortAnswers(TestSessions.SmbAddress, TestSessions.SmbPort), "the SMB port still answers");
            Assert.IsFalse(Directory.Exists(cache) && Directory.EnumerateFileSystemEntries(cache).Any(), "the cache is still there");
        }

        [TestMethod]
        public void Auto_falls_back_to_WebDAV_when_SMB_cannot_start()
        {
            SkipUnlessSmbWorksHere();

            //something else holds the SMB port
            using var holder = new TcpListener(TestSessions.SmbAddress, TestSessions.SmbPort);
            holder.Start();

            var port = TestSessions.FreeTcpPort();
            var started = TestSessions.Start(fake, settings =>
            {
                settings.Vfs.Transport = TransportMode.Auto;
                settings.Vfs.WebDAV.ServerUrl = $"http://localhost:{port}";
            });

            try
            {
                Assert.IsTrue(started.Finished.Success, started.Finished.Message);
                Assert.AreEqual(TransportMode.WebDAV, started.Session.ActiveTransport);
                Assert.IsTrue(TestSessions.PortAnswers(IPAddress.Loopback, port), "the WebDAV server isn't listening");
            }
            finally
            {
                started.Session.Stop();
            }
        }

        [TestMethod]
        public void SMB_asked_for_outright_fails_rather_than_falling_back()
        {
            SkipUnlessSmbWorksHere();

            using var holder = new TcpListener(TestSessions.SmbAddress, TestSessions.SmbPort);
            holder.Start();

            var started = TestSessions.Start(fake, settings => settings.Vfs.Transport = TransportMode.Smb);

            Assert.IsFalse(started.Finished.Success);
            StringAssert.Contains(started.Finished.Message, "SMB is not available");
            Assert.IsFalse(started.Session.IsServing);
        }
    }
}
