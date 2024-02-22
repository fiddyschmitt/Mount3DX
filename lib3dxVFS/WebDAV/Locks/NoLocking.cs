using NWebDav.Server;
using NWebDav.Server.Locking;
using NWebDav.Server.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace lib3dxVFS.WebDAV.Locks
{
    public class NoLocking : ILockingManager
    {
        public IEnumerable<ActiveLock> GetActiveLockInfo(IStoreItem item)
        {
            yield break;
        }

        public IEnumerable<LockEntry> GetSupportedLocks(IStoreItem item)
        {
            yield break;
        }

        public bool HasLock(IStoreItem item, Uri lockToken)
        {
            return false;
        }

        public bool IsLocked(IStoreItem item)
        {
            return false;
        }

        public LockResult Lock(IStoreItem item, LockType lockType, LockScope lockScope, XElement owner, Uri lockRootUri, bool recursiveLock, IEnumerable<int> timeouts)
        {
            return new LockResult(DavStatusCode.Ok);
        }

        public LockResult RefreshLock(IStoreItem item, bool recursiveLock, IEnumerable<int> timeouts, Uri lockTokenUri)
        {
            return new LockResult(DavStatusCode.Ok);
        }

        public DavStatusCode Unlock(IStoreItem item, Uri token)
        {
            return DavStatusCode.Ok;
        }
    }
}
