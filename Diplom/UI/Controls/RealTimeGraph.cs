using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Diplom.UI.Controls
{
    public class RealTimeGraph : Control
    {
        private readonly float[] _values;
        private int _writeIndex = 0;
        private int _dataCount = 0;
        private readonly object _lock = new();

        public float MinValue { get; set; } = 0f;
        public float MaxValue { get; set; } = 100f;
        public Color LineColor { get; set; } = Color.LimeGreen;
        public Color FillColor { get; set; } = Color.FromArgb(60, Color.LimeGreen);
        public Color GridColor { get; set; } = Color.DarkGray;
        public string? Label { get; set; } = "";

        private Pen? _linePen;
        private Brush? _fillBrush;
        private Pen? _gridPen;
        private Font? _font;
        private Brush? _textBrush;

        public RealTimeGraph(int capacity = 100)
        {
            if (capacity < 2) capacity = 2;
            _values = new float[capacity];

            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.DoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            UpdateResources();
        }

        protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); UpdateResources(); }
        protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); UpdateResources(); }

        private void UpdateResources()
        {
            _linePen?.Dispose(); _fillBrush?.Dispose(); _gridPen?.Dispose(); _font?.Dispose(); _textBrush?.Dispose();
            _linePen = new Pen(LineColor, 2f) { Alignment = PenAlignment.Center };
            _fillBrush = new SolidBrush(FillColor);
            _gridPen = new Pen(GridColor, 1f) { DashStyle = DashStyle.Dot };
            _font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _textBrush = new SolidBrush(this.ForeColor);
        }

        public void AddValue(float value)
        {
            lock (_lock)
            {
                _values[_writeIndex] = value;
                _writeIndex++;
                if (_writeIndex >= _values.Length) _writeIndex = 0;
                if (_dataCount < _values.Length) _dataCount++;
            }
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_linePen == null) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            int w = this.Width;
            int h = this.Height;
            int paddingTop = 25;
            int paddingBottom = 10;
            int graphHeight = h - paddingTop - paddingBottom;
            int graphY = h - paddingBottom;

            DrawGrid(g, w, graphY, graphHeight);

            lock (_lock)
            {
                if (_dataCount == 0) return;
                float range = MaxValue - MinValue;
                if (range <= 0) range = 1;

                var points = new List<PointF>(_dataCount);
                float xStep = (float)w / Math.Max(_values.Length - 1, 1);
                int startIndex = (_dataCount < _values.Length) ? 0 : (_writeIndex % _values.Length);

                for (int i = 0; i < _dataCount; i++)
                {
                    int idx = (startIndex + i) % _values.Length;
                    float val = _values[idx];
                    float normalized = Math.Clamp((val - MinValue) / range, 0f, 1f);
                    float x = i * xStep;
                    float y = graphY - (normalized * graphHeight);
                    points.Add(new PointF(x, y));
                }

                if (points.Count > 1)
                {
                    var fillPoints = new List<PointF>(points);
                    fillPoints.Add(new PointF(points[^1].X, graphY));
                    fillPoints.Add(new PointF(points[0].X, graphY));
                    g.FillPolygon(_fillBrush, fillPoints.ToArray());
                    g.DrawLines(_linePen, points.ToArray());
                }
            }

            if (!string.IsNullOrEmpty(Label))
            {
                float lastVal = GetLastValue();
                g.DrawString($"{Label}: {lastVal:F1}%", _font!, _textBrush!, 10, 5);
            }
        }

        private float GetLastValue()
        {
            lock (_lock)
            {
                if (_dataCount == 0) return 0f;
                int idx = (_writeIndex - 1 + _values.Length) % _values.Length;
                return _values[idx];
            }
        }

        private void DrawGrid(Graphics g, int width, int baselineY, int height)
        {
            float y0 = baselineY;
            float y50 = baselineY - (height * 0.5f);
            float y100 = baselineY - height;

            g.DrawLine(_gridPen!, 0, y0, width, y0);
            g.DrawLine(_gridPen!, 0, y50, width, y50);
            g.DrawLine(_gridPen!, 0, y100, width, y100);

            g.DrawString($"{MinValue:F0}", _font!, _textBrush!, 5, y0 - 12);
            g.DrawString($"{MinValue + (MaxValue - MinValue) / 2:F0}", _font!, _textBrush!, 5, y50 - 12);
            g.DrawString($"{MaxValue:F0}", _font!, _textBrush!, 5, y100 - 12);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _linePen?.Dispose(); _fillBrush?.Dispose(); _gridPen?.Dispose(); _font?.Dispose(); _textBrush?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}