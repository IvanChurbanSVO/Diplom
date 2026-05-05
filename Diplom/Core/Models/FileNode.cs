using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Diplom.Core.Models
{
    public class FileNode
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; } 
        public long TotalSize { get; set; } 
        public bool IsDirectory { get; set; }
        public List<FileNode> Children { get; set; } = new();

        public int Level { get; set; }
    }
}