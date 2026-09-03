using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lib3dx.Files
{
    public abstract class _3dxDownloadableFile : _3dxItem
    {
        protected _3dxDownloadableFile(string objectId, string name, _3dxItem? parent, DateTime creationTimeUtc, DateTime lastWriteTimeUtc, DateTime lastAccessTimeUtc, ulong size) : base(objectId, name, parent, creationTimeUtc, lastWriteTimeUtc, lastAccessTimeUtc)
        {
            Size = size;
        }

        //Served as getcontentlength and used for Content-Length, so it must match what Download()
        //produces. Derived classes whose content is generated override this with the real length.
        public virtual ulong Size { get; }

        public abstract Stream Download(_3dxServer _3dxServer);
    }
}
