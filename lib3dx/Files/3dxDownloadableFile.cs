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

        //Served as getcontentlength and by HEAD. A derived class whose content can be built locally
        //overrides this with the real length; one whose content must be fetched reports a
        //placeholder instead, so that folder listings stay free of upstream calls.
        public virtual ulong Size { get; }

        public abstract Stream Download(_3dxServer _3dxServer);
    }
}
