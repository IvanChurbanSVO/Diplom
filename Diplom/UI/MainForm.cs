using Diplom.Core.Interfaces;
using Diplom.Core.Models;
using Diplom.UI.Controls;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Net.NetworkInformation;
using System.Collections.Concurrent;
using System.IO;
using System.Diagnostics;
using System.Management; // Для WMI
using System.Runtime.InteropServices;

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
        private TabPage _tabNetwork;
        private TabPage _tabStartup;
        private TabPage _tabSystem;      
        private TabPage _tabDisk;

        private DoubleBufferedDataGridView _dataGridView;
        private RealTimeGraph _cpuGraph;
        private RealTimeGraph _ramGraph;

        // Элементы для вкладки Сети
        private DoubleBufferedDataGridView _networkGrid;
        private System.Windows.Forms.Timer _networkTimer;
        private ConcurrentDictionary<string, NetworkConnectionInfo> _networkConnections = new();

        // Элементы для вкладки Автозагрузка
        private DoubleBufferedDataGridView _startupGrid;
        private Button _btnDisableStartup;
        private List<StartupItem> _startupItems = new();

        // Элементы для вкладки О системе
        private DoubleBufferedDataGridView _systemGrid;
        private List<SystemInfoItem> _systemItems = new();

        private TreemapControl _treemapControl;
        private Button _btnScan;
        private Label _lblStatus;

        private List<ProcessSnapshot> _currentSnapshots = new();
        private string _sortColumn = "Cpu";
        private bool _sortAscending = false;
        private bool _isUpdatingGrid = false;

        private string _cpuModelCache = "";

        public MainForm(
            IProcessMonitorService processService,
            ISystemMetricsService metricsService,
            IFileScannerService fileScannerService)
        {
            InitializeComponentManual();

            _processService = processService;
            _metricsService = metricsService;
            _fileScannerService = fileScannerService;
            _cts = new CancellationTokenSource();

            this.Text = "Diplom: System Monitor";
            this.Size = new Size(1400, 950); 
            this.StartPosition = FormStartPosition.CenterScreen;

            _cpuModelCache = GetCpuModel();

            _processService.ProcessesUpdated += OnProcessesUpdated;
            _metricsService.MetricsUpdated += OnMetricsUpdated;
            _fileScannerService.ScanCompleted += OnScanCompleted;

            // Таймер сети
            _networkTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _networkTimer.Tick += (s, e) => UpdateNetworkGrid();
            _networkTimer.Start();

            // Загрузка данных автозагрузки и системы
            LoadStartupItems();

            // Безопасный вызов загрузки информации о системе
            if (_systemGrid != null && _systemItems != null)
            {
                LoadSystemInfo();
            }
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _ = _processService.StartAsync(_cts.Token);
            _ = _metricsService.StartAsync(_cts.Token);
            _lblStatus.Text = "Нажмите 'Сканировать диски' для анализа";

            UpdateNetworkGrid();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cts.Cancel();
            if (_networkTimer != null)
            {
                _networkTimer.Stop();
                _networkTimer.Dispose();
            }

            _processService.ProcessesUpdated -= OnProcessesUpdated;
            _metricsService.MetricsUpdated -= OnMetricsUpdated;
            _fileScannerService.ScanCompleted -= OnScanCompleted;
            base.OnFormClosing(e);
        }

        private string GetCpuModel()
        {
            if (!string.IsNullOrEmpty(_cpuModelCache)) return _cpuModelCache;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (key != null)
                {
                    var name = key.GetValue("ProcessorNameString")?.ToString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        _cpuModelCache = name.Trim();
                        if (_cpuModelCache.Length > 45) _cpuModelCache = _cpuModelCache.Substring(0, 42) + "...";
                        return _cpuModelCache;
                    }
                }
            }
            catch { }
            _cpuModelCache = "Загрузка ЦП";
            return _cpuModelCache;
        }

        private void InitializeComponentManual()
        {
            // Шрифты побольше
            Font tabFont = new Font("Segoe UI", 11F, FontStyle.Regular);
            Font gridHeaderFont = new Font("Segoe UI", 10F, FontStyle.Bold);
            Font gridCellFont = new Font("Segoe UI", 10F, FontStyle.Regular);
            Font buttonFont = new Font("Segoe UI", 11F, FontStyle.Bold);

            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = tabFont,
                ItemSize = new Size(170, 55), // Вкладки выше
                SizeMode = TabSizeMode.Fixed
            };
            _tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabControl.DrawItem += TabControl_DrawItem;

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
                BackgroundColor = Color.FromArgb(45, 45, 60),
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(50, 50, 65) },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(30, 30, 40),
                    ForeColor = Color.Cyan,
                    Font = gridHeaderFont
                },
                DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.White, SelectionBackColor = Color.DarkSlateBlue, Font = gridCellFont }
            };
            _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "PID", Width = 70 });
            _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Имя процесса", Width = 220 });
            _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cpu", HeaderText = "ЦП (%)", Width = 90 });
            _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "MemoryMB", HeaderText = "Память (МБ)", Width = 110 });
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
                Padding = new Padding(15),
                BackColor = Color.FromArgb(30, 30, 40)
            };
            layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            _cpuGraph = new RealTimeGraph(capacity: 100)
            {
                Dock = DockStyle.Fill,
                LineColor = Color.Lime,
                GlowColor = Color.Lime,
                FillColor = Color.FromArgb(40, Color.Lime),
                Label = "ЦП",
                BackColor = Color.FromArgb(30, 30, 40),
                ForeColor = Color.White,
                MinValue = 0,
                MaxValue = 100,
                EnableGlow = true
            };
            // Увеличим шрифт внутри графика через свойство, если есть, или оставим дефолт (он крупный)

            _ramGraph = new RealTimeGraph(capacity: 100)
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 40),
                ForeColor = Color.White,
                MinValue = 0,
                MaxValue = 100,
                EnableGlow = true,
                Label = "Оперативная память"
            };
            _ramGraph.LineColor = Color.DeepSkyBlue;
            _ramGraph.GlowColor = Color.DeepSkyBlue;
            _ramGraph.FillColor = Color.FromArgb(50, Color.DeepSkyBlue);
            // Принудительное обновление ресурсов
            var method = _ramGraph.GetType().GetMethod("UpdateResources",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method != null) method.Invoke(_ramGraph, null);
            _ramGraph.Invalidate();

            layoutPanel.Controls.Add(_cpuGraph, 0, 0);
            layoutPanel.Controls.Add(_ramGraph, 0, 1);
            _tabPerformance.Controls.Add(layoutPanel);

            // --- Вкладка 3: Сеть ---
            _tabNetwork = new TabPage("Сеть");
            _networkGrid = new DoubleBufferedDataGridView
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
                BackgroundColor = Color.FromArgb(45, 45, 60),
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(50, 50, 65) },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(30, 30, 40),
                    ForeColor = Color.Orange,
                    Font = gridHeaderFont
                },
                DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.White, SelectionBackColor = Color.DarkSlateBlue, Font = gridCellFont }
            };
            _networkGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Protocol", HeaderText = "Протокол", Width = 80 });
            _networkGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LocalAddress", HeaderText = "Локальный адрес", Width = 200 });
            _networkGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteAddress", HeaderText = "Удаленный адрес", Width = 200 });
            _networkGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "Состояние", Width = 110 });
            _networkGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProcessName", HeaderText = "Процесс", Width = 160 });
            _tabNetwork.Controls.Add(_networkGrid);

            // --- Вкладка 4: Автозагрузка ---
            _tabStartup = new TabPage("Автозагрузка");
            var startupPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            var topStartupPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 60,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(5),
                BackColor = Color.FromArgb(30, 30, 40)
            };

            _btnDisableStartup = new Button
            {
                Text = "🚫 Отключить выбранное",
                Font = buttonFont,
                Size = new Size(220, 40),
                BackColor = Color.FromArgb(180, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnDisableStartup.FlatAppearance.BorderSize = 0;
            _btnDisableStartup.Click += BtnDisableStartup_Click;

            var lblStartupInfo = new Label
            {
                Text = "Внимание: для удаления записей требуются права администратора.",
                Font = new Font("Segoe UI", 10F),
                AutoSize = true,
                Margin = new Padding(20, 15, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.Gray
            };

            topStartupPanel.Controls.Add(_btnDisableStartup);
            topStartupPanel.Controls.Add(lblStartupInfo);

            _startupGrid = new DoubleBufferedDataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToOrderColumns = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ReadOnly = false,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                BackgroundColor = Color.FromArgb(45, 45, 60),
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(50, 50, 65) },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(30, 30, 40),
                    ForeColor = Color.Magenta,
                    Font = gridHeaderFont
                },
                DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.White, SelectionBackColor = Color.DarkSlateBlue, Font = gridCellFont }
            };
            _startupGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Имя", Width = 220 });
            _startupGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Path", HeaderText = "Путь к файлу", Width = 450 });
            _startupGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Источник", Width = 170 });

            startupPanel.Controls.Add(_startupGrid);
            startupPanel.Controls.Add(topStartupPanel);
            _tabStartup.Controls.Add(startupPanel);

            // --- Вкладка 5: О СИСТЕМЕ  ---
            _tabSystem = new TabPage("О системе");
            var systemPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = Color.FromArgb(30, 30, 40) };

            _systemGrid = new DoubleBufferedDataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToOrderColumns = false, // Запретим сортировку для инфо
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                BackgroundColor = Color.FromArgb(45, 45, 60),
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(50, 50, 65) },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(30, 30, 40),
                    ForeColor = Color.YellowGreen,
                    Font = gridHeaderFont
                },
                DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.White, SelectionBackColor = Color.DarkSlateBlue, Font = gridCellFont }
            };
            _systemGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Property", HeaderText = "Параметр", Width = 250 });
            _systemGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Значение", Width = 600 });

            systemPanel.Controls.Add(_systemGrid);
            _tabSystem.Controls.Add(systemPanel);

            // --- Вкладка 6: Диск ---
            _tabDisk = new TabPage("Анализ диска");
            var diskPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = Color.FromArgb(30, 30, 40) };
            var topPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 60,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(5),
                BackColor = Color.FromArgb(30, 30, 40)
            };
            _btnScan = new Button
            {
                Text = "🔍 Сканировать диски",
                Font = buttonFont,
                Size = new Size(220, 40),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnScan.FlatAppearance.BorderSize = 0;
            _btnScan.Click += BtnScan_Click;
            _lblStatus = new Label
            {
                Text = "Ожидание...",
                Font = new Font("Segoe UI", 11F),
                AutoSize = true,
                Margin = new Padding(20, 15, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.White
            };
            topPanel.Controls.Add(_btnScan);
            topPanel.Controls.Add(_lblStatus);
            _treemapControl = new TreemapControl { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 60) };
            diskPanel.Controls.Add(_treemapControl);
            diskPanel.Controls.Add(topPanel);
            _tabDisk.Controls.Add(diskPanel);

            // Сборка вкладок
            _tabControl.TabPages.Add(_tabProcesses);
            _tabControl.TabPages.Add(_tabPerformance);
            _tabControl.TabPages.Add(_tabNetwork);
            _tabControl.TabPages.Add(_tabStartup);
            _tabControl.TabPages.Add(_tabSystem); 
            _tabControl.TabPages.Add(_tabDisk);
            this.Controls.Add(_tabControl);
        }

        // Отрисовка вкладок
        private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (sender == null) return;
            TabControl tabControl = (TabControl)sender;
            if (e.Index < 0 || e.Index >= tabControl.TabPages.Count) return;

            TabPage page = tabControl.TabPages[e.Index];
            Rectangle bounds = e.Bounds;

            bool isSelected = (e.Index == tabControl.SelectedIndex);

            using (var brush = new SolidBrush(isSelected ? Color.FromArgb(0, 120, 215) : Color.FromArgb(40, 40, 50)))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }

            if (isSelected)
            {
                using (var pen = new Pen(Color.Cyan, 3))
                {
                    e.Graphics.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
                }
            }

            StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var font = new Font("Segoe UI", 11F, isSelected ? FontStyle.Bold : FontStyle.Regular))
            using (var textBrush = new SolidBrush(Color.White))
            {
                e.Graphics.DrawString(page.Text, font, textBrush, bounds, sf);
            }
        }

        private class DoubleBufferedDataGridView : DataGridView
        {
            public DoubleBufferedDataGridView() { this.DoubleBuffered = true; this.SetStyle(ControlStyles.ResizeRedraw, true); }
        }

        // --- Логика Процессов ---
        private void DataGridView_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1 && e.ColumnIndex >= 0)
            {
                var colName = _dataGridView.Columns[e.ColumnIndex].Name;
                if (_sortColumn == colName) _sortAscending = !_sortAscending;
                else { _sortColumn = colName; _sortAscending = false; }
                RefreshGridFromSortedData();
            }
        }

        private void KillProcessMenuItem_Click(object? sender, EventArgs e)
        {
            if (_dataGridView.CurrentRow?.Cells["Id"].Value is int id)
            {
                if (MessageBox.Show($"Завершить процесс PID {id}?", "Подтверждение", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    _processService.KillProcess(id);
            }
        }

        // --- Логика Сети ---
        private void UpdateNetworkGrid()
        {
            if (_networkGrid == null || _networkGrid.InvokeRequired)
            {
                if (_networkGrid != null) _networkGrid.Invoke(UpdateNetworkGrid);
                return;
            }

            _networkGrid.SuspendLayout();
            var newConnections = new ConcurrentDictionary<string, NetworkConnectionInfo>();

            try
            {
                var ipProps = IPGlobalProperties.GetIPGlobalProperties();
                var connections = ipProps.GetActiveTcpConnections();

                foreach (var conn in connections)
                {
                    string localAddr = $"{conn.LocalEndPoint.Address}:{conn.LocalEndPoint.Port}";
                    string remoteAddr = $"{conn.RemoteEndPoint.Address}:{conn.RemoteEndPoint.Port}";
                    string key = $"{localAddr}->{remoteAddr}";

                    string processName = "Служба / Система";
                    newConnections[key] = new NetworkConnectionInfo
                    {
                        Protocol = "TCP",
                        LocalAddress = localAddr,
                        RemoteAddress = remoteAddr,
                        State = conn.State.ToString(),
                        ProcessName = processName
                    };
                }
                _networkConnections = newConnections;
            }
            catch { }

            _networkGrid.Rows.Clear();

            Color bgDark = Color.FromArgb(45, 45, 60);
            Color bgAlt = Color.FromArgb(50, 50, 65);
            Color textEstablished = Color.Lime;
            Color textListen = Color.Gold;
            Color textOther = Color.LightGray;

            int rowIndex = 0;
            foreach (var conn in _networkConnections.Values.OrderByDescending(c => c.LocalAddress))
            {
                _networkGrid.Rows.Add(conn.Protocol, conn.LocalAddress, conn.RemoteAddress, conn.State, conn.ProcessName);
                var row = _networkGrid.Rows[rowIndex];
                row.DefaultCellStyle.BackColor = (rowIndex % 2 == 0) ? bgDark : bgAlt;
                row.DefaultCellStyle.SelectionBackColor = Color.DarkSlateBlue;

                if (conn.State == "Established")
                {
                    row.DefaultCellStyle.ForeColor = textEstablished;
                    row.DefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
                }
                else if (conn.State == "Listen")
                {
                    row.DefaultCellStyle.ForeColor = textListen;
                    row.DefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
                }
                else
                {
                    row.DefaultCellStyle.ForeColor = textOther;
                    row.DefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
                }
                rowIndex++;
            }
            _networkGrid.ResumeLayout();
        }

        // --- Логика Автозагрузки ---
        private void LoadStartupItems()
        {
            if (_startupGrid == null || _startupItems == null) return;

            _startupItems.Clear();
            _startupGrid.Rows.Clear();

            ReadRegistryKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Пользователь (HKCU)");
            ReadRegistryKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "Система (HKLM)");

            RefreshStartupGrid();
        }

        private void ReadRegistryKey(RegistryKey root, string subKey, string source)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key != null)
                {
                    foreach (var valueName in key.GetValueNames())
                    {
                        var value = key.GetValue(valueName)?.ToString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            _startupItems.Add(new StartupItem
                            {
                                Name = valueName,
                                Path = value,
                                Source = source,
                                IsRegistry = true,
                                ValueName = valueName,
                                RootKey = root == Registry.CurrentUser ? "HKCU" : "HKLM"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading {source}: {ex.Message}");
            }
        }

        private void RefreshStartupGrid()
        {
            if (_startupGrid == null || _startupItems == null) return;

            _startupGrid.Rows.Clear();
            Color bgDark = Color.FromArgb(45, 45, 60);
            Color bgAlt = Color.FromArgb(50, 50, 65);

            int i = 0;
            foreach (var item in _startupItems)
            {
                int idx = _startupGrid.Rows.Add(item.Name, item.Path, item.Source);
                var row = _startupGrid.Rows[idx];
                row.DefaultCellStyle.BackColor = (i % 2 == 0) ? bgDark : bgAlt;
                row.DefaultCellStyle.SelectionBackColor = Color.DarkSlateBlue;
                i++;
            }
        }

        private void BtnDisableStartup_Click(object? sender, EventArgs e)
        {
            if (_startupGrid == null || _startupGrid.CurrentRow == null || _startupItems == null)
            {
                MessageBox.Show("Выберите элемент для отключения.", "Инфо", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedItem = _startupItems[_startupGrid.CurrentRow.Index];

            if (!selectedItem.IsRegistry)
            {
                MessageBox.Show("Удаление из папок пока не поддерживается.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show($"Вы уверены, что хотите удалить запись \"{selectedItem.Name}\" из автозагрузки?",
                "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                try
                {
                    RegistryKey root = (selectedItem.RootKey == "HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
                    using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    key?.DeleteValue(selectedItem.ValueName);

                    MessageBox.Show("Запись успешно удалена!", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    LoadStartupItems();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при удалении: {ex.Message}\nЗапустите программу от имени администратора.",
                        "Ошибка доступа", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // --- Логика О СИСТЕМЕ ---
        private void LoadSystemInfo()
        {
            if (_systemGrid == null || _systemItems == null) return;

            _systemItems.Clear();
            _systemGrid.Rows.Clear();

            Color bgDark = Color.FromArgb(45, 45, 60);
            Color bgAlt = Color.FromArgb(50, 50, 65);
            int i = 0;

            try
            {
                // 1. Процессор
                string cpuName = "Неизвестно";
                uint coreCount = 0;
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores FROM Win32_Processor");
                    foreach (var mo in searcher.Get())
                    {
                        if (cpuName == "Неизвестно") cpuName = mo["Name"]?.ToString() ?? "Неизвестно";
                        if (coreCount == 0 && mo["NumberOfCores"] != null) coreCount = Convert.ToUInt32(mo["NumberOfCores"]);
                    }
                }
                catch { cpuName = "Недоступно"; }
                AddRow(_systemGrid, "Процессор", $"{cpuName} ({coreCount} яд.)", ref i, bgDark, bgAlt);

                // 2. ОЗУ
                string ramSize = "Неизвестно";
                try
                {
                    ulong totalBytes = 0;
                    using var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
                    foreach (var mo in searcher.Get())
                    {
                        if (mo["Capacity"] != null) totalBytes += Convert.ToUInt64(mo["Capacity"]);
                    }
                    double gb = totalBytes / (1024.0 * 1024.0 * 1024.0);
                    ramSize = $"{gb:F2} ГБ";
                }
                catch { ramSize = "Недоступно"; }
                AddRow(_systemGrid, "Оперативная память", ramSize, ref i, bgDark, bgAlt);

                // 3. Видеокарты
                string gpuList = "Не найдено";
                try
                {
                    var gpus = new List<string>();
                    using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                    foreach (var mo in searcher.Get())
                    {
                        string name = mo["Name"]?.ToString();
                        if (!string.IsNullOrEmpty(name)) gpus.Add(name);
                    }
                    if (gpus.Count > 0) gpuList = string.Join("\n", gpus);
                }
                catch { gpuList = "Недоступно"; }
                AddRow(_systemGrid, "Видеокарта(ы)", gpuList, ref i, bgDark, bgAlt);

                // 4. Материнская плата
                string moboInfo = "Неизвестно";
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                    foreach (var mo in searcher.Get())
                    {
                        string m = mo["Manufacturer"]?.ToString() ?? "";
                        string p = mo["Product"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(m) || !string.IsNullOrEmpty(p)) moboInfo = $"{m} {p}".Trim();
                    }
                }
                catch { moboInfo = "Недоступно"; }
                AddRow(_systemGrid, "Материнская плата", moboInfo, ref i, bgDark, bgAlt);

                // 5. BIOS
                string biosInfo = "Неизвестно";
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
                    foreach (var mo in searcher.Get())
                    {
                        string ver = mo["SMBIOSBIOSVersion"]?.ToString() ?? "";
                        string dateRaw = mo["ReleaseDate"]?.ToString() ?? "";
                        string date = (!string.IsNullOrEmpty(dateRaw) && dateRaw.Length >= 8) ? dateRaw.Substring(0, 8) : "";
                        if (!string.IsNullOrEmpty(ver) || !string.IsNullOrEmpty(date)) biosInfo = $"{ver} ({date})".Trim();
                    }
                }
                catch { biosInfo = "Недоступно"; }
                AddRow(_systemGrid, "Версия BIOS", biosInfo, ref i, bgDark, bgAlt);

                // 6. ОС
                string osName = Environment.OSVersion.VersionString;
                string arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
                AddRow(_systemGrid, "Операционная система", $"{osName} ({arch})", ref i, bgDark, bgAlt);

                // 7. .NET
                string netVer = RuntimeInformation.FrameworkDescription;
                AddRow(_systemGrid, ".NET Версия", netVer, ref i, bgDark, bgAlt);

                // 8. Время работы
                string uptimeStr = "Недоступно";
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem WHERE Primary='true'");
                    foreach (var mo in searcher.Get())
                    {
                        string timeStr = mo["LastBootUpTime"]?.ToString();
                        if (!string.IsNullOrEmpty(timeStr))
                        {
                            DateTime lastBoot = ManagementDateTimeConverter.ToDateTime(timeStr);
                            TimeSpan up = DateTime.Now - lastBoot;
                            uptimeStr = $"{up.Days} дн. {up.Hours} ч. {up.Minutes} мин.";
                        }
                    }
                }
                catch { uptimeStr = "Вычисление..."; }
                AddRow(_systemGrid, "Время работы", uptimeStr, ref i, bgDark, bgAlt);

                // 9. Пользователь
                AddRow(_systemGrid, "Пользователь", $"{Environment.UserName}@{Environment.MachineName}", ref i, bgDark, bgAlt);
            }
            catch (Exception ex)
            {
                AddRow(_systemGrid, "Ошибка сбора данных", ex.Message, ref i, bgDark, bgAlt);
            }
        }

        private void AddRow(DataGridView grid, string prop, string val, ref int index, Color dark, Color alt)
        {
            if (grid == null) return;
            int idx = grid.Rows.Add(prop, val);
            var row = grid.Rows[idx];
            row.DefaultCellStyle.BackColor = (index % 2 == 0) ? dark : alt;
            row.DefaultCellStyle.SelectionBackColor = Color.DarkSlateBlue;
            row.DefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            index++;
        }

        // --- Логика Диска ---
        private void BtnScan_Click(object? sender, EventArgs e)
        {
            if (_btnScan.Enabled)
            {
                _btnScan.Enabled = false;
                _lblStatus.Text = "Сканирование... Пожалуйста, подождите.";
                _treemapControl.Clear();
                _ = _fileScannerService.StartScanAsync(_cts.Token);
            }
        }

        private void OnScanCompleted()
        {
            if (this.InvokeRequired) this.Invoke(() => HandleScanResult());
            else HandleScanResult();
        }

        private void HandleScanResult()
        {
            var root = _fileScannerService.GetLastResult();
            if (root != null)
            {
                long gb = root.TotalSize / (1024 * 1024 * 1024);
                _lblStatus.Text = $"Готово! Найдено файлов на {gb} ГБ";
                _treemapControl.Render(root);
            }
            else _lblStatus.Text = "Ошибка: данные не получены";
            _btnScan.Enabled = true;
        }

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
            if (this.InvokeRequired) this.Invoke(() => FillGridManual());
            else FillGridManual();
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
                int firstDisplayedRow = _dataGridView.FirstDisplayedScrollingRowIndex;
                int selectedRow = -1;
                if (_dataGridView.CurrentRow != null) selectedRow = _dataGridView.CurrentRow.Index;

                var newPidMap = new Dictionary<int, int>();
                for (int i = 0; i < _currentSnapshots.Count; i++) newPidMap[_currentSnapshots[i].Id] = i;

                var rowsToRemove = new List<int>();
                for (int i = 0; i < _dataGridView.Rows.Count; i++)
                {
                    var row = _dataGridView.Rows[i];
                    if (row.Cells["Id"].Value is int existingPid)
                    {
                        if (newPidMap.TryGetValue(existingPid, out int newIndex))
                        {
                            var p = _currentSnapshots[newIndex];
                            row.Cells["Id"].Value = p.Id;
                            row.Cells["Name"].Value = p.Name;
                            row.Cells["Cpu"].Value = p.CpuUsage.ToString("0.0");
                            row.Cells["MemoryMB"].Value = p.MemoryMB.ToString("N0");
                            row.DefaultCellStyle.BackColor = (p.CpuUsage > 50) ? Color.FromArgb(80, 30, 30) : ((i % 2 == 0) ? Color.FromArgb(45, 45, 60) : Color.FromArgb(50, 50, 65));
                        }
                        else rowsToRemove.Add(i);
                    }
                    else rowsToRemove.Add(i);
                }
                for (int i = rowsToRemove.Count - 1; i >= 0; i--) _dataGridView.Rows.RemoveAt(rowsToRemove[i]);

                int currentRowCount = _dataGridView.Rows.Count;
                int neededCount = _currentSnapshots.Count;
                if (neededCount > currentRowCount) _dataGridView.Rows.Add(neededCount - currentRowCount);

                for (int i = 0; i < _currentSnapshots.Count; i++)
                {
                    if (i < _dataGridView.Rows.Count)
                    {
                        var row = _dataGridView.Rows[i];
                        var p = _currentSnapshots[i];
                        row.Cells["Id"].Value = p.Id;
                        row.Cells["Name"].Value = p.Name;
                        row.Cells["Cpu"].Value = p.CpuUsage.ToString("0.0");
                        row.Cells["MemoryMB"].Value = p.MemoryMB.ToString("N0");
                        row.DefaultCellStyle.BackColor = (p.CpuUsage > 50) ? Color.FromArgb(80, 30, 30) : ((i % 2 == 0) ? Color.FromArgb(45, 45, 60) : Color.FromArgb(50, 50, 65));
                    }
                }

                if (firstDisplayedRow >= 0 && firstDisplayedRow < _dataGridView.Rows.Count)
                    _dataGridView.FirstDisplayedScrollingRowIndex = firstDisplayedRow;
                if (selectedRow >= 0 && selectedRow < _dataGridView.Rows.Count)
                {
                    _dataGridView.Rows[selectedRow].Selected = true;
                    _dataGridView.CurrentCell = _dataGridView.Rows[selectedRow].Cells[0];
                }
                _dataGridView.ResumeLayout();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Grid Error: {ex.Message}"); }
            finally { _isUpdatingGrid = false; }
        }

        private void OnMetricsUpdated(SystemMetrics metrics)
        {
            float cpu = (float)metrics.TotalCpuUsage;
            float ramPercent = 0;
            string ramInfoText = "";

            if (metrics.TotalPhysicalMemory > 0)
            {
                long usedRamBytes = (long)metrics.TotalPhysicalMemory - (long)metrics.AvailablePhysicalMemory;
                ramPercent = (float)(usedRamBytes * 100.0 / (long)metrics.TotalPhysicalMemory);
                double usedGb = usedRamBytes / (1024.0 * 1024.0 * 1024.0);
                double totalGb = (double)((long)metrics.TotalPhysicalMemory / (1024.0 * 1024.0 * 1024.0));
                ramInfoText = $"Использовано: {usedGb:F2} ГБ из {totalGb:F1} ГБ";
            }

            string cpuModel = GetCpuModel();

            if (this.InvokeRequired)
            {
                this.Invoke(() => {
                    _cpuGraph.UpdateData(cpu, cpuModel);
                    _ramGraph.UpdateData(ramPercent, ramInfoText);
                });
            }
            else
            {
                _cpuGraph.UpdateData(cpu, cpuModel);
                _ramGraph.UpdateData(ramPercent, ramInfoText);
            }
        }

        // Вспомогательные классы
        private class NetworkConnectionInfo
        {
            public string Protocol { get; set; } = "";
            public string LocalAddress { get; set; } = "";
            public string RemoteAddress { get; set; } = "";
            public string State { get; set; } = "";
            public string ProcessName { get; set; } = "";
        }

        private class StartupItem
        {
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
            public string Source { get; set; } = "";
            public bool IsRegistry { get; set; }
            public string ValueName { get; set; } = "";
            public string RootKey { get; set; } = "";
        }

        private class SystemInfoItem
        {
            public string Property { get; set; } = "";
            public string Value { get; set; } = "";
        }
    }
}