using Mount3DX.Tests.Fake3dx;
using System.Security.Cryptography;

namespace Mount3DX.Tests
{
    //The documents the fake 3DX holds, with the names Mount3DX should give them on the drive.
    //Besides the ordinary ones: names Windows doesn't allow, unicode, the characters a WebDAV URL
    //has to escape, two files with the same name, a document without files, files too large to
    //keep whole (several of the SMB cache's 8 MiB chunks) and enough documents for search to take
    //more than one page.
    public static class TestTree
    {
        public const long MiB = 1024 * 1024;

        public static readonly FakeFile Drawing = new("F0000000000000000000000000000101", "drawing.pdf", FakeContent.Of("%PDF-1.4 a fake drawing of a pump"));
        public static readonly FakeFile Notes = new("F0000000000000000000000000000102", "notes.txt", FakeContent.Of("Notes about the pump.\r\nA second line.\r\n"));
        public static readonly FakeFile Data = new("F0000000000000000000000000000103", "data.bin", FakeContent.Of(Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray()));

        public static readonly FakeDocument Pump = new("4698020700001DA85B7CC007000CF401", "DOC-0001", "A", "Pump assembly", [Drawing, Notes, Data], DottedId: "46123.17123.44970.51524");

        public static readonly FakeFile Overview = new("F0000000000000000000000000000201", "Übersicht.txt", FakeContent.Of("Grüße aus dem Wärmetauscher\r\n"));

        public static readonly FakeDocument HeatExchanger = new("4698020700001DA85B7CC007000CF402", "DOC-0002", "B", "Wärmetauscher – Übersicht", [Overview]);

        public static readonly FakeDocument Bare = new("4698020700001DA85B7CC007000CF403", "DOC-0003", "C", "DOC-0003", []);

        //read in full by the tests
        public static readonly FakeFile Model = new("F0000000000000000000000000000401", "model.bin", FakeContent.Generated(80 * MiB, seed: 1));

        //read only in part, so what is fetched of it shows how a read deep inside a file is served
        public static readonly FakeFile Scan = new("F0000000000000000000000000000402", "scan.bin", FakeContent.Generated(80 * MiB, seed: 2));

        public static readonly FakeDocument Big = new("4698020700001DA85B7CC007000CF404", "DOC-0004", "1", "Big model", [Model, Scan]);

        public static readonly FakeFile Spec = new("F0000000000000000000000000000501", "spec?.txt", FakeContent.Of("the spec"));
        public static readonly FakeFile Readme1 = new("F0000000000000000000000000000502", "readme.txt", FakeContent.Of("the first readme"));
        public static readonly FakeFile Readme2 = new("F0000000000000000000000000000503", "readme.txt", FakeContent.Of("the second readme"));

        public static readonly FakeDocument Odd = new("4698020700001DA85B7CC007000CF405", "DOC-0005", "A", "Specs: v1/v2?", [Spec, Readme1, Readme2]);

        public static readonly FakeFile Offer = new("F0000000000000000000000000000601", "50% off #2 & more.txt", FakeContent.Of("an offer"));

        public static readonly FakeDocument Escapes = new("4698020700001DA85B7CC007000CF406", "DOC-0006", "A", "Q&A #1 at 100%", [Offer]);

        //enough of them that search takes two pages of 100
        public static readonly IReadOnlyList<FakeDocument> Fillers = [.. Enumerable
                                                                            .Range(1, 120)
                                                                            .Select(i => new FakeDocument(
                                                                                            $"4698020700001DA85B7CC0070010{i:X4}",
                                                                                            $"FILL-{i:D4}",
                                                                                            "A",
                                                                                            $"Filler {i}",
                                                                                            [new FakeFile($"F00000000000000000000000000{i:X5}", "filler.txt", FakeContent.Of($"filler {i}"))]))];

        public static readonly IReadOnlyList<FakeDocument> Documents = [Pump, HeatExchanger, Bare, Big, Odd, Escapes, .. Fillers];

        //The folder each document gets: "{name} Rev {revision}", then " ({title})" unless the title is
        //the name, with characters Windows doesn't allow replaced by _
        public static readonly IReadOnlyDictionary<FakeDocument, string> FolderNames = new Dictionary<FakeDocument, string>
        {
            [Pump] = "DOC-0001 Rev A (Pump assembly)",
            [HeatExchanger] = "DOC-0002 Rev B (Wärmetauscher – Übersicht)",
            [Bare] = "DOC-0003 Rev C",
            [Big] = "DOC-0004 Rev 1 (Big model)",
            [Odd] = "DOC-0005 Rev A (Specs_ v1_v2_)",
            [Escapes] = "DOC-0006 Rev A (Q&A #1 at 100%)",
        }
        .Concat(Fillers.Select(filler => KeyValuePair.Create(filler, $"{filler.Name} Rev A ({filler.Title})")))
        .ToDictionary();

        //Every document folder also has these two, made by Mount3DX
        public const string LinkFile = "_link.url";
        public const string MetadataFile = "_metadata.json";

        //The file names in each of the documents with files that the tests look at
        public static readonly IReadOnlyDictionary<FakeFile, string> FileNames = new Dictionary<FakeFile, string>
        {
            [Drawing] = "drawing.pdf",
            [Notes] = "notes.txt",
            [Data] = "data.bin",
            [Overview] = "Übersicht.txt",
            [Model] = "model.bin",
            [Scan] = "scan.bin",
            [Spec] = "spec_.txt",
            [Readme1] = "readme (1).txt",
            [Readme2] = "readme (2).txt",
            [Offer] = "50% off #2 & more.txt",
        };

        public static string FolderOf(FakeDocument document) => FolderNames[document];

        //The path of a file below the drive's root, with the given separator
        public static string PathOf(FakeDocument document, FakeFile file, char separator) => $"{FolderOf(document)}{separator}{FileNames[file]}";

        static readonly Dictionary<FakeFile, string> hashes = [];

        public static string Sha256Of(FakeFile file)
        {
            lock (hashes)
            {
                if (!hashes.TryGetValue(file, out var hash))
                {
                    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[4 * (int)MiB];
                    for (long position = 0; position < file.Content.Length; position += buffer.Length)
                    {
                        var count = (int)Math.Min(buffer.Length, file.Content.Length - position);
                        file.Content.Read(position, buffer.AsSpan(0, count));
                        sha.AppendData(buffer, 0, count);
                    }

                    hash = Convert.ToHexString(sha.GetHashAndReset());
                    hashes[file] = hash;
                }

                return hash;
            }
        }
    }
}
