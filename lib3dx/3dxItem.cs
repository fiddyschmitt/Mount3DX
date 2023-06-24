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
        public string FullPath
        {
            get
            {
                var result = this
                                .Recurse(folder => folder.Parent)
                                .Reverse()
                                .Select(ancestor => ancestor.Name)
                                .ToString(@"\");

                return result;
            }
        }

        public _3dxItem? Parent { get; set; }
        public DateTime CreationTimeUtc { get; set; } = new DateTime(2027, 1, 1);
        public DateTime LastWriteTimeUtc { get; set; } = new DateTime(2027, 1, 1);
        public DateTime LastAccessTimeUtc { get; set; } = new DateTime(2027, 1, 1);
    }
}
