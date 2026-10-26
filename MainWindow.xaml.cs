using HidSharp;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Globalization;
using System.IO;
using System.IO.Ports;
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
using TERMINAL.Notifications;


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
        private volatile bool _platinaBusy = false;
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
        private bool _autoStopFurnace = true;

        // ── АГК карточка ──
        private bool _showAgkCard = true;

        private int _agkPressureTimeoutMinutes = 5;

        // ── СКТБ ЭлПА ──
        private string _sktbFilterScript = "Filter_for_KOSA_GUI";
        private bool _showSktbCard = true;

        // ── Платина (СКТБ ЭлПА) ──
        private string _platinumSource = "mtk_termex";   // mtk_termex | mit_803 | tercon
        private string _platinumComPort = "COM1";
        private bool _readPlatinum = true;
        private string _platinumMtkCommand = "pass2 meas2? 2";



        private SerialPort? _platinumSerial;
        private readonly object _platinumSerialLock = new object();

        public string PlatinumSource => _platinumSource;
        public string PlatinumComPort => _platinumComPort;
        public bool ReadPlatinum => _readPlatinum;
        public string PlatinumMtkCommand => _platinumMtkCommand;
        private double _mit803LastGoodValue = double.NaN;

        // Режимы (не настраиваются)
        private const ushort FurnaceModeManual = 2;
        private const ushort FurnaceModeIdle = 1;

        private const int PlatinaVid = 0xFFFF;
        private const int PlatinaPid = 0x0009;

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
        private double _furnaceStabilizationTolerance = 0.1;

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
        private bool _logSlowTicks = true;
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

        // ★ Счётчик для медленных команд АГК (get_units, get_point, get_state_in_point)
        private int _agkSlowCounter = 0;

        // ★ Мьютекс: только одна команда АГК за раз (чтобы ответы не перепутывались)
        private readonly SemaphoreSlim _agkCommandLock = new SemaphoreSlim(1, 1);

        private volatile bool _agkPollBusy = false;

        // ★ Флаг: сейчас идёт vent — щадящий опрос АГК
        private volatile bool _agkInVentMode = false;

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
        private readonly BatchProgressNotifier _batchProgress = new();

        // Публичные свойства для окна настроек
        public string FurnaceIp => _furnaceIp;
        public int FurnacePort => _furnacePort;
        public ushort FurnaceSetPointRegister => _furnaceSetPointRegister;
        public ushort FurnaceCurrentTempRegister => _furnaceCurrentTempRegister;
        public ushort FurnaceModeRegister => _furnaceModeRegister;
        public bool ShowFurnaceCard => _showFurnaceCard;

        public bool AutoStopFurnace => _autoStopFurnace;
        public double FurnaceStabilizationTolerance => _furnaceStabilizationTolerance;

        public string DeviceIp => _deviceIp;
        public int DevicePort => _devicePort;
        public bool ShowAgkCard => _showAgkCard;

        public int AgkPressureTimeoutMinutes => _agkPressureTimeoutMinutes;

        public string SktbFilterScript => _sktbFilterScript;
        public bool ShowSktbCard => _showSktbCard;

        private string _currentAgkMode = "";

        // ═══════════════════════════════════════════════════════════════════
        // УВЕДОМЛЕНИЯ
        // ═══════════════════════════════════════════════════════════════════
        private bool _notificationsEnabled = false;
        private int _notificationMethodIndex = 0;

        // ★ НОВЫЕ ПОЛЯ (для галочек)
        private bool _notifyTgEnabled = false;
        private bool _notifyEmailEnabled = false;
        private bool _notifyVkEnabled = false;
        private bool _notifyMaxEnabled = false;

        private bool _notifyYandexEnabled = false;
        private bool _notifyMailEnabled = false;
        private bool _notifyGmailEnabled = false;
        private bool _notifyCustomEnabled = false;




        // ★ ПУБЛИЧНЫЕ СВОЙСТВА (чтобы окно настроек могло их прочитать)
        public bool NotifyTgEnabled => _notifyTgEnabled;
        public bool NotifyEmailEnabled => _notifyEmailEnabled;
        public bool NotifyVkEnabled => _notifyVkEnabled;
        public bool NotifyMaxEnabled => _notifyMaxEnabled;

        public bool NotifyYandexEnabled => _notifyYandexEnabled;
        public bool NotifyMailEnabled => _notifyMailEnabled;
        public bool NotifyGmailEnabled => _notifyGmailEnabled;
        public bool NotifyCustomEnabled => _notifyCustomEnabled;

        // Telegram
        private string _telegramToken = "";
        private string _telegramChatId = "";

        // ★ Для живой таблицы в Telegram
        private int _currentStepIndexForNotify = -1;
        private int _currentTimerRemaining = 0;
        private string _currentStepStatus = "ожидание";
        private string _lastSentTable = "";

        // Email
        private string _emailHost = "";
        private int _emailPort = 587;
        private string _emailUser = "";
        private string _emailPass = "";
        private string _emailTo = "";
        private int _emailProviderIndex = 0;

        // ★ ВОТ ЭТИ ПЕРЕМЕННЫЕ НУЖНО ДОБАВИТЬ:
        private string _emailYandexUser = "";
        private string _emailYandexPass = "";
        private string _emailYandexTo = "";

        private string _emailMailUser = "";
        private string _emailMailPass = "";
        private string _emailMailTo = "";

        private string _emailGmailUser = "";
        private string _emailGmailPass = "";
        private string _emailGmailTo = "";

        private string _emailCustomHost = "";
        private int _emailCustomPort = 587;
        private string _emailCustomUser = "";
        private string _emailCustomPass = "";
        private string _emailCustomTo = "";

        // VK
        private string _vkToken = "";
        private long _vkPeerId = 0;

        // МАКС

        private string _maxToken = "";
        private string _maxChatId = "";

        // SMS
        private string _smsLogin = "";
        private string _smsPass = "";
        private string _smsPhone = "";

        // Публичные свойства для окна настроек (DeviceSettingsWindow)
        public bool NotificationsEnabled => _notificationsEnabled;
        public int NotificationMethodIndex => _notificationMethodIndex;
        public string TelegramToken => _telegramToken;
        public string TelegramChatId => _telegramChatId;
        public string EmailHost => _emailHost;
        public int EmailPort => _emailPort;
        public string EmailUser => _emailUser;
        public string EmailPass => _emailPass;
        public string EmailTo => _emailTo;
        public int EmailProviderIndex => _emailProviderIndex;

        // ★ ВОТ ЭТИ СВОЙСТВА НУЖНО ДОБАВИТЬ (с сеттерами!):
        public string EmailYandexUser { get => _emailYandexUser; set => _emailYandexUser = value ?? ""; }
        public string EmailYandexPass { get => _emailYandexPass; set => _emailYandexPass = value ?? ""; }
        public string EmailYandexTo { get => _emailYandexTo; set => _emailYandexTo = value ?? ""; }

        public string EmailMailUser { get => _emailMailUser; set => _emailMailUser = value ?? ""; }
        public string EmailMailPass { get => _emailMailPass; set => _emailMailPass = value ?? ""; }
        public string EmailMailTo { get => _emailMailTo; set => _emailMailTo = value ?? ""; }

        public string EmailGmailUser { get => _emailGmailUser; set => _emailGmailUser = value ?? ""; }
        public string EmailGmailPass { get => _emailGmailPass; set => _emailGmailPass = value ?? ""; }
        public string EmailGmailTo { get => _emailGmailTo; set => _emailGmailTo = value ?? ""; }

        public string EmailCustomHost { get => _emailCustomHost; set => _emailCustomHost = value ?? ""; }
        public int EmailCustomPort { get => _emailCustomPort; set => _emailCustomPort = value; }
        public string EmailCustomUser { get => _emailCustomUser; set => _emailCustomUser = value ?? ""; }
        public string EmailCustomPass { get => _emailCustomPass; set => _emailCustomPass = value ?? ""; }
        public string EmailCustomTo { get => _emailCustomTo; set => _emailCustomTo = value ?? ""; }
        public string VkToken => _vkToken;
        public long VkPeerId => _vkPeerId;
        public string MaxToken => _maxToken;
        public string MaxChatId => _maxChatId;
        public string SmsLogin => _smsLogin;
        public string SmsPass => _smsPass;
        public string SmsPhone => _smsPhone;

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
            LoadSettings(); // ★ Читаем JSON, заполняем _loadedSettings (и _isDarkTheme, если добавили в LoadSettings)

            // ─── Инициализация сервиса уведомлений из сохранённых настроек ───
            if (_notificationsEnabled
                && !string.IsNullOrEmpty(_telegramToken)
                && long.TryParse(_telegramChatId, out long _initChatId))
            {
                try
                {
                    NotificationManager.SetService(
                        new TelegramNotificationService(_telegramToken, _initChatId));
                }
                catch (Exception ex)
                {
                    NotificationManager.SetService(new NullNotificationService());
                    System.Diagnostics.Debug.WriteLine(
                        "Уведомления: не удалось инициализировать — " + ex.Message);
                }
            }
            else
            {
                NotificationManager.SetService(new NullNotificationService());
            }

            ApplyWindowPositionOrFallback();

            _uiLoaded = true;

            ApplyLoadedSettingsToUi(); // ★ Здесь _isDarkTheme тоже обновляется (если добавили)

            // ★★★ ВОТ СЮДА ПЕРЕНОСИМ ПРИМЕНЕНИЕ ТЕМЫ ★★★
            if (_isDarkTheme)
            {
                ChangeTheme(true);
                if (ThemeToggleButton != null) ThemeToggleButton.Content = "💡";
            }
            // ★★★ КОНЕЦ БЛОКА ★★★

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
                    try
                    {
                        await RefreshAgkStateAsync();
                        await RefreshFurnaceStateAsync();
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"КРИТИЧЕСКАЯ ОШИБКА ФОНОВОГО ОПРОСА: {ex.Message}", Brushes.Red);
                    }
                };
                _stateRefreshTimer.Start();

                await Task.Delay(500);
                await RefreshFurnaceCurrentTempAsync();
                await RefreshFurnaceSetPointAsync();

                // ─── Таймер печи ───
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
                    if (_platinaBusy) return;
                    _platinaBusy = true;

                    var start = DateTime.Now;
                    try { await RefreshPlatinaAsync(); }
                    finally
                    {
                        _platinaBusy = false;
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

        private void NotificationsButton_Click(object sender, RoutedEventArgs e)
        {
            // Открываем окно настроек сразу на вкладке "Уведомления" (индекс 4)
            OpenDeviceSettings(4);
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
                    _tickTimingEnabled,
                    _logSlowTicks,
                    _autoStopFurnace,
                    _platinumSource,
                    _platinumComPort,        // ← 10-й, ДОБАВЛЕН
                    _readPlatinum,           // ← 11-й
                    _platinumMtkCommand);    // ← 12-й

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
            bool autoStopFurnace, bool showFurnaceCard,
            string agkIp, int agkPort, bool showAgkCard,
            int agkPressureTimeoutMinutes,
            string sktbFilterScript, bool showSktbCard,
            string platinumSource, string platinumComPort, bool readPlatinum,
            string platinumMtkCommand,
            double furnaceStabilizationTolerance)
        {
            {
                _furnaceIp = furnaceIp;
                _furnacePort = furnacePort;
                _furnaceSetPointRegister = furnaceSetPointReg;
                _furnaceCurrentTempRegister = furnaceCurrentTempReg;
                _furnaceModeRegister = furnaceModeReg;
                _autoStopFurnace = autoStopFurnace;
                _showFurnaceCard = showFurnaceCard;

                _deviceIp = agkIp;
                _devicePort = agkPort;
                _showAgkCard = showAgkCard;
                _agkPressureTimeoutMinutes = agkPressureTimeoutMinutes > 0
                    ? agkPressureTimeoutMinutes : 5;

                _sktbFilterScript = sktbFilterScript;
                _showSktbCard = showSktbCard;

                _platinumSource = platinumSource;
                _platinumComPort = platinumComPort;
                _readPlatinum = readPlatinum;
                _platinumMtkCommand = platinumMtkCommand;
                _furnaceStabilizationTolerance = furnaceStabilizationTolerance > 0
                    ? furnaceStabilizationTolerance
                    : 0.1;   // ★ НОВОЕ



                // ─── Пункт 12: закрыть старый COM-порт, если источник/порт изменился
                //     или чтение платины выключили ───
                lock (_platinumSerialLock)
                {
                    bool needClose =
                        _platinumSerial != null &&
                        (!_readPlatinum ||
                         _platinumSource != "mit_803" ||
                         _platinumComPort != (_platinumSerial.PortName ?? ""));

                    if (needClose)
                    {
                        try { _platinumSerial.Close(); } catch { }
                        try { _platinumSerial.Dispose(); } catch { }
                        _platinumSerial = null;
                    }
                }

                SaveSettings();
                RefreshDeviceCardsVisibility();

                _ = Task.Run(ReconnectDevicesAsync);
            }
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

            // ─── Закрыть COM-порт платины (МИТ 8.03) ───
            lock (_platinumSerialLock)
            {
                try { _platinumSerial?.Close(); } catch { }
                try { _platinumSerial?.Dispose(); } catch { }
                _platinumSerial = null;
            }

            base.OnClosed(e);
            Environment.Exit(0);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Работа с Excel
        // ═══════════════════════════════════════════════════════════════════

        private async void RunExcelMacroButton_Click(object sender, RoutedEventArgs e)
        {
            await RunExcelMacroInternalAsync(silent: false);
        }

        /// <summary>
        /// Асинхронная версия RunExcelMacroInternal — не блокирует UI.
        /// Подготовка (пути, sensorCount, caseForCopy) — синхронно,
        /// работа с Excel — в фоне через Task.Run.
        /// </summary>
        private async Task RunExcelMacroInternalAsync(bool silent)
        {
            // ═══════════════════════════════════════════════════════════════
            // 1. Подготовка — синхронно, в UI-потоке
            // ═══════════════════════════════════════════════════════════════

            string dataFolder = SavePathBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(dataFolder) || !Directory.Exists(dataFolder))
            {
                string msg = "Папка сохранения не существует:\n" + dataFolder;
                AppendLog($"Excel: {msg}", Brushes.OrangeRed);
                if (!silent) MessageBox.Show(msg, "Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ─── Уставка для имени CSV ───
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

            // ─── CSV-файл ───
            string? csvPath = FindCsvFile(dataFolder, setpoint);
            if (csvPath == null)
            {
                string msg = "CSV-файл не найден в " + dataFolder;
                AppendLog($"Excel: {msg}", Brushes.OrangeRed);
                if (!silent) MessageBox.Show(msg, "Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ─── Количество датчиков (Case) ───
            int sensorCount = GetSelectedClientCase();
            if (sensorCount < 1)
            {
                string msg = "Количество датчиков должно быть не меньше 1.\n\n" +
                             "Убедитесь, что клиент подключён и передал CASE.";
                AppendLog($"Excel: {msg}", Brushes.OrangeRed);
                if (!silent) MessageBox.Show(msg, "Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ─── CASE для второго макроса ───
            // Если пакет запущен — берем текущее задание. 
            // Если нет (ручной тест) — берем первое выбранное задание из списка или 1.
            int caseForCopy = 0;

            if (_batchRunning)
            {
                caseForCopy = GetCurrentBatchCase();
            }

            // Если пакет не запущен или не удалось определить CASE, ищем в списке заданий
            if (caseForCopy == 0)
            {
                // Ищем первое активное (включенное) задание
                var activeJob = _batchJobs.FirstOrDefault(j => j.IsEnabled);
                if (activeJob != null && !string.IsNullOrEmpty(activeJob.CaseText) && int.TryParse(activeJob.CaseText, out int parsedCase))
                {
                    caseForCopy = parsedCase;
                }

                // Если задания нет, берем 1 (или можно поставить 0, если хотите, чтобы макрос упал)
                if (caseForCopy == 0)
                    caseForCopy = 1;
            }

            // ─── Родительская папка ───
            string parentFolder = dataFolder.EndsWith("Данные", StringComparison.OrdinalIgnoreCase)
                ? dataFolder.Substring(0, dataFolder.Length - "Данные".Length).TrimEnd('\\')
                : (Directory.GetParent(dataFolder)?.FullName ?? dataFolder);

            // ─── Путь к надстройке IvanoFF.xlam ───
            string? addinPath = FindAddinPath();
            if (addinPath == null)
            {
                string msg = "Не найден файл надстройки IvanoFF.xlam.";
                AppendLog($"Excel: {msg}", Brushes.Red);
                if (!silent) MessageBox.Show(msg, "Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppendLog($"Excel: запуск макросов для {Path.GetFileName(csvPath)} " +
                      $"(Case={sensorCount}, CASE={caseForCopy})", Brushes.Blue);

            // ═══════════════════════════════════════════════════════════════
            // 2. Работа с Excel — асинхронно, UI не блокируется
            // ═══════════════════════════════════════════════════════════════

            await RunExcelPipeline(csvPath, sensorCount, caseForCopy, parentFolder, addinPath, silent);
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

        /// <summary>
        /// Синхронная часть работы с Excel. Вызывается из Task.Run — фон.
        /// Внутри — только COM-вызовы и AppendLog/SetStatus (уже потокобезопасные).
        /// </summary>
        private async Task RunExcelPipeline(
    string csvPath,
    int sensorCount,
    int caseForCopy,
    string parentFolder,
    string addinPath,
    bool silent)
        {
            object? xlApp = null;
            object? csvBook = null;

            try
            {
                Type? excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    AppendLog("Excel: Excel не установлен на этой машине.", Brushes.Red);
                    if (!silent)
                        UI(() => MessageBox.Show("Excel не установлен на этой машине.",
                            "Excel", MessageBoxButton.OK, MessageBoxImage.Error));
                    return;
                }

                dynamic xl = Activator.CreateInstance(excelType)!;
                xlApp = xl;
                xl.Visible = false;
                xl.DisplayAlerts = false;
                xl.ScreenUpdating = false;
                xl.EnableEvents = false;

                // ─── Временная книга ───
                dynamic tempBook = xl.Workbooks.Add();
                xl.Calculation = -4135;
                tempBook.Close(false);

                // ─── Загрузка надстройки IvanoFF ───
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

                // ─── Открываем CSV ───
                csvBook = xl.Workbooks.Open(csvPath);
                dynamic wb = csvBook;
                wb.Activate();

                dynamic sheet = wb.ActiveSheet;
                sheet.Range("Z1").Value = sensorCount;
                sheet.Range("Z2").Value = caseForCopy;
                sheet.Range("Z3").Value = parentFolder;

                // ═══════════════════════════════════════════════════════
                // ★ МАКРОС 1: выбор фильтра
                // ═══════════════════════════════════════════════════════
                string filterMacro = _sktbFilterScript switch
                {
                    "Фильтр_данных_ПДС" => "Фильтр_данных_ПДС",
                    "Фильтр_данных_БИДТ" => "Фильтр_данных_БИДТ",
                    "Фильтр_РЧ" => "Run_Filter_RcH",
                    "Filter_for_KOSA_GUI" => "Filter_KOSA_GUI_Auto",
                    _ => "Filter_KOSA_GUI_Auto"
                };

                AppendLog($"Excel: выбран фильтр «{_sktbFilterScript}» → макрос «{filterMacro}»", Brushes.Gray);

                try
                {
                    AppendLog($"Excel: запуск макроса «{filterMacro}»", Brushes.Blue);

                    // ★ Включаем авто-режим
                    xl.Run("SetAutoMode", true);

                    // ★ ЗАПУСК С ТАЙМАУТОМ (2 минуты)
                    var runTask1 = Task.Run(() => xl.Run(filterMacro));
                    if (await Task.WhenAny(runTask1, Task.Delay(10000)) != runTask1)
                    {
                        throw new TimeoutException($"Макрос «{filterMacro}» не завершился за 2 минуты. Возможно, он завис.");
                    }
                    await runTask1; // пробрасываем исключение, если оно было

                    // ★ Выключаем авто-режим
                    xl.Run("SetAutoMode", false);

                    AppendLog($"Excel: макрос «{filterMacro}» выполнен", Brushes.Green);
                }
                catch (TimeoutException tex)
                {
                    AppendLog($"Excel: {tex.Message}", Brushes.Red);
                    if (!silent)
                        UI(() => MessageBox.Show(tex.Message, "Excel", MessageBoxButton.OK, MessageBoxImage.Error));
                    throw;
                }
                catch (System.Runtime.InteropServices.COMException comEx)
                {
                    string msg = comEx.Message;

                    if (msg.Contains("Cannot run the macro"))
                    {
                        string friendly = $"Макрос «{filterMacro}» не найден в надстройке IvanoFF.xlam.\n\n" +
                                          "Проверьте, что в настройках СКТБ ЭлПа выбран существующий скрипт.";
                        AppendLog($"Excel: {friendly}", Brushes.Red);
                        if (!silent)
                            UI(() => MessageBox.Show(friendly, "Excel", MessageBoxButton.OK, MessageBoxImage.Error));
                        throw;
                    }

                    AppendLog($"Excel: ошибка в {filterMacro} — {msg}", Brushes.OrangeRed);
                    throw;
                }

                // Ждём завершения пересчёта
                WaitForExcelIdle(xl, timeoutMs: 30000);
                System.Threading.Thread.Sleep(800);

                // ─── Перезаписываем Z1/Z2/Z3 ───
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

                // ═══════════════════════════════════════════════════════
                // ★ МАКРОС 2: Universal_Insert_FULL_Auto (с таймаутом)
                // ═══════════════════════════════════════════════════════
                try
                {
                    AppendLog("Excel: запуск макроса «Universal Insert»", Brushes.Blue);

                    // ★ ЗАПУСК С ТАЙМАУТОМ (3 минуты)
                    var runTask2 = Task.Run(() => xl.Run("Universal_Insert_FULL_Auto"));
                    if (await Task.WhenAny(runTask2, Task.Delay(180000)) != runTask2)
                    {
                        throw new TimeoutException("Макрос «Universal Insert» не завершился за 3 минуты. Возможно, он завис.");
                    }
                    await runTask2;

                    AppendLog("Excel: макрос «Universal Insert» выполнен", Brushes.Green);
                }
                catch (TimeoutException tex)
                {
                    AppendLog($"Excel: {tex.Message}", Brushes.Red);
                    if (!silent)
                        UI(() => MessageBox.Show(tex.Message, "Excel", MessageBoxButton.OK, MessageBoxImage.Error));
                    throw;
                }
                catch (Exception ex)
                {
                    AppendLog($"Excel: ошибка в Universal_Insert — {ex.Message}", Brushes.OrangeRed);
                    throw;
                }

                WaitForExcelIdle(xl, timeoutMs: 120000);
                System.Threading.Thread.Sleep(500);

                // ─── Закрываем CSV-книгу ───
                try { wb.Close(false); } catch { }
                csvBook = null;

                AppendLog($"Excel: обработка завершена — {Path.GetFileName(csvPath)}", Brushes.Green);
                SetStatus($"Excel: обработка завершена — {Path.GetFileName(csvPath)}");
            }
            catch (Exception ex)
            {
                AppendLog($"Excel: {ex.GetType().Name} — {ex.Message}", Brushes.OrangeRed);
                if (!silent)
                    UI(() => MessageBox.Show($"Ошибка Excel: {ex.Message}", "Excel", MessageBoxButton.OK, MessageBoxImage.Error));
                SetStatusError("Excel: ошибка — см. журнал");
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
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private void UI(Action action)
        {
            if (Dispatcher.CheckAccess()) action();
            else Dispatcher.Invoke(action);
        }

        /// <summary>
        /// Ищет CSV-файл по уставке. Если не найден — берёт самый свежий.
        /// </summary>
        private static string? FindCsvFile(string dataFolder, string setpoint)
        {
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
            }

            return csvPath;
        }

        /// <summary>
        /// Ищет путь к надстройке IvanoFF.xlam.
        /// </summary>
        private static string? FindAddinPath()
        {
            string addinPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\AddIns\IvanoFF.xlam");

            if (File.Exists(addinPath)) return addinPath;

            string altPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Excel\XLSTART\IvanoFF.xlam");

            if (File.Exists(altPath)) return altPath;

            return null;
        }


        /// <summary>
        /// Ждёт, пока Excel завершит все вычисления (CalculationState == xlDone).
        /// </summary>
        private void WaitForExcelIdle(dynamic xl, int timeoutMs = 30000)
        {
            const int xlDone = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    int state = (int)xl.CalculationState;
                    if (state == xlDone)
                    {
                        AppendLog($"Excel: готов (за {sw.ElapsedMilliseconds} мс)", Brushes.Gray);
                        return;
                    }
                    System.Threading.Thread.Sleep(100);
                }
                AppendLog($"Excel: WaitForExcelIdle таймаут {timeoutMs} мс", Brushes.OrangeRed);
            }
            catch (Exception ex)
            {
                AppendLog($"Excel: WaitForExcelIdle ошибка — {ex.Message}", Brushes.Orange);
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

            if (totalMs > 500 && _logSlowTicks)
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
            bool tickTimingEnabled, bool logSlowTicksEnabled)
        {
            _batchKosaEnabled = q2viewEnabled;
            _batchTimerEnabled = timerEnabled;
            _batchKosaTimerSeconds = timerSeconds;
            _autoExcelEnabled = autoExcelEnabled;
            _tickTimingEnabled = tickTimingEnabled;
            _logSlowTicks = logSlowTicksEnabled;


            ApplyTickTimingVisibility();
            UpdateKosaBadge();
            SaveSettings();
        }

        // ═══════════════════════════════════════════════════════════════════
        // УВЕДОМЛЕНИЯ — применение настроек
        // ═══════════════════════════════════════════════════════════════════
        public void ApplyNotificationSettings(
    bool enabled,
    bool tgEnabled, bool yandexEnabled, bool mailEnabled, bool gmailEnabled,
    bool customEnabled, bool vkEnabled, bool maxEnabled,
    int methodIndex,
    string tgToken, string tgChatId,
    string vkToken, long vkPeerId,
    string maxToken, string maxChatId,
    string smsLogin, string smsPass, string smsPhone)
        {
            _notificationsEnabled = enabled;
            _notificationMethodIndex = methodIndex;

            _notifyTgEnabled = tgEnabled;
            _notifyYandexEnabled = yandexEnabled;
            _notifyMailEnabled = mailEnabled;
            _notifyGmailEnabled = gmailEnabled;
            _notifyCustomEnabled = customEnabled;
            _notifyVkEnabled = vkEnabled;
            _notifyMaxEnabled = maxEnabled;

            _telegramToken = tgToken ?? "";
            _telegramChatId = tgChatId ?? "";

            _vkToken = vkToken ?? "";
            _vkPeerId = vkPeerId;

            _maxToken = maxToken ?? "";
            _maxChatId = maxChatId ?? "";

            _smsLogin = smsLogin ?? "";
            _smsPass = smsPass ?? "";
            _smsPhone = smsPhone ?? "";

            // ★ Email-данные каждого провайдера уже сохранены в полях MainWindow
            //   (через SaveCurrentEmailFields в окне настроек)

            NotificationManager.ClearServices();

            if (enabled)
            {
                if (_notifyTgEnabled && !string.IsNullOrEmpty(_telegramToken) && long.TryParse(_telegramChatId, out long chatIdLong))
                    NotificationManager.AddService(new TelegramNotificationService(_telegramToken, chatIdLong));

                // ★ Каждая почта — со своими данными
                if (_notifyYandexEnabled && !string.IsNullOrEmpty(_emailYandexUser))
                    NotificationManager.AddService(new EmailNotificationService(
                        "smtp.yandex.ru", 587,
                        _emailYandexUser, _emailYandexPass, _emailYandexTo));

                if (_notifyMailEnabled && !string.IsNullOrEmpty(_emailMailUser))
                    NotificationManager.AddService(new EmailNotificationService(
                        "smtp.mail.ru", 587,
                        _emailMailUser, _emailMailPass, _emailMailTo));

                if (_notifyGmailEnabled && !string.IsNullOrEmpty(_emailGmailUser))
                    NotificationManager.AddService(new EmailNotificationService(
                        "smtp.gmail.com", 587,
                        _emailGmailUser, _emailGmailPass, _emailGmailTo));

                if (_notifyCustomEnabled && !string.IsNullOrEmpty(_emailCustomHost))
                    NotificationManager.AddService(new EmailNotificationService(
                        _emailCustomHost, _emailCustomPort,
                        _emailCustomUser, _emailCustomPass, _emailCustomTo));

                if (_notifyVkEnabled && !string.IsNullOrEmpty(_vkToken) && _vkPeerId > 0)
                    NotificationManager.AddService(new VkNotificationService(_vkToken, _vkPeerId));

                if (_notifyMaxEnabled && !string.IsNullOrEmpty(_maxToken) && long.TryParse(_maxChatId, out long maxId))
                    NotificationManager.AddService(new MaxNotificationService(_maxToken, maxId));
            }

            UpdateNotificationStatus(_notificationsEnabled);
            SaveSettings();
        }

        private void ApplyTickTimingVisibility()
        {
            if (TickTimingPanel != null)
                TickTimingPanel.Visibility = _tickTimingEnabled
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private bool _isDarkTheme = false;

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isDarkTheme = !_isDarkTheme;
            ChangeTheme(_isDarkTheme);
            ThemeToggleButton.Content = _isDarkTheme ? "💡" : "🌙";

            SaveSettings(); // ★ ДОБАВИТЬ ЭТУ СТРОКУ
        }

        public void ChangeTheme(bool isDark)
        {
            var themeUri = new Uri(isDark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", UriKind.Relative);
            var themeDict = new ResourceDictionary { Source = themeUri };

            // Ищем словарь с темой и заменяем его
            var mergedDicts = Application.Current.MainWindow.Resources.MergedDictionaries;
            if (mergedDicts.Count > 0)
            {
                mergedDicts[0] = themeDict;
            }
        }

        public void UpdateNotificationStatus(bool isActive)
        {
            if (NotificationStatusEllipse != null)
            {
                NotificationStatusEllipse.Fill = isActive ? Brushes.Green : Brushes.Gray;
                NotificationStatusText.Text = isActive ? "Уведомления: вкл" : "Уведомления: выкл";
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                // 1. Ссылка на файл version.txt в вашем Gitea (ветка main)
                string versionUrl = "https://gitea.sctbelpa/Ivanoff/T.E.R.M.I.N.A.L./raw/branch/main/version.txt";

                // 2. Текущая версия программы (берется автоматически из свойств проекта)
                string currentVersion = GetAppVersion();

                // 3. Токен из Gitea (вставьте свой новый токен)
                string giteaToken = "2e144c6218e171356d5b9798bc349797b72d2374";

                using var client = new HttpClient();

                // Добавляем заголовок авторизации для приватного репозитория
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("token", giteaToken);

                // Получаем строку версии с сервера
                string latestVersion = (await client.GetStringAsync(versionUrl)).Trim();

                // Если версия на сервере отличается от текущей
                if (latestVersion != currentVersion)
                {
                    var result = MessageBox.Show(
                        $"Доступна новая версия {latestVersion}.\nТекущая версия: {currentVersion}\n\nСкачать обновление?",
                        "Обновление",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Открываем ссылку на страницу релизов в браузере
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://gitea.sctbelpa/Ivanoff/T.E.R.M.I.N.A.L./releases",
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // Логируем ошибку (например, если нет интернета), но не мешаем работе программы
                System.Diagnostics.Debug.WriteLine($"Ошибка проверки обновлений: {ex.Message}");
            }
        }

        // Отправляет событие во все каналы, КРОМЕ Telegram (чтобы не дублировать живую таблицу)
        private void NotifyEvents(string text)
        {
            // Временно отключаем Telegram, чтобы событие не ушло туда
            var services = NotificationManager.GetServices(); // Нам понадобится метод GetServices
            foreach (var svc in services)
            {
                if (svc is not TelegramNotificationService)
                {
                    _ = svc.SendAsync(text);
                }
            }
        }


    }
}