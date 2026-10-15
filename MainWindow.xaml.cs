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
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TERMINAL.Models;

namespace TERMINAL
{
    public partial class MainWindow : Window
    {
        public static MainWindow? Current { get; private set; }
        private TcpClient? _client;
        private NetworkStream? _stream;
        private float _currentBatchSetPointForSave = 0f;
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

        // ═══════════════════════════════════════════════════════════════════
        // НАСТРОЙКИ ПРИБОРОВ
        // ═══════════════════════════════════════════════════════════════════

        // ── АГК (прибор давления) ──
        private string _deviceIp = "192.168.0.159";
        private int _devicePort = 8888;

        // ── ST-351 (печь) ──
        private string _furnaceIp = "192.168.0.167";
        private int _furnacePort = 502;
        private ushort _furnaceSetPointRegister = 201;
        private ushort _furnaceCurrentTempRegister = 205;
        private ushort _furnaceModeRegister = 200;
        private bool _showFurnaceCard = true;

        // ── АГК карточка ──
        private bool _showAgkCard = true;

        // ── СКТБ ЭлПА ──
        private string _sktbFilterScript = "Filter_for_KOSA_GUI";
        private bool _showSktbCard = true;

        // Режимы (не настраиваются)
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

        private readonly bool _uiLoaded = false;

        // ── Общие настройки пакета ──
        private bool _batchKosaEnabled = false;
        private int _batchKosaTimerSeconds = 25;

        private bool _batchTimerEnabled = true;
        private bool _autoExcelEnabled = true;
        private bool _tickTimingEnabled = true;
        public bool BatchTimerEnabled { get; set; } = true;

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

        // ─── Аккумулятор таймингов ───
        private readonly Dictionary<string, long> _tickTimingAccum = new();
        private readonly object _tickTimingLock = new();
        private long _tickTimingTotalMs = 0;

        /// <summary>
        /// Фиксированный порядок меток в плашке «такт».
        /// </summary>
        private static readonly string[] TickTimingOrder =
        {
                        "main",
                        "agk-poll",
                        "furnace",
                        "platina",
                        "sensorUI",
                        "clientsUI",
                        "kosa",
                        "kosa-idle",
                        "timer-step",
                        "timer-idle",
                        "timer-max",
                        "timer-atmo",
                        "batch-stab",
                        "batch-tolerance",
                        "wait-state",
                        "wait-pressure"
};

        // ─── Кэш состояния АГК ───
        // ─── Кэш состояния АГК ───
        private class AgkSnapshot
        {
            public string Units = "";
            public double? Pressure;
            public double? SetPoint;
            public string Mode = "";
            public string Status = "";

            public bool StateInPointOk;          // ответ "ok;"
            public bool StateInPointWait;        // ответ "wait;"
            public string StateInPointRaw = "";  // сырой ответ (для отладки)

            public bool Connected;
            public DateTime UpdatedAt = DateTime.MinValue;
        }

        private AgkSnapshot _agkState = new();
        private readonly object _agkStateLock = new();

        // ─── Кэш состояния печи ───
        private class FurnaceSnapshot
        {
            public float? CurrentTemp;
            public float? SetPoint;
            public bool Connected;
            public DateTime UpdatedAt = DateTime.MinValue;
        }

        private FurnaceSnapshot _furnaceState = new();
        private readonly object _furnaceStateLock = new();

        // ─── Таймер обновления кэша ───
        private DispatcherTimer? _stateRefreshTimer;

        private readonly DateTime _lastAgkStatusRequest = DateTime.MinValue;
        private const int AgkStatusRequestIntervalMs = 1000;

        // ── Журнал событий ──
        private readonly ObservableCollection<LogEntry> _logEntries = new();
        private Window? _logWindow = null;
        private System.Windows.Controls.ListBox? _logListBox = null;
        private System.Windows.Controls.TextBox? _logFilterBox = null;
        private const int LogMaxEntries = 5000;
        private System.Windows.Controls.TextBlock? _logCounterText = null;

        // Публичные свойства для окна настроек
        public string FurnaceIp => _furnaceIp;
        public int FurnacePort => _furnacePort;
        public ushort FurnaceSetPointRegister => _furnaceSetPointRegister;
        public ushort FurnaceCurrentTempRegister => _furnaceCurrentTempRegister;
        public ushort FurnaceModeRegister => _furnaceModeRegister;
        public bool ShowFurnaceCard => _showFurnaceCard;

        public string DeviceIp => _deviceIp;
        public int DevicePort => _devicePort;
        public bool ShowAgkCard => _showAgkCard;

        public string SktbFilterScript => _sktbFilterScript;
        public bool ShowSktbCard => _showSktbCard;

        private string _currentAgkMode = "";

        public MainWindow()
        {
            InitializeComponent();
            Current = this;

            if (StartButtonBatchBrush is SolidColorBrush sb2 && sb2.CanFreeze)
                sb2.Freeze();

            // ─── Предзаполняем аккумулятор таймингов всеми метками ───
            foreach (var label in TickTimingOrder)
                _tickTimingAccum[label] = 0;

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

                // ─── Основной таймер ───
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timer.Tick += (s, e) =>
                {
                    var start = DateTime.Now;
                    try { Timer_Tick(s, e); }
                    finally
                    {
                        long ms = (long)(DateTime.Now - start).TotalMilliseconds;
                        AddTickTiming("main", ms);
                        FlushTickTiming();
                    }
                };
                _timer.Start();

                // ─── Таймер обновления кэша состояния ───
                _stateRefreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _stateRefreshTimer.Tick += async (s, e) =>
                {
                    await RefreshAgkStateAsync();
                    await RefreshFurnaceStateAsync();
                };
                _stateRefreshTimer.Start();

                await Task.Delay(500);
                await RefreshFurnaceCurrentTempAsync();
                await RefreshFurnaceSetPointAsync();

                // ─── Таймер печи ───
                // ─── Таймер стабилизации (обновляет UI из кэша) ───
                // ─── Таймер стабилизации (обновляет UI из кэша) ───
                _furnaceTempTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _furnaceTempTimer.Tick += (s, e) =>
                {
                    RefreshFurnaceCurrentTempAsync();
                    RefreshFurnaceSetPointAsync();
                    UpdateStabilizationTimer();
                };
                _furnaceTempTimer.Start();

                await RefreshFurnaceCurrentTempAsync();
                await RefreshFurnaceSetPointAsync();
                UpdateStabilizationTimer();

                // ─── Таймер платины ───
                _platinaTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _platinaTimer.Tick += async (s, e) =>
                {
                    var start = DateTime.Now;
                    try { await RefreshPlatinaAsync(); }
                    finally
                    {
                        long ms = (long)(DateTime.Now - start).TotalMilliseconds;
                        AddTickTiming("platina", ms);
                    }
                };
                _platinaTimer.Start();

                await RefreshPlatinaAsync();

                // ─── Таймер сенсоров ───
                _sensorUiTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _sensorUiTimer.Tick += (s, e) =>
                {
                    var start = DateTime.Now;
                    try { RefreshSensorUiFromClients(); }
                    finally
                    {
                        long ms = (long)(DateTime.Now - start).TotalMilliseconds;
                        AddTickTiming("sensorUI", ms);
                    }
                };
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
        // НАСТРОЙКИ ПРИБОРОВ — открытие окна и применение
        // ═══════════════════════════════════════════════════════════════════

        private void DeviceSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenDeviceSettings(1);
        }

        private void FurnaceTitle_Click(object sender, MouseButtonEventArgs e)
        {
            OpenDeviceSettings(1);
        }

        private void FurnaceSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenDeviceSettings(1);
        }

        private void AgkTitle_Click(object sender, MouseButtonEventArgs e)
        {
            OpenDeviceSettings(2);
        }

        private void AgkSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenDeviceSettings(2);
        }

        private void SktbTitle_Click(object sender, MouseButtonEventArgs e)
        {
            OpenDeviceSettings(3);
        }

        private void SktbSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenDeviceSettings(3);
        }

        private void OpenDeviceSettings(int initialTab)
        {
            try
            {
                var win = new TERMINAL.Windows.DeviceSettingsWindow(
                    initialTab,
                    _batchKosaEnabled,
                    _batchTimerEnabled,
                    _batchKosaTimerSeconds,
                    _autoExcelEnabled,
                    _tickTimingEnabled);       // ← новая строка

                win.Owner = this;
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть окно настроек:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ApplyDeviceSettings(
            string furnaceIp, int furnacePort,
            ushort furnaceSetPointReg, ushort furnaceCurrentTempReg, ushort furnaceModeReg,
            bool showFurnaceCard,
            string agkIp, int agkPort, bool showAgkCard,
            string sktbFilterScript, bool showSktbCard)
        {
            _furnaceIp = furnaceIp;
            _furnacePort = furnacePort;
            _furnaceSetPointRegister = furnaceSetPointReg;
            _furnaceCurrentTempRegister = furnaceCurrentTempReg;
            _furnaceModeRegister = furnaceModeReg;
            _showFurnaceCard = showFurnaceCard;

            _deviceIp = agkIp;
            _devicePort = agkPort;
            _showAgkCard = showAgkCard;

            _sktbFilterScript = sktbFilterScript;
            _showSktbCard = showSktbCard;

            SaveSettings();
            RefreshDeviceCardsVisibility();

            _ = Task.Run(ReconnectDevicesAsync);
        }

        public void RefreshDeviceCardsVisibility()
        {
            try
            {
                if (FurnaceCardBorder != null)
                    FurnaceCardBorder.Visibility = _showFurnaceCard ? Visibility.Visible : Visibility.Collapsed;

                if (AgkCardBorder != null)
                    AgkCardBorder.Visibility = _showAgkCard ? Visibility.Visible : Visibility.Collapsed;

                if (SktbCardBorder != null)
                    SktbCardBorder.Visibility = _showSktbCard ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        public async Task ReconnectDevicesAsync()
        {
            try
            {
                AppendLog("Настройки приборов изменены, переподключение...", Brushes.Blue);

                lock (_streamLock)
                {
                    try { _stream?.Close(); } catch { }
                    try { _client?.Close(); } catch { }
                    _stream = null;
                    _client = null;
                }

                _ = Task.Run(ConnectToDeviceLoopAsync);

                AppendLog("Переподключение запущено (АГК + ST-351)", Brushes.Gray);
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка переподключения: {ex.Message}", Brushes.OrangeRed);
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
            RunExcelMacroInternal(silent: false);
        }

        private string GetCurrentBatchSetPoint()
        {
            if (!_batchRunning) return "";

            for (int i = 0; i < _batchJobs.Count; i++)
            {
                var job = _batchJobs[i];
                string st = job.StatusText ?? "";

                if (st.Contains("запись") || st.Contains("стабилизация") ||
                    st.Contains("проверка") || st.Contains("Идёт съём") ||
                    st.Contains("бароциклирование") || st.Contains("завершено"))
                {
                    string spText = job.SetPointText ?? "";
                    spText = spText.Trim().TrimStart('+').Replace(',', '.');

                    bool negative = spText.StartsWith("-");
                    if (negative) spText = spText.TrimStart('-');

                    if (spText.Contains('.')) spText = spText.Split('.')[0];

                    return (negative ? "-" : "") + spText;
                }
            }

            return "";
        }

        private void RunExcelMacroInternal(bool silent)
        {
            // ─── 1. Данные из UI ────────────────────────────────────────────
            string dataFolder = SavePathBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(dataFolder) || !Directory.Exists(dataFolder))
            {
                string msg = "Папка сохранения не существует:\n" + dataFolder;
                AppendLog($"Excel: {msg}", Brushes.OrangeRed);
                if (!silent) MessageBox.Show(msg, "Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ─── 2. Уставка для имени CSV ──────────────────────────────────
            string setpoint = "";
            if (_batchRunning && _currentBatchSetPointForSave > 0)
            {
                setpoint = ((int)_currentBatchSetPointForSave).ToString();
            }
            else
            {
                setpoint = (FurnaceSetPointBox.Text ?? "").Trim()
                    .TrimStart('+').Replace(',', '.');
                if (setpoint.Contains('.')) setpoint = setpoint.Split('.')[0];
            }

            if (string.IsNullOrEmpty(setpoint))
            {
                setpoint = (FurnaceSetPointBox.Text ?? "").Trim()
                    .TrimStart('+').Replace(',', '.');
                if (setpoint.Contains('.')) setpoint = setpoint.Split('.')[0];
            }

            // ─── 3. Поиск CSV-файла ────────────────────────────────────────
            string[] candidates;

            if (setpoint.StartsWith("-"))
            {
                candidates = new[]
                {
                    Path.Combine(dataFolder, setpoint + ".csv"),
                };
            }
            else
            {
                candidates = new[]
                {
                    Path.Combine(dataFolder, "+" + setpoint + ".csv"),
                    Path.Combine(dataFolder, setpoint + ".csv"),
                };
            }

            string? csvPath = candidates.FirstOrDefault(File.Exists);

            if (csvPath == null)
            {
                csvPath = Directory.GetFiles(dataFolder, "*.csv")
                    .OrderByDescending(File.GetLastWriteTime)
                    .FirstOrDefault();

                if (csvPath == null)
                {
                    string msg = "CSV-файл не найден. Пробовал:\n" + string.Join("\n", candidates);
                    AppendLog($"Excel: {msg}", Brushes.OrangeRed);
                    if (!silent) MessageBox.Show(msg, "Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                AppendLog($"Excel: использован свежий файл {Path.GetFileName(csvPath)}", Brushes.Gray);
                StatusText.Text = $"Excel: использован свежий файл {Path.GetFileName(csvPath)}";
            }

            // ─── 4. Количество датчиков (Case) для первого макроса ────────
            int sensorCount = GetSelectedClientCase();
            if (sensorCount < 1)
            {
                string msg = "Количество датчиков должно быть не меньше 1.\n\n" +
                             "Убедитесь, что клиент подключён и передал CASE.";
                AppendLog($"Excel: {msg}", Brushes.OrangeRed);
                if (!silent) MessageBox.Show(msg, "Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ─── 5. CASE для второго макроса — из активного задания ────────
            int caseForCopy = GetCurrentBatchCase();
            if (caseForCopy == 0)
            {
                string msg = "Не удалось определить номер текущего задания пакета.\n" +
                             "Убедитесь, что пакетный режим запущен.";
                AppendLog($"Excel: {msg}", Brushes.OrangeRed);
                if (!silent) MessageBox.Show(msg, "Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ─── 6. Папка для второго макроса = родительская от "Данные" ───
            string parentFolder = dataFolder.EndsWith("Данные", StringComparison.OrdinalIgnoreCase)
                ? dataFolder.Substring(0, dataFolder.Length - "Данные".Length).TrimEnd('\\')
                : (Directory.GetParent(dataFolder)?.FullName ?? dataFolder);

            // ─── 7. Путь к надстройке IvanoFF.xlam ─────────────────────────
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
                    string msg = "Не найден файл надстройки IvanoFF.xlam.\n\n" +
                                 "Проверьте путь:\n" + addinPath + "\n" + altPath;
                    AppendLog($"Excel: {msg}", Brushes.Red);
                    if (!silent) MessageBox.Show(msg, "Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            AppendLog($"Excel: запуск макросов для {Path.GetFileName(csvPath)} " +
                      $"(Case={sensorCount}, CASE={caseForCopy})", Brushes.Blue);

            // ─── 8. Запускаем Excel ────────────────────────────────────────
            object? xlApp = null;
            object? csvBook = null;

            try
            {
                Type? excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    string msg = "Excel не установлен на этой машине.";
                    AppendLog($"Excel: {msg}", Brushes.Red);
                    if (!silent) MessageBox.Show(msg, "Excel", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                dynamic xl = Activator.CreateInstance(excelType)!;
                xlApp = xl;
                xl.Visible = false;
                xl.DisplayAlerts = false;

                dynamic tempBook = xl.Workbooks.Add();
                tempBook.Close(false);

                bool addinLoaded = false;
                dynamic addins = xl.AddIns;

                foreach (dynamic addin in addins)
                {
                    try
                    {
                        string name = addin.Name;
                        string fullName = addin.FullName;

                        if (name.Contains("IvanoFF", StringComparison.OrdinalIgnoreCase) ||
                            fullName.Contains("IvanoFF", StringComparison.OrdinalIgnoreCase))
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

                AppendLog("Excel: надстройка IvanoFF загружена", Brushes.Gray);

                csvBook = xl.Workbooks.Open(csvPath);
                dynamic wb = csvBook;
                wb.Activate();

                dynamic sheet = wb.ActiveSheet;
                sheet.Range("Z1").Value = sensorCount;
                sheet.Range("Z2").Value = caseForCopy;
                sheet.Range("Z3").Value = parentFolder;

                AppendLog("Excel: запуск макроса «Фильтр КОСА»", Brushes.Blue);
                xl.Run("Filter_KOSA_GUI_Auto");
                AppendLog("Excel: макрос «Фильтр КОСА» выполнен", Brushes.Green);

                try
                {
                    dynamic activeBook = xl.ActiveWorkbook;
                    dynamic activeSheet = activeBook.ActiveSheet;
                    activeSheet.Range("Z1").Value = sensorCount;
                    activeSheet.Range("Z2").Value = caseForCopy;
                    activeSheet.Range("Z3").Value = parentFolder;
                    AppendLog("Excel: параметры Z1/Z2/Z3 перезаписаны в новую книгу", Brushes.Gray);
                }
                catch (Exception ex)
                {
                    AppendLog($"Excel: не удалось перезаписать Z1/Z2/Z3 — {ex.Message}", Brushes.OrangeRed);
                }

                AppendLog("Excel: запуск макроса «Universal Insert»", Brushes.Blue);
                xl.Run("Universal_Insert_FULL_Auto");

                try
                {
                    var sensors = new List<TERMINAL.Models.SensorSummary>();
                    foreach (dynamic ws in xl.ActiveWorkbook.Sheets)
                    {
                        string wsName = ws.Name;
                        if (wsName == "TERMINAL" || wsName == "БЧХ" ||
                            wsName == "ТЧХ" || wsName == "Проверка" ||
                            wsName == "ChangeLog" || wsName == "Данные")
                            continue;

                        SensorSummary s = TERMINAL.Services.SummaryService.ReadSensorFromOpenWorkbook(
                            (object)xl.ActiveWorkbook);
                        sensors.Add(s);

                        AppendLog($"Прочитан датчик №{s.Number}: " +
                                  $"опис.град={s.GradDescription}, " +
                                  $"проверок={s.CheckDescriptions.Count(x => x.HasValue)}, " +
                                  $"гист.град={s.GradHysteresis.Values.GetLength(0)}x{s.GradHysteresis.Values.GetLength(1)}, " +
                                  $"погр={s.Errors.Values.GetLength(0)}x{s.Errors.Values.GetLength(1)}",
                                  Brushes.Green);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Ошибка чтения: {ex.Message}", Brushes.Red);
                }

                AppendLog("Excel: макрос «Universal Insert» выполнен", Brushes.Green);

                try { wb.Close(false); } catch { }
                csvBook = null;

                AppendLog($"Excel: обработка завершена — {Path.GetFileName(csvPath)}", Brushes.Green);
                StatusText.Text = $"Excel: обработка завершена — {Path.GetFileName(csvPath)}";
            }
            catch (System.Runtime.InteropServices.COMException comEx)
            {
                string msg = comEx.Message;
                string friendly;

                if (msg.Contains("Cannot run the macro"))
                    friendly = "Макрос не найден в надстройке IvanoFF.xlam";
                else if (msg.Contains("Unable to get the Add property"))
                    friendly = "Не удалось загрузить надстройку IvanoFF.xlam";
                else if (msg.Contains("method 'Run'"))
                    friendly = "Ошибка вызова макроса (проверьте VBA)";
                else
                    friendly = $"Excel COM: {msg}";

                AppendLog($"Excel: {friendly}", Brushes.OrangeRed);
                if (!silent)
                    MessageBox.Show(friendly, "Excel", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Excel: ошибка — см. журнал";
            }
            catch (FileNotFoundException fnf)
            {
                string friendly = $"Файл не найден: {fnf.FileName}";
                AppendLog($"Excel: {friendly}", Brushes.OrangeRed);
                if (!silent)
                    MessageBox.Show(friendly, "Excel", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Excel: файл не найден";
            }
            catch (Exception ex)
            {
                AppendLog($"Excel: {ex.GetType().Name} — {ex.Message}", Brushes.OrangeRed);
                if (!silent)
                    MessageBox.Show($"Ошибка: {ex.Message}", "Excel", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Excel: ошибка — см. журнал";
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

        private int GetSelectedClientCase()
        {
            var client = _connectedClients.FirstOrDefault(c => c.IsSelected);
            if (client == null) return 0;
            return int.TryParse(client.CaseValue, out int c) ? c : 0;
        }

        private int GetCurrentBatchCase()
        {
            if (!_batchRunning) return 0;

            for (int i = 0; i < _batchJobs.Count; i++)
            {
                var job = _batchJobs[i];
                string st = job.StatusText ?? "";
                if (st.Contains("запись") || st.Contains("стабилизация") ||
                    st.Contains("проверка") || st.Contains("Идёт съём"))
                {
                    if (int.TryParse(job.CaseText, out int c) && c != 0)
                        return c;
                    return i + 1;
                }
            }
            return 0;
        }

        private void SummaryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dataFolder = SavePathBox.Text ?? "";
                if (string.IsNullOrWhiteSpace(dataFolder))
                {
                    MessageBox.Show("Не указан путь сохранения.",
                        "Сводка данных", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var win = new TERMINAL.Windows.SummaryWindow(dataFolder);
                win.Owner = this;
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть окно сводки:\n{ex.Message}",
                                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Добавляет замер в аккумулятор. Вызывается из каждого таймера.
        /// </summary>
        private void AddTickTiming(string label, long ms)
        {
            if (!_tickTimingEnabled) return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AddTickTiming(label, ms));
                return;
            }

            lock (_tickTimingLock)
            {
                if (!_tickTimingAccum.ContainsKey(label))
                    _tickTimingAccum[label] = 0;

                _tickTimingAccum[label] += ms;
                _tickTimingTotalMs += ms;
            }
        }

        /// <summary>
        /// Показывает накопленный итог и сбрасывает аккумулятор.
        /// Вызывается раз в секунду (в main-таймере).
        /// </summary>
        private void FlushTickTiming()
        {
            if (!_tickTimingEnabled) return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(FlushTickTiming);
                return;
            }

            if (TickTimingText == null) return;

            long totalMs;
            Dictionary<string, long> snapshot;

            lock (_tickTimingLock)
            {
                // Убедимся, что все метки из TickTimingOrder присутствуют
                foreach (var label in TickTimingOrder)
                {
                    if (!_tickTimingAccum.ContainsKey(label))
                        _tickTimingAccum[label] = 0;
                }

                totalMs = _tickTimingTotalMs;
                snapshot = new Dictionary<string, long>(_tickTimingAccum);

                foreach (var key in _tickTimingAccum.Keys.ToList())
                    _tickTimingAccum[key] = 0;

                _tickTimingTotalMs = 0;
            }

            if (totalMs == 0)
            {
                TickTimingText.Text = "такт: —";
                if (TickTimingText2 != null) TickTimingText2.Text = "";
                return;
            }

            var brush = totalMs < 300 ? Brushes.Green
                      : totalMs < 600 ? Brushes.Orange
                                      : Brushes.Red;

            TickTimingText.Foreground = brush;
            if (TickTimingText2 != null) TickTimingText2.Foreground = brush;

            // Сортируем по TickTimingOrder
            var ordered = snapshot
                .OrderBy(kv =>
                {
                    int idx = Array.IndexOf(TickTimingOrder, kv.Key);
                    return idx >= 0 ? idx : int.MaxValue;
                })
                .ToList();

            // Делим на две группы: первая — «горячие» (main, agk-poll, furnace, platina, sensorUI, clientsUI, kosa, kosa-idle)
            // Вторая — редко используемые (timer-*, batch-*, wait-*)
            var firstGroupKeys = new HashSet<string>
    {
        "main", "agk-poll", "furnace", "platina",
        "sensorUI", "clientsUI", "kosa", "kosa-idle"
    };

            var firstParts = ordered
                .Where(kv => firstGroupKeys.Contains(kv.Key))
                .Select(kv => $"{kv.Key} {kv.Value}");

            var secondParts = ordered
                .Where(kv => !firstGroupKeys.Contains(kv.Key))
                .Select(kv => $"{kv.Key} {kv.Value}");

            TickTimingText.Text =
                $"такт: {totalMs} мс ({string.Join(", ", firstParts)})";

            if (TickTimingText2 != null)
            {
                string line2 = string.Join(", ", secondParts);
                TickTimingText2.Text = string.IsNullOrEmpty(line2)
                    ? ""
                    : $"({line2})";
            }

            if (totalMs > 500)
            {
                string allParts = string.Join(", ",
                    ordered.Select(kv => $"{kv.Key} {kv.Value}"));
                AppendLog($"⚠ Медленный такт: {totalMs} мс ({allParts})",
                          Brushes.OrangeRed);
            }
        }

        public void ApplyGeneralSettings(
            bool q2viewEnabled, bool timerEnabled,
            int timerSeconds, bool autoExcelEnabled,
            bool tickTimingEnabled)
        {
            _batchKosaEnabled = q2viewEnabled;
            _batchTimerEnabled = timerEnabled;
            _batchKosaTimerSeconds = timerSeconds;
            _autoExcelEnabled = autoExcelEnabled;
            _tickTimingEnabled = tickTimingEnabled;

            ApplyTickTimingVisibility();
            UpdateKosaBadge();
            SaveSettings();
        }

        private void ApplyTickTimingVisibility()
        {
            if (TickTimingPanel != null)
                TickTimingPanel.Visibility = _tickTimingEnabled
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

    }
}