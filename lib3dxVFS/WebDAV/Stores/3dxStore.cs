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

namespace libVFS.WebDAV.Stores
{
    public class _3dxStore : IStore
    {
        _3dxServer _3dxServer;

        ILockingManager LockingManager = new InMemoryLockingManager();

        _3dxFolder? rootFolderInfo;
        Dictionary<string, _3dxStoreCollection> pathToCollectionMapping = new();
        Dictionary<string, _3dxStoreItem> pathToItemMapping = new();

        public _3dxStore(string serverUrl, string cookies, bool keepAlive, int keepAliveIntervalMinutes, int queryThreads)
        {
            _3dxServer = new _3dxServer(serverUrl, cookies, keepAlive, keepAliveIntervalMinutes);

            /*
            rootFolderInfo = new _3DXFolder()
            {
                ObjectId = "46256.17925.4852.48657",
                Title = "K Workspace WHITE",
                FullPath = ""
            };
            */

            /*
            rootFolderInfo = new _3DXFolder()
            {
                ObjectId = "46256.17925.55089.11812",
                Name = "04. Production(77)",
                FullPath = ""
            };

            rootFolderInfo.PopulateSubfoldersRecursively(serverUrl, cookies, queryThreads);

            allCollections = new[] { rootFolderInfo }
                                .Recurse(folder => folder.Subfolders)
                                .Select(folder => new _3dxStoreCollection(LockingManager, folder))
                                .ToList();
            */

            rootFolderInfo = new _3dxFolder()
            {
                Name = "",
                Subfolders = _3dxServer
                                    .GetAllDocuments(serverUrl, cookies, queryThreads)
                                    .Cast<_3dxFolder>()
                                    .ToList()
            };

            //some documents have identical names. Give each an index number
            var duplicateDocuments = new[] { rootFolderInfo }
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
            var documentsWithDuplicateFiles = new[] { rootFolderInfo }
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



            pathToCollectionMapping = new[] { rootFolderInfo }
                                        .Recurse(folder => folder.Subfolders)
                                        .Select(folder => new _3dxStoreCollection(LockingManager, folder, serverUrl, cookies))
                                        .ToDictionary(folder => folder.FullPath, folder => folder);

            pathToItemMapping = new[] { rootFolderInfo }
                                        .Recurse(folder => folder.Subfolders)
                                        .OfType<_3dxDocument>()
                                        .SelectMany(document => document.Files)
                                        .Select(file => new _3dxStoreItem(LockingManager, file, false, serverUrl, cookies))
                                        .ToDictionary(folder => folder.FullPath, folder => folder);
        }

        public Task<IStoreCollection> GetCollectionAsync(Uri uri, IHttpContext httpContext)
        {
            var requestedPath = UriHelper.GetDecodedPath(uri).Substring(1).Replace('/', Path.DirectorySeparatorChar);

            var collection = pathToCollectionMapping[requestedPath];

            return Task.FromResult<IStoreCollection>(collection);
        }

        public Task<IStoreItem> GetItemAsync(Uri uri, IHttpContext httpContext)
        {
            var requestedPath = UriHelper.GetDecodedPath(uri).Substring(1).Replace('/', Path.DirectorySeparatorChar);

            if (pathToCollectionMapping.ContainsKey(requestedPath))
            {
                var collection = pathToCollectionMapping[requestedPath];

                return Task.FromResult<IStoreItem>(collection);
            }

            if (pathToItemMapping.ContainsKey(requestedPath))
            {
                var item = pathToItemMapping[requestedPath];
                return Task.FromResult<IStoreItem>(item);
            }

            // The item doesn't exist
            return Task.FromResult<IStoreItem>(null);
        }

        public void Close()
        {
            _3dxServer.Close();
        }
    }
}
