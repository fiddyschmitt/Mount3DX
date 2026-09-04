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

        //A complete view of the document tree. Refreshes build a full replacement and publish it
        //with a single reference assignment, so in-flight requests always see a consistent tree.
        volatile StoreSnapshot snapshot = new();

        public string WebDavServerUrl { get; }
        public _3dxServer _3dxServer { get; }
        public int QueryThreads { get; }
        private readonly uint MaxMetadataSizeInBytes;
        public EventHandler<ProgressEventArgs>? Progress { get; }

        //Raised by background refreshes: Warning when a refresh fails (the previously published
        //document list is kept and stays available) and Good when a later refresh succeeds again.
        public event EventHandler<ProgressEventArgs>? RefreshStatus;
        bool lastRefreshFailed;

        public _3dxStore(_3dxServer _3dxServer, string webDavServerUrl, int queryThreads, uint maxMetadataSizeInBytes, EventHandler<ProgressEventArgs>? progress)
        {
            WebDavServerUrl = webDavServerUrl;
            this._3dxServer = _3dxServer;
            QueryThreads = queryThreads;
            MaxMetadataSizeInBytes = maxMetadataSizeInBytes;
            Progress = progress;


            progress?.Invoke(this, new ProgressEventArgs()
            {
                Message = $"Querying 3DX for documents",
                Nature = ProgressEventArgs.EnumNature.Neutral
            });

            RefreshDocumentsList(throwOnError: true);

            //We don't want subsequent refreshes to appear on the GUI
            Progress = null;
        }

        void RefreshDocumentsList(bool throwOnError = false)
        {
            Log.WriteLine("Refreshing document list");
            var startTime = DateTime.Now;

            try
            {
                var rootFolder = new _3dxFolder(
                                    Guid.NewGuid().ToString(),
                                    "",
                                    null,
                                    DateTime.UtcNow,
                                    DateTime.UtcNow,
                                    DateTime.UtcNow);

                var docsRoot = rootFolder;

                int attempt;
                int maxAttempts = 5;
                for (attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        var allDocuments = _3dxServer
                                                .GetAllDocuments(docsRoot, QueryThreads, Progress);

                        docsRoot.Subfolders = allDocuments
                                                    .Cast<_3dxFolder>()
                                                    .OrderBy(folder => folder.Name)
                                                    .ToList();

                        break;
                    }
                    catch (Exception ex)
                    {
                        //an InvalidOperationException is a state error (e.g. no search service
                        //URL) that retrying won't fix
                        if (attempt == maxAttempts || ex is InvalidOperationException)
                        {
                            var exceptionStr = $"Could not retrieve documents after {attempt} {"attempt".Pluralize(attempt)}.";
                            if (!ex.Message.Contains("A task was canceled"))
                            {
                                exceptionStr += $" {ex.Message}";
                            }
                            throw new Exception(exceptionStr);
                        }

                        //Back off before trying again (each page has already retried internally),
                        //and stop altogether if the session is being stopped meanwhile
                        var backoff = TimeSpan.FromSeconds(10 * attempt);
                        Log.WriteLine($"Attempt {attempt} to retrieve documents failed; retrying in {backoff.TotalSeconds:N0} seconds. {ex.Message}");

                        if (CancelRefreshTask.Token.WaitHandle.WaitOne(backoff))
                        {
                            throw new OperationCanceledException("The refresh was cancelled.");
                        }
                    }
                }

                //some documents have identical names. Give each an index number
                var documentsInTree = new[] { rootFolder }
                                               .Recurse(folder => folder.Subfolders)
                                               .OfType<_3dxDocument>()
                                               .ToList();

                var duplicateDocuments = documentsInTree
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

                //a renamed document must not collide with any other document either
                var usedDocumentPaths = new HashSet<string>(documentsInTree.Select(doc => doc.FullPath), StringComparer.OrdinalIgnoreCase);

                duplicateDocuments
                    .ForEach(grp =>
                    {
                        var i = 1;
                        foreach (var document in grp.Documents)
                        {
                            var originalName = document.Name;
                            do
                            {
                                document.Name = $"{originalName} ({i}) ({document.DocumentType})";
                                i++;
                            } while (!usedDocumentPaths.Add(document.FullPath));
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
                        //a renamed file must not collide with any other file in the same document either
                        var usedFilePaths = new HashSet<string>(doc.Document.Files.Select(file => file.FullPath), StringComparer.OrdinalIgnoreCase);

                        foreach (var duplicateGroup in doc.DuplicateGroups)
                        {
                            var i = 1;
                            foreach (var file in duplicateGroup.Files)
                            {
                                var nameWithoutExtension = Path.GetFileNameWithoutExtension(file.Name);
                                var extension = Path.GetExtension(file.Name);

                                do
                                {
                                    file.Name = $"{nameWithoutExtension} ({i}){extension}";
                                    i++;
                                } while (!usedFilePaths.Add(file.FullPath));
                            }
                        }
                    });

                //Windows 10 has very restrictive WebDAV settings. By default, metadata returned by PROPFIND must be less than 1,000,000 bytes.
                //This is controlled by HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WebClient\Parameters\FileAttributesLimitInBytes
                //Let's create a folder structure which fits within that constraint.

                var originalTopLevelFolders = rootFolder
                                                .Subfolders
                                                .OrderBy(folder => folder.Name, new ExplorerComparer())
                                                .ToList();

                var numberFoldersToUse = 1;
                var candidate = new StoreSnapshot();
                while (true)
                {
                    //build the mappings tolerantly; a residual duplicate path shouldn't bring down the whole store
                    var collectionMapping = new Dictionary<string, _3dxStoreCollection>(StringComparer.OrdinalIgnoreCase);
                    foreach (var collection in new[] { rootFolder }
                                                .Recurse(folder => folder.Subfolders)
                                                .Select(folder => new _3dxStoreCollection(_3dxServer, LockingManager, folder)))
                    {
                        if (!collectionMapping.TryAdd(collection.FullPath, collection))
                        {
                            Log.WriteLine($"Skipping folder with duplicate path: {collection.FullPath}");
                        }
                    }

                    var itemMapping = new Dictionary<string, _3dxStoreItem>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in new[] { rootFolder }
                                                .Recurse(folder => folder.Subfolders)
                                                .OfType<_3dxDocument>()
                                                .SelectMany(document => document.Files)
                                                .Select(file => new _3dxStoreItem(_3dxServer, LockingManager, file, false)))
                    {
                        if (!itemMapping.TryAdd(item.FullPath, item))
                        {
                            Log.WriteLine($"Skipping file with duplicate path: {item.FullPath}");
                        }
                    }

                    candidate = new StoreSnapshot()
                    {
                        PathToCollectionMapping = collectionMapping,
                        PathToItemMapping = itemMapping
                    };

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

                    //Measure the largest listing among the folders Explorer opens at the top level
                    var largestListing = folderUrlsToCheck
                                            .Select(folder =>
                                            {
                                                //Check how large the metadata for this folder is
                                                //(probe the candidate snapshot; it isn't published to live requests yet)
                                                var propFindHandler = new PropFindHandler();
                                                var fakeHttpContext = new FakeHttpContext(new Uri(folder), 1);
                                                _ = propFindHandler.HandleRequestAsync(fakeHttpContext, candidate).Result;
                                                var folderMetadataLength = fakeHttpContext.Response.Stream.Length;
                                                fakeHttpContext.Response.Stream.Close();

                                                return folderMetadataLength;
                                            })
                                            .Max();

                    if (largestListing > MaxMetadataSizeInBytes)
                    {
                        Log.WriteLine($"Largest top-level folder listing is {largestListing:N0} bytes with {numberFoldersToUse:N0} {"folder".Pluralize(numberFoldersToUse)}, which exceeds WebClient's maximum of {MaxMetadataSizeInBytes:N0} bytes.");

                        //If every top-level folder already holds a single item, we can't subdivide any
                        //further. A single document whose own listing exceeds the limit would otherwise
                        //make this loop run forever.
                        if (numberFoldersToUse >= originalTopLevelFolders.Count)
                        {
                            Log.WriteLine($"Folder metadata still exceeds WebClient's maximum of {MaxMetadataSizeInBytes:N0} bytes with one item per folder. Continuing anyway; that folder may not display in Explorer.");
                            break;
                        }

                        //Jump straight to the folder count the measurement implies rather than adding
                        //one folder per pass; every pass rebuilds the mappings and serialises every
                        //top-level listing. Listing size is roughly proportional to item count, so
                        //scale the current count by the overshoot, and always make progress.
                        var estimatedFolders = (int)Math.Ceiling(numberFoldersToUse * largestListing / (double)MaxMetadataSizeInBytes);
                        numberFoldersToUse = Math.Min(originalTopLevelFolders.Count, Math.Max(numberFoldersToUse + 1, estimatedFolders));
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
                                                            //OriginalName is the raw 3DX name, which may contain characters
                                                            //that are invalid in a path (the document's own Name was sanitised,
                                                            //but reads better here without its "Rev" suffix)
                                                            newVirtualFolderName = $"{FileUtility.MakeSafeFolderName(firstDocName, 60)} ... {FileUtility.MakeSafeFolderName(lastDocName, 60)}";
                                                        }

                                                        var newVirtualFolder = new _3dxFolder(
                                                                            Guid.NewGuid().ToString(),
                                                                            newVirtualFolderName,
                                                                            rootFolder,
                                                                            DateTime.UtcNow,
                                                                            DateTime.UtcNow,
                                                                            DateTime.UtcNow);

                                                        var subfolders = chunk
                                                                            .OfType<_3dxFolder>()
                                                                            .ToList();

                                                        subfolders
                                                            .ForEach(subfolder => subfolder.Parent = newVirtualFolder);

                                                        newVirtualFolder.Subfolders.AddRange(subfolders);

                                                        return newVirtualFolder;
                                                    })
                                                    .ToList();

                        //two chunks can produce the same "first ... last" label; make the names unique
                        var usedFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var newVirtualFolder in newTopLevelFolders)
                        {
                            var originalName = newVirtualFolder.Name;
                            var i = 1;
                            while (!usedFolderNames.Add(newVirtualFolder.Name))
                            {
                                newVirtualFolder.Name = $"{originalName} ({i})";
                                i++;
                            }
                        }

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

                //publish the new tree to live requests in one step
                snapshot = candidate;

                var duration = DateTime.Now - startTime;
                Log.WriteLine($"Document list refreshed in {duration.FormatTimeSpan()}, with {attempt:N0} {"attempt".Pluralize(attempt)}. {snapshot.PathToItemMapping.Count:N0} {"file".Pluralize(snapshot.PathToItemMapping.Count)}.");

                if (lastRefreshFailed)
                {
                    lastRefreshFailed = false;
                    RefreshStatus?.Invoke(this, new ProgressEventArgs()
                    {
                        Message = "Document list refreshed successfully.",
                        Nature = ProgressEventArgs.EnumNature.Good
                    });
                }
            }
            catch (OperationCanceledException) when (!throwOnError)
            {
                Log.WriteLine("Document list refresh cancelled.");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while refreshing the document list:{Environment.NewLine}{ex}");

                //During the initial load there is no list to fall back on, so the failure must
                //propagate to the caller or the session would report success with a broken store
                if (throwOnError)
                {
                    throw;
                }

                //A later refresh failing is not fatal: the snapshot published by the last good
                //refresh is still being served, and the next interval will try again
                lastRefreshFailed = true;
                RefreshStatus?.Invoke(this, new ProgressEventArgs()
                {
                    Message = $"Could not refresh the document list; the previous list is still being served. {ex.Message}",
                    Nature = ProgressEventArgs.EnumNature.Warning
                });
            }
        }

        public Task<IStoreCollection?> GetCollectionAsync(Uri uri, IHttpContext httpContext)
        {
            return snapshot.GetCollectionAsync(uri, httpContext);
        }

        public Task<IStoreItem?> GetItemAsync(Uri uri, IHttpContext httpContext)
        {
            return snapshot.GetItemAsync(uri, httpContext);
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

    //An atomically-publishable view of the document tree. Also serves as the IStore used to
    //probe candidate trees for the WebClient metadata size constraint before they go live.
    class StoreSnapshot : IStore
    {
        //Windows paths are case-insensitive, so lookups must be too
        public Dictionary<string, _3dxStoreCollection> PathToCollectionMapping { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, _3dxStoreItem> PathToItemMapping { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IStoreCollection?> GetCollectionAsync(Uri uri, IHttpContext httpContext)
        {
            var requestedPath = UriHelper.GetDecodedPath(uri)[1..].Replace('/', Path.DirectorySeparatorChar);

            if (PathToCollectionMapping.TryGetValue(requestedPath, out _3dxStoreCollection? collection))
            {
                return Task.FromResult<IStoreCollection?>(collection);
            }

            // The collection doesn't exist
            return Task.FromResult<IStoreCollection?>(null);
        }

        public Task<IStoreItem?> GetItemAsync(Uri uri, IHttpContext httpContext)
        {
            var requestedPath = UriHelper.GetDecodedPath(uri)[1..].Replace('/', Path.DirectorySeparatorChar);
            requestedPath = requestedPath.TrimEnd(''); //for some reason, this character (60656) is sometimes at the end of the string

            if (PathToCollectionMapping.TryGetValue(requestedPath, out _3dxStoreCollection? collection))
            {
                return Task.FromResult<IStoreItem?>(collection);
            }

            if (PathToItemMapping.TryGetValue(requestedPath, out _3dxStoreItem? item))
            {
                return Task.FromResult<IStoreItem?>(item);
            }

            // The item doesn't exist
            return Task.FromResult<IStoreItem?>(null);
        }
    }
}
