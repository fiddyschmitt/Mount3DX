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

namespace libVFS.WebDAV.Stores
{
    public class _3dxStore : IStore
    {
        _3dxServer _3dxServer;
        
        ILockingManager LockingManager = new InMemoryLockingManager();

        _3DXFolder? rootFolderInfo;
        List<_3dxStoreCollection> allCollections = new();

        public _3dxStore(string serverUrl, string cookies, bool keepAlive, int keepAliveIntervalMinutes, int queryThreads)
        {
            _3dxServer = new _3dxServer(serverUrl, cookies, keepAlive, keepAliveIntervalMinutes);

            /*
            rootFolderInfo = new _3DXFolder()
            {
                ObjectId = "46256.17925.4852.48657",
                Title = "K Workspace WHITE"
            };
            */

            rootFolderInfo = new _3DXFolder()
            {
                ObjectId = "46256.17925.55089.11812",
                Title = "04. Production(77)",
                FullPath = ""
            };

            rootFolderInfo.PopulateSubfoldersRecursively(serverUrl, cookies, queryThreads);

            allCollections = new[] { rootFolderInfo }
                                .Recurse(folder => folder.Subfolders)
                                .Select(folder => new _3dxStoreCollection(LockingManager, folder))
                                .ToList();
        }

        public Task<IStoreCollection> GetCollectionAsync(Uri uri, IHttpContext httpContext)
        {
            var requestedPath = UriHelper.GetDecodedPath(uri).Substring(1).Replace('/', Path.DirectorySeparatorChar);

            var collection = allCollections
                                .First(collection => collection.FullPath.Equals(requestedPath, StringComparison.CurrentCultureIgnoreCase));

            return Task.FromResult<IStoreCollection>(collection);
        }

        public Task<IStoreItem> GetItemAsync(Uri uri, IHttpContext httpContext)
        {
            var requestedPath = UriHelper.GetDecodedPath(uri).Substring(1).Replace('/', Path.DirectorySeparatorChar);

            var collection = allCollections
                                .FirstOrDefault(collection => collection.FullPath.Equals(requestedPath, StringComparison.CurrentCultureIgnoreCase));

            return Task.FromResult<IStoreItem>(collection);
        }

        public void Close()
        {
            _3dxServer.Close();
        }
    }
}
