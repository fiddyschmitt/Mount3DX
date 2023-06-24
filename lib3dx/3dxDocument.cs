using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lib3dx
{
    public class _3dxDocument : _3dxFolder
    {
        public List<_3dxFile> Files = new List<_3dxFile>();
        public string DocumentType;
    }
}
