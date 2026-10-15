using libCommon;
using System.Reflection;
using System.Security.Cryptography;

namespace libSMB
{
    //Finds the portable-smb-server executable. Every build embeds one (scripts/fetch-smb-server.ps1
    //puts it where Mount3DX.csproj picks it up); it is extracted into the state folder on first use,
    //named by its hash so a new build never runs an old copy. For development, MOUNT3DX_SMB_SERVER or
    //a copy beside the exe take precedence.
    public static class SmbServerBinary
    {
        public const string EnvironmentVariable = "MOUNT3DX_SMB_SERVER";
        public const string ResourceName = "portable-smb-server";
        public const string FileName = "portable-smb-server.exe";

        //Whether a binary can be found without extracting anything
        public static bool IsAvailable(out string reason)
        {
            if (FromEnvironment() != null || BesideExecutable() != null || HasEmbedded())
            {
                reason = "";
                return true;
            }

            reason = "The SMB server is not bundled in this build (run scripts/fetch-smb-server.ps1 and build again).";
            return false;
        }

        //The path to run, extracting the embedded copy into the state folder when that is what we have
        public static string Locate(string stateDirectory)
        {
            if (FromEnvironment() is { } env)
            {
                Log.WriteLine($"Using the SMB server named by {EnvironmentVariable}: {env}");
                return env;
            }

            if (BesideExecutable() is { } beside)
            {
                Log.WriteLine($"Using the SMB server beside the executable: {beside}");
                return beside;
            }

            using var resource = OpenEmbedded() ?? throw new InvalidOperationException("The SMB server is not bundled in this build (run scripts/fetch-smb-server.ps1 and build again).");
            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);
            var bytes = buffer.ToArray();

            var hash = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();
            var dir = Directory.CreateDirectory(Path.Combine(stateDirectory, "bin")).FullName;
            var path = Path.Combine(dir, $"portable-smb-server-{hash}.exe");

            if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
            {
                //write-then-rename, so a crash mid-way never leaves a half-written exe at the final name
                var temp = path + ".tmp";
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path, overwrite: true);
                Log.WriteLine($"Extracted the bundled SMB server to {path}");
            }

            return path;
        }

        static string? FromEnvironment()
        {
            var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return !string.IsNullOrWhiteSpace(value) && File.Exists(value) ? value : null;
        }

        static string? BesideExecutable()
        {
            var path = Path.Combine(AppContext.BaseDirectory, FileName);
            return File.Exists(path) ? path : null;
        }

        static bool HasEmbedded()
        {
            using var stream = OpenEmbedded();
            return stream != null;
        }

        //The executable carries the resource (only it is published for a runtime); look in this
        //assembly too, so a different host could embed it
        static Stream? OpenEmbedded()
        {
            foreach (var assembly in new[] { Assembly.GetEntryAssembly(), typeof(SmbServerBinary).Assembly })
            {
                if (assembly?.GetManifestResourceStream(ResourceName) is { } stream)
                {
                    return stream;
                }
            }

            return null;
        }
    }
}
