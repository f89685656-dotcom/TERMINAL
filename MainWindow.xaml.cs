using HidSharp;
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
    }
}