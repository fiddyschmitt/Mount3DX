using libVFS.VFS.Folders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using libVFS;
using libCommon;

namespace libVFS.VFS
{
    public abstract class FileSystemEntry
    {
        public string Name { get; set; }

        Folder? parent;
        public Folder? Parent
        {
            get
            {
                return parent;
            }
            set
            {
                parent = value;
                parent?.AddChild(this);
            }
        }
        public bool Hidden { get; set; } = false;
        public bool System { get; set; } = false;

        public FileSystemEntry(string name, Folder? parent)
        {
            Name = name;
            Parent = parent;
        }

        public DateTime Modified;
        public DateTime Created;
        public DateTime Accessed;

        public List<FileSystemEntry> Ancestors
        {
            get
            {
                var ancestors = this
                                    .Recurse(ancestor => ancestor.Parent)
                                    .Reverse()
                                    .ToList();

                return ancestors;
            }
        }

        public bool IsAccessibleToProcess(int requestPID)
        {
            //check if any of the ancestors are restricted

            var restrictedAncestors = Ancestors
                                        .OfType<RestrictedFolderByPID>()
                                        .ToList();

            bool isRestricted = restrictedAncestors
                                    .Any(ancestor => !ancestor.IsProcessPermitted(requestPID));

            return isRestricted;
        }

        public string FullPath
        {
            get
            {
                var folderPath = Ancestors
                                .Where(a => a is not RootFolder)
                                .Select(a => a.Name)
                                .ToString("\\");

                var root = Ancestors
                            .OfType<RootFolder>()
                            .FirstOrDefault()?.MountPoint ?? "";

                var fullPath = Path.Combine(root, folderPath);

                return fullPath;
            }
        }

        public override string ToString()
        {
            var result = Name;
            return result;
        }
    }
}
