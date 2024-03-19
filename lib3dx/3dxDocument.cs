using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lib3dx
{
    public class _3dxDocument : _3dxFolder
    {
        public string DocumentType;
        public string Revision { get; set; }

        public string? OriginalName { get; internal set; }
        public string? Description { get; internal set; }

        public _3dxDocument(string objectId, string displayName, _3dxItem? parent, DateTime creationTimeUtc, DateTime lastWriteTimeUtc, DateTime lastAccessTimeUtc, string originalName, string revision, string documentType) : base(objectId, displayName, parent, creationTimeUtc, lastWriteTimeUtc, lastAccessTimeUtc)
        {
            OriginalName = originalName;
            Revision = revision;
            DocumentType = documentType;
        }

    }
}
