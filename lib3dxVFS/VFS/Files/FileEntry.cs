using libVFS.VFS.Folders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libVFS.VFS.Files
{
    public abstract class FileEntry : FileSystemEntry
    {
        public long Length { get; set; }

        public abstract Stream GetStream();

        public FileEntry(string name, Folder? parent) : base(name, parent)
        {
        }

        public override string ToString()
        {
            var result = $"{Name}";
            return result;
        }
    }
}
