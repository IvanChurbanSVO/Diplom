using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Diplom.Core.Models
{
    public class FileNode
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; } // Размер самого файла или 0 для папки
        public bool IsDirectory { get; set; }

        // Суммарный размер (рекурсивно для папок)
        public long TotalSize { get; set; }

        public List<FileNode> Children { get; set; } = new();

        // Для отрисовки Treemap
        public int RectX { get; set; }
        public int RectY { get; set; }
        public int RectWidth { get; set; }
        public int RectHeight { get; set; }
        public Color DisplayColor { get; set; } = Color.LightGray;
    }
}
