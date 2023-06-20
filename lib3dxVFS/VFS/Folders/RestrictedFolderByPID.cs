using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libVFS.VFS.Folders
{
    public class RestrictedFolderByPID : Folder
    {
        public RestrictedFolderByPID(string name, Folder? parent, Func<int, bool> isProcessPermitted) : base(name, parent)
        {
            IsProcessPermitted = isProcessPermitted;
        }

        public Func<int, bool> IsProcessPermitted { get; }
    }
}
