using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Diplom.Core.Models;
using System.Linq;

namespace Diplom.UI.Controls
{
    public class TreemapControl : Control
    {
        private List<TreemapRect> _rects = new();
        private FileNode? _rootNode;
        private ToolTip _toolTip;

        private const long MinFileSizeToRender = 50 * 1024 * 1024;

        public TreemapControl()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            _toolTip = new ToolTip();
            _toolTip.InitialDelay = 100;
            _toolTip.ReshowDelay = 50;
        }

        public void Render(FileNode root)
        {
            _rootNode = root;
            _rects.Clear();

            if (root == null || root.TotalSize == 0)
            {
                this.Invalidate();
                return;
            }

            // 1. Собираем ВСЕ файлы
            var allFiles = new List<FileNode>();
            CollectAllFiles(root, allFiles);

            // 2. Фильтруем: оставляем только крупные файлы (> 50 МБ)
            var filesToRender = allFiles.Where(f => f.Size >= MinFileSizeToRender).ToList();

            if (filesToRender.Count < 20)
            {
                filesToRender = allFiles.OrderByDescending(f => f.Size).Take(100).ToList();
            }

            // 4. Сортировка по убыванию 
            filesToRender.Sort((a, b) => b.Size.CompareTo(a.Size));

            if (filesToRender.Count > 0)
            {
                var bounds = new RectangleF(0, 0, this.Width, this.Height);

                SplitRecursive(filesToRender, 0, filesToRender.Count, bounds, true);
            }

            this.Invalidate();
        }

        public void Clear()
        {
            _rootNode = null;
            _rects.Clear();
            _toolTip.SetToolTip(this, "");
            this.Invalidate();
        }

        #region Logic: Binary Split Algorithm (Гарантированно без полосок)

        private void CollectAllFiles(FileNode node, List<FileNode> list)
        {
            if (!node.IsDirectory)
            {
                list.Add(node);
                return;
            }
            foreach (var child in node.Children)
            {
                CollectAllFiles(child, list);
            }
        }

        /// <summary>
        /// Рекурсивно делит список файлов и область пополам.
        /// </summary>
        private void SplitRecursive(List<FileNode> nodes, int start, int count, RectangleF bounds, bool splitVertical)
        {
            if (count <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

            // Если остался один файл - рисуем его на всю доступную область
            if (count == 1)
            {
                var node = nodes[start];
                AddRect(node, bounds);
                return;
            }

            // Считаем общий размер текущей группы файлов
            long totalSize = 0;
            for (int i = start; i < start + count; i++) totalSize += nodes[i].Size;

            if (totalSize == 0) return;

            // Ищем точку раздела, чтобы суммы размеров слева и справа были примерно равны
            long currentSize = 0;
            long halfSize = totalSize / 2;
            int splitIndex = start;

            for (int i = start; i < start + count; i++)
            {
                currentSize += nodes[i].Size;
                if (currentSize >= halfSize)
                {
                    splitIndex = i;
                    break;
                }
            }

            // Защита от зацикливания 
            if (splitIndex == start + count - 1 && count > 2)
            {
                splitIndex = start + count / 2;
            }

            int count1 = splitIndex - start + 1;
            int count2 = count - count1;

            if (count1 <= 0 || count2 <= 0) return;

            // Считаем размер первой части для пропорционального деления области
            long size1 = 0;
            for (int i = start; i < start + count1; i++) size1 += nodes[i].Size;

            double ratio = (double)size1 / totalSize;

            RectangleF rect1, rect2;

            if (splitVertical)
            {
                // Делим по вертикали (левая и правая часть)
                float w = (float)(bounds.Width * ratio);
                rect1 = new RectangleF(bounds.X, bounds.Y, w, bounds.Height);
                rect2 = new RectangleF(bounds.X + w, bounds.Y, bounds.Width - w, bounds.Height);
            }
            else
            {
                // Делим по горизонтали (верхняя и нижняя часть)
                float h = (float)(bounds.Height * ratio);
                rect1 = new RectangleF(bounds.X, bounds.Y, bounds.Width, h);
                rect2 = new RectangleF(bounds.X, bounds.Y + h, bounds.Width, bounds.Height - h);
            }

            // Рекурсивный вызов для частей с чередованием направления разреза
            SplitRecursive(nodes, start, count1, rect1, !splitVertical);
            SplitRecursive(nodes, splitIndex + 1, count2, rect2, !splitVertical);
        }

        private void AddRect(FileNode node, RectangleF bounds)
        {
            if (bounds.Width < 1 || bounds.Height < 1) return;

            var rect = Rectangle.Round(bounds);
            if (rect.Width > 0 && rect.Height > 0)
            {
                _rects.Add(new TreemapRect
                {
                    Node = node,
                    Rectangle = rect,
                    Color = GetColorForSize(node.Size)
                });
            }
        }

        #endregion

        #region Rendering

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_rects.Count == 0)
            {
                using var bgBrush = new SolidBrush(Color.FromArgb(245, 245, 245));
                e.Graphics.FillRectangle(bgBrush, ClientRectangle);

                string msg = _rootNode == null ? "Нажмите 'Сканировать диски'" : "Нет файлов для отображения";
                using var font = new Font("Segoe UI", 12f, FontStyle.Bold);
                using var textBrush = new SolidBrush(Color.Gray);
                var size = e.Graphics.MeasureString(msg, font);
                e.Graphics.DrawString(msg, font, textBrush, (Width - size.Width) / 2, (Height - size.Height) / 2);
                return;
            }

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Белые границы чуть толще для красоты
            using var borderPen = new Pen(Color.White, 2f);

            using var nameFont = new Font("Segoe UI", 9f, FontStyle.Bold); // Шрифт чуть крупнее
            using var shadowFont = new Font("Segoe UI", 9f, FontStyle.Bold);

            foreach (var tr in _rects)
            {
                // Заливка
                using (var fillBrush = new SolidBrush(tr.Color))
                {
                    g.FillRectangle(fillBrush, tr.Rectangle);
                }

                // Граница
                g.DrawRectangle(borderPen, tr.Rectangle);

                // Текст
                if (tr.Rectangle.Width > 30 && tr.Rectangle.Height > 20)
                {
                    string fileName = tr.Node.Name;
                    if (fileName.Length > 18) fileName = fileName.Substring(0, 15) + "...";

                    // Тень
                    using (var shadowBrush = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                    {
                        g.DrawString(fileName, shadowFont, shadowBrush, tr.Rectangle.X + 2, tr.Rectangle.Y + 2);
                    }

                    // Текст
                    using (var textBrush = new SolidBrush(Color.White))
                    {
                        g.DrawString(fileName, nameFont, textBrush, tr.Rectangle.X + 1, tr.Rectangle.Y + 1);
                    }
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = _rects.FirstOrDefault(r => r.Rectangle.Contains(e.Location));

            if (hit != null)
            {
                string sizeStr = FormatSize(hit.Node.Size);
                string tooltip = $"Файл: {hit.Node.Name}\nПуть: {hit.Node.Path}\nРазмер: {sizeStr}";

                if (_toolTip.GetToolTip(this) != tooltip)
                {
                    _toolTip.SetToolTip(this, tooltip);
                }
                this.Cursor = Cursors.Hand;
            }
            else
            {
                _toolTip.SetToolTip(this, "");
                this.Cursor = Cursors.Default;
            }
        }

        #endregion

        #region Helpers

        private Color GetColorForSize(long sizeBytes)
        {
            long GB = 1024L * 1024 * 1024;
            long MB = 1024L * 1024;

            if (sizeBytes < 100 * MB) return Color.FromArgb(173, 216, 230); // LightBlue
            if (sizeBytes < 500 * MB) return Color.FromArgb(32, 178, 170);  // LightSeaGreen
            if (sizeBytes < 1 * GB) return Color.FromArgb(46, 139, 87);     // SeaGreen
            if (sizeBytes < 2 * GB) return Color.FromArgb(124, 252, 0);     // LawnGreen
            if (sizeBytes < 5 * GB) return Color.FromArgb(154, 205, 50);    // YellowGreen
            if (sizeBytes < 10 * GB) return Color.FromArgb(218, 165, 32);   // GoldenRod
            if (sizeBytes < 20 * GB) return Color.FromArgb(255, 191, 0);    // Amber
            if (sizeBytes < 30 * GB) return Color.FromArgb(255, 165, 79);   // DarkOrange
            if (sizeBytes < 40 * GB) return Color.FromArgb(255, 127, 80);   // Coral
            if (sizeBytes < 50 * GB) return Color.FromArgb(255, 99, 71);    // Tomato
            if (sizeBytes < 100 * GB) return Color.FromArgb(255, 69, 0);    // OrangeRed
            return Color.FromArgb(139, 0, 0);                               // DarkRed
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        #endregion

        private class TreemapRect
        {
            public FileNode Node { get; set; } = null!;
            public Rectangle Rectangle { get; set; }
            public Color Color { get; set; }
        }
    }
}