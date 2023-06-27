using NWebDav.Server.Http;
using NWebDav.Server.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using lib3dx;
using NWebDav.Server.Helpers;
using NWebDav.Server.Locking;
using System.IO;
using lib3dxVFS.WebDAV.Stores;
using libCommon;
using System.Xml.Linq;
using libCommon.Utilities;
using System.Collections.Concurrent;
using System.Diagnostics;
using libVFS.VFS.Folders;
using libCommon.Events;
using System.Web;

namespace libVFS.WebDAV.Stores
{
    public class _3dxStore : IStore
    {
        readonly _3dxServer _3dxServer;

        readonly ILockingManager LockingManager = new InMemoryLockingManager();

        readonly _3dxFolder? rootFolder;
        readonly Dictionary<string, _3dxStoreCollection> pathToCollectionMapping = new();
        readonly Dictionary<string, _3dxStoreItem> pathToItemMapping = new();

        public _3dxStore(string serverUrl, string cookies, bool keepAlive, int keepAliveIntervalMinutes, int queryThreads, EventHandler<ProgressEventArgs>? progress)
        {
            _3dxServer = new _3dxServer(serverUrl, cookies, keepAlive, keepAliveIntervalMinutes);

            progress?.Invoke(this, new ProgressEventArgs()
            {
                Message = $"Querying 3DX for documents",
                Nature = ProgressEventArgs.EnumNature.Neutral
            });

            rootFolder = new _3dxFolder(
                                "root",
                                "",
                                null,
                                DateTime.Now,
                                DateTime.Now,
                                DateTime.Now);

            var allDocuments = _3dxServer
                                    .GetAllDocuments(rootFolder, serverUrl, cookies, queryThreads, progress);

            //var abc = allDocuments.SerializeToJson();
            //File.WriteAllText(@$"C:\Users\rx831f\Desktop\Temp\2023-06-24\{take}.txt", abc);

            rootFolder.Subfolders = allDocuments
                                        .Cast<_3dxFolder>()
                                        .ToList();


            //create folders for each document name, containing subfolders for revisions
            /*
            rootFolder.Subfolders = allDocuments
                                        .GroupBy(
                                            doc => doc.OriginalName,
                                            doc => doc,
                                            (documentName, grp) =>
                                            {
                                                var docFolder = new _3dxFolder()
                                                {
                                                    Name = documentName,
                                                    ObjectId = Guid.NewGuid().ToString(),
                                                    Parent = rootFolder,
                                                    Subfolders = grp
                                                                    .Cast<_3dxFolder>()
                                                                    .ToList(),
                                                    Revision = ""
                                                };

                                                docFolder
                                                    .Subfolders
                                                    .ForEach(folder => folder.Parent = docFolder);

                                                return docFolder;
                                            })
                                        .ToList();
            */

            //Windows has a limit of 260 character paths. When Url Encoding is taken into account, this is much smaller.
            //Let's truncate names here so they don't cause errors for Windows.
            //This didn't help the affected file...
            /*
            new[] { rootFolder }
                    .Recurse(folder => folder.Subfolders)
                    .SelectMany(folder =>
                    {
                        var items = new List<_3dxItem>()
                        {
                            folder
                        };

                        if (folder is _3dxDocument doc)
                        {
                            var files = doc.Files.Cast<_3dxItem>();
                            items.AddRange(files);
                        }

                        return items;
                    })
                    .ToList()
                    .ForEach(item =>
                    {
                        if (HttpUtility.UrlEncode(item.FullPath).Length >= 200)
                        {
                            if (item is _3dxFile file)
                            {
                                //file.Name = file.Name.TruncateFilename(100);
                            }
                            else
                            {
                                item.Name = item.Name.Truncate(100);
                            }
                        }
                    });
            */


            //some documents have identical names. Give each an index number
            var duplicateDocuments = new[] { rootFolder }
                                           .Recurse(folder => folder.Subfolders)
                                           .OfType<_3dxDocument>()
                                           .GroupBy(
                                               folder => folder.FullPath.ToLower(),
                                               folder => folder,
                                               (key, grp) => new
                                               {
                                                   FullPath = key,
                                                   Documents = grp.ToList()
                                               })
                                           .Where(grp => grp.Documents.Count > 1)
                                           .ToList();

            duplicateDocuments
                .ForEach(grp =>
                    {
                        var i = 1;
                        foreach (var document in grp.Documents)
                        {
                            document.Name += $" ({i}) ({document.DocumentType})";
                            i++;
                        }
                    });

            //some files have identical names. Make them unique by adding the rev number
            var documentsWithDuplicateFiles = new[] { rootFolder }
                                .Recurse(folder => folder.Subfolders)
                                .OfType<_3dxDocument>()
                                .Select(document => new
                                {
                                    Document = document,
                                    DuplicateGroups = document
                                                        .Files
                                                        .GroupBy(
                                                            file => file.FullPath.ToLower(),
                                                            file => file,
                                                            (key, grp) => new
                                                            {
                                                                FullPath = key,
                                                                Files = grp.ToList()
                                                            })
                                                        .Where(grp => grp.Files.Count > 1)
                                })
                                .Where(document => document.DuplicateGroups.Any())
                                .ToList();

            documentsWithDuplicateFiles
                .ForEach(doc =>
                {
                    foreach (var duplicateGroup in doc.DuplicateGroups)
                    {
                        var i = 1;
                        foreach (var file in duplicateGroup.Files)
                        {
                            file.Name = $"{Path.GetFileNameWithoutExtension(file.Name)} ({i}){Path.GetExtension(file.Name)}";
                            i++;
                        }
                    }
                });

            pathToCollectionMapping = new[] { rootFolder }
                                        .Recurse(folder => folder.Subfolders)
                                        .Select(folder => new _3dxStoreCollection(LockingManager, folder, serverUrl, cookies))
                                        .ToDictionary(folder => folder.FullPath, folder => folder);

            pathToItemMapping = new[] { rootFolder }
                                        .Recurse(folder => folder.Subfolders)
                                        .OfType<_3dxDocument>()
                                        .SelectMany(document => document.Files)
                                        .Select(file => new _3dxStoreItem(LockingManager, file, false, serverUrl, cookies))
                                        .ToDictionary(folder => folder.FullPath, folder => folder);

            progress?.Invoke(this, new ProgressEventArgs()
            {
                Message = $"Found {allDocuments:N0} documents",
                Nature = ProgressEventArgs.EnumNature.Neutral
            });
        }

        //Whilst the following approach is good in the sense that it uses a proper API call to get folder contents, it doesn't return all revision of a document. It returns the revisions the user added to that folder.
        /*
        public _3dxStore(string serverUrl, string cookies, bool keepAlive, int keepAliveIntervalMinutes, int queryThreads)
        {
            rootFolder = new _3dxFolder()
            {
                Name = "",
                Subfolders = _3dxServer.GetRootFolders()
            };

            var securityContext = _3dxServer.GetSecurityContext();

            var folderQueue = new ConcurrentQueue<_3dxFolder>();
            rootFolder.Subfolders.ForEach(folder => folderQueue.Enqueue(folder));

            int totalFolders = 0;
            int totalDocs = 0;
            int totalFiles = 0;

            var recurseTask = QueueUtility
                    .Process(folderQueue, folder =>
                    {
                        //folder.Name = folder.Name + " " + folder.ObjectId;

                        if (folder is _3dxDocument) return new List<_3dxFolder>();

                        //if (totalFolders >= 100) return new List<_3dxFolder>();

                        var itemsInFolder = _3dxServer.GetItemsInFolder(folder, securityContext);

                        var documents = itemsInFolder
                                            .OfType<_3dxDocument>()
                                            .ToList();

                        var subfolders = itemsInFolder
                                            .Except(documents)
                                            .OfType<_3dxFolder>()
                                            .ToList();

                        var files = documents
                                        .Sum(doc => doc.Files.Count);

                        folder.Subfolders = itemsInFolder
                                                .OfType<_3dxFolder>()
                                                .ToList();

                        Interlocked.Add(ref totalFolders, folder.Subfolders.Count);
                        Interlocked.Add(ref totalDocs, documents.Count);
                        Interlocked.Add(ref totalFiles, files);

                        //Debug.WriteLine($"{folder.FullPath}\tSubfolders: {folder.Subfolders.Count:N0}\tDocs: {documents.Count:N0}");
                        Debug.WriteLine($"Total folders: {totalFolders:N0}\tTotal docs: {totalDocs:N0}\tTotal files: {totalFiles:N0}");

                        return folder.Subfolders;

                    }, queryThreads, new CancellationToken());
            recurseTask.Wait();

            //temporarily use flat hierarchy
            //rootFolder.Subfolders = rootFolder
            //                                .Subfolders
            //                                .Recurse(folder => folder.Subfolders)
            //                                .OfType<_3dxDocument>()
            //                                .Select(doc =>
            //                                {
            //                                    doc.Parent = rootFolder;
            //                                    return doc;
            //                                })
            //                                .Cast<_3dxFolder>()
            //                                .ToList();

            var docsWithMultipleVersion = rootFolder
                                            .Subfolders
                                            .Recurse(folder => folder.Subfolders)
                                            .OfType<_3dxDocument>()
                                            .GroupBy(doc => doc.OriginalName, doc => doc, (k, g) => new { Name = k, Docs = g.ToList() })
                                            .Select(grp => new
                                            {
                                                Doc = grp.Name,
                                                DistinctRevisions = grp
                                                                    .Docs
                                                                    .GroupBy(doc => doc.Revision, doc => doc, (k, g) => new { Revision = k, Docs = g.ToList() })
                                                                    .ToList()
                                            })
                                            .Where(grp => grp.DistinctRevisions.Count() > 1)
                                            .ToList();


            //some documents have identical names. Give each an index number
            var duplicateDocuments = new[] { rootFolder }
                                        .Recurse(folder => folder.Subfolders)
                                        .OfType<_3dxDocument>()
                                        .GroupBy(
                                            folder => folder.FullPath.ToLower(),
                                            folder => folder,
                                            (key, grp) => new
                                            {
                                                FullPath = key,
                                                Documents = grp.ToList()
                                            })
                                        .Where(grp => grp.Documents.Count() > 1)
                                        .ToList();

            duplicateDocuments
                .ForEach(grp =>
                {
                    var i = 1;
                    foreach (var document in grp.Documents)
                    {
                        document.Name += $" ({i}) ({document.DocumentType})";
                        i++;
                    }
                });

            //some files have identical names. Make them unique by adding the rev number
            var documentsWithDuplicateFiles = new[] { rootFolder }
                                .Recurse(folder => folder.Subfolders)
                                .OfType<_3dxDocument>()
                                .Select(document => new
                                {
                                    Document = document,
                                    DuplicateGroups = document
                                                        .Files
                                                        .GroupBy(
                                                            file => file.FullPath.ToLower(),
                                                            file => file,
                                                            (key, grp) => new
                                                            {
                                                                FullPath = key,
                                                                Files = grp.ToList()
                                                            })
                                                        .Where(grp => grp.Files.Count() > 1)
                                })
                                .Where(document => document.DuplicateGroups.Count() > 0)
                                .ToList();

            documentsWithDuplicateFiles
                .ForEach(doc =>
                {
                    foreach (var duplicateGroup in doc.DuplicateGroups)
                    {
                        var i = 1;
                        foreach (var file in duplicateGroup.Files)
                        {
                            file.Name = $"{Path.GetFileNameWithoutExtension(file.Name)} ({i}){Path.GetExtension(file.Name)}";
                            i++;
                        }
                    }
                });

            pathToCollectionMapping = new[] { rootFolder }
                                        .Recurse(folder => folder.Subfolders)
                                        .Select(folder => new _3dxStoreCollection(LockingManager, folder, serverUrl, cookies))
                                        .ToDictionary(folder => folder.FullPath, folder => folder);

            pathToItemMapping = new[] { rootFolder }
                                        .Recurse(folder => folder.Subfolders)
                                        .OfType<_3dxDocument>()
                                        .SelectMany(document => document.Files)
                                        .Select(file => new _3dxStoreItem(LockingManager, file, false, serverUrl, cookies))
                                        .ToDictionary(folder => folder.FullPath, folder => folder);
        }
        */

        public Task<IStoreCollection> GetCollectionAsync(Uri uri, IHttpContext httpContext)
        {
            var requestedPath = UriHelper.GetDecodedPath(uri)[1..].Replace('/', Path.DirectorySeparatorChar);

            var collection = pathToCollectionMapping[requestedPath];

            return Task.FromResult<IStoreCollection>(collection);
        }

        public Task<IStoreItem?> GetItemAsync(Uri uri, IHttpContext httpContext)
        {
            var requestedPath = UriHelper.GetDecodedPath(uri)[1..].Replace('/', Path.DirectorySeparatorChar);

            if (pathToCollectionMapping.ContainsKey(requestedPath))
            {
                var collection = pathToCollectionMapping[requestedPath];

                return Task.FromResult<IStoreItem?>(collection);
            }

            if (pathToItemMapping.ContainsKey(requestedPath))
            {
                var item = pathToItemMapping[requestedPath];
                return Task.FromResult<IStoreItem?>(item);
            }

            // The item doesn't exist
            return Task.FromResult<IStoreItem?>(null);
        }

        public void Close()
        {
            _3dxServer.Close();
        }
    }
}
