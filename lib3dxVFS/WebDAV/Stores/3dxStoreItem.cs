using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using lib3dx;
using NWebDav.Server;
using NWebDav.Server.Helpers;
using NWebDav.Server.Http;
using NWebDav.Server.Locking;
using NWebDav.Server.Logging;
using NWebDav.Server.Props;
using NWebDav.Server.Stores;

namespace lib3dxVFS.WebDAV.Stores
{
    [DebuggerDisplay("{_fileInfo.FullPath}")]
    public sealed class _3dxStoreItem : IDiskStoreItem
    {
        public readonly _3dxFile _fileInfo;

        public _3dxStoreItem(ILockingManager lockingManager, _3dxFile fileInfo, bool isWritable, string serverUrl, string cookies)
        {
            LockingManager = lockingManager;
            _fileInfo = fileInfo;
            IsWritable = isWritable;
            ServerUrl = serverUrl;
            Cookies = cookies;
        }

        public static PropertyManager<_3dxStoreItem> DefaultPropertyManager { get; } = new PropertyManager<_3dxStoreItem>(new DavProperty<_3dxStoreItem>[]
        {
            // RFC-2518 properties
            new DavCreationDate<_3dxStoreItem>
            {
                Getter = (context, item) => item._fileInfo.CreationTimeUtc,
                Setter = (context, item, value) =>
                {
                    item._fileInfo.CreationTimeUtc = value;
                    return DavStatusCode.Ok;
                }
            },
            new DavDisplayName<_3dxStoreItem>
            {
                Getter = (context, item) => item._fileInfo.Name
            },
            new DavGetContentLength<_3dxStoreItem>
            {
                Getter = (context, item) => (long)item._fileInfo.Size
            },
            new DavGetContentType<_3dxStoreItem>
            {
                Getter = (context, item) => item.DetermineContentType()
            },
            new DavGetEtag<_3dxStoreItem>
            {
                // Calculating the Etag is an expensive operation,
                // because we need to scan the entire file.
                IsExpensive = true,
                Getter = (context, item) => item.CalculateEtag()
            },
            new DavGetLastModified<_3dxStoreItem>
            {
                Getter = (context, item) => item._fileInfo.LastWriteTimeUtc,
                Setter = (context, item, value) => DavStatusCode.NotImplemented
            },
            new DavGetResourceType<_3dxStoreItem>
            {
                Getter = (context, item) => null
            },

            // Default locking property handling via the LockingManager
            new DavLockDiscoveryDefault<_3dxStoreItem>(),
            new DavSupportedLockDefault<_3dxStoreItem>(),

            // Hopmann/Lippert collection properties
            // (although not a collection, the IsHidden property might be valuable)
            new DavExtCollectionIsHidden<_3dxStoreItem>
            {
                Getter = (context, item) => false
            },

            // Win32 extensions
            new Win32CreationTime<_3dxStoreItem>
            {
                Getter = (context, item) => item._fileInfo.CreationTimeUtc,
                Setter = (context, item, value) => DavStatusCode.NotImplemented
            },
            new Win32LastAccessTime<_3dxStoreItem>
            {
                Getter = (context, item) => item._fileInfo.LastAccessTimeUtc,
                Setter = (context, item, value) => DavStatusCode.NotImplemented
            },
            new Win32LastModifiedTime<_3dxStoreItem>
            {
                Getter = (context, item) => item._fileInfo.LastWriteTimeUtc,
                Setter = (context, item, value) => DavStatusCode.NotImplemented
            },
            new Win32FileAttributes<_3dxStoreItem>
            {
                Getter = (context, item) => FileAttributes.Normal,
                Setter = (context, item, value) => DavStatusCode.NotImplemented
            }
        });

        public bool IsWritable { get; }
        public string ServerUrl { get; }
        public string Cookies { get; }

        public string Name => _fileInfo.Name;
        public string UniqueKey => _fileInfo.ObjectId;
        public string FullPath => _fileInfo.FullPath;
        public Task<Stream> GetReadableStreamAsync(IHttpContext httpContext)
        {
            //var result = Task.FromResult((Stream)_fileInfo.OpenRead());
            //var result = Task.FromResult((Stream)new MemoryStream());
            var fileInMemory = _fileInfo.Download(ServerUrl, Cookies);
            fileInMemory.Seek(0, SeekOrigin.Begin);
            var result = Task.FromResult((Stream)fileInMemory);
            return result;
        }

        public async Task<DavStatusCode> UploadFromStreamAsync(IHttpContext httpContext, Stream inputStream) => DavStatusCode.NotImplemented;

        public IPropertyManager PropertyManager => DefaultPropertyManager;
        public ILockingManager LockingManager { get; }

        public async Task<StoreItemResult> CopyAsync(IStoreCollection destination, string name, bool overwrite, IHttpContext httpContext)
        {
            try
            {
                // If the destination is also a disk-store, then we can use the FileCopy API
                // (it's probably a bit more efficient than copying in C#)
                if (destination is DiskStoreCollection diskCollection)
                {
                    // Check if the collection is writable
                    if (!diskCollection.IsWritable)
                        return new StoreItemResult(DavStatusCode.PreconditionFailed);

                    var destinationPath = Path.Combine(diskCollection.FullPath, name);

                    // Check if the file already exists
                    var fileExists = File.Exists(destinationPath);
                    if (fileExists && !overwrite)
                        return new StoreItemResult(DavStatusCode.PreconditionFailed);

                    // Copy the file
                    File.Copy(_fileInfo.FullPath, destinationPath, true);

                    // Return the appropriate status
                    return new StoreItemResult(fileExists ? DavStatusCode.NoContent : DavStatusCode.Created);
                }
                else
                {
                    // Create the item in the destination collection
                    var result = await destination.CreateItemAsync(name, overwrite, httpContext).ConfigureAwait(false);

                    // Check if the item could be created
                    if (result.Item != null)
                    {
                        using (var sourceStream = await GetReadableStreamAsync(httpContext).ConfigureAwait(false))
                        {
                            var copyResult = await result.Item.UploadFromStreamAsync(httpContext, sourceStream).ConfigureAwait(false);
                            if (copyResult != DavStatusCode.Ok)
                                return new StoreItemResult(copyResult, result.Item);
                        }
                    }

                    // Return result
                    return new StoreItemResult(result.Result, result.Item);
                }
            }
            catch (Exception exc)
            {
                //s_log.Log(LogLevel.Error, () => "Unexpected exception while copying data.", exc);
                return new StoreItemResult(DavStatusCode.InternalServerError);
            }
        }

        public override int GetHashCode()
        {
            return _fileInfo.FullPath.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (!(obj is _3dxStoreItem storeItem))
                return false;
            return storeItem._fileInfo.FullPath.Equals(_fileInfo.FullPath, StringComparison.CurrentCultureIgnoreCase);
        }

        private string DetermineContentType()
        {
            return MimeTypeHelper.GetMimeType(_fileInfo.Name);
        }

        private string CalculateEtag()
        {
            using (var stream = File.OpenRead(_fileInfo.FullPath))
            {
                var hash = SHA256.Create().ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }
    }
}
