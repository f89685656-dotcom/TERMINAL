using HidSharp;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TERMINAL.Models;

namespace TERMINAL
{
    public partial class MainWindow : Window
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly object _streamLock = new object();

        private DispatcherTimer? _timer;
        private DispatcherTimer? _furnaceTempTimer;
        private DispatcherTimer? _platinaTimer;
        private DispatcherTimer? _batchStatusTimer;

        private string? _selectedTargetPressure;
        private int _currentStepIndex = 0;
        private int _completedSteps = 0;

        private bool _isRunning = false;
        private bool _stopRequested = false;

        private volatile bool _pauseRequested = false;

        private DateTime _totalTimerStart = DateTime.MinValue;

        private TcpListener? _broadcastServer;
        private readonly ObservableCollection<ConnectedClient> _connectedClients = new();
        private readonly object _clientsLock = new();
        private readonly CancellationTokenSource _cts = new();

        private DispatcherTimer? _clientsUiTimer;

        private const int OfflineRetentionSeconds = 60;
        private const int ClientDeadThresholdSeconds = 120;

        private const string DeviceIp = "192.168.0.159";
        private const int DevicePort = 8888;

        private const string FurnaceIp = "192.168.0.167";
        private const int FurnacePort = 502;
        private const ushort FurnaceSetPointRegister = 201;
        private const ushort FurnaceCurrentTempRegister = 205;
        private const ushort FurnaceModeRegister = 200;
        private const ushort FurnaceModeManual = 2;
        private const ushort FurnaceModeIdle = 1;

        private const int PlatinaVid = 0xFFFF;
        private const int PlatinaPid = 0x0009;
        private const string PlatinaRequest = "pass2 meas2? 2\n\0";

        private double _platinaLastValue = double.NaN;
        private DateTime _platinaLastSuccess = DateTime.MinValue;
        private int _platinaFailCount = 0;

        private double _pdaLastValue = double.NaN;
        private DateTime _pdaLastSuccess = DateTime.MinValue;
        private string _pdaSourceSid = "";

        private double _ptkLastValue = double.NaN;
        private DateTime _ptkLastSuccess = DateTime.MinValue;
        private string _ptkSourceSid = "";

        private DispatcherTimer? _sensorUiTimer;

        private const double SensorStaleSeconds = 5.0;
        private const double SktbAllDeadSeconds = 5.0;

        private int BroadcastPort = 9990;

        private DateTime _stabilizationTimerStart = DateTime.MinValue;
        private bool _stabilizationTimerActive = false;
        private const double FurnaceStabilizationTolerance = 0.1;

        private DateTime _stabilizationOutOfRangeSince = DateTime.MinValue;
        private const int StabilizationOutOfRangeGraceSeconds = 60;

        private double _lastFurnaceSetPoint = double.NaN;
        private float _lastValidFurnaceSetPoint = 0f;

        private string? _lastSavedFilePath = null;

        private const int ConnectionLostThreshold = 5;

        private int _agkFailCount = 0;
        private int _furnaceTempFailCount = 0;
        private int _furnaceSetPointFailCount = 0;
        private volatile bool _agkBatchBusy = false;

        private readonly HashSet<string> _saveResponses = new();
        private readonly object _saveLock = new();

        private readonly HashSet<string> _clearResponses = new();
        private readonly object _clearLock = new();

        private ObservableCollection<StepItem> _stepItems = new();

        private bool _uiLoaded = false;

        // ── Общие настройки пакета ──
        private bool _batchKosaEnabled = false;
        private int _batchKosaTimerSeconds = 25;

        private string _currentPressureUnitsLabel = "";
        private double? _lastKnownPressure = null;

        private readonly ObservableCollection<TemperatureJob> _batchJobs = new();
        private bool _batchRunning = false;
        private bool _batchStopRequested = false;
        private TaskCompletionSource<bool>? _batchRunDoneSignal;
        private readonly ObservableCollection<BatchPreset> _batchPresets = new();

        private static readonly Brush StartButtonBatchBrush =
            new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x8B));

        private Settings? _loadedSettings = null;

        private DateTime _lastAgkStatusRequest = DateTime.MinValue;
        private const int AgkStatusRequestIntervalMs = 1000;

        // ── Журнал событий ──
        private readonly ObservableCollection<LogEntry> _logEntries = new();
        private Window? _logWindow = null;
        private System.Windows.Controls.ListBox? _logListBox = null;
        private System.Windows.Controls.TextBox? _logFilterBox = null;
        private const int LogMaxEntries = 5000;
        private System.Windows.Controls.TextBlock? _logCounterText = null;

        public MainWindow()
        {
            InitializeComponent();

            if (StartButtonBatchBrush is SolidColorBrush sb2 && sb2.CanFreeze)
                sb2.Freeze();

            _uiLoaded = false;

            LoadTargetPressures();
            LoadTimerSets();
            LoadSettings();

            ApplyWindowPositionOrFallback();

            _uiLoaded = true;

            ApplyLoadedSettingsToUi();

            if (BatchList != null)
                BatchList.ItemsSource = _batchJobs;

            if (ClientsList != null)
                ClientsList.ItemsSource = _connectedClients;

            UpdateKosaBadge();

            RenumberBatchJobs();
            UpdateStartButtonMode();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (VersionText != null)
                    VersionText.Text = GetAppVersion();

                AppendLog($"Программа запущена, версия {GetAppVersion()}", Brushes.Green);

                AskForStartupMode();
                StartBroadcastServer();

                _ = Task.Run(ConnectToDeviceLoopAsync);

                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timer.Tick += Timer_Tick;
                _timer.Start();

                await Task.Delay(500);
                await RefreshFurnaceSetPointAsync();

                _furnaceTempTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _furnaceTempTimer.Tick += async (s, e) =>
                {
                    await RefreshFurnaceCurrentTempAsync();
                    await RefreshFurnaceSetPointAsync();
                    UpdateStabilizationTimer();
                };
                _furnaceTempTimer.Start();

                await RefreshFurnaceCurrentTempAsync();
                await RefreshFurnaceSetPointAsync();
                UpdateStabilizationTimer();

                _platinaTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _platinaTimer.Tick += async (s, e) => await RefreshPlatinaAsync();
                _platinaTimer.Start();

                await RefreshPlatinaAsync();

                _sensorUiTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _sensorUiTimer.Tick += (s, e) => RefreshSensorUiFromClients();
                _sensorUiTimer.Start();

                RefreshSensorUiFromClients();
                UpdateSktbPanelVisibility();
                UpdateStartButtonMode();
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка инициализации окна: {ex.Message}", Brushes.OrangeRed);
            }
        }



        // ═══════════════════════════════════════════════════════════════════
        // ЖУРНАЛ СОБЫТИЙ
        // ═══════════════════════════════════════════════════════════════════

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isRunning || _batchRunning)
            {
                string mode = _batchRunning ? "пакетный режим" : "«Градуировка»";

                var result = MessageBox.Show(
                    $"Сейчас идёт {mode}.\n\n" +
                    "Вы уверены, что хотите закрыть программу?\n" +
                    "Работа будет прервана, данные не будут сохранены.",
                    "Подтверждение закрытия",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                _stopRequested = true;
                _batchStopRequested = true;
                _pauseRequested = false;
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            try { SaveWindowPositionToSettings(); } catch { }
            try { SaveSettings(); } catch { }
            _stopRequested = true;
            _pauseRequested = false;
            _batchStopRequested = true;
            _timer?.Stop();
            _clientsUiTimer?.Stop();
            _furnaceTempTimer?.Stop();
            _platinaTimer?.Stop();
            _sensorUiTimer?.Stop();
            _cts.Cancel();

            lock (_streamLock)
            {
                try { _stream?.Close(); } catch { }
                try { _client?.Close(); } catch { }
                _stream = null; _client = null;
            }

            try
            {
                lock (_clientsLock)
                {
                    foreach (var c in _connectedClients) { try { c.TcpClient.Close(); } catch { } }
                }
                try { _broadcastServer?.Stop(); } catch { }
            }
            catch { }

            base.OnClosed(e);
            Environment.Exit(0);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Работа с Excel
        // ═══════════════════════════════════════════════════════════════════

        private void RunExcelMacroButton_Click(object sender, RoutedEventArgs e)
        {
            // ─── 1. Данные из UI ────────────────────────────────────────────
            string dataFolder = SavePathBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(dataFolder) || !Directory.Exists(dataFolder))
            {
                MessageBox.Show("Папка сохранения не существует:\n" + dataFolder,
                                "Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Уставка без плюса, без °C, без дробной части: "115.006" → "115"
            string setpoint = (FurnaceSetPointBox.Text ?? "").Trim()
                .TrimStart('+').Replace(',', '.');
            if (setpoint.Contains('.')) setpoint = setpoint.Split('.')[0];

            string[] candidates =
            {
        Path.Combine(dataFolder, "+" + setpoint + ".csv"),
        Path.Combine(dataFolder, setpoint + ".csv"),
    };

            string? csvPath = candidates.FirstOrDefault(File.Exists);

            if (csvPath == null)
            {
                csvPath = Directory.GetFiles(dataFolder, "*.csv")
                    .OrderByDescending(File.GetLastWriteTime)
                    .FirstOrDefault();

                if (csvPath == null)
                {
                    MessageBox.Show("CSV-файл не найден. Пробовал:\n" +
                                    string.Join("\n", candidates),
                                    "Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusText.Text = $"Excel: использован свежий файл {Path.GetFileName(csvPath)}";
            }

            int sensorCount = GetSelectedClientCase();
            if (sensorCount < 1 || sensorCount > 10)
            {
                MessageBox.Show("Количество датчиков (CASE) должно быть от 1 до 10.\n\n" +
                                "Отметьте клиента галочкой и введите CASE рядом с SID.",
                                "Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int caseForCopy = GetCurrentBatchCase();
            if (caseForCopy == 0)
            {
                MessageBox.Show("Не удалось определить номер текущего задания пакета.\n" +
                                "Убедитесь, что пакетный режим запущен.",
                                "Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string parentFolder = dataFolder.EndsWith("Данные", StringComparison.OrdinalIgnoreCase)
                ? dataFolder.Substring(0, dataFolder.Length - "Данные".Length).TrimEnd('\\')
                : (Directory.GetParent(dataFolder)?.FullName ?? dataFolder);

            // ─── 2. Путь к надстройке IvanoFF.xlam ─────────────────────────
            string addinPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\AddIns\IvanoFF.xlam");

            if (!File.Exists(addinPath))
            {
                string altPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Excel\XLSTART\IvanoFF.xlam");

                if (File.Exists(altPath))
                    addinPath = altPath;
                else
                {
                    MessageBox.Show("Не найден файл надстройки IvanoFF.xlam.\n\n" +
                                    "Проверьте путь:\n" + addinPath + "\n" + altPath,
                                    "Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // ─── 3. Запускаем Excel ─────────────────────────────────────────
            object? xlApp = null;
            object? csvBook = null;

            try
            {
                Type? excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    MessageBox.Show("Excel не установлен на этой машине.",
                                    "Excel", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                dynamic xl = Activator.CreateInstance(excelType)!;
                xlApp = xl;
                xl.Visible = true;          // ← для отладки
                xl.DisplayAlerts = false;

                // Создаём временную книгу (обход бага с AddIns)
                dynamic tempBook = xl.Workbooks.Add();
                tempBook.Close(false);

                // Загружаем надстройку
                bool addinLoaded = false;
                dynamic addins = xl.AddIns;

                foreach (dynamic addin in addins)
                {
                    try
                    {
                        string name = addin.Name;
                        string fullName = addin.FullName;

                        if (name.IndexOf("IvanoFF", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            fullName.IndexOf("IvanoFF", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            addin.Installed = false;
                            addin.Installed = true;
                            addinLoaded = true;
                            break;
                        }
                    }
                    catch { }
                }

                if (!addinLoaded)
                {
                    dynamic newAddin = addins.Add(addinPath);
                    newAddin.Installed = true;
                }

                // Открываем CSV
                csvBook = xl.Workbooks.Open(csvPath);
                dynamic wb = csvBook;
                wb.Activate();

                // ─── Пишем параметры в скрытые ячейки Z1, Z2, Z3 ─────────
                dynamic sheet = wb.ActiveSheet;
                sheet.Range("Z1").Value = sensorCount;      // количество датчиков
                sheet.Range("Z2").Value = caseForCopy;      // CASE для второго макроса
                sheet.Range("Z3").Value = parentFolder;     // путь без \Данные

                // Скрываем значения — не обязательно, но чище
                try
                {
                    sheet.Range("Z1:Z3").Font.Color = sheet.Range("Z1").Interior.Color;
                }
                catch { }

                // ─── Вызываем макросы-обёртки ────────────────────────────
                xl.Run("Filter_KOSA_GUI_Auto");
                xl.Run("Universal_Insert_FULL_Auto");

                // Закрываем CSV
                wb.Close(false);
                csvBook = null;

                StatusText.Text = $"Excel: обработка завершена — {Path.GetFileName(csvPath)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при работе с Excel:\n{ex.Message}",
                                "Excel", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Excel: ошибка — см. диалог";
            }
            finally
            {
                if (csvBook != null)
                {
                    try { ((dynamic)csvBook).Close(false); } catch { }
                }

                if (xlApp != null)
                {
                    try { ((dynamic)xlApp).Quit(); } catch { }
                    try { Marshal.ReleaseComObject(xlApp); } catch { }
                    xlApp = null;
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        /// <summary>
        /// Возвращает CASE первого клиента с галочкой.
        /// Если ни один не выбран или значение некорректное — 0.
        /// </summary>
        private int GetSelectedClientCase()
        {
            var client = _connectedClients.FirstOrDefault(c => c.IsSelected);
            if (client == null) return 0;
            return int.TryParse(client.CaseValue, out int c) ? c : 0;
        }


        /// <summary>
        /// Возвращает номер текущего активного задания в пакете (1-based).
        /// Ищет по _batchJobs, ориентируясь на StatusText.
        /// Если пакет не запущен — 0.
        /// </summary>
        private int GetCurrentBatchCase()
        {
            if (!_batchRunning) return 0;

            int activeCount = 0;
            int totalActive = 0;

            for (int i = 0; i < _batchJobs.Count; i++)
            {
                if (!_batchJobs[i].IsEnabled) continue;
                totalActive++;

                string st = _batchJobs[i].StatusText ?? "";
                if (st.Contains("запись") || st.Contains("стабилизация") ||
                    st.Contains("проверка") || st.Contains("Идёт съём"))
                {
                    activeCount = totalActive;
                    return activeCount;
                }
            }

            return 0;
        }
    }
}