using Diplom.Core.Interfaces;
using Diplom.Core.Models;
using Diplom.UI.Controls;
using System.Drawing;
using System.Windows.Forms;

namespace Diplom.UI
{
    public partial class MainForm : Form
    {
        private readonly IProcessMonitorService _processService;
        private readonly ISystemMetricsService _metricsService;
        private readonly IFileScannerService _fileScannerService;
        private readonly CancellationTokenSource _cts;

        // Элементы UI
        private TabControl _tabControl;
        private TabPage _tabProcesses;
        private TabPage _tabPerformance;
        private TabPage _tabDisk; // Новая вкладка

        private DoubleBufferedDataGridView _dataGridView;
        private RealTimeGraph _cpuGraph;
        private RealTimeGraph _ramGraph;

        // Элементы для вкладки Диска
        private TreemapControl _treemapControl;
        private Button _btnScan;
        private Label _lblStatus;

        private List<ProcessSnapshot> _currentSnapshots = new();
        private string _sortColumn = "Name";
        private bool _sortAscending = true;
        private bool _isUpdatingGrid = false;

        public MainForm(
            IProcessMonitorService processService,
            ISystemMetricsService metricsService,
            IFileScannerService fileScannerService) // Внедряем сканер
        {
            InitializeComponentManual();

            _processService = processService;
            _metricsService = metricsService;
            _fileScannerService = fileScannerService;
            _cts = new CancellationTokenSource();

            this.Text = "Diplom: System Monitor";
            this.Size = new Size(1200, 850); // Чуть шире для карты диска
            this.StartPosition = FormStartPosition.CenterScreen;

            // Подписки
            _processService.ProcessesUpdated += OnProcessesUpdated;
            _metricsService.MetricsUpdated += OnMetricsUpdated;
            _fileScannerService.ScanCompleted += OnScanCompleted;

        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _ = _processService.StartAsync(_cts.Token);
            _ = _metricsService.StartAsync(_cts.Token);

            // Сканер не запускаем автоматически, ждем кнопку пользователя
            _lblStatus.Text = "Нажмите 'Сканировать диски' для анализа";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cts.Cancel();
            _processService.ProcessesUpdated -= OnProcessesUpdated;
            _metricsService.MetricsUpdated -= OnMetricsUpdated;
            _fileScannerService.ScanCompleted -= OnScanCompleted;
            base.OnFormClosing(e);
        }

        #region UI Initialization

        private void InitializeComponentManual()
        {
            _tabControl = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F) };

            // --- Вкладка 1: Процессы ---
            _tabProcesses = new TabPage("Процессы");
            _dataGridView = new DoubleBufferedDataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToOrderColumns = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(240, 240, 240) }
            };

            _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "PID", Width = 60 });
            _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Имя процесса", Width = 200 });
            _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cpu", HeaderText = "ЦП (%)", Width = 80 });
            _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "MemoryMB", HeaderText = "Память (МБ)", Width = 100 });

            _dataGridView.CellClick += DataGridView_CellClick;

            var contextMenu = new ContextMenuStrip();
            var killItem = new ToolStripMenuItem("Завершить процесс");
            killItem.Click += KillProcessMenuItem_Click;
            contextMenu.Items.Add(killItem);
            _dataGridView.ContextMenuStrip = contextMenu;

            _tabProcesses.Controls.Add(_dataGridView);

            // --- Вкладка 2: Производительность ---
            _tabPerformance = new TabPage("Производительность");
            var layoutPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(10),
                BackColor = Color.White
            };
            layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            _cpuGraph = new RealTimeGraph(capacity: 100)
            {
                Dock = DockStyle.Fill,
                LineColor = Color.FromArgb(0, 150, 0),
                FillColor = Color.FromArgb(80, Color.FromArgb(0, 150, 0)),
                Label = "ЦП",
                BackColor = Color.White,
                MinValue = 0,
                MaxValue = 100
            };

            _ramGraph = new RealTimeGraph(capacity: 100)
            {
                Dock = DockStyle.Fill,
                LineColor = Color.FromArgb(0, 100, 200),
                FillColor = Color.FromArgb(80, Color.FromArgb(0, 100, 200)),
                Label = "Память",
                BackColor = Color.White,
                MinValue = 0,
                MaxValue = 100
            };

            layoutPanel.Controls.Add(_cpuGraph, 0, 0);
            layoutPanel.Controls.Add(_ramGraph, 0, 1);
            _tabPerformance.Controls.Add(layoutPanel);

            // --- Вкладка 3: Диск (Treemap) ---
            _tabDisk = new TabPage("Анализ диска");

            var diskPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            // Верхняя панель с кнопкой
            var topPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 50,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(5)
            };

            _btnScan = new Button
            {
                Text = "🔍 Сканировать диски",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(200, 35),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnScan.FlatAppearance.BorderSize = 0;
            _btnScan.Click += BtnScan_Click;

            _lblStatus = new Label
            {
                Text = "Ожидание...",
                Font = new Font("Segoe UI", 10F),
                AutoSize = true,
                Margin = new Padding(20, 10, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };

            topPanel.Controls.Add(_btnScan);
            topPanel.Controls.Add(_lblStatus);

            // Контрол карты диска
            _treemapControl = new TreemapControl
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };

            diskPanel.Controls.Add(_treemapControl);
            diskPanel.Controls.Add(topPanel);

            _tabDisk.Controls.Add(diskPanel);

            // Сборка вкладок
            _tabControl.TabPages.Add(_tabProcesses);
            _tabControl.TabPages.Add(_tabPerformance);
            _tabControl.TabPages.Add(_tabDisk);

            this.Controls.Add(_tabControl);
        }

        private class DoubleBufferedDataGridView : DataGridView
        {
            public DoubleBufferedDataGridView()
            {
                this.DoubleBuffered = true;
                this.SetStyle(ControlStyles.ResizeRedraw, true);
            }
        }

        #endregion

        #region Handlers (Процессы)

        private void DataGridView_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1 && e.ColumnIndex >= 0)
            {
                var colName = _dataGridView.Columns[e.ColumnIndex].Name;
                if (_sortColumn == colName) _sortAscending = !_sortAscending;
                else { _sortColumn = colName; _sortAscending = true; }
                RefreshGridFromSortedData();
            }
        }

        private void KillProcessMenuItem_Click(object? sender, EventArgs e)
        {
            if (_dataGridView.CurrentRow?.Cells["Id"].Value is int id)
            {
                if (MessageBox.Show($"Завершить процесс PID {id}?", "Подтверждение", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    _processService.KillProcess(id);
                }
            }
        }

        #endregion

        #region Handlers (Диск)

        private void BtnScan_Click(object? sender, EventArgs e)
        {
            if (_btnScan.Enabled)
            {
                _btnScan.Enabled = false;
                _lblStatus.Text = "Сканирование... Пожалуйста, подождите.";
                _treemapControl.Clear();

                // Запуск сканирования в фоне
                _ = _fileScannerService.StartScanAsync(_cts.Token);
            }
        }

        private void OnScanCompleted()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(() => HandleScanResult());
            }
            else
            {
                HandleScanResult();
            }
        }

        private void HandleScanResult()
        {
            var root = _fileScannerService.GetLastResult();
            if (root != null)
            {
                _lblStatus.Text = $"Готово! Найдено файлов на {FormatSize(root.TotalSize)}";
                _treemapControl.Render(root);
            }
            else
            {
                _lblStatus.Text = "Ошибка: данные не получены";
            }
            _btnScan.Enabled = true;
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        #endregion

        #region Logic (Процессы)

        private void OnProcessesUpdated(IEnumerable<ProcessSnapshot> snapshots)
        {
            if (_isUpdatingGrid) return;
            _currentSnapshots = snapshots.ToList();
            RefreshGridFromSortedData();
        }

        private void RefreshGridFromSortedData()
        {
            var sorted = _sortAscending
                ? _currentSnapshots.OrderBy(x => GetSortKey(x))
                : _currentSnapshots.OrderByDescending(x => GetSortKey(x));

            _currentSnapshots = sorted.ToList();

            if (this.InvokeRequired)
                this.Invoke(() => FillGridManual());
            else
                FillGridManual();
        }

        private object GetSortKey(ProcessSnapshot p)
        {
            return _sortColumn switch
            {
                "Id" => p.Id,
                "Name" => p.Name,
                "Cpu" => p.CpuUsage,
                "MemoryMB" => p.MemoryMB,
                _ => 0
            };
        }

        private void FillGridManual()
        {
            _isUpdatingGrid = true;
            try
            {
                _dataGridView.SuspendLayout();

                // 1. Сохраняем позицию скролла
                int firstDisplayedRow = _dataGridView.FirstDisplayedScrollingRowIndex;
                int selectedRow = -1;
                if (_dataGridView.CurrentRow != null)
                    selectedRow = _dataGridView.CurrentRow.Index;

                // 2. Маппинг PID -> Индекс
                var newPidMap = new Dictionary<int, int>();
                for (int i = 0; i < _currentSnapshots.Count; i++)
                {
                    newPidMap[_currentSnapshots[i].Id] = i;
                }

                // 3. Обновляем существующие или помечаем на удаление
                var rowsToRemove = new List<int>();
                for (int i = 0; i < _dataGridView.Rows.Count; i++)
                {
                    var row = _dataGridView.Rows[i];
                    if (row.Cells["Id"].Value is int pid)
                    {
                        if (newPidMap.TryGetValue(pid, out int newIndex))
                        {
                            var p = _currentSnapshots[newIndex];
                            row.Cells["Name"].Value = p.Name;
                            row.Cells["Cpu"].Value = p.CpuUsage.ToString("0.0");
                            row.Cells["MemoryMB"].Value = p.MemoryMB.ToString("N0");
                            row.DefaultCellStyle.BackColor = (p.CpuUsage > 50) ? Color.LightCoral : ((i % 2 == 0) ? Color.White : Color.FromArgb(240, 240, 240));
                        }
                        else
                        {
                            rowsToRemove.Add(i);
                        }
                    }
                }

                // Удаляем
                for (int i = rowsToRemove.Count - 1; i >= 0; i--)
                {
                    _dataGridView.Rows.RemoveAt(rowsToRemove[i]);
                }

                // Добавляем новые, если нужно
                int currentRowCount = _dataGridView.Rows.Count;
                int neededCount = _currentSnapshots.Count;
                if (neededCount > currentRowCount)
                {
                    _dataGridView.Rows.Add(neededCount - currentRowCount);
                }

                // Синхронизируем данные по индексу (так как список отсортирован)
                for (int i = 0; i < _currentSnapshots.Count; i++)
                {
                    if (i < _dataGridView.Rows.Count)
                    {
                        var row = _dataGridView.Rows[i];
                        var p = _currentSnapshots[i];

                        // Проверка: если PID не совпадает (редкий кейн при баге), перезаписываем всё
                        if (row.Cells["Id"].Value is int rowPid && rowPid != p.Id)
                        {
                            row.Cells["Id"].Value = p.Id;
                        }

                        row.Cells["Name"].Value = p.Name;
                        row.Cells["Cpu"].Value = p.CpuUsage.ToString("0.0");
                        row.Cells["MemoryMB"].Value = p.MemoryMB.ToString("N0");
                        row.DefaultCellStyle.BackColor = (p.CpuUsage > 50) ? Color.LightCoral : ((i % 2 == 0) ? Color.White : Color.FromArgb(240, 240, 240));
                    }
                }

                // Восстанавливаем скролл
                if (firstDisplayedRow >= 0 && firstDisplayedRow < _dataGridView.Rows.Count)
                {
                    _dataGridView.FirstDisplayedScrollingRowIndex = firstDisplayedRow;
                }
                if (selectedRow >= 0 && selectedRow < _dataGridView.Rows.Count)
                {
                    _dataGridView.Rows[selectedRow].Selected = true;
                    _dataGridView.CurrentCell = _dataGridView.Rows[selectedRow].Cells[0];
                }

                _dataGridView.ResumeLayout();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Grid Error: {ex.Message}");
            }
            finally
            {
                _isUpdatingGrid = false;
            }
        }

        private void OnMetricsUpdated(SystemMetrics metrics)
        {
            float cpu = (float)metrics.TotalCpuUsage;
            float ram = 0;
            if (metrics.TotalPhysicalMemory > 0)
            {
                long used = metrics.TotalPhysicalMemory - metrics.AvailablePhysicalMemory;
                ram = (float)(used * 100.0 / metrics.TotalPhysicalMemory);
            }

            if (this.InvokeRequired)
            {
                this.Invoke(() => {
                    _cpuGraph.AddValue(cpu);
                    _ramGraph.AddValue(ram);
                });
            }
            else
            {
                _cpuGraph.AddValue(cpu);
                _ramGraph.AddValue(ram);
            }
        }

        #endregion
    }
}