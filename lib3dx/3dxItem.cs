using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lib3dx
{
    public class _3dxItem
    {
        public string ObjectId { get; init; }
        public string Title { get; init; }
        public string FullPath { get; init; }

        public _3DXFolder? Parent { get; set; }
        public DateTime CreationTimeUtc { get; set; } = new DateTime(2027, 1, 1);
        public DateTime LastWriteTimeUtc { get; set; } = new DateTime(2027, 1, 1);
        public DateTime LastAccessTimeUtc { get; set; } = new DateTime(2027, 1, 1);
    }
}
