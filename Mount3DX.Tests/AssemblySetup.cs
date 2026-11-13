using libCommon;

//The tests share the fake 3DX's ports, the SMB share and Mount3DX's static log
[assembly: DoNotParallelize]

namespace Mount3DX.Tests
{
    [TestClass]
    public sealed class AssemblySetup
    {
        [AssemblyInitialize]
        public static void Initialize(TestContext context)
        {
            //Mount3DX's own log, to look into when a test fails
            var folder = Path.Combine(Path.GetTempPath(), "Mount3DX.Tests");
            Directory.CreateDirectory(folder);
            Log.Filename = Path.Combine(folder, "mount3dx.log");
            File.Delete(Log.Filename);
        }
    }
}
