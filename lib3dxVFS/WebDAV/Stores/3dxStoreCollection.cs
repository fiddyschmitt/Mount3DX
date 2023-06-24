using lib3dx;
using NWebDav.Server;
using NWebDav.Server.Http;
using NWebDav.Server.Locking;
using NWebDav.Server.Props;
using NWebDav.Server.Stores;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace lib3dxVFS.WebDAV.Stores
{
    public class _3dxStoreCollection : IDiskStoreCollection
    {
        private static readonly XElement s_xDavCollection = new XElement(WebDavNamespaces.DavNs + "collection");

        public _3dxStoreCollection(ILockingManager lockingManager, _3dxFolder folderInfo, string serverUrl, string cookies)
        {
            this.LockingManager = lockingManager;
            FolderInfo = folderInfo;
            ServerUrl = serverUrl;
            Cookies = cookies;
        }

        public bool IsWritable => false;

        public string FullPath => FolderInfo.FullPath;

        public InfiniteDepthMode InfiniteDepthMode => InfiniteDepthMode.Rejected;

        public string Name => FolderInfo.Name;

        public string UniqueKey => FolderInfo.ObjectId;
        public IPropertyManager PropertyManager => DefaultPropertyManager;

        public ILockingManager LockingManager { get; }

        public _3dxFolder FolderInfo { get; }
        public string ServerUrl { get; }
        public string Cookies { get; }

        public Task<StoreItemResult> CopyAsync(IStoreCollection destination, string name, bool overwrite, IHttpContext httpContext)
        {
            throw new NotImplementedException();
        }

        public Task<StoreCollectionResult> CreateCollectionAsync(string name, bool overwrite, IHttpContext httpContext)
        {
            throw new NotImplementedException();
        }

        public Task<StoreItemResult> CreateItemAsync(string name, bool overwrite, IHttpContext httpContext)
        {
            throw new NotImplementedException();
        }

        public Task<DavStatusCode> DeleteItemAsync(string name, IHttpContext httpContext)
        {
            throw new NotImplementedException();
        }

        public Task<IStoreItem> GetItemAsync(string name, IHttpContext httpContext)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<IStoreItem>> GetItemsAsync(IHttpContext httpContext)
        {
            IEnumerable<IStoreItem> GetItemsInternal()
            {
                // Add all directories
                foreach (var subDirectory in FolderInfo.Subfolders)
                {
                    yield return new _3dxStoreCollection(LockingManager, subDirectory, ServerUrl, Cookies);
                }

                // Add all files
                if (FolderInfo is _3dxDocument doc)
                {
                    foreach (var file in doc.Files)
                    {
                        yield return new _3dxStoreItem(LockingManager, file, IsWritable, ServerUrl, Cookies);
                    }
                }
            }

            return Task.FromResult(GetItemsInternal());
        }

        public Task<Stream> GetReadableStreamAsync(IHttpContext httpContext)
        {
            throw new NotImplementedException();
        }

        public Task<StoreItemResult> MoveItemAsync(string sourceName, IStoreCollection destination, string destinationName, bool overwrite, IHttpContext httpContext)
        {
            throw new NotImplementedException();
        }

        public bool SupportsFastMove(IStoreCollection destination, string destinationName, bool overwrite, IHttpContext httpContext)
        {
            throw new NotImplementedException();
        }

        public Task<DavStatusCode> UploadFromStreamAsync(IHttpContext httpContext, Stream source)
        {
            throw new NotImplementedException();
        }

        public static PropertyManager<_3dxStoreCollection> DefaultPropertyManager { get; } = new PropertyManager<_3dxStoreCollection>(new DavProperty<_3dxStoreCollection>[]
        {
            // RFC-2518 properties
            new DavCreationDate<_3dxStoreCollection>
            {
                Getter = (context, collection) => collection.FolderInfo.CreationTimeUtc,
                Setter = (context, collection, value) => DavStatusCode.NotImplemented
            },
            new DavDisplayName<_3dxStoreCollection>
            {
                Getter = (context, collection) => collection.FolderInfo.Name
            },
            new DavGetLastModified<_3dxStoreCollection>
            {
                Getter = (context, collection) => collection.FolderInfo.LastWriteTimeUtc,
                Setter = (context, collection, value) => DavStatusCode.NotImplemented
            },
            new DavGetResourceType<_3dxStoreCollection>
            {
                Getter = (context, collection) => new[] { s_xDavCollection }
            },

            // Default locking property handling via the LockingManager
            new DavLockDiscoveryDefault<_3dxStoreCollection>(),
            new DavSupportedLockDefault<_3dxStoreCollection>(),

            // Hopmann/Lippert collection properties
            new DavExtCollectionChildCount<_3dxStoreCollection>
            {
                Getter = (context, collection) => collection.FolderInfo.Subfolders.Count()
            },
            new DavExtCollectionIsFolder<_3dxStoreCollection>
            {
                Getter = (context, collection) => true
            },
            new DavExtCollectionIsHidden<_3dxStoreCollection>
            {
                Getter = (context, collection) => false
            },
            new DavExtCollectionIsStructuredDocument<_3dxStoreCollection>
            {
                Getter = (context, collection) => false
            },
            new DavExtCollectionHasSubs<_3dxStoreCollection>
            {
                Getter = (context, collection) => collection.FolderInfo.Subfolders.Any()
            },
            new DavExtCollectionNoSubs<_3dxStoreCollection>
            {
                Getter = (context, collection) => false
            },
            new DavExtCollectionObjectCount<_3dxStoreCollection>
            {
                Getter = (context, collection) => collection.FolderInfo.Subfolders.Count()
            },
            new DavExtCollectionReserved<_3dxStoreCollection>
            {
                Getter = (context, collection) => !collection.IsWritable
            },
            new DavExtCollectionVisibleCount<_3dxStoreCollection>
            {
                Getter = (context, collection) => collection.FolderInfo.Subfolders.Count()
            },

            // Win32 extensions
            new Win32CreationTime<_3dxStoreCollection>
            {
                Getter = (context, collection) => collection.FolderInfo.CreationTimeUtc,
                Setter = (context, collection, value) => DavStatusCode.NotImplemented
            },
            new Win32LastAccessTime<_3dxStoreCollection>
            {
                Getter = (context, collection) => collection.FolderInfo.LastAccessTimeUtc,
                Setter = (context, collection, value) => DavStatusCode.NotImplemented
            },
            new Win32LastModifiedTime<_3dxStoreCollection>
            {
                Getter = (context, collection) => collection.FolderInfo.LastWriteTimeUtc,
                Setter = (context, collection, value) => DavStatusCode.NotModified
            },
            new Win32FileAttributes<_3dxStoreCollection>
            {
                Getter = (context, collection) => FileAttributes.Directory,
                Setter = (context, collection, value) => DavStatusCode.NotModified
            }
            });

    }
}
