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
using lib3dxVFS.WebDAV.Locks;
using NWebDav.Server.Handlers;
using lib3dxVFS.WebDAV.Stubs;
using libCommon.Comparers;

namespace libVFS.WebDAV.Stores
{
    public class _3dxStore : IStore
    {
        readonly ILockingManager LockingManager = new NoLocking();

        _3dxFolder? rootFolder;
        Dictionary<string, _3dxStoreCollection> pathToCollectionMapping = new();
        Dictionary<string, _3dxStoreItem> pathToItemMapping = new();

        public string ServerUrl { get; }
        public string WebDavServerUrl { get; }
        public _3dxCookies Cookies { get; }
        public int QueryThreads { get; }
        private uint MaxMetadataSizeInBytes;
        public EventHandler<ProgressEventArgs>? Progress { get; }
        public EventHandler<ProgressEventArgs>? RefreshFailed { get; set; }

        public _3dxStore(string serverUrl, string webDavServerUrl, _3dxCookies cookies, int queryThreads, uint maxMetadataSizeInBytes, EventHandler<ProgressEventArgs>? progress)
        {
            ServerUrl = serverUrl;
            WebDavServerUrl = webDavServerUrl;
            Cookies = cookies;
            QueryThreads = queryThreads;
            MaxMetadataSizeInBytes = maxMetadataSizeInBytes;
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
            Log.WriteLine("Refreshing document list");
            var startTime = DateTime.Now;

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

                int attempt;
                int maxAttempts = 5;
                for (attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        var _3dxServer = new _3dxServer(ServerUrl, Cookies);

                        var allDocuments = _3dxServer
                                                .GetAllDocuments(docsRoot, Cookies, QueryThreads, Progress);

                        //var abc = allDocuments.SerializeToJson();
                        //File.WriteAllText(@$"C:\Users\rx831f\Desktop\Temp\2023-06-24\{take}.txt", abc);

                        docsRoot.Subfolders = allDocuments
                                                    .Cast<_3dxFolder>()
                                                    .OrderBy(folder => folder.Name)
                                                    .ToList();

                        //Add one level of indirection: A folder for each document (without Rev number)
                        //docsRoot.Subfolders = allDocuments
                        //                        .GroupBy(
                        //                            doc => doc.OriginalName,
                        //                            doc => doc,
                        //                            (k, grp) => new
                        //                            {
                        //                                FolderName = k,
                        //                                Docs = grp.ToList()
                        //                            })
                        //                        .Select(grp =>
                        //                        {
                        //                            var genericFolderForDocument = new _3dxFolder(
                        //                                                    Guid.NewGuid().ToString(),
                        //                                                    grp.FolderName,
                        //                                                    docsRoot,
                        //                                                    grp.Docs.FirstOrDefault()?.CreationTimeUtc ?? DateTime.Now,
                        //                                                    grp.Docs.FirstOrDefault()?.LastWriteTimeUtc ?? DateTime.Now,
                        //                                                    grp.Docs.FirstOrDefault()?.LastAccessTimeUtc ?? DateTime.Now);

                        //                            var subfolders = grp
                        //                                                .Docs
                        //                                                .Cast<_3dxFolder>()
                        //                                                .ToList();

                        //                            subfolders
                        //                                .ForEach(subfolder => subfolder.Parent = genericFolderForDocument);

                        //                            genericFolderForDocument.Subfolders.AddRange(subfolders);

                        //                            return genericFolderForDocument;
                        //                        })
                        //                        .ToList();

                        break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == maxAttempts)
                        {
                            var exceptionStr = $"Could not retrieve documents after {attempt} {"attempt".Pluralize(attempt)}.";
                            if (!ex.Message.Contains("A task was canceled"))
                            {
                                exceptionStr += $" {ex.Message}";
                            }
                            throw new Exception(exceptionStr);
                        }
                    }
                }

                if (attempt > 1)
                {
                    //Debugger.Break();
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

                //Windows 10 has very restrictive WebDAV settings. By default, metadata returned by PROPFIND must be less than 1,000,000 bytes.
                //This is controlled by HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WebClient\Parameters\FileAttributesLimitInBytes
                //Let's create a folder structure which fits within that constraint.

                //MaxMetadataSizeInBytes = 1_000_000;
                var originalTopLevelFolders = rootFolder
                                                .Subfolders
                                                .OrderBy(folder => folder.Name, new ExplorerComparer())
                                                .ToList();

                var numberFoldersToUse = 1;
                while (true)
                {
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

                    var folderUrlsToCheck = new List<string>();
                    if (numberFoldersToUse == 1)
                    {
                        folderUrlsToCheck.Add(WebDavServerUrl);
                    }
                    else
                    {
                        folderUrlsToCheck = rootFolder
                                                .Subfolders
                                                .Select(f => WebDavServerUrl.UrlCombine(f.Name))
                                                .ToList();
                    }

                    var anyAreOversize = folderUrlsToCheck
                                            .Any(folder =>
                                            {
                                                //Check how large the metadata is folder this folder
                                                var propFindHandler = new PropFindHandler();
                                                var fakeHttpContext = new FakeHttpContext(new Uri(folder), 1);
                                                _ = propFindHandler.HandleRequestAsync(fakeHttpContext, this).Result;
                                                var folderMetadataLength = fakeHttpContext.Response.Stream.Length;
                                                fakeHttpContext.Response.Stream.Close();

                                                var isOversize = folderMetadataLength > MaxMetadataSizeInBytes;

                                                if (isOversize)
                                                {
                                                    Log.WriteLine($"Folder metadata is {folderMetadataLength:N0} bytes, which exceeds WebClient's maximum.");
                                                }

                                                return isOversize;
                                            });

                    if (anyAreOversize)
                    {
                        numberFoldersToUse++;
                        var itemsPerFolder = (int)Math.Ceiling(originalTopLevelFolders.Count / (double)numberFoldersToUse);

                        var newTopLevelFolders = originalTopLevelFolders
                                                    .Chunk(itemsPerFolder)
                                                    .Select((chunk, index) =>
                                                    {
                                                        var firstDocName = (chunk.First() as _3dxDocument)?.OriginalName;
                                                        var lastDocName = (chunk.Last() as _3dxDocument)?.OriginalName;

                                                        string newVirtualFolderName;
                                                        if (firstDocName == null || lastDocName == null)
                                                        {
                                                            newVirtualFolderName = $"{index + 1}";
                                                        }
                                                        else
                                                        {
                                                            newVirtualFolderName = $"{firstDocName} ... {lastDocName}";
                                                        }

                                                        var newVirtualFolder = new _3dxFolder(
                                                                            Guid.NewGuid().ToString(),
                                                                            newVirtualFolderName,
                                                                            rootFolder,
                                                                            DateTime.Now,
                                                                            DateTime.Now,
                                                                            DateTime.Now);

                                                        var subfolders = chunk
                                                                            .OfType<_3dxFolder>()
                                                                            .ToList();

                                                        subfolders
                                                            .ForEach(subfolder => subfolder.Parent = newVirtualFolder);

                                                        newVirtualFolder.Subfolders.AddRange(subfolders);

                                                        return newVirtualFolder;
                                                    })
                                                    .ToList();


                        rootFolder.Subfolders = newTopLevelFolders;
                    }
                    else
                    {
                        //all fit
                        break;
                    }
                }

                if (numberFoldersToUse > 1)
                {
                    Log.WriteLine($"Had to use {numberFoldersToUse:N0} top-level folders to fit within the WebClient constraint of {MaxMetadataSizeInBytes:N0} bytes.");
                }

                var duration = DateTime.Now - startTime;
                Log.WriteLine($"Document list refreshed in {duration.FormatTimeSpan()}, with {attempt:N0} {"attempt".Pluralize(attempt)}. {pathToItemMapping.Count:N0} {"file".Pluralize(pathToItemMapping.Count)}.");
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
            requestedPath = requestedPath.TrimEnd(''); //for some reason, this character (60656) is sometimes at the end of the string

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
                    try { Task.Delay(keepAliveInterval, CancelRefreshTask.Token).Wait(); } catch { }

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
