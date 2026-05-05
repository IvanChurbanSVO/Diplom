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

        // Настройки графика
        public float MinValue { get; set; } = 0f;
        public float MaxValue { get; set; } = 100f;
        public Color LineColor { get; set; } = Color.LimeGreen;
        public Color FillColor { get; set; } = Color.FromArgb(60, Color.LimeGreen);
        public Color GridColor { get; set; } = Color.Gray;
        public string Label { get; set; } = "График";

        // Свойства для неонового свечения
        public bool EnableGlow { get; set; } = true;
        public Color GlowColor { get; set; } = Color.LimeGreen;

        // Внутренние ресурсы
        private Pen? _linePen;
        private Brush? _fillBrush;
        private Pen? _gridPen;
        private Font? _mainFont;
        private Font? _infoFont;
        private Brush? _textBrush;

        private string _extraText = "";

        public RealTimeGraph(int capacity = 100)
        {
            if (capacity < 2) capacity = 2;
            _values = new float[capacity];

            // Настройка стилей для двойной буферизации и плавности
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.DoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            this.BackColor = Color.FromArgb(30, 30, 40); 
            this.ForeColor = Color.White; 

            UpdateResources();
        }

        protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); UpdateResources(); }
        protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); UpdateResources(); }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
        }

        private void UpdateResources()
        {
            _linePen?.Dispose(); _fillBrush?.Dispose(); _gridPen?.Dispose();
            _mainFont?.Dispose(); _infoFont?.Dispose(); _textBrush?.Dispose();

            _linePen = new Pen(LineColor, 2.5f) { Alignment = PenAlignment.Center };
            _fillBrush = new SolidBrush(FillColor);
            _gridPen = new Pen(GridColor, 1f) { DashStyle = DashStyle.Dot };
            _mainFont = new Font("Segoe UI", 11f, FontStyle.Bold);
            _infoFont = new Font("Segoe UI", 9f, FontStyle.Regular);
            _textBrush = new SolidBrush(this.ForeColor);
        }

        public void UpdateData(float value, string extraInfo)
        {
            lock (_lock)
            {
                _values[_writeIndex] = value;
                _extraText = extraInfo;

                _writeIndex++;
                if (_writeIndex >= _values.Length) _writeIndex = 0;
                if (_dataCount < _values.Length) _dataCount++;
            }
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_linePen == null || _mainFont == null) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            int w = ClientRectangle.Width;
            int h = ClientRectangle.Height;

            if (w <= 0 || h <= 0) return;

            int paddingTop = 55;
            int paddingBottom = 10;
            int graphHeight = h - paddingTop - paddingBottom;
            int graphY = h - paddingBottom;

            // 1. Рисуем сетку
            DrawGrid(g, w, graphY, graphHeight);

            // 2. Рисуем график
            lock (_lock)
            {
                if (_dataCount > 0)
                {
                    float range = MaxValue - MinValue;
                    if (range <= 0) range = 1;

                    var points = new List<PointF>(_dataCount);
                    float xStep = (float)w / Math.Max(_values.Length - 1, 1);
                    int startIndex = (_dataCount < _values.Length) ? 0 : (_writeIndex % _values.Length);

                    for (int i = 0; i < _dataCount; i++)
                    {
                        int idx = (startIndex + i) % _values.Length;
                        float val = _values[idx];

                        float normalized = (val - MinValue) / range;
                        if (normalized < 0) normalized = 0;
                        if (normalized > 1) normalized = 1;

                        float x = i * xStep;
                        float y = graphY - (normalized * graphHeight);
                        points.Add(new PointF(x, y));
                    }

                    if (points.Count > 1)
                    {
                        if (EnableGlow)
                        {
                            using (var glowPen = new Pen(Color.FromArgb(80, GlowColor), 6f))
                            {
                                g.DrawLines(glowPen, points.ToArray());
                            }
                            using (var glowPen2 = new Pen(Color.FromArgb(120, GlowColor), 4f))
                            {
                                g.DrawLines(glowPen2, points.ToArray());
                            }
                        }

                        // Заливка области под графиком
                        var fillPoints = new List<PointF>(points);
                        fillPoints.Add(new PointF(points[^1].X, graphY));
                        fillPoints.Add(new PointF(points[0].X, graphY));
                        g.FillPolygon(_fillBrush, fillPoints.ToArray());

                        // Основная яркая линия
                        g.DrawLines(_linePen, points.ToArray());
                    }
                }
            }

            // 3. Рисуем текст 
            if (!string.IsNullOrEmpty(Label))
            {
                float lastVal = GetLastValue();
                string mainText = $"{Label}: {lastVal:F1}%";

                // Основной текст 
                g.DrawString(mainText, _mainFont, _textBrush, 15, 8);

                // Дополнительная информация
                if (!string.IsNullOrEmpty(_extraText))
                {
                    g.DrawString(_extraText, _infoFont, _textBrush, 15, 28);
                }
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

            // Подписи оси Y 
            using Font smallFont = new Font("Segoe UI", 7f);
            using Brush textBrush = new SolidBrush(Color.LightGray);

            g.DrawString($"{MinValue:F0}", smallFont, textBrush, 2, y0 - 10);
            g.DrawString($"{MinValue + (MaxValue - MinValue) / 2:F0}", smallFont, textBrush, 2, y50 - 10);
            g.DrawString($"{MaxValue:F0}", smallFont, textBrush, 2, y100 - 10);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _linePen?.Dispose(); _fillBrush?.Dispose(); _gridPen?.Dispose();
                _mainFont?.Dispose(); _infoFont?.Dispose(); _textBrush?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}