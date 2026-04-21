using System.Drawing;
using System.Windows.Forms;
using Diplom.Core.Models;

namespace Diplom.UI.Controls
{
    public class TreemapControl : Control
    {
        private List<RectItem> _items = new();
        private ToolTip _toolTip;
        private FileNode? _rootNode;

        public TreemapControl()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            _toolTip = new ToolTip();
            this.MouseMove += TreemapControl_MouseMove;
        }

        private void TreemapControl_MouseMove(object? sender, MouseEventArgs e)
        {
            var hit = GetItemAt(e.X, e.Y);
            if (hit != null)
            {
                _toolTip.SetToolTip(this, $"{hit.Name}\nРазмер: {FormatSize(hit.Size)}");
                this.Cursor = Cursors.Hand;
            }
            else
            {
                _toolTip.SetToolTip(this, "");
                this.Cursor = Cursors.Default;
            }
        }

        public void Clear()
        {
            _items.Clear();
            _rootNode = null;
            this.Invalidate();
        }

        public void Render(FileNode root)
        {
            _rootNode = root;
            _items.Clear();

            if (root == null) return;

            // 1. Собираем ВСЕ файлы из дерева в плоский список
            var allFiles = new List<FileNode>();
            CollectAllFiles(root, allFiles);

            if (allFiles.Count == 0) return;

            // 2. Сортируем по убыванию размера
            allFiles.Sort((a, b) => b.Size.CompareTo(a.Size));

            // 3. Оптимизация отрисовки:
            // Если файлов слишком много (> 2000), мелкие файлы мы не рисуем по отдельности,
            // а суммируем их в один блок "Прочее", иначе прямоугольники будут размером в 1 пиксель.
            long drawnSize = 0;
            long totalSize = allFiles.Sum(f => f.Size);
            var filesToDraw = new List<FileNode>();
            long otherSize = 0;

            int maxItems = Math.Min(allFiles.Count, 1000); // Рисуем топ-1000 файлов

            for (int i = 0; i < allFiles.Count; i++)
            {
                if (i < maxItems)
                {
                    filesToDraw.Add(allFiles[i]);
                    drawnSize += allFiles[i].Size;
                }
                else
                {
                    otherSize += allFiles[i].Size;
                }
            }

            if (otherSize > 0)
            {
                filesToDraw.Add(new FileNode
                {
                    Name = $"Прочие файлы ({allFiles.Count - maxItems} шт.)",
                    Size = otherSize,
                    IsDirectory = false,
                    Path = ""
                });
            }

            // 4. Запускаем алгоритм Squarified Treemap
            var rect = new RectangleF(0, 0, this.Width, this.Height);
            Squarify(filesToDraw, rect, _items);

            this.Invalidate();
        }

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

        // Алгоритм Squarified Treemap
        private void Squarify(List<FileNode> nodes, RectangleF targetRect, List<RectItem> result)
        {
            if (nodes.Count == 0) return;

            double totalSize = nodes.Sum(n => n.Size);
            if (totalSize <= 0) return;

            double aspectRatio = targetRect.Width / targetRect.Height;

            // Рекурсивное разбиение
            LayoutRow(nodes, targetRect, result, aspectRatio);
        }

        private void LayoutRow(List<FileNode> nodes, RectangleF rect, List<RectItem> result, double aspectRatio)
        {
            if (nodes.Count == 0) return;

            // Пытаемся разместить все узлы в текущем ряду
            // Упрощенная реализация для вертикального/горизонтального деления

            double currentSize = 0;
            int splitIndex = 0;

            // Эвристика: делим список пополам или ищем точку разрыва по соотношению сторон
            // Для простоты и скорости используем рекурсивное деление пополам (Binary Split), 
            // что дает хороший результат для больших списков и работает быстрее классического Squarify

            if (nodes.Count == 1)
            {
                result.Add(new RectItem(rect, nodes[0]));
                return;
            }

            // Делим список на две части примерно равные по площади
            long total = nodes.Sum(n => n.Size);
            long half = total / 2;
            long currentSum = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                currentSum += nodes[i].Size;
                if (currentSum >= half)
                {
                    splitIndex = i + 1;
                    break;
                }
            }
            if (splitIndex == 0) splitIndex = nodes.Count / 2;
            if (splitIndex >= nodes.Count) splitIndex = nodes.Count - 1;

            var leftNodes = nodes.GetRange(0, splitIndex);
            var rightNodes = nodes.GetRange(splitIndex, nodes.Count - splitIndex);

            double leftSize = leftNodes.Sum(n => n.Size);
            double rightSize = rightNodes.Sum(n => n.Size);

            double ratio = leftSize / (leftSize + rightSize);

            RectangleF rect1, rect2;

            if (rect.Width > rect.Height)
            {
                // Делим по вертикали
                float w = (float)(rect.Width * ratio);
                rect1 = new RectangleF(rect.X, rect.Y, w, rect.Height);
                rect2 = new RectangleF(rect.X + w, rect.Y, rect.Width - w, rect.Height);
            }
            else
            {
                // Делим по горизонтали
                float h = (float)(rect.Height * ratio);
                rect1 = new RectangleF(rect.X, rect.Y, rect.Width, h);
                rect2 = new RectangleF(rect.X, rect.Y + h, rect.Width, rect.Height - h);
            }

            LayoutRow(leftNodes, rect1, result, aspectRatio);
            LayoutRow(rightNodes, rect2, result, aspectRatio);
        }

        private RectItem? GetItemAt(int x, int y)
        {
            foreach (var item in _items)
            {
                if (item.Rectangle.Contains(x, y)) return item;
            }
            return null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(240, 240, 240));

            using var font = new Font("Segoe UI", 8f, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.Black);
            using var borderPen = new Pen(Color.White, 1f);

            foreach (var item in _items)
            {
                if (item.Rectangle.Width < 2 || item.Rectangle.Height < 2) continue;

                // Цвет зависит от размера (градиент от синего к красному) или типа
                Color color = GetColorForSize(item.Size);

                using (var brush = new SolidBrush(color))
                {
                    g.FillRectangle(brush, item.Rectangle);
                }

                g.DrawRectangle(borderPen,
                    item.Rectangle.X, item.Rectangle.Y,
                    item.Rectangle.Width - 1, item.Rectangle.Height - 1);

                // Текст, если влезает
                if (item.Rectangle.Width > 30 && item.Rectangle.Height > 20)
                {
                    string text = item.Name.Length > 15 ? item.Name.Substring(0, 12) + "..." : item.Name;
                    // Простая обрезка текста по ширине не реализована для краткости, но база есть
                    g.DrawString(text, font, textBrush, item.Rectangle.X + 2, item.Rectangle.Y + 2);
                }
            }
        }

        private Color GetColorForSize(long size)
        {
            // Логика цветов: большие файлы - красные, средние - желтые, мелкие - зеленые/синие
            if (size > 500 * 1024 * 1024) return Color.FromArgb(200, 50, 50); // > 500MB (Красный)
            if (size > 100 * 1024 * 1024) return Color.FromArgb(200, 100, 50); // > 100MB (Оранжевый)
            if (size > 50 * 1024 * 1024) return Color.FromArgb(200, 150, 50);  // > 50MB (Желтый)
            if (size > 10 * 1024 * 1024) return Color.FromArgb(50, 150, 50);   // > 10MB (Зеленый)
            if (size > 1 * 1024 * 1024) return Color.FromArgb(50, 100, 200);   // > 1MB (Синий)
            return Color.FromArgb(150, 150, 150); // Мелкие (Серый)
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:0.##} {sizes[order]}";
        }

        private class RectItem
        {
            public RectangleF Rectangle { get; }
            public FileNode Node { get; }
            public string Name => Node.Name;
            public long Size => Node.Size;

            public RectItem(RectangleF rect, FileNode node)
            {
                Rectangle = rect;
                Node = node;
            }
        }
    }
}