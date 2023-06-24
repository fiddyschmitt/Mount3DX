using libCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lib3dx
{
    public class _3dxItem
    {
        public string ObjectId { get; set; }
        public string Name { get; set; }
        public _3dxItem? Parent { get; set; }
        public DateTime CreationTimeUtc { get; set; } = new DateTime(2027, 1, 1);
        public DateTime LastWriteTimeUtc { get; set; } = new DateTime(2027, 1, 1);
        public DateTime LastAccessTimeUtc { get; set; } = new DateTime(2027, 1, 1);

        public _3dxItem(string objectId, string name, _3dxItem? parent, DateTime creationTimeUtc, DateTime lastWriteTimeUtc, DateTime lastAccessTimeUtc)
        {
            ObjectId = objectId;
            Name = name;
            Parent = parent;
            CreationTimeUtc = creationTimeUtc;
            LastWriteTimeUtc = lastWriteTimeUtc;
            LastAccessTimeUtc = lastAccessTimeUtc;
        }

        public string FullPath
        {
            get
            {
                var result = this
                                .Recurse(folder => folder.Parent)
                                .Reverse()
                                .Where(ancestor => !string.IsNullOrEmpty(ancestor.Name))
                                .Select(ancestor => ancestor.Name)
                                .ToString(@"\");

                return result;
            }
        }
    }
}
