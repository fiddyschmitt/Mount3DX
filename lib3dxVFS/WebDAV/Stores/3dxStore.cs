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
        readonly ILockingManager LockingManager = new InMemoryLockingManager();

        _3dxFolder? rootFolder;
        Dictionary<string, _3dxStoreCollection> pathToCollectionMapping = new();
        Dictionary<string, _3dxStoreItem> pathToItemMapping = new();

        public string ServerUrl { get; }
        public string Cookies { get; }
        public int QueryThreads { get; }
        public EventHandler<ProgressEventArgs>? Progress { get; }
        public EventHandler<ProgressEventArgs>? RefreshFailed { get; set; }

        public _3dxStore(string serverUrl, string cookies, int queryThreads, EventHandler<ProgressEventArgs>? progress)
        {
            ServerUrl = serverUrl;
            Cookies = cookies;
            QueryThreads = queryThreads;
            Progress = progress;


            progress?.Invoke(this, new ProgressEventArgs()
            {
                Message = $"Querying 3DX for documents",
                Nature = ProgressEventArgs.EnumNature.Neutral
            });

            RefreshDocumentsList();


            /*
            progress?.Invoke(this, new ProgressEventArgs()
            {
                Message = $"Found {allDocuments:N0} documents",
                Nature = ProgressEventArgs.EnumNature.Neutral
            });
            */

            //We don't want subsequent refreshes to appear on the GUI
            Progress = null;
        }

        void RefreshDocumentsList()
        {
            try
            {
                rootFolder = new _3dxFolder(
                                    Guid.NewGuid().ToString(),
                                    "",
                                    null,
                                    DateTime.Now,
                                    DateTime.Now,
                                    DateTime.Now);

                /*
                var docsRoot = new _3dxFolder(
                                    Guid.NewGuid().ToString(),
                                    "Documents",
                                    rootFolder,
                                    DateTime.Now,
                                    DateTime.Now,
                                    DateTime.Now);

                rootFolder.Subfolders.Add(docsRoot);
                */

                var docsRoot = rootFolder;

                int attempt = 1;
                int maxAttempts = 5;
                for (attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        var allDocuments = _3dxServer
                                                .GetAllDocuments(docsRoot, ServerUrl, Cookies, QueryThreads, Progress, CancelRefreshTask.Token);

                        //var abc = allDocuments.SerializeToJson();
                        //File.WriteAllText(@$"C:\Users\rx831f\Desktop\Temp\2023-06-24\{take}.txt", abc);

                        docsRoot.Subfolders = allDocuments
                                                    .Cast<_3dxFolder>()
                                                    .ToList();

                        break;
                    }
                    catch (Exception)
                    {
                        if (attempt == maxAttempts)
                        {
                            throw;
                        }
                    }
                }

                if (attempt > 1)
                {
                    Debugger.Break();
                }

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
                                            .Select(folder => new _3dxStoreCollection(LockingManager, folder, ServerUrl, Cookies))
                                            .ToDictionary(folder => folder.FullPath, folder => folder);

                pathToItemMapping = new[] { rootFolder }
                                            .Recurse(folder => folder.Subfolders)
                                            .OfType<_3dxDocument>()
                                            .SelectMany(document => document.Files)
                                            .Select(file => new _3dxStoreItem(LockingManager, file, false, ServerUrl, Cookies))
                                            .ToDictionary(folder => folder.FullPath, folder => folder);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while refreshing the document list:{Environment.NewLine}{ex}");

                RefreshFailed?.Invoke(this, new ProgressEventArgs()
                {
                    Message = $"Error while refreshing document list: {ex.Message}",
                    Nature = ProgressEventArgs.EnumNature.Bad
                });
            }
        }

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


        CancellationTokenSource CancelRefreshTask = new();
        Task? RefreshTask;

        public void StartRefresh(int keepAliveIntervalMinutes)
        {
            var keepAliveInterval = TimeSpan.FromMinutes(keepAliveIntervalMinutes);
            CancelRefreshTask = new();

            RefreshTask = Task.Factory.StartNew(() =>
            {
                while (!CancelRefreshTask.IsCancellationRequested)
                {
                    try
                    {
                        Task.Delay(keepAliveInterval, CancelRefreshTask.Token).Wait();
                    }
                    catch { }

                    if (CancelRefreshTask.IsCancellationRequested) break;

                    RefreshDocumentsList();

                    if (CancelRefreshTask.IsCancellationRequested) break;
                }
            });

            Log.WriteLine($"Started Document Refresh task at interval of {keepAliveIntervalMinutes:N0} minutes.");
        }

        public void StopRefresh()
        {
            CancelRefreshTask.Cancel();
            RefreshTask?.Wait(1000);    //a timeout because it might be the RefreshTask which has told the session to stop
        }
    }
}
