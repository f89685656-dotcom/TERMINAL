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

namespace TERMINAL
{
    public class ConnectedClient : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private Brush _statusColor;
        private string _statusText = "Готов";
        private bool _isOffline = false;

        private volatile bool _isWaiting = false;
        private volatile bool _lastDoneOk = false;
        private volatile bool _lastDoneFail = false;

        public ConnectedClient()
        {
            _statusColor = CreateFrozenBrush(Colors.Gray);
        }

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public Brush StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(nameof(StatusColor)); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public string Sid { get; set; } = "";
        public TcpClient TcpClient { get; set; } = null!;
        public DateTime ConnectedAt { get; set; } = DateTime.Now;

        public bool IsWaiting { get => _isWaiting; set => _isWaiting = value; }
        public bool LastDoneOk { get => _lastDoneOk; set => _lastDoneOk = value; }
        public bool LastDoneFail { get => _lastDoneFail; set => _lastDoneFail = value; }

        public bool IsOffline => _isOffline;
        public DateTime? DisconnectedAt { get; set; } = null;
        public DateTime LastActivityAt { get; set; } = DateTime.Now;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static void RunOnUI(Action action)
        {
            var app = Application.Current;
            if (app == null) { action(); return; }
            if (app.Dispatcher.CheckAccess()) action();
            else app.Dispatcher.Invoke(action);
        }

        public string DisplayText => $"SID: {Sid}";

        public void SetStatusReady()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Green);
                StatusText = "Готов";
                IsWaiting = false; LastDoneOk = true; DisconnectedAt = null;
            });
        }

        public void SetStatusWaiting()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = "Занят съёмом";
                IsWaiting = true; LastDoneOk = false;
            });
        }

        public void SetStatusWaitingWithTime(int seconds)
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = $"Занят съёмом ({seconds} сек)";
            });
        }

        public void SetStatusOffline()
        {
            RunOnUI(() =>
            {
                _isOffline = true;
                StatusColor = CreateFrozenBrush(Colors.Red);
                StatusText = "Не отвечает";
                IsWaiting = false; LastDoneOk = false;
                if (DisconnectedAt == null) DisconnectedAt = DateTime.Now;
            });
        }

        public void SetStatusAlive()
        {
            if (_isOffline)
            {
                RunOnUI(() =>
                {
                    _isOffline = false;
                    StatusColor = CreateFrozenBrush(Colors.Green);
                    StatusText = "Готов";
                    DisconnectedAt = null;
                });
            }
        }

        public void SetStatusSaving()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = "Сохраняет данные";
            });
        }

        public void SetStatusSaved()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Green);
                StatusText = "Сохранил данные";
            });
        }

        public void SetStatusSaveFailed()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = "Ошибка сохранения";
            });
        }

        public void SetStatusDoneSuccess()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Green);
                StatusText = "Съём завершён";
                IsWaiting = false; LastDoneOk = true;
            });
        }

        public void SetStatusDoneFail()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = "Ошибка съёма";
                IsWaiting = false; LastDoneFail = true;
            });
        }

        public void SetStatusClearing()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = "Ожидает очистки";
            });
        }

        public void SetStatusCleared()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Green);
                StatusText = "Очищено";
            });
        }

        public void SetStatusClearFailed()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = "Очистка не удалась";
            });
        }
    }

    public class StepItem : INotifyPropertyChanged
    {
        private string _timerText = "";
        private string _arrowText = "  ";
        private string _statusMark = "☐";
        private string _kosaCounterText = "";

        public string IndexText { get; set; } = "";
        public string ValueNumber { get; set; } = "";
        public string ValueUnits { get; set; } = "";

        public string TimerText
        {
            get => _timerText;
            set { _timerText = value; OnPropertyChanged(nameof(TimerText)); }
        }

        public string ArrowText
        {
            get => _arrowText;
            set { _arrowText = value; OnPropertyChanged(nameof(ArrowText)); }
        }

        public string StatusMark
        {
            get => _statusMark;
            set { _statusMark = value; OnPropertyChanged(nameof(StatusMark)); }
        }

        public string KosaCounterText
        {
            get => _kosaCounterText;
            set { _kosaCounterText = value; OnPropertyChanged(nameof(KosaCounterText)); }
        }

        public int PlannedDuration { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class TemperatureJob : INotifyPropertyChanged
    {
        private string _setPointText = "";
        private string _stabilizationMinutesText = "120";
        private string _statusText = "ожидает";
        private Brush _statusColor = Brushes.Gray;
        private bool _isEnabled = true;

        public string IndexText { get; set; } = "";

        public string SetPointText
        {
            get => _setPointText;
            set { _setPointText = value; OnPropertyChanged(nameof(SetPointText)); }
        }

        public string StabilizationMinutesText
        {
            get => _stabilizationMinutesText;
            set { _stabilizationMinutesText = value; OnPropertyChanged(nameof(StabilizationMinutesText)); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public Brush StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(nameof(StatusColor)); }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        public float SetPoint { get; set; }
        public int StabilizationMinutes { get; set; } = 120;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class MainWindow : Window
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly object _streamLock = new object();

        private DispatcherTimer? _timer;
        private DispatcherTimer? _statusResetTimer;
        private DispatcherTimer? _furnaceTempTimer;
        private DispatcherTimer? _platinaTimer;

        private string? _selectedTargetPressure;
        private int _currentStepIndex = 0;
        private int _completedSteps = 0;

        private bool _isRunning = false;
        private bool _stopRequested = false;

        private volatile bool _pauseRequested = false;

        private bool _timerActive = false;

        private DateTime _totalTimerStart = DateTime.MinValue;
        private bool _totalTimerActive = false;

        private TcpListener? _broadcastServer;
        private readonly List<ConnectedClient> _connectedClients = new();
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
        private const ushort FurnaceSetPointReadRegister = 202;
        private const ushort FurnaceCurrentTempRegister = 205;
        private const ushort FurnaceModeRegister = 200;
        private const ushort FurnaceModeManual = 2;
        private const ushort FurnaceModeIdle = 1;

        // СКТБ ЭлПа (платина) — USB HID
        private const int PlatinaVid = 0xFFFF;
        private const int PlatinaPid = 0x0009;
        private const string PlatinaRequest = "pass2 meas2? 2\n\0";

        private double _platinaLastValue = double.NaN;
        private DateTime _platinaLastSuccess = DateTime.MinValue;
        private int _platinaFailCount = 0;

        // Порт broadcast-сервера (задаётся при старте: 9990 — рабочая, 9999 — тестовая)
        private int BroadcastPort = 9990;

        // Таймер стабилизации температуры печи
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

        private readonly HashSet<string> _saveResponses = new();
        private readonly object _saveLock = new();

        private readonly HashSet<string> _clearResponses = new();
        private readonly object _clearLock = new();

        private ObservableCollection<StepItem> _stepItems = new();

        private string _savedStartStep = "1";
        private string _savedCycleCount = "10";

        private bool _isCalibrationMode = true;

        private bool _uiLoaded = false;

        private bool _kosaEnabled = false;
        private int _kosaTimerSeconds = 25;

        // автоСтарт
        private bool _autoStartEnabled = false;
        private bool _autoStartTriggered = false;
        private const int AutoStartCalibrationMinutes = 120;
        private const int AutoStartPumpMinutes = 30;

        private string _currentPressureUnitsLabel = "";

        // Пакетный режим по температурам (только для «Градуировка»)
        private readonly ObservableCollection<TemperatureJob> _batchJobs = new();
        private bool _batchRunning = false;
        private bool _batchStopRequested = false;
        private TaskCompletionSource<bool>? _batchRunDoneSignal;

        // Цвета кнопки СТАРТ
        private static readonly Brush StartButtonNormalBrush =
            new SolidColorBrush(Color.FromRgb(0x50, 0x90, 0xD0));
        private static readonly Brush StartButtonBatchBrush =
            new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x8B));

        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
                "TERMINAL");

        private static readonly string SettingsFile =
            Path.Combine(SettingsDir, "settings.json");

        private Settings? _loadedSettings = null;

        private DateTime _lastAgkStatusRequest = DateTime.MinValue;
        private int _agkStatusFailCount = 0;
        private const int AgkStatusRequestIntervalMs = 1000;

        public MainWindow()
        {
            InitializeComponent();

            // Замораживаем кисти, чтобы можно было использовать из любого потока
            if (StartButtonNormalBrush is SolidColorBrush sb1 && sb1.CanFreeze) sb1.Freeze();
            if (StartButtonBatchBrush is SolidColorBrush sb2 && sb2.CanFreeze) sb2.Freeze();

            _uiLoaded = false;

            LoadTargetPressures();
            LoadTimerSets();
            LoadSettings();

            ApplyWindowPositionOrFallback();

            _uiLoaded = true;

            ApplyLoadedSettingsToUi();

            if (BatchList != null)
                BatchList.ItemsSource = _batchJobs;

            RenumberBatchJobs();
        }

        /// <summary>
        /// Ставит окно в правую половину рабочего стола (учитывает панель задач).
        /// </summary>
        private void PositionWindowRightHalf()
        {
            try
            {
                var work = SystemParameters.WorkArea;

                double halfWidth = work.Width / 2.0;

                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.Width = halfWidth;
                this.Height = work.Height;
                this.Left = work.Left + work.Width - halfWidth;
                this.Top = work.Top;
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Пробует применить сохранённую позицию из настроек.
        /// Если её нет, или она вне текущего рабочего стола — ставит окно в правую половину.
        /// </summary>
        private void ApplyWindowPositionOrFallback()
        {
            try
            {
                var work = SystemParameters.WorkArea;

                double l = _loadedSettings?.WindowLeft ?? double.NaN;
                double t = _loadedSettings?.WindowTop ?? double.NaN;
                double w = _loadedSettings?.WindowWidth ?? double.NaN;
                double h = _loadedSettings?.WindowHeight ?? double.NaN;

                bool hasSaved =
                    !double.IsNaN(l) && !double.IsNaN(t) &&
                    !double.IsNaN(w) && !double.IsNaN(h) &&
                    w >= 300 && h >= 300;

                if (hasSaved)
                {
                    bool intersects =
                        l + w > work.Left &&
                        t + h > work.Top &&
                        l < work.Left + work.Width &&
                        t < work.Top + work.Height;

                    if (intersects)
                    {
                        this.WindowStartupLocation = WindowStartupLocation.Manual;
                        this.Width = w;
                        this.Height = h;
                        this.Left = l;
                        this.Top = t;
                        return;
                    }
                }
            }
            catch
            {
                // ignore
            }

            PositionWindowRightHalf();
        }

        /// <summary>
        /// Сохраняет текущую позицию/размер окна в _loadedSettings
        /// (реальная запись в файл — в SaveSettings()).
        /// </summary>
        private void SaveWindowPositionToSettings()
        {
            try
            {
                if (_loadedSettings == null) return;

                var bounds = this.WindowState == WindowState.Normal
                    ? new Rect(this.Left, this.Top, this.Width, this.Height)
                    : this.RestoreBounds;

                if (bounds.Width < 100 || bounds.Height < 100) return;

                _loadedSettings.WindowLeft = bounds.Left;
                _loadedSettings.WindowTop = bounds.Top;
                _loadedSettings.WindowWidth = bounds.Width;
                _loadedSettings.WindowHeight = bounds.Height;
            }
            catch { }
        }

        // ─────────────────────────── ЗАПОЛНЕНИЕ ───────────────────────────

        private void LoadTargetPressures()
        {
            TargetPressureCombo.Items.Add("100 кгс/см²");
            TargetPressureCombo.Items.Add("200 кгс/см²");
            TargetPressureCombo.Items.Add("250 кгс/см²");
            TargetPressureCombo.Items.Add("300 кгс/см²");
            TargetPressureCombo.Items.Add("350 кгс/см²");
            TargetPressureCombo.Items.Add("350 кгс/см² (БИДТ)");
            TargetPressureCombo.Items.Add("400 кгс/см²");
            TargetPressureCombo.Items.Add("450 кгс/см²");
            TargetPressureCombo.Items.Add("500 кгс/см²");
            TargetPressureCombo.Items.Add("600 кгс/см²");
            TargetPressureCombo.Items.Add("600 кгс/см² (13 точек)");
            TargetPressureCombo.Items.Add("25 МПа");
            TargetPressureCombo.Items.Add("60 МПа");
            TargetPressureCombo.Items.Add("75 МПа");
            TargetPressureCombo.Items.Add("80 МПа");
            TargetPressureCombo.Items.Add("100 МПа");
            TargetPressureCombo.Items.Add("110 МПа");
            TargetPressureCombo.Items.Add("130 МПа");
            TargetPressureCombo.Items.Add("138 МПа (БУРСЕРВИС)");
            TargetPressureCombo.Items.Add("140 МПа");
            TargetPressureCombo.Items.Add("160 МПа (БУРСЕРВИС)");
            TargetPressureCombo.Items.Add("160 МПа (Schlumberger)");
            TargetPressureCombo.SelectedIndex = 0;
        }

        private void LoadTimerSets()
        {
            TimerSetCombo.Items.Add("Короткие таймеры");
            TimerSetCombo.Items.Add("Длинные таймеры");
            TimerSetCombo.SelectedIndex = 0;
        }

        private readonly Dictionary<string, double[]> _stepsByTarget = new()
        {
            { "100 кгс/см²", new double[] { 0, 20, 30, 40, 50, 60, 70, 80, 90, 100, 80, 70, 60, 50, 30, 0 } },
            { "200 кгс/см²", new double[] { 0, 30, 60, 80, 100, 120, 140, 160, 180, 200, 160, 140, 120, 100, 60, 0 } },
            { "250 кгс/см²", new double[] { 0, 30, 60, 90, 120, 150, 180, 210, 230, 250, 210, 180, 150, 120, 60, 0 } },
            { "300 кгс/см²", new double[] { 0, 50, 100, 125, 150, 175, 200, 225, 250, 300, 225, 200, 175, 150, 100, 0 } },
            { "350 кгс/см²", new double[] { 0, 50, 70, 120, 170, 220, 270, 300, 320, 350, 300, 270, 220, 170, 70, 0 } },
            { "350 кгс/см² (БИДТ)", new double[] { 0, 40, 80, 120, 160, 200, 240, 280, 320, 350, 320, 280, 200, 160, 80, 0 } },
            { "400 кгс/см²", new double[] { 0, 50, 70, 120, 170, 220, 270, 320, 370, 400, 320, 270, 220, 170, 70, 0 } },
            { "450 кгс/см²", new double[] { 0, 10, 20, 30, 40, 50, 120, 270, 370, 450, 370, 270, 120, 50, 30, 0 } },
            { "500 кгс/см²", new double[] { 0, 70, 120, 170, 220, 270, 320, 420, 470, 500, 420, 320, 270, 220, 120, 0 } },
            { "600 кгс/см²", new double[] { 0, 70, 120, 170, 220, 320, 420, 520, 570, 600, 520, 420, 320, 220, 120, 0 } },
            { "600 кгс/см² (13 точек)", new double[] { 0, 70, 120, 170, 270, 370, 470, 570, 600, 470, 270, 120, 0 } },
            { "25 МПа", new double[] { 0, 5, 7.5, 10, 12.5, 15, 17.5, 20, 22.5, 25, 20, 17.5, 15, 12.5, 7.5, 0 } },
            { "60 МПа", new double[] { 0, 7.5, 15, 22.5, 30, 37.5, 45, 50, 55, 60, 50, 45, 37.5, 30, 15, 0 } },
            { "75 МПа", new double[] { 0, 7.5, 15, 22.5, 30, 40, 50, 60, 67.5, 75, 60, 50, 40, 30, 15, 0 } },
            { "80 МПа", new double[] { 0, 7.5, 15, 22.5, 30, 40, 50, 60, 70, 80, 60, 50, 40, 30, 15, 0 } },
            { "100 МПа", new double[] { 0, 7.5, 15, 30, 45, 60, 70, 80, 90, 100, 80, 70, 60, 45, 15, 0 } },
            { "110 МПа", new double[] { 0, 7.5, 15, 30, 45, 60, 75, 90, 100, 110, 90, 75, 60, 45, 15, 0 } },
            { "130 МПа", new double[] { 0, 15, 30, 45, 60, 75, 90, 105, 120, 130, 105, 90, 75, 60, 30, 0 } },
            { "138 МПа (БУРСЕРВИС)", new double[] { 0, 27.5, 55, 85.75, 110.25, 138, 131, 103.5, 75.75, 48.25, 20.75, 2.5 } },
            { "140 МПа", new double[] { 0, 15, 30, 45, 60, 75, 90, 105, 120, 140, 105, 90, 75, 60, 30, 0 } },
            { "160 МПа (БУРСЕРВИС)", new double[] { 0, 34.5, 69, 103.5, 138, 160, 155, 120.75, 86.25, 51.75, 17.25, 2.5 } },
            { "160 МПа (Schlumberger)", new double[] { 0, 15, 30, 45, 60, 80, 100, 120, 140, 160, 120, 80, 60, 45, 15, 0 } },
        };

        // ─────────────────────────── НАСТРОЙКИ ───────────────────────────

        private class Settings
        {
            public int TargetPressureIndex { get; set; } = 0;
            public int TimerSetIndex { get; set; } = 0;
            public string SavePath { get; set; } = "";
            public int ModeIndex { get; set; } = 0;
            public string StartStep { get; set; } = "1";
            public string CycleCount { get; set; } = "10";
            public bool KosaEnabled { get; set; } = false;
            public int KosaTimerSeconds { get; set; } = 25;
            public bool AutoStartEnabled { get; set; } = false;
            public List<BatchJobDto> BatchJobs { get; set; } = new();
            public double WindowLeft { get; set; } = double.NaN;
            public double WindowTop { get; set; } = double.NaN;
            public double WindowWidth { get; set; } = double.NaN;
            public double WindowHeight { get; set; } = double.NaN;
        }

        private class BatchJobDto
        {
            public string SetPoint { get; set; } = "25";
            public string StabilizationMinutes { get; set; } = "120";
            public bool IsEnabled { get; set; } = true;
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    _loadedSettings = JsonSerializer.Deserialize<Settings>(json);
                }

                if (_loadedSettings == null)
                    _loadedSettings = new Settings();

                if (string.IsNullOrWhiteSpace(_loadedSettings.SavePath))
                {
                    string docs = Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments);
                    _loadedSettings.SavePath = Path.Combine(docs, "TERMINAL");
                }

                _savedStartStep = string.IsNullOrWhiteSpace(_loadedSettings.StartStep)
                    ? "1" : _loadedSettings.StartStep;
                _savedCycleCount = string.IsNullOrWhiteSpace(_loadedSettings.CycleCount)
                    ? "10" : _loadedSettings.CycleCount;
                _kosaEnabled = _loadedSettings.KosaEnabled;
                _kosaTimerSeconds = _loadedSettings.KosaTimerSeconds > 0
                    ? _loadedSettings.KosaTimerSeconds : 25;
                _autoStartEnabled = _loadedSettings.AutoStartEnabled;

                if (_loadedSettings.BatchJobs != null && _loadedSettings.BatchJobs.Count > 0)
                {
                    _batchJobs.Clear();
                    foreach (var dto in _loadedSettings.BatchJobs)
                    {
                        _batchJobs.Add(new TemperatureJob
                        {
                            SetPointText = string.IsNullOrWhiteSpace(dto.SetPoint)
                                ? "25" : dto.SetPoint,
                            StabilizationMinutesText =
                                string.IsNullOrWhiteSpace(dto.StabilizationMinutes)
                                    ? "120" : dto.StabilizationMinutes,
                            IsEnabled = dto.IsEnabled
                        });
                    }
                }
            }
            catch
            {
                _loadedSettings = new Settings();
                _savedStartStep = "1";
                _savedCycleCount = "10";
                _kosaEnabled = false;
                _kosaTimerSeconds = 25;
                _autoStartEnabled = false;

                string docs = Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments);
                _loadedSettings.SavePath = Path.Combine(docs, "TERMINAL");
            }
        }

        private void ApplyLoadedSettingsToUi()
        {
            if (_loadedSettings == null) return;

            if (_loadedSettings.TargetPressureIndex >= 0 &&
                _loadedSettings.TargetPressureIndex < TargetPressureCombo.Items.Count)
            {
                TargetPressureCombo.SelectedIndex = _loadedSettings.TargetPressureIndex;
            }
            else
            {
                TargetPressureCombo.SelectedIndex = 0;
            }

            if (_loadedSettings.TimerSetIndex >= 0 &&
                _loadedSettings.TimerSetIndex < TimerSetCombo.Items.Count)
            {
                TimerSetCombo.SelectedIndex = _loadedSettings.TimerSetIndex;
            }
            else
            {
                TimerSetCombo.SelectedIndex = 0;
            }

            if (!string.IsNullOrWhiteSpace(_loadedSettings.SavePath))
                SavePathBox.Text = _loadedSettings.SavePath;

            if (_loadedSettings.ModeIndex >= 0 &&
                _loadedSettings.ModeIndex < ModeCombo.Items.Count)
            {
                ModeCombo.SelectedIndex = _loadedSettings.ModeIndex;
            }
            else
            {
                ModeCombo.SelectedIndex = 0;
            }

            if (KosaCheckBox != null)
                KosaCheckBox.IsChecked = _kosaEnabled;
            if (KosaTimerBox != null)
                KosaTimerBox.Text = _kosaTimerSeconds.ToString(CultureInfo.InvariantCulture);

            if (AutoStartCheckBox != null)
                AutoStartCheckBox.IsChecked = _autoStartEnabled;

            UpdateModeUi();

            if (TargetPressureCombo.SelectedItem != null)
            {
                _selectedTargetPressure = TargetPressureCombo.SelectedItem.ToString();
                _currentStepIndex = 0;
                _completedSteps = 0;
                ShowSteps();
                UpdateMaxMinFromRange();
            }
        }

        private void SaveSettings()
        {
            try
            {
                if (!_uiLoaded) return;
                if (SavePathBox == null) return;
                if (TargetPressureCombo == null) return;
                if (TimerSetCombo == null) return;
                if (ModeCombo == null) return;

                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                var s = new Settings
                {
                    TargetPressureIndex = TargetPressureCombo.SelectedIndex,
                    TimerSetIndex = TimerSetCombo.SelectedIndex,
                    SavePath = SavePathBox.Text,
                    ModeIndex = ModeCombo.SelectedIndex,
                    StartStep = _savedStartStep,
                    CycleCount = _savedCycleCount,
                    KosaEnabled = _kosaEnabled,
                    KosaTimerSeconds = _kosaTimerSeconds,
                    AutoStartEnabled = _autoStartEnabled,
                    BatchJobs = _batchJobs.Select(j => new BatchJobDto
                    {
                        SetPoint = j.SetPointText,
                        StabilizationMinutes = j.StabilizationMinutesText,
                        IsEnabled = j.IsEnabled
                    }).ToList(),
                    WindowLeft = _loadedSettings?.WindowLeft ?? double.NaN,
                    WindowTop = _loadedSettings?.WindowTop ?? double.NaN,
                    WindowWidth = _loadedSettings?.WindowWidth ?? double.NaN,
                    WindowHeight = _loadedSettings?.WindowHeight ?? double.NaN
                };

                string json = JsonSerializer.Serialize(s,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }

        // ─────────────────────────── РЕЖИМ РАБОТЫ ───────────────────────────

        private void ModeCombo_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_uiLoaded) return;
            if (ModeCombo == null) return;

            SaveCurrentParamValue();

            _isCalibrationMode = ModeCombo.SelectedIndex == 0;

            UpdateModeUi();
            SaveSettings();
        }

        private void SaveCurrentParamValue()
        {
            if (ParamBox == null) return;

            string val = ParamBox.Text?.Trim() ?? "";
            if (_isCalibrationMode) _savedStartStep = val;
            else _savedCycleCount = val;
        }

        private void UpdateModeUi()
        {
            if (ParamLabel == null || ParamBox == null) return;

            if (_isCalibrationMode)
            {
                ParamLabel.Text = "Начать с шага:";
                ParamBox.Text = string.IsNullOrWhiteSpace(_savedStartStep)
                    ? "1" : _savedStartStep;
            }
            else
            {
                ParamLabel.Text = "Циклов:";
                ParamBox.Text = string.IsNullOrWhiteSpace(_savedCycleCount)
                    ? "10" : _savedCycleCount;
            }

            if (TimerSetPanel != null)
                TimerSetPanel.Visibility = Visibility.Visible;

            if (CalibrationPanel != null && PumpPanel != null)
            {
                if (_isCalibrationMode)
                {
                    CalibrationPanel.Visibility = Visibility.Visible;
                    PumpPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    CalibrationPanel.Visibility = Visibility.Collapsed;
                    PumpPanel.Visibility = Visibility.Visible;
                }
            }

            UpdateStartButtonMode();

            UpdateCyclesLabel();
            UpdateAutoStartInfo();
        }

        /// <summary>
        /// Обновляет текст и цвет кнопки СТАРТ в зависимости от того,
        /// запустится ли обычная градуировка или пакетный режим.
        /// </summary>
        private void UpdateStartButtonMode()
        {
            if (StartButton == null) return;

            int activeJobs = _batchJobs.Count(j => j.IsEnabled);
            bool isBatch = _isCalibrationMode && activeJobs > 0;

            if (isBatch)
            {
                StartButton.Content = "СТАРТ (пакет)";
                StartButton.Background = StartButtonBatchBrush;
            }
            else
            {
                StartButton.Content = "СТАРТ (обычный)";
                StartButton.Background = StartButtonNormalBrush;
            }
        }

        private void ParamBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_uiLoaded) return;
            if (ParamBox == null) return;

            string val = ParamBox.Text?.Trim() ?? "";

            if (_isCalibrationMode) _savedStartStep = val;
            else _savedCycleCount = val;

            if (!_isCalibrationMode)
                UpdateCyclesLabel();

            SaveSettings();
        }

        private void UpdateCyclesLabel()
        {
            if (CyclesText == null) return;

            int total = 0;
            if (ParamBox != null &&
                int.TryParse(ParamBox.Text?.Trim(), out int c) && c > 0)
            {
                total = c;
            }

            CyclesText.Text = $"Цикл: 0/{total}";
        }

        // ─────────────────────────── KOSA UI ───────────────────────────

        private void KosaCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_uiLoaded) return;
            _kosaEnabled = KosaCheckBox.IsChecked == true;

            foreach (var item in _stepItems)
            {
                if (item.KosaCounterText == "0/3" || item.KosaCounterText == "0/1")
                {
                    item.KosaCounterText = _kosaEnabled ? "0/3" : "0/1";
                }
            }

            SaveSettings();
        }

        private void KosaTimerBox_PreviewTextInput(object sender,
            System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void KosaTimerBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_uiLoaded) return;
            if (KosaTimerBox == null) return;

            string val = KosaTimerBox.Text?.Trim() ?? "";
            if (int.TryParse(val, out int v) && v > 0)
            {
                _kosaTimerSeconds = v;
                SaveSettings();
            }
        }

        // ─────────────────────────── АВТОСТАРТ ───────────────────────────

        private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_uiLoaded) return;
            _autoStartEnabled = AutoStartCheckBox.IsChecked == true;
            _autoStartTriggered = false;
            UpdateAutoStartInfo();
            SaveSettings();
        }

        private void UpdateAutoStartInfo()
        {
            if (AutoStartInfoText == null) return;

            if (!_autoStartEnabled)
            {
                AutoStartInfoText.Text = "";
                return;
            }

            if (_autoStartTriggered)
            {
                AutoStartInfoText.Text =
                    "автоСтарт уже сработал/заблокирован. " +
                    "Смените уставку или сбросьте таймер стабилизации для повтора.";
                return;
            }

            int minutes = _isCalibrationMode
                ? AutoStartCalibrationMinutes
                : AutoStartPumpMinutes;

            AutoStartInfoText.Text =
                $"Запуск «{(_isCalibrationMode ? "Градуировка" : "Жамкать")}» " +
                $"после {minutes} мин стабилизации";
        }

        // ═══════════════════════════════════════════════════════════════════
        // ПАКЕТНЫЙ РЕЖИМ ПО ТЕМПЕРАТУРАМ (только «Градуировка»)
        // ═══════════════════════════════════════════════════════════════════

        private void AddBatchJob_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning) return;

            var job = new TemperatureJob
            {
                SetPointText = "25",
                StabilizationMinutesText = "120",
                IsEnabled = true
            };
            _batchJobs.Add(job);
            RenumberBatchJobs();
            SaveSettings();
            UpdateStartButtonMode();
        }

        private void RemoveBatchJob_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning) return;

            if (sender is System.Windows.Controls.Button btn &&
                btn.Tag is TemperatureJob job)
            {
                job.PropertyChanged -= BatchJob_PropertyChanged;
                _batchJobs.Remove(job);
                RenumberBatchJobs();
                SaveSettings();
                UpdateStartButtonMode();
            }
        }

        private void ClearBatch_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning) return;

            foreach (var j in _batchJobs)
                j.PropertyChanged -= BatchJob_PropertyChanged;

            _batchJobs.Clear();
            RenumberBatchJobs();
            SaveSettings();
            UpdateStartButtonMode();
        }

        private void RenumberBatchJobs()
        {
            for (int i = 0; i < _batchJobs.Count; i++)
            {
                _batchJobs[i].IndexText = (i + 1) + ".";

                _batchJobs[i].PropertyChanged -= BatchJob_PropertyChanged;
                _batchJobs[i].PropertyChanged += BatchJob_PropertyChanged;
            }

            BatchList.ItemsSource = null;
            BatchList.ItemsSource = _batchJobs;
        }

        private void BatchJob_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_uiLoaded) return;

            if (e.PropertyName == nameof(TemperatureJob.IsEnabled))
                UpdateStartButtonMode();

            SaveSettings();
        }

        private async Task RunBatchAsync()
        {
            if (_batchJobs.Count == 0)
            {
                MessageBox.Show("Список температур пуст. Добавьте хотя бы одну.");
                return;
            }

            // Валидация активных заданий
            for (int i = 0; i < _batchJobs.Count; i++)
            {
                var job = _batchJobs[i];
                if (!job.IsEnabled) continue;

                if (!float.TryParse(job.SetPointText?.Replace(',', '.'),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out float sp))
                {
                    MessageBox.Show($"Задание #{i + 1}: некорректная уставка «{job.SetPointText}»");
                    return;
                }

                if (!int.TryParse(job.StabilizationMinutesText?.Trim(), out int mins) || mins <= 0)
                {
                    MessageBox.Show($"Задание #{i + 1}: некорректное время стабилизации " +
                                    $"«{job.StabilizationMinutesText}» (нужно положительное число минут)");
                    return;
                }

                job.SetPoint = sp;
                job.StabilizationMinutes = mins;
            }

            int enabledCount = _batchJobs.Count(j => j.IsEnabled);
            if (enabledCount == 0)
            {
                MessageBox.Show("Все задания отключены. Включите хотя бы одно.");
                return;
            }

            _batchRunning = true;
            _batchStopRequested = false;

            SetExternalButtonsEnabled(false);
            StartButton.IsEnabled = false;
            AddBatchJobButton.IsEnabled = false;
            ClearBatchButton.IsEnabled = false;

            try
            {
                int completedCount = 0;

                for (int i = 0; i < _batchJobs.Count; i++)
                {
                    if (_batchStopRequested || _stopRequested)
                    {
                        SetStatusWarning("Пакетный режим остановлен оператором");
                        return;
                    }

                    var job = _batchJobs[i];

                    if (!job.IsEnabled)
                    {
                        job.StatusText = "пропущено";
                        job.StatusColor = Brushes.Gray;
                        continue;
                    }

                    completedCount++;

                    // 1) Запись уставки
                    job.StatusText = "запись уставки";
                    job.StatusColor = Brushes.Orange;

                    SetStatus($"[Пакет {completedCount}/{enabledCount}] запись уставки " +
                              $"{job.SetPoint.ToString("0.##", CultureInfo.InvariantCulture)} °C в ST-351...");

                    bool writeOk = await WriteFurnaceSetPointAsync(
                        job.SetPoint, FurnaceIp, FurnacePort, FurnaceSetPointRegister);

                    if (!writeOk)
                    {
                        job.StatusText = "ошибка записи";
                        job.StatusColor = Brushes.Red;
                        SetStatusError($"[Пакет {completedCount}] не удалось записать уставку");
                        return;
                    }

                    _lastValidFurnaceSetPoint = job.SetPoint;
                    await RefreshFurnaceSetPointAsync();

                    ResetStabilizationTimer();
                    _autoStartTriggered = true;
                    UpdateAutoStartInfo();

                    // 2) Стабилизация — своё время для каждого задания
                    job.StatusText = $"стабилизация {job.StabilizationMinutes} мин";
                    job.StatusColor = Brushes.Orange;

                    bool stable = await WaitForBatchStabilizationAsync(
                        job.StabilizationMinutes, completedCount, enabledCount);

                    if (!stable)
                    {
                        job.StatusText = "стабилизация прервана";
                        job.StatusColor = Brushes.Red;
                        SetStatusWarning($"[Пакет {completedCount}] стабилизация прервана");
                        return;
                    }

                    // 3) Запуск «Градуировка» и ожидание завершения
                    job.StatusText = "работа";
                    job.StatusColor = Brushes.Blue;

                    SetStatus($"[Пакет {completedCount}/{enabledCount}] запуск «Градуировка» " +
                              $"на {job.SetPoint.ToString("0.##", CultureInfo.InvariantCulture)} °C");

                    _batchRunDoneSignal = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Dispatcher.InvokeAsync(RunCalibrationModeAsync);
                        }
                        finally
                        {
                            _batchRunDoneSignal?.TrySetResult(true);
                        }
                    });

                    await _batchRunDoneSignal.Task;
                    _batchRunDoneSignal = null;

                    if (_batchStopRequested || _stopRequested)
                    {
                        SetStatusWarning("Пакетный режим остановлен оператором");
                        return;
                    }

                    job.StatusText = "завершено";
                    job.StatusColor = Brushes.Green;

                    SetStatus($"[Пакет {completedCount}/{enabledCount}] завершён");
                }

                SetStatusSuccess($"Пакетный режим завершён: обработано {completedCount} температур");
            }
            finally
            {
                _batchRunning = false;
                _batchRunDoneSignal = null;
                _batchStopRequested = false;

                StartButton.IsEnabled = true;
                AddBatchJobButton.IsEnabled = true;
                ClearBatchButton.IsEnabled = true;
                SetExternalButtonsEnabled(true);

                BatchProgressText.Text = "";

                UpdateStartButtonMode();
            }
        }

        private async Task<bool> WaitForBatchStabilizationAsync(
            int requiredMinutes, int jobIndex, int totalJobs)
        {
            var start = DateTime.Now;
            var outOfRangeSince = DateTime.MinValue;

            while (true)
            {
                if (_batchStopRequested || _stopRequested) return false;

                await RefreshFurnaceCurrentTempAsync();
                await RefreshFurnaceSetPointAsync();

                string spTxt = FurnaceSetPointBox?.Text?.Replace(',', '.') ?? "";
                string tTxt = FurnaceCurrentTempText?.Text ?? "";

                int idx = tTxt.IndexOf(" °C", StringComparison.Ordinal);
                if (idx >= 0) tTxt = tTxt.Substring(0, idx);
                tTxt = tTxt.Trim().TrimStart('+').Replace(',', '.');

                if (!double.TryParse(spTxt, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double setPoint) ||
                    !double.TryParse(tTxt, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double currentTemp))
                {
                    await Task.Delay(1000);
                    continue;
                }

                bool inRange = Math.Abs(currentTemp - setPoint) <= FurnaceStabilizationTolerance;

                if (inRange)
                {
                    outOfRangeSince = DateTime.MinValue;
                    var elapsed = DateTime.Now - start;
                    var remaining = TimeSpan.FromMinutes(requiredMinutes) - elapsed;
                    if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

                    BatchProgressText.Text =
                        $"Задание {jobIndex}/{totalJobs}: стабилизация " +
                        $"{setPoint.ToString("0.##", CultureInfo.InvariantCulture)} °C, " +
                        $"осталось {remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";

                    if (elapsed.TotalMinutes >= requiredMinutes)
                        return true;
                }
                else
                {
                    if (outOfRangeSince == DateTime.MinValue)
                        outOfRangeSince = DateTime.Now;

                    var outOfRange = DateTime.Now - outOfRangeSince;
                    if (outOfRange.TotalSeconds >= StabilizationOutOfRangeGraceSeconds)
                    {
                        start = DateTime.Now;
                        outOfRangeSince = DateTime.MinValue;
                        BatchProgressText.Text =
                            $"Задание {jobIndex}/{totalJobs}: " +
                            $"температура вне диапазона — таймер стабилизации сброшен";
                    }
                    else
                    {
                        BatchProgressText.Text =
                            $"Задание {jobIndex}/{totalJobs}: " +
                            $"температура вне диапазона " +
                            $"({outOfRange.TotalSeconds:F0} сек из {StabilizationOutOfRangeGraceSeconds})";
                    }
                }

                await Task.Delay(1000);
            }
        }

        // ─────────────────────────── COMBOBOX ───────────────────────────

        private void TargetPressureCombo_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_uiLoaded) return;
            if (TargetPressureCombo == null) return;
            if (StepsList == null) return;

            if (TargetPressureCombo.SelectedItem == null)
            {
                _selectedTargetPressure = null;
                StepsList.ItemsSource = null;
                return;
            }

            _selectedTargetPressure = TargetPressureCombo.SelectedItem.ToString();
            _currentStepIndex = 0;
            _completedSteps = 0;
            ShowSteps();
            UpdateMaxMinFromRange();
            UpdateModeUi();
            SaveSettings();
        }

        private void TimerSetCombo_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_uiLoaded) return;
            if (TimerSetCombo == null) return;

            RecalcStepTimers();
            SaveSettings();
        }

        private void ShowSteps()
        {
            if (_selectedTargetPressure == null) return;
            if (!_stepsByTarget.TryGetValue(_selectedTargetPressure, out var values))
            {
                _stepItems.Clear();
                StepsList.ItemsSource = null;
                return;
            }

            bool longTimers = TimerSetCombo.SelectedIndex == 1;
            string unitsLabel = GetUnitsLabel(_selectedTargetPressure);

            _stepItems = new ObservableCollection<StepItem>();

            for (int i = 0; i < values.Length; i++)
            {
                int dur = GetStepDuration(values, i, longTimers);
                _stepItems.Add(new StepItem
                {
                    IndexText = (i + 1) + ".",
                    ValueNumber = values[i].ToString(CultureInfo.InvariantCulture),
                    ValueUnits = unitsLabel,
                    TimerText = dur.ToString(),
                    ArrowText = "  ",
                    StatusMark = "☐",
                    KosaCounterText = _kosaEnabled ? "0/3" : "0/1",
                    PlannedDuration = dur
                });
            }

            bool isLong = unitsLabel.Contains("кгс");

            StepsList.ItemTemplate = (DataTemplate)FindResource(
                isLong ? "LongUnitsTemplate" : "ShortUnitsTemplate");

            StepsHeaderShort.Visibility = isLong ? Visibility.Collapsed : Visibility.Visible;
            StepsHeaderLong.Visibility = isLong ? Visibility.Visible : Visibility.Collapsed;

            StepsList.ItemsSource = _stepItems;
            UpdateStepCounter();
        }

        private void RecalcStepTimers()
        {
            if (_selectedTargetPressure == null) return;
            if (!_stepsByTarget.TryGetValue(_selectedTargetPressure, out var values)) return;
            if (_stepItems.Count != values.Length) return;

            bool longTimers = TimerSetCombo.SelectedIndex == 1;
            for (int i = 0; i < _stepItems.Count; i++)
            {
                int dur = GetStepDuration(values, i, longTimers);
                _stepItems[i].TimerText = dur.ToString();
                _stepItems[i].PlannedDuration = dur;
            }
        }

        private void UpdateMaxMinFromRange()
        {
            if (_selectedTargetPressure == null) return;
            if (!_stepsByTarget.TryGetValue(_selectedTargetPressure, out var steps))
                return;

            string unitsLabel = GetUnitsLabel(_selectedTargetPressure);
            double maxValue = steps.Max();
            double minValue = 0;

            MaxValueText.Text = maxValue.ToString(CultureInfo.InvariantCulture);
            MaxUnitsText.Text = unitsLabel;
            MinValueText.Text = minValue.ToString(CultureInfo.InvariantCulture);
            MinUnitsText.Text = unitsLabel;
        }

        private void ResetStepsToInitial()
        {
            _completedSteps = 0;
            _currentStepIndex = 0;

            foreach (var item in _stepItems)
            {
                item.StatusMark = "☐";
                item.ArrowText = "  ";
                item.TimerText = item.PlannedDuration.ToString();
                item.KosaCounterText = _kosaEnabled ? "0/3" : "0/1";
            }

            UpdateStepCounter();
        }

        private void ResetStepTimers()
        {
            foreach (var item in _stepItems)
            {
                item.TimerText = item.PlannedDuration.ToString();
                item.ArrowText = "  ";
                item.KosaCounterText = _kosaEnabled ? "0/3" : "0/1";
            }
            UpdateStepCounter();
        }

        private void UpdateStepCounter()
        {
            if (_selectedTargetPressure == null || _stepItems.Count == 0)
            {
                StepCounterText.Text = "Шаг: 0/0";
                return;
            }

            int currentStep = 0;
            for (int i = 0; i < _stepItems.Count; i++)
            {
                if (_stepItems[i].ArrowText == "←")
                {
                    currentStep = i + 1;
                    break;
                }
            }

            if (currentStep == 0)
            {
                int completed = _stepItems.Count(x => x.StatusMark == "☑");
                StepCounterText.Text = $"Шаг: {completed}/{_stepItems.Count}";
            }
            else
            {
                StepCounterText.Text = $"Шаг: {currentStep}/{_stepItems.Count}";
            }
        }

        private void SavePathBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_uiLoaded) return;
            SaveSettings();
        }

        // ═══════════════════════════════════════════════════════════════════
        // ДИАЛОГ ВЫБОРА РЕЖИМА ЗАПУСКА (порт broadcast-сервера)
        // ═══════════════════════════════════════════════════════════════════

        private void AskForStartupMode()
        {
            var dlg = new Window
            {
                Title = "Режим запуска",
                Width = 420,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0))
            };

            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var title = new System.Windows.Controls.TextBlock
            {
                Text = "В каком режиме запустить программу?",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            System.Windows.Controls.Grid.SetRow(title, 0);
            grid.Children.Add(title);

            var info = new System.Windows.Controls.TextBlock
            {
                Text = "Рабочая — порт 9990\n" +
                       "Тестовая — порт 9999 (для параллельного запуска)",
                FontSize = 12,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15)
            };
            System.Windows.Controls.Grid.SetRow(info, 1);
            grid.Children.Add(info);

            var buttonsPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var workBtn = new System.Windows.Controls.Button
            {
                Content = "Рабочая (9990)",
                Width = 140,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x50, 0x90, 0xD0)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };

            var testBtn = new System.Windows.Controls.Button
            {
                Content = "Тестовая (9999)",
                Width = 140,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(0xB0, 0xA0, 0x50)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };

            bool isWork = true;

            workBtn.Click += (s, e) => { isWork = true; dlg.Close(); };
            testBtn.Click += (s, e) => { isWork = false; dlg.Close(); };

            buttonsPanel.Children.Add(workBtn);
            buttonsPanel.Children.Add(testBtn);
            System.Windows.Controls.Grid.SetRow(buttonsPanel, 2);
            grid.Children.Add(buttonsPanel);

            dlg.Content = grid;
            dlg.ShowDialog();

            BroadcastPort = isWork ? 9990 : 9999;
        }

        // ═══════════════════════════════════════════════════════════════════
        // BROADCAST-СЕРВЕР
        // ═══════════════════════════════════════════════════════════════════

        private void StartBroadcastServer()
        {
            try
            {
                _broadcastServer = new TcpListener(IPAddress.Loopback, BroadcastPort);
                _broadcastServer.Start();

                _ = Task.Run(async () =>
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        try
                        {
                            var client = await _broadcastServer.AcceptTcpClientAsync();
                            _ = Task.Run(() => HandleClientAsync(client));
                        }
                        catch { break; }
                    }
                });

                _clientsUiTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _clientsUiTimer.Tick += (s, e) => RefreshClientsUi();
                _clientsUiTimer.Start();

                string mode = BroadcastPort == 9990 ? "рабочая" : "тестовая";
                SetStatus($"Broadcast-сервер ({mode}) запущен на 127.0.0.1:{BroadcastPort}", false);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                string which = BroadcastPort == 9990 ? "рабочая" : "тестовая";
                MessageBox.Show(
                    $"Порт {BroadcastPort} уже занят.\n\n" +
                    $"Похоже, {which} версия уже запущена (или не закрыт старый экземпляр).\n" +
                    $"Закройте другую копию или выберите другой режим при следующем запуске.",
                    "Порт занят", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось запустить broadcast-сервер: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private void RefreshClientsUi()
        {
            List<ConnectedClient> snapshot;
            List<ConnectedClient> toRemove = new();

            lock (_clientsLock)
            {
                var now = DateTime.Now;
                foreach (var c in _connectedClients)
                {
                    if (!c.IsOffline &&
                        (now - c.LastActivityAt).TotalSeconds >= ClientDeadThresholdSeconds)
                    {
                        c.SetStatusOffline();
                    }

                    if (c.DisconnectedAt.HasValue &&
                        (now - c.DisconnectedAt.Value).TotalSeconds >= OfflineRetentionSeconds)
                    {
                        toRemove.Add(c);
                    }
                }
                foreach (var c in toRemove) _connectedClients.Remove(c);
                snapshot = _connectedClients.ToList();
            }

            ClientsList.ItemsSource = null;
            ClientsList.ItemsSource = snapshot;
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            NetworkStream? stream = null;
            string sid = "(неизвестен)";
            ConnectedClient? connectedClient = null;

            try
            {
                stream = client.GetStream();

                var sb = new StringBuilder();
                byte[] one = new byte[1];
                while (true)
                {
                    int n = await stream.ReadAsync(one, 0, 1);
                    if (n == 0) return;
                    if (one[0] == 10) break;
                    sb.Append((char)one[0]);
                }

                string hello = sb.ToString().Trim();
                if (hello.StartsWith("HELLO "))
                    sid = hello.Substring(6).Trim();

                connectedClient = new ConnectedClient
                {
                    Sid = sid,
                    TcpClient = client,
                    IsSelected = true,
                    ConnectedAt = DateTime.Now,
                    LastActivityAt = DateTime.Now
                };
                connectedClient.SetStatusReady();

                lock (_clientsLock)
                {
                    var existing = _connectedClients.FirstOrDefault(c => c.Sid == sid);
                    if (existing != null)
                    {
                        existing.TcpClient = client;
                        existing.ConnectedAt = DateTime.Now;
                        existing.LastActivityAt = DateTime.Now;
                        existing.SetStatusAlive();
                        connectedClient = existing;
                    }
                    else
                    {
                        _connectedClients.Add(connectedClient);
                    }
                }

                if (Dispatcher.CheckAccess()) RefreshClientsUi();
                else Dispatcher.Invoke(() => RefreshClientsUi());

                var buffer = new StringBuilder();
                byte[] buf = new byte[512];

                while (!_cts.IsCancellationRequested)
                {
                    int n;
                    try
                    {
                        n = await stream.ReadAsync(buf, 0, buf.Length, _cts.Token);
                    }
                    catch { break; }

                    if (n == 0) break;

                    string chunk = Encoding.UTF8.GetString(buf, 0, n);
                    buffer.Append(chunk);

                    while (true)
                    {
                        string full = buffer.ToString();
                        int idx = full.IndexOf('\n');
                        if (idx < 0) break;

                        string line = full.Substring(0, idx).Trim();
                        buffer.Remove(0, idx + 1);

                        if (string.IsNullOrEmpty(line)) continue;
                        ProcessClientMessage(connectedClient, line);
                    }
                }
            }
            catch { }
            finally
            {
                try { stream?.Close(); } catch { }
                try { client.Close(); } catch { }

                if (connectedClient != null)
                {
                    bool sameClient;
                    lock (_clientsLock)
                        sameClient = connectedClient.TcpClient == client;

                    if (sameClient) connectedClient.SetStatusOffline();

                    if (Dispatcher.CheckAccess()) RefreshClientsUi();
                    else Dispatcher.Invoke(() => RefreshClientsUi());
                }
            }
        }

        private void ProcessClientMessage(ConnectedClient client, string line)
        {
            client.LastActivityAt = DateTime.Now;

            if (line.StartsWith("HELLO ")) client.SetStatusAlive();
            else if (line.StartsWith("STATUS_BUSY")) client.SetStatusWaiting();
            else if (line.StartsWith("DONE_OK"))
            {
                client.LastDoneOk = true; client.LastDoneFail = false;
                client.SetStatusDoneSuccess();
            }
            else if (line.StartsWith("DONE_FAIL"))
            {
                client.LastDoneOk = false; client.LastDoneFail = true;
                client.SetStatusDoneFail();
            }
            else if (line.StartsWith("SAVED_OK"))
            {
                lock (_saveLock) { _saveResponses.Add(client.Sid + ":OK"); }
                client.SetStatusSaved();
            }
            else if (line.StartsWith("SAVED_FAIL"))
            {
                lock (_saveLock) { _saveResponses.Add(client.Sid + ":FAIL"); }
                client.SetStatusSaveFailed();
            }
            else if (line.StartsWith("CLEARED_OK"))
            {
                lock (_clearLock) { _clearResponses.Add(client.Sid + ":OK"); }
                client.SetStatusCleared();
            }
            else if (line.StartsWith("CLEARED_FAIL"))
            {
                lock (_clearLock) { _clearResponses.Add(client.Sid + ":FAIL"); }
                client.SetStatusClearFailed();
            }
        }

        // ─────────────────────────── УДАЛЕНИЕ КЛИЕНТОВ ───────────────────────────

        private void RemoveOffline_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning) return;

            lock (_clientsLock)
            {
                var toRemove = _connectedClients.Where(c => c.IsOffline).ToList();
                foreach (var c in toRemove)
                {
                    try { c.TcpClient.Close(); } catch { }
                    _connectedClients.Remove(c);
                }
            }
            RefreshClientsUi();
        }

        private void DisconnectClient_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi &&
                mi.Tag is ConnectedClient client)
            {
                lock (_clientsLock)
                {
                    try { client.TcpClient.Close(); } catch { }
                    _connectedClients.Remove(client);
                }
                RefreshClientsUi();
            }
        }

        // ─────────────────────────── ОЖИДАНИЕ DONE ───────────────────────────

        private async Task<bool> TakePointAndWaitAllAsync()
        {
            List<ConnectedClient> selected;
            lock (_clientsLock)
            {
                selected = _connectedClients
                    .Where(c => c.IsSelected && !c.IsOffline).ToList();
            }

            if (selected.Count == 0)
            {
                SetStatus("Нет отмеченных клиентов");
                return false;
            }

            foreach (var c in selected)
            {
                c.LastDoneOk = false; c.LastDoneFail = false;
                c.SetStatusWaiting();
            }

            byte[] message = Encoding.ASCII.GetBytes("TAKE_POINT\n");

            var sendTasks = selected.Select(async c =>
            {
                try
                {
                    var stream = c.TcpClient.GetStream();
                    await stream.WriteAsync(message, 0, message.Length);
                    await stream.FlushAsync();
                }
                catch { }
            }).ToList();

            await Task.WhenAll(sendTasks);

            var waitTasks = selected.Select(c => WaitForClientDoneAsync(c)).ToList();
            bool[] results = await Task.WhenAll(waitTasks);

            for (int i = 0; i < selected.Count; i++)
            {
                if (!results[i])
                {
                    SetStatus($"Клиент {selected[i].Sid} не завершил съём");
                    return false;
                }
            }

            SetStatus($"Все клиенты ({selected.Count}) ответили DONE_OK. Пауза 3 сек...");
            await SafeDelayAsync(3000);

            foreach (var c in selected)
            {
                if (!c.IsOffline)
                    c.SetStatusReady();
            }

            return true;
        }

        private async Task<bool> WaitForClientDoneAsync(ConnectedClient client)
        {
            var start = DateTime.Now;
            var timeout = TimeSpan.FromMinutes(15);
            var pausedTotal = TimeSpan.Zero;
            var pauseStartedAt = DateTime.MinValue;

            while (true)
            {
                if (_stopRequested) return false;

                if (_pauseRequested)
                {
                    if (pauseStartedAt == DateTime.MinValue) pauseStartedAt = DateTime.Now;
                    await Task.Delay(100);
                    continue;
                }
                else if (pauseStartedAt != DateTime.MinValue)
                {
                    pausedTotal += DateTime.Now - pauseStartedAt;
                    pauseStartedAt = DateTime.MinValue;
                }

                if (DateTime.Now - start - pausedTotal >= timeout) return false;
                if (client.LastDoneOk) return true;
                if (client.LastDoneFail) return false;

                if (client.IsWaiting)
                {
                    var elapsed = (int)(DateTime.Now - start - pausedTotal).TotalSeconds;
                    client.SetStatusWaitingWithTime(elapsed);
                }

                await Task.Delay(1000);
            }
        }

        private async void TakePointBroadcast_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning)
            {
                MessageBox.Show("Идёт градуировка/жамкать. Дождитесь завершения.");
                return;
            }

            try
            {
                TakePointBroadcastButton.IsEnabled = false;
                await TakePointAndWaitAllAsync();
            }
            finally { TakePointBroadcastButton.IsEnabled = true; }
        }

        // ═══════════════════════════════════════════════════════════════════
        // KOSA ЛОГИКА
        // ═══════════════════════════════════════════════════════════════════

        private async Task<bool> TakePointForStepAsync(int stepIndex, bool waitForState)
        {
            if (!_kosaEnabled)
            {
                if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                    _stepItems[stepIndex].KosaCounterText = "1/1";

                return await TakePointAndWaitAllAsync();
            }

            SetStatus("КОСА: съём 1/3");
            if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                _stepItems[stepIndex].KosaCounterText = "1/3";
            if (!await TakePointKosaAllAsync()) return false;

            for (int i = 0; i < 2; i++)
            {
                if (_stopRequested) return false;

                SetStatus($"КОСА: пауза перед съёмом {i + 2}/3 ({_kosaTimerSeconds} сек)");
                await KosaWaitAsync(_kosaTimerSeconds, waitForState, stepIndex);
                if (_stopRequested) return false;

                SetStatus($"КОСА: съём {i + 2}/3");
                if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                    _stepItems[stepIndex].KosaCounterText = $"{i + 2}/3";
                if (!await TakePointKosaAllAsync()) return false;
            }

            return true;
        }

        private async Task<bool> TakePointKosaAllAsync()
        {
            List<ConnectedClient> selected;
            lock (_clientsLock)
            {
                selected = _connectedClients
                    .Where(c => c.IsSelected && !c.IsOffline).ToList();
            }

            if (selected.Count == 0)
            {
                SetStatus("Нет отмеченных клиентов");
                return false;
            }

            var tasks = selected.Select(c => TakePointUntilOkAsync(c)).ToList();
            bool[] results = await Task.WhenAll(tasks);

            for (int i = 0; i < selected.Count; i++)
            {
                if (!results[i]) return false;
            }

            SetStatus($"КОСА: все клиенты ({selected.Count}) ответили DONE_OK. Пауза 3 сек...");
            await SafeDelayAsync(3000);

            foreach (var c in selected)
            {
                if (!c.IsOffline)
                    c.SetStatusReady();
            }

            return true;
        }

        private async Task<bool> TakePointUntilOkAsync(ConnectedClient client)
        {
            while (!_stopRequested)
            {
                if (!await WaitIfPausedAsync()) return false;

                client.LastDoneOk = false;
                client.LastDoneFail = false;
                client.SetStatusWaiting();

                byte[] msg = Encoding.ASCII.GetBytes("TAKE_POINT\n");
                try
                {
                    var s = client.TcpClient.GetStream();
                    await s.WriteAsync(msg, 0, msg.Length);
                    await s.FlushAsync();
                }
                catch
                {
                    await Task.Delay(1000);
                    continue;
                }

                bool done = await WaitForClientDoneAsync(client);
                if (done && client.LastDoneOk) return true;

                if (_stopRequested) return false;

                await Task.Delay(500);
            }
            return false;
        }

        private async Task KosaWaitAsync(int seconds, bool waitForState, int stepIndex)
        {
            int remaining = seconds;
            _timerActive = true;

            if (!waitForState)
            {
                while (remaining > 0)
                {
                    if (_stopRequested) { _timerActive = false; return; }
                    if (!await WaitIfPausedAsync()) { _timerActive = false; return; }
                    SetStatus($"КОСА: пауза {remaining} сек");
                    if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                        _stepItems[stepIndex].TimerText = remaining.ToString();
                    await SafeDelayAsync(1000);
                    remaining--;
                }
                if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                    _stepItems[stepIndex].TimerText = "0";
                _timerActive = false;
                return;
            }

            bool firstOkReceived = false;

            while (!firstOkReceived)
            {
                if (_stopRequested) { _timerActive = false; return; }
                if (!await WaitIfPausedAsync()) { _timerActive = false; return; }

                await RefreshPressureAndSetPointAsync();
                string? s = await SendCommandAsync("get_state_in_point;");

                if (s != null && s.Contains("ok;", StringComparison.Ordinal))
                {
                    firstOkReceived = true;
                }
                else
                {
                    SetStatus("КОСА: ожидание стабилизации давления...");
                    await SafeDelayAsync(1000);
                }
            }

            while (remaining > 0)
            {
                if (_stopRequested) { _timerActive = false; return; }
                if (!await WaitIfPausedAsync()) { _timerActive = false; return; }

                await RefreshPressureAndSetPointAsync();
                string? s = await SendCommandAsync("get_state_in_point;");

                if (s != null && s.Contains("ok;", StringComparison.Ordinal))
                {
                    remaining--;
                    SetStatus($"КОСА: пауза {remaining} сек");
                    if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                        _stepItems[stepIndex].TimerText = remaining.ToString();
                }
                else if (s != null && s.Contains("wait;", StringComparison.Ordinal))
                {
                    remaining += 30;
                    SetStatus($"КОСА: срыв! Таймер +30 сек. Осталось: {remaining}");
                    if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                        _stepItems[stepIndex].TimerText = remaining.ToString();

                    while (true)
                    {
                        if (_stopRequested) { _timerActive = false; return; }
                        if (!await WaitIfPausedAsync()) { _timerActive = false; return; }
                        await SafeDelayAsync(1000);

                        string? s2 = await SendCommandAsync("get_state_in_point;");
                        if (s2 != null && s2.Contains("ok;", StringComparison.Ordinal))
                            break;
                    }
                }

                await SafeDelayAsync(1000);
            }

            if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                _stepItems[stepIndex].TimerText = "0";

            _timerActive = false;
        }

        // ═══════════════════════════════════════════════════════════════════
        // КНОПКИ СТАРТ / ПАУЗА
        // ═══════════════════════════════════════════════════════════════════

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) { MessageBox.Show("Программа уже запущена."); return; }
            if (_batchRunning) { MessageBox.Show("Пакетный режим уже идёт."); return; }
            if (_selectedTargetPressure == null) { MessageBox.Show("Выберите диапазон давлений."); return; }

            _autoStartTriggered = false;

            int activeJobs = _batchJobs.Count(j => j.IsEnabled);

            // Пакетный режим — только для «Градуировка» и только если есть активные задания
            if (_isCalibrationMode && activeJobs > 0)
            {
                await RunBatchAsync();
                return;
            }

            // Обычная градуировка / жамка
            if (_autoStartEnabled && IsStabilizationThresholdReached())
            {
                _autoStartTriggered = true;
                SetStatusWarning(
                    "автоСтарт: ручной пуск — автозапуск отменён для текущего цикла стабилизации");
                UpdateAutoStartInfo();
            }

            if (_isCalibrationMode && _kosaEnabled)
            {
                if (!int.TryParse(KosaTimerBox.Text?.Trim(), out int kt) || kt <= 0)
                {
                    MessageBox.Show("Введите положительное число секунд для КОСА.");
                    return;
                }
                _kosaTimerSeconds = kt;
            }

            if (_isCalibrationMode)
                await RunCalibrationModeAsync();
            else
                await RunPumpModeAsync();
        }

        private bool IsStabilizationThresholdReached()
        {
            if (!_stabilizationTimerActive) return false;

            string spTxt = FurnaceSetPointBox?.Text?.Replace(',', '.') ?? "";
            if (!double.TryParse(spTxt, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double sp))
                return false;

            string tTxt = FurnaceCurrentTempText?.Text ?? "";
            int idx = tTxt.IndexOf(" °C", StringComparison.Ordinal);
            if (idx >= 0) tTxt = tTxt.Substring(0, idx);
            tTxt = tTxt.Trim().TrimStart('+').Replace(',', '.');

            if (!double.TryParse(tTxt, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double ct))
                return false;

            if (Math.Abs(ct - sp) > FurnaceStabilizationTolerance) return false;

            var elapsed = DateTime.Now - _stabilizationTimerStart;

            int requiredMinutes = _isCalibrationMode
                ? AutoStartCalibrationMinutes
                : AutoStartPumpMinutes;

            return elapsed.TotalMinutes >= requiredMinutes;
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRunning) return;

            if (_pauseRequested)
            {
                _pauseRequested = false;
                PauseButton.Content = "ПАУЗА";
                PauseButton.Background = new SolidColorBrush(Color.FromRgb(0xB0, 0xA0, 0x50));
                SetStatus("Продолжение работы...");
            }
            else
            {
                _pauseRequested = true;
                PauseButton.Content = "ПРОДОЛЖИТЬ";
                PauseButton.Background = new SolidColorBrush(Color.FromRgb(0x50, 0x90, 0xD0));
                SetStatusWarning("ПАУЗА");
            }
        }

        private async Task<bool> WaitIfPausedAsync()
        {
            while (_pauseRequested && !_stopRequested)
                await Task.Delay(100);
            return !_stopRequested;
        }

        // ─────────────────────────── ГРАДУИРОВКА ───────────────────────────

        private async Task RunCalibrationModeAsync()
        {
            if (!_stepsByTarget.TryGetValue(_selectedTargetPressure!, out var steps))
            { MessageBox.Show($"Для «{_selectedTargetPressure}» не заданы шаги."); return; }

            bool noConnection;
            lock (_streamLock) { noConnection = (_stream == null); }
            if (noConnection) { MessageBox.Show("Нет соединения с прибором."); return; }

            if (!int.TryParse(ParamBox.Text?.Trim(), out int startStep) ||
                startStep < 1 || startStep > steps.Length)
            {
                MessageBox.Show($"Некорректный номер шага: {ParamBox.Text}\n" +
                    $"Допустимо от 1 до {steps.Length}.");
                return;
            }

            bool longTimers = TimerSetCombo.SelectedIndex == 1;

            _isRunning = true;
            _stopRequested = false;
            _pauseRequested = false;
            _completedSteps = startStep - 1;
            _currentStepIndex = startStep - 1;

            TargetPressureCombo.IsEnabled = false;
            TimerSetCombo.IsEnabled = false;
            ModeCombo.IsEnabled = false;
            PauseButton.IsEnabled = true;
            PauseButton.Content = "ПАУЗА";
            PauseButton.Background = new SolidColorBrush(Color.FromRgb(0xB0, 0xA0, 0x50));

            StartButton.IsEnabled = false;

            SetExternalButtonsEnabled(false);

            for (int i = 0; i < _stepItems.Count; i++)
            {
                if (i < startStep - 1)
                {
                    _stepItems[i].StatusMark = "☑";
                    _stepItems[i].TimerText = "0";
                    _stepItems[i].ArrowText = "  ";
                    _stepItems[i].KosaCounterText = _kosaEnabled ? "3/3" : "1/1";
                }
                else
                {
                    _stepItems[i].StatusMark = "☐";
                    _stepItems[i].TimerText = _stepItems[i].PlannedDuration.ToString();
                    _stepItems[i].ArrowText = "  ";
                    _stepItems[i].KosaCounterText = _kosaEnabled ? "0/3" : "0/1";
                }
            }
            UpdateStepCounter();

            _totalTimerStart = DateTime.Now;
            _totalTimerActive = true;

            bool programCompleted = false;

            try
            {
                await RunSequenceAsync(steps, longTimers, startStep - 1);
                programCompleted = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
                StatusText.Foreground = Brushes.Red;
            }
            finally
            {
                _isRunning = false;
                _totalTimerActive = false;
                _pauseRequested = false;

                StartButton.IsEnabled = true;
                PauseButton.IsEnabled = false;
                PauseButton.Content = "ПАУЗА";
                PauseButton.Background = new SolidColorBrush(Color.FromRgb(0xB0, 0xA0, 0x50));
                TargetPressureCombo.IsEnabled = true;
                TimerSetCombo.IsEnabled = true;
                ModeCombo.IsEnabled = true;

                var elapsed = DateTime.Now - _totalTimerStart;
                await ShowCompletionMessageAsync(elapsed);

                if (programCompleted && !_stopRequested)
                {
                    bool saved = await SaveDataCoreAsync(silent: true);

                    if (saved)
                    {
                        bool fileExists = await WaitForSavedFileAsync(_lastSavedFilePath, 15000);

                        if (fileExists)
                        {
                            SetStatusSuccess(
                                $"Файл подтверждён на диске: {Path.GetFileName(_lastSavedFilePath)}");

                            await ClearDataCoreAsync(silent: true);
                        }
                        else
                        {
                            SetStatusWarning(
                                $"Файл не найден на диске: {_lastSavedFilePath} — очистка данных отменена");
                        }
                    }
                }

                ResetStepsToInitial();

                SetExternalButtonsEnabled(true);

                List<ConnectedClient> clientsSnapshot;
                lock (_clientsLock)
                    clientsSnapshot = _connectedClients.ToList();
                foreach (var c in clientsSnapshot)
                {
                    if (!c.IsOffline) c.SetStatusReady();
                }

                UpdateAutoStartInfo();

                _batchRunDoneSignal?.TrySetResult(true);
            }
        }

        private async Task RunSequenceAsync(double[] steps, bool longTimers, int startIndex)
        {
            string unitsCode = GetUnitsCode(_selectedTargetPressure!);
            double atmoThreshold = GetAtmosphereThreshold(unitsCode);

            SetStatus($"Установка единиц: {unitsCode}");
            bool unitsOk = await SendWithRetryAsync($"set_units={unitsCode};", "ok;");
            if (!unitsOk) { SetStatus("Не удалось установить единицы"); return; }

            string unitsLabel = GetUnitsLabel(_selectedTargetPressure!);
            _currentPressureUnitsLabel = unitsLabel;

            for (int i = startIndex; i < steps.Length; i++)
            {
                if (_stopRequested) { SetStatus("Остановлено оператором"); return; }
                if (!await WaitIfPausedAsync()) { SetStatus("Остановлено оператором"); return; }

                _currentStepIndex = i;

                for (int k = 0; k < _stepItems.Count; k++)
                    _stepItems[k].ArrowText = "  ";
                if (i < _stepItems.Count)
                    _stepItems[i].ArrowText = "←";

                UpdateStepCounter();

                int human = i + 1;
                double v = steps[i];
                int duration = GetStepDuration(steps, i, longTimers);

                if (i == 0)
                {
                    SetStatus("Шаг 1: сброс до атмосферы (vent)");
                    bool ventOk = await SendWithRetryAsync("set_mode=vent;", "ok;");
                    if (!ventOk) return;

                    SetStatus($"Шаг 1: измерение {duration} сек");
                    await RunTimerWithStepAsync(duration, i, waitForState: false);

                    SetStatus("Шаг 1: команда съёма");
                    bool q1 = await TakePointForStepAsync(i, waitForState: false);
                    if (!q1) return;

                    if (i < _stepItems.Count)
                    {
                        _stepItems[i].StatusMark = "☑";
                        _stepItems[i].ArrowText = "  ";
                    }
                    _completedSteps = 1;
                    UpdateStepCounter();
                    continue;
                }

                if (i == steps.Length - 1)
                {
                    SetStatus($"Шаг {human}: сброс до атмосферы (vent)");
                    bool ventOk = await SendWithRetryAsync("set_mode=vent;", "ok;");
                    if (!ventOk) return;

                    SetStatus($"Шаг {human}: ожидание снижения давления");
                    bool lowOk = await WaitUntilPressureBelowAsync(atmoThreshold, 300_000);
                    if (!lowOk) return;

                    SetStatus($"Шаг {human}: измерение {duration} сек");
                    await RunTimerWithStepAsync(duration, i, waitForState: v != 0);

                    SetStatus($"Шаг {human}: команда съёма");
                    bool qLast = await TakePointForStepAsync(i, waitForState: v != 0);
                    if (!qLast) return;

                    if (i < _stepItems.Count)
                    {
                        _stepItems[i].StatusMark = "☑";
                        _stepItems[i].ArrowText = "  ";
                    }
                    _completedSteps = i + 1;
                    UpdateStepCounter();
                    continue;
                }

                SetStatus($"Шаг {human}: set_mode=control (1)");
                bool m1 = await SendWithRetryAsync("set_mode=control;", "ok;");
                if (!m1) return;

                await SafeDelayAsync(1000);

                SetStatus($"Шаг {human}: set_mode=control (2)");
                bool m2 = await SendWithRetryAsync("set_mode=control;", "ok;");
                if (!m2) return;

                SetStatus($"Шаг {human}/{steps.Length}: установка {v}");
                bool setOk = await SendWithRetryAsync($"set_point={v};", "ok;");
                if (!setOk) return;

                SetStatus($"Шаг {human}: ожидание стабилизации");
                bool stable = await WaitForStateInPointAsync();
                if (!stable) return;

                SetStatus($"Шаг {human}: измерение {duration} сек");
                await RunTimerWithStepAsync(duration, i, waitForState: v != 0);

                SetStatus($"Шаг {human}: команда съёма");
                bool q = await TakePointForStepAsync(i, waitForState: v != 0);
                if (!q) return;

                if (i < _stepItems.Count)
                {
                    _stepItems[i].StatusMark = "☑";
                    _stepItems[i].ArrowText = "  ";
                }
                _completedSteps = i + 1;
                UpdateStepCounter();
            }

            SetStatus("Программа завершена");
        }

        // ─────────────────────────── ЖАМКАТЬ ───────────────────────────

        private async Task RunPumpModeAsync()
        {
            if (!_stepsByTarget.TryGetValue(_selectedTargetPressure!, out var steps))
            { MessageBox.Show($"Шаги не заданы."); return; }

            bool noConnection;
            lock (_streamLock) { noConnection = (_stream == null); }
            if (noConnection) { MessageBox.Show("Нет соединения."); return; }

            if (!int.TryParse(ParamBox.Text?.Trim(), out int cycles) || cycles <= 0)
            { MessageBox.Show("Введите корректное количество циклов."); return; }

            double maxPressure = steps.Max();
            string unitsCode = GetUnitsCode(_selectedTargetPressure!);
            string unitsLabel = GetUnitsLabel(_selectedTargetPressure);
            double atmoThreshold = GetAtmosphereThreshold(unitsCode);

            _isRunning = true;
            _stopRequested = false;
            _pauseRequested = false;

            TargetPressureCombo.IsEnabled = false;
            TimerSetCombo.IsEnabled = false;
            ModeCombo.IsEnabled = false;
            PauseButton.IsEnabled = true;
            PauseButton.Content = "ПАУЗА";
            PauseButton.Background = new SolidColorBrush(Color.FromRgb(0xB0, 0xA0, 0x50));

            StartButton.IsEnabled = false;

            SetExternalButtonsEnabled(false);

            _totalTimerStart = DateTime.Now;
            _totalTimerActive = true;

            try { await RunPumpSequenceAsync(maxPressure, cycles, unitsCode, unitsLabel, atmoThreshold); }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
                StatusText.Foreground = Brushes.Red;
            }
            finally
            {
                _isRunning = false;
                _totalTimerActive = false;
                _pauseRequested = false;

                StartButton.IsEnabled = true;
                PauseButton.IsEnabled = false;
                PauseButton.Content = "ПАУЗА";
                PauseButton.Background = new SolidColorBrush(Color.FromRgb(0xB0, 0xA0, 0x50));
                TargetPressureCombo.IsEnabled = true;
                TimerSetCombo.IsEnabled = true;
                ModeCombo.IsEnabled = true;

                var elapsed = DateTime.Now - _totalTimerStart;
                await ShowCompletionMessageAsync(elapsed);

                MaxArrow.Text = "  ";
                MinArrow.Text = "  ";
                MaxTimerText.Text = "";
                MinTimerText.Text = "";

                SetExternalButtonsEnabled(true);

                List<ConnectedClient> clientsSnapshot;
                lock (_clientsLock)
                    clientsSnapshot = _connectedClients.ToList();
                foreach (var c in clientsSnapshot)
                {
                    if (!c.IsOffline) c.SetStatusReady();
                }

                UpdateAutoStartInfo();

                _batchRunDoneSignal?.TrySetResult(true);
            }
        }

        private async Task RunPumpSequenceAsync(double maxPressure, int totalCycles,
            string unitsCode, string unitsLabel, double atmoThreshold)
        {
            MaxArrow.Text = "  ";
            MinArrow.Text = "  ";

            SetStatus($"Установка единиц: {unitsCode}");
            bool unitsOk = await SendWithRetryAsync($"set_units={unitsCode};", "ok;");
            if (!unitsOk) return;

            _currentPressureUnitsLabel = unitsLabel;

            for (int cycle = 1; cycle <= totalCycles; cycle++)
            {
                if (_stopRequested) return;
                if (!await WaitIfPausedAsync()) return;

                MaxArrow.Text = "  ";
                MinArrow.Text = "  ";

                MaxTimerText.Text = "300";
                MinTimerText.Text = "300";

                CyclesText.Text = $"Цикл: {cycle}/{totalCycles}";

                SetStatus($"[Цикл {cycle}] set_mode=control (1)");
                bool m1 = await SendWithRetryAsync("set_mode=control;", "ok;");
                if (!m1) return;

                await SafeDelayAsync(1000);

                SetStatus($"[Цикл {cycle}] set_mode=control (2)");
                bool m2 = await SendWithRetryAsync("set_mode=control;", "ok;");
                if (!m2) return;

                SetStatus($"[Цикл {cycle}] установка {maxPressure}");
                bool setOk = await SendWithRetryAsync(
                    $"set_point={maxPressure.ToString(CultureInfo.InvariantCulture)};", "ok;");
                if (!setOk) return;

                MaxArrow.Text = "→";

                SetStatus($"[Цикл {cycle}] ожидание стабилизации");
                bool stable = await WaitForStateInPointAsync();
                if (!stable) return;

                SetStatus($"[Цикл {cycle}] Выдерживаем максимальное давление");
                await RunTimerWithRightBlockAsync(300, true);

                SetStatus($"[Цикл {cycle}] сброс (vent)");
                bool ventOk = await SendWithRetryAsync("set_mode=vent;", "ok;");
                if (!ventOk) return;

                MaxArrow.Text = "  ";
                MinArrow.Text = "→";

                SetStatus($"[Цикл {cycle}] ожидание снижения");
                bool lowOk = await WaitUntilPressureBelowAsync(atmoThreshold, 300_000);
                if (!lowOk) return;

                SetStatus($"[Цикл {cycle}] Выдерживаем атмосферное давление");
                await RunTimerWithRightBlockAsync(300, false);
            }

            MaxArrow.Text = "  ";
            MinArrow.Text = "  ";
            MaxTimerText.Text = "";
            MinTimerText.Text = "";

            SetStatus($"«Жамкать» завершено ({totalCycles} циклов)");
        }

        private async Task RunTimerWithRightBlockAsync(int seconds, bool isMax)
        {
            int remaining = seconds;
            _timerActive = true;

            if (!isMax)
            {
                while (remaining > 0)
                {
                    if (_stopRequested) { _timerActive = false; return; }
                    if (!await WaitIfPausedAsync()) { _timerActive = false; return; }

                    remaining--;
                    MinTimerText.Text = remaining.ToString();

                    SetStatus($"Атмосфера: {remaining} сек");
                    await SafeDelayAsync(1000);
                }

                MinTimerText.Text = "0";
                _timerActive = false;
                return;
            }

            while (remaining > 0)
            {
                if (_stopRequested) { _timerActive = false; return; }
                if (!await WaitIfPausedAsync()) { _timerActive = false; return; }

                await RefreshPressureAndSetPointAsync();

                string? stateResponse = await SendCommandAsync("get_state_in_point;");

                if (stateResponse != null && stateResponse.Contains("ok;", StringComparison.Ordinal))
                {
                    remaining--;
                    MaxTimerText.Text = remaining.ToString();
                }

                SetStatus($"Максимум: {remaining} сек");
                await SafeDelayAsync(1000);
            }

            MaxTimerText.Text = "0";
            _timerActive = false;
        }

        private async Task RunTimerWithStepAsync(int seconds, int stepIndex, bool waitForState)
        {
            int remaining = seconds;
            _timerActive = true;

            if (!waitForState)
            {
                while (remaining > 0)
                {
                    if (_stopRequested) { _timerActive = false; return; }
                    if (!await WaitIfPausedAsync()) { _timerActive = false; return; }

                    remaining--;
                    if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                        _stepItems[stepIndex].TimerText = remaining.ToString();

                    SetStatus($"До начала съёма данных: {remaining} сек");
                    await SafeDelayAsync(1000);
                }

                if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                    _stepItems[stepIndex].TimerText = "0";

                _timerActive = false;
                return;
            }

            bool firstOkReceived = false;

            while (remaining > 0)
            {
                if (_stopRequested) { _timerActive = false; return; }
                if (!await WaitIfPausedAsync()) { _timerActive = false; return; }

                await RefreshPressureAndSetPointAsync();

                string? unitsResponse = await SendCommandAsync("get_units;");
                if (unitsResponse != null) UpdateUnits(unitsResponse);

                string? stateResponse = await SendCommandAsync("get_state_in_point;");

                if (stateResponse != null && stateResponse.Contains("ok;", StringComparison.Ordinal))
                {
                    firstOkReceived = true;
                    remaining--;
                    if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                        _stepItems[stepIndex].TimerText = remaining.ToString();
                }
                else if (stateResponse != null && stateResponse.Contains("wait;", StringComparison.Ordinal))
                {
                    if (firstOkReceived)
                    {
                        remaining += 30;
                        SetStatus($"Срыв! Таймер +30 сек. Осталось: {remaining}");
                        if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                            _stepItems[stepIndex].TimerText = remaining.ToString();
                    }
                    else
                    {
                        SetStatus("Ожидание стабилизации давления...");
                    }

                    while (true)
                    {
                        if (_stopRequested) { _timerActive = false; return; }
                        if (!await WaitIfPausedAsync()) { _timerActive = false; return; }
                        await SafeDelayAsync(1000);

                        string? s = await SendCommandAsync("get_state_in_point;");
                        if (s != null && s.Contains("ok;", StringComparison.Ordinal))
                        {
                            firstOkReceived = true;
                            break;
                        }
                    }
                    continue;
                }

                SetStatus($"До начала съёма данных: {remaining} сек");
                await SafeDelayAsync(1000);
            }

            if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                _stepItems[stepIndex].TimerText = "0";

            _timerActive = false;
        }

        // ─────────────────────────── ПОКАЗ СООБЩЕНИЯ ───────────────────────────

        private async Task ShowCompletionMessageAsync(TimeSpan elapsed)
        {
            string timeStr = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

            float? temp = await TryReadCurrentTemperatureAsync();

            string msg;
            if (temp.HasValue)
            {
                double rounded = Math.Round((double)temp.Value, 2);
                string sign = rounded >= 0 ? "+" : "";
                msg = $"Съём данных на температуре {sign}{rounded.ToString("0.##", CultureInfo.InvariantCulture)} °C длился {timeStr}";
            }
            else
            {
                msg = $"Съём данных: ошибка чтения температурной уставки из камеры, время работы {timeStr}";
            }

            SetStatusSuccess(msg);

            _statusResetTimer?.Stop();
            _statusResetTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _statusResetTimer.Tick += (s, e) =>
            {
                _statusResetTimer.Stop();
                SetStatusSuccess($"Подключено к {DeviceIp}:{DevicePort}");
            };
            _statusResetTimer.Start();

            await Task.CompletedTask;
        }

        // ─────────────────────────── ПЕЧЬ ST-351 ───────────────────────────

        private async Task<float?> TryReadCurrentTemperatureAsync()
        {
            return await Task.Run(() =>
            {
                System.Net.Sockets.Socket? sock = null;
                try
                {
                    sock = new System.Net.Sockets.Socket(
                        System.Net.Sockets.AddressFamily.InterNetwork,
                        System.Net.Sockets.SocketType.Stream,
                        System.Net.Sockets.ProtocolType.Tcp);

                    sock.ReceiveTimeout = 1000;
                    sock.SendTimeout = 1000;

                    var endpoint = new IPEndPoint(IPAddress.Parse(FurnaceIp), FurnacePort);

                    var ar = sock.BeginConnect(endpoint, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(3000))
                        return (float?)null;
                    sock.EndConnect(ar);

                    byte[] request = new byte[]
                    {
                        0x00, 0x01,
                        0x00, 0x00,
                        0x00, 0x06,
                        0x00,
                        0x04,
                        0x00, 0xCD,
                        0x00, 0x02
                    };

                    sock.Send(request);

                    byte[] buf = new byte[20];
                    int n = sock.Receive(buf);

                    if (n >= 13 && buf[7] == 0x04)
                    {
                        byte b1 = buf[9];
                        byte b2 = buf[10];
                        byte b3 = buf[11];
                        byte b4 = buf[12];

                        uint bits = (uint)((b3 << 24) | (b4 << 16) | (b1 << 8) | b2);

                        int sign = (int)((bits >> 31) & 1);
                        int exponent = (int)((bits >> 23) & 0xFF);
                        int mantissa = (int)(bits & 0x7FFFFF);

                        double temp;
                        if (exponent == 0)
                        {
                            temp = (mantissa == 0)
                                ? 0.0
                                : Math.Pow(2.0, -126) * (mantissa / Math.Pow(2.0, 23));
                        }
                        else if (exponent == 255)
                        {
                            temp = 0.0;
                        }
                        else
                        {
                            temp = Math.Pow(2.0, exponent - 127) *
                                   (1.0 + mantissa / Math.Pow(2.0, 23));
                        }

                        if (sign == 1) temp = -temp;

                        return (float?)temp;
                    }

                    return (float?)null;
                }
                catch
                {
                    return (float?)null;
                }
                finally
                {
                    try { sock?.Close(); } catch { }
                }
            });
        }

        private async Task RefreshFurnaceCurrentTempAsync()
        {
            float? temp = await TryReadCurrentTemperatureAsync();

            if (temp.HasValue && !float.IsNaN(temp.Value) && !float.IsInfinity(temp.Value))
            {
                _furnaceTempFailCount = 0;

                double rounded = Math.Round((double)temp.Value, 3);
                string sign = rounded >= 0 ? "+" : "";
                FurnaceCurrentTempText.Text =
                    $"{sign}{rounded.ToString("0.000", CultureInfo.InvariantCulture)} °C";
                FurnaceCurrentTempText.Foreground = Brushes.White;

                if (FurnaceStatusEllipse != null)
                    FurnaceStatusEllipse.Fill = Brushes.Green;
            }
            else
            {
                _furnaceTempFailCount++;

                if (_furnaceTempFailCount >= ConnectionLostThreshold)
                {
                    FurnaceCurrentTempText.Text = "— °C";
                    FurnaceCurrentTempText.Foreground =
                        new SolidColorBrush(Color.FromRgb(0xCC, 0xDD, 0xEE));

                    if (FurnaceStatusEllipse != null)
                        FurnaceStatusEllipse.Fill = Brushes.Red;
                }
            }
        }

        private async Task<bool> RefreshFurnaceSetPointAsync()
        {
            if (FurnaceSetPointBox.IsKeyboardFocused) return false;

            try
            {
                string result = await Task.Run(() =>
                {
                    System.Net.Sockets.Socket? sock = null;
                    try
                    {
                        sock = new System.Net.Sockets.Socket(
                            System.Net.Sockets.AddressFamily.InterNetwork,
                            System.Net.Sockets.SocketType.Stream,
                            System.Net.Sockets.ProtocolType.Tcp);

                        sock.ReceiveTimeout = 1000;
                        sock.SendTimeout = 1000;

                        var endpoint = new System.Net.IPEndPoint(
                            System.Net.IPAddress.Parse(FurnaceIp), FurnacePort);

                        var ar = sock.BeginConnect(endpoint, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(3000))
                            return "CONNECT_TIMEOUT";
                        sock.EndConnect(ar);

                        byte[] request = new byte[]
                        {
                            0x00, 0x01,
                            0x00, 0x00,
                            0x00, 0x06,
                            0x00,
                            0x03,
                            0x00, 0xCA,
                            0x00, 0x02
                        };

                        sock.Send(request);

                        byte[] buf = new byte[20];
                        int n = sock.Receive(buf);

                        if (n >= 13 && buf[7] == 0x03)
                        {
                            byte b1 = buf[9], b2 = buf[10], b3 = buf[11], b4 = buf[12];
                            float val = BitConverter.ToSingle(new byte[] { b4, b3, b2, b1 }, 0);
                            return "OK:" + val.ToString(CultureInfo.InvariantCulture);
                        }
                        return "FAIL";
                    }
                    catch (Exception ex)
                    {
                        return "EXC:" + ex.GetType().Name;
                    }
                    finally
                    {
                        try { sock?.Close(); } catch { }
                    }
                });

                if (result.StartsWith("OK:"))
                {
                    string valStr = result.Substring(3);
                    if (float.TryParse(valStr, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float v)
                        && !float.IsNaN(v) && !float.IsInfinity(v))
                    {
                        _furnaceSetPointFailCount = 0;

                        double rounded = Math.Round((double)v, 2);
                        FurnaceSetPointBox.Text =
                            rounded.ToString("0.##", CultureInfo.InvariantCulture);
                        _lastValidFurnaceSetPoint = v;
                        return true;
                    }
                }

                _furnaceSetPointFailCount++;
                if (_furnaceSetPointFailCount >= ConnectionLostThreshold)
                {
                    FurnaceSetPointBox.Text = "—";
                }
                return false;
            }
            catch
            {
                _furnaceSetPointFailCount++;
                if (_furnaceSetPointFailCount >= ConnectionLostThreshold)
                {
                    FurnaceSetPointBox.Text = "—";
                }
                return false;
            }
        }

        // ─────────────────────────── ТАЙМЕР СТАБИЛИЗАЦИИ ───────────────────────────

        private void FurnaceSetPointBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_uiLoaded) return;
            if (FurnaceSetPointBox == null) return;

            if (!FurnaceSetPointBox.IsKeyboardFocused) return;

            string txt = FurnaceSetPointBox.Text?.Replace(',', '.') ?? "";
            if (double.TryParse(txt, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double sp))
            {
                if (!double.IsNaN(_lastFurnaceSetPoint) &&
                    Math.Abs(sp - _lastFurnaceSetPoint) > 0.0001)
                {
                    ResetStabilizationTimer();
                    _autoStartTriggered = false;
                    UpdateAutoStartInfo();
                }
                _lastFurnaceSetPoint = sp;
            }
        }

        private void ResetStabilizationTimer()
        {
            _stabilizationTimerActive = false;
            _stabilizationTimerStart = DateTime.MinValue;
            _stabilizationOutOfRangeSince = DateTime.MinValue;
            if (StabilizationTimerText != null)
                StabilizationTimerText.Text = "00:00:00";
        }

        private void ResetStabilizationTimer_Click(object sender, RoutedEventArgs e)
        {
            ResetStabilizationTimer();
            _autoStartTriggered = false;
            UpdateAutoStartInfo();
            SetStatus("Таймер стабилизации сброшен, автоСтарт разрешён", false);
        }

        private void UpdateStabilizationTimer()
        {
            if (StabilizationTimerText == null) return;

            string spTxt = FurnaceSetPointBox?.Text?.Replace(',', '.') ?? "";
            if (!double.TryParse(spTxt, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double setPoint))
            {
                return;
            }

            string tTxt = FurnaceCurrentTempText?.Text ?? "";
            int idx = tTxt.IndexOf(" °C", StringComparison.Ordinal);
            if (idx >= 0) tTxt = tTxt.Substring(0, idx);
            tTxt = tTxt.Trim().TrimStart('+').Replace(',', '.');

            if (!double.TryParse(tTxt, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double currentTemp))
            {
                return;
            }

            bool inRange = Math.Abs(currentTemp - setPoint) <= FurnaceStabilizationTolerance;

            if (inRange)
            {
                _stabilizationOutOfRangeSince = DateTime.MinValue;

                if (!_stabilizationTimerActive)
                {
                    _stabilizationTimerStart = DateTime.Now;
                    _stabilizationTimerActive = true;
                }
                var elapsed = DateTime.Now - _stabilizationTimerStart;
                StabilizationTimerText.Text =
                    $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

                TryTriggerAutoStart(elapsed);
            }
            else
            {
                if (_stabilizationOutOfRangeSince == DateTime.MinValue)
                {
                    _stabilizationOutOfRangeSince = DateTime.Now;
                }

                var outOfRange = DateTime.Now - _stabilizationOutOfRangeSince;

                if (outOfRange.TotalSeconds >= StabilizationOutOfRangeGraceSeconds)
                {
                    ResetStabilizationTimer();
                    _stabilizationOutOfRangeSince = DateTime.MinValue;
                }
                else
                {
                    if (_stabilizationTimerActive)
                    {
                        var elapsed = DateTime.Now - _stabilizationTimerStart;
                        StabilizationTimerText.Text =
                            $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

                        TryTriggerAutoStart(elapsed);
                    }
                }
            }
        }

        private void TryTriggerAutoStart(TimeSpan elapsed)
        {
            if (!_autoStartEnabled) return;
            if (_autoStartTriggered) return;

            int activeJobs = _batchJobs.Count(j => j.IsEnabled);
            if (_isCalibrationMode && activeJobs > 0) return; // в пакетном режиме автоСтарт не работает

            int requiredMinutes = _isCalibrationMode
                ? AutoStartCalibrationMinutes
                : AutoStartPumpMinutes;

            if (elapsed.TotalMinutes < requiredMinutes) return;

            if (_isRunning)
            {
                _autoStartTriggered = true;
                UpdateAutoStartInfo();
                return;
            }

            _autoStartTriggered = true;
            UpdateAutoStartInfo();

            Dispatcher.InvokeAsync(async () =>
            {
                SetStatusSuccess(
                    $"автоСтарт: стабилизация {requiredMinutes} мин — " +
                    $"запуск «{(_isCalibrationMode ? "Градуировка" : "Жамкать")}»");

                try
                {
                    if (_isCalibrationMode)
                        await RunCalibrationModeAsync();
                    else
                        await RunPumpModeAsync();
                }
                catch (Exception ex)
                {
                    SetStatusError($"автоСтарт ошибка: {ex.Message}");
                }
            });
        }

        private async Task<bool> WaitForSavedFileAsync(string? path, int timeoutMs)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            var start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                if (File.Exists(path)) return true;
                await Task.Delay(250);
            }
            return File.Exists(path);
        }

        // ─────────────────────────── СКТБ ЭлПа (ПЛАТИНА) ───────────────────────────

        private async Task<double?> TryReadPlatinaAsync()
        {
            return await Task.Run(() =>
            {
                HidStream? stream = null;
                try
                {
                    var devices = DeviceList.Local.GetHidDevices(PlatinaVid, PlatinaPid);

                    int deviceCount = 0;
                    foreach (var d in devices) deviceCount++;

                    if (deviceCount == 0)
                        return (double?)null;

                    foreach (var device in devices)
                    {
                        try
                        {
                            stream = device.Open();
                            stream.ReadTimeout = 1000;
                            stream.WriteTimeout = 1000;

                            byte[] payload = Encoding.ASCII.GetBytes(PlatinaRequest);

                            for (int off = 0; off < payload.Length; off += 64)
                            {
                                byte[] report = new byte[65];
                                int chunk = Math.Min(64, payload.Length - off);
                                Array.Copy(payload, off, report, 1, chunk);
                                stream.Write(report, 0, report.Length);
                            }

                            var data = new List<byte>();
                            var start = DateTime.Now;

                            while ((DateTime.Now - start).TotalMilliseconds < 2000)
                            {
                                byte[] buf = new byte[65];
                                int n = stream.Read(buf, 0, buf.Length);
                                if (n <= 1) continue;

                                for (int i = 1; i < n; i++) data.Add(buf[i]);

                                for (int i = 0; i < data.Count - 1; i++)
                                {
                                    if (data[i] == 0x0A && data[i + 1] == 0x00)
                                    {
                                        string s = Encoding.ASCII.GetString(
                                            data.GetRange(0, i).ToArray());

                                        string first = s.Split(new[] { ' ', '\t' },
                                            StringSplitOptions.RemoveEmptyEntries)
                                            .FirstOrDefault() ?? "";
                                        first = first.Replace(',', '.');

                                        if (double.TryParse(first, NumberStyles.Float,
                                            CultureInfo.InvariantCulture, out double val))
                                        {
                                            return (double?)val;
                                        }
                                        return (double?)null;
                                    }
                                }
                            }

                            return (double?)null;
                        }
                        catch
                        {
                            // ignore, try next device
                        }
                        finally
                        {
                            try { stream?.Close(); } catch { }
                            stream = null;
                        }
                    }
                    return (double?)null;
                }
                catch
                {
                    return (double?)null;
                }
            });
        }

        private async Task RefreshPlatinaAsync()
        {
            double? val = await TryReadPlatinaAsync();

            if (val.HasValue && !double.IsNaN(val.Value) && !double.IsInfinity(val.Value))
            {
                _platinaFailCount = 0;
                _platinaLastValue = val.Value;
                _platinaLastSuccess = DateTime.Now;

                double rounded = Math.Round(val.Value, 3);
                string sign = rounded >= 0 ? "+" : "";
                PlatinaTempText.Text =
                    $"{sign}{rounded.ToString("0.000", CultureInfo.InvariantCulture)} °C";
                PlatinaTempText.Foreground = Brushes.White;

                if (PlatinaStatusEllipse != null)
                    PlatinaStatusEllipse.Fill = Brushes.Green;
            }
            else
            {
                _platinaFailCount++;

                if (_platinaFailCount >= ConnectionLostThreshold)
                {
                    PlatinaTempText.Text = "— °C";
                    PlatinaTempText.Foreground =
                        new SolidColorBrush(Color.FromRgb(0xCC, 0xDD, 0xEE));

                    if (PlatinaStatusEllipse != null)
                        PlatinaStatusEllipse.Fill = Brushes.Red;
                }
            }
        }

        // ─────────────────────────── СТАТУС АГК ───────────────────────────

        private async Task RefreshAgkStatusAsync()
        {
            if ((DateTime.Now - _lastAgkStatusRequest).TotalMilliseconds < AgkStatusRequestIntervalMs)
                return;
            _lastAgkStatusRequest = DateTime.Now;

            string? resp = await SendCommandAsync("get_status;");
            if (resp == null)
            {
                _agkStatusFailCount++;
                if (_agkStatusFailCount >= ConnectionLostThreshold)
                {
                    if (AgkStatusTextBox != null)
                    {
                        AgkStatusTextBox.Text = "(нет ответа)";
                        AgkStatusTextBox.Foreground = Brushes.OrangeRed;
                    }
                }
                return;
            }
            _agkStatusFailCount = 0;

            string display = resp.Trim();
            if (string.IsNullOrEmpty(display)) display = "(пусто)";

            if (AgkStatusTextBox != null)
            {
                AgkStatusTextBox.Text = display;
                AgkStatusTextBox.Foreground = (display == "0;" || display == "0")
                    ? new SolidColorBrush(Color.FromRgb(0x80, 0xFF, 0x80))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x80));
            }
        }

        // ─────────────────────────── ПРОЧЕЕ ───────────────────────────

        private async void ReadFurnaceSetPoint_Click(object sender, RoutedEventArgs e)
        {
            ReadFurnaceSetPointButton.IsEnabled = false;
            try
            {
                SetStatus("Чтение уставки из печи ST-351...", false);
                bool ok = await RefreshFurnaceSetPointAsync();
                if (ok)
                    SetStatusSuccess($"Уставка печи: {FurnaceSetPointBox.Text} °C");
                else
                    SetStatusError("Не удалось прочитать уставку из печи");
            }
            finally
            {
                ReadFurnaceSetPointButton.IsEnabled = true;
            }
        }

        private async void TestWriteFurnace_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning) { MessageBox.Show("Программа занята."); return; }

            string input = FurnaceSetPointBox.Text?.Trim() ?? "";
            if (!float.TryParse(input.Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out float temp))
            {
                MessageBox.Show("Некорректное значение.");
                return;
            }

            TestFurnaceButton.IsEnabled = false;
            try
            {
                SetStatus($"Запись уставки {temp} °C в печь...", false);
                bool ok = await WriteFurnaceSetPointAsync(
                    temp, FurnaceIp, FurnacePort, FurnaceSetPointRegister);

                if (ok)
                {
                    _lastValidFurnaceSetPoint = temp;
                    SetStatusSuccess($"Уставка печи изменена на {temp} °C");
                }
                else SetStatusError($"Ошибка записи уставки печи");
            }
            finally { TestFurnaceButton.IsEnabled = true; }
        }

        private async Task<bool> WriteFurnaceSetPointAsync(
            float value, string ip, int port, ushort startRegister)
        {
            try
            {
                byte[] floatBytes = BitConverter.GetBytes(value);
                if (BitConverter.IsLittleEndian) Array.Reverse(floatBytes);
                byte[] ordered = new[] { floatBytes[2], floatBytes[3], floatBytes[0], floatBytes[1] };

                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(3000);
                if (await Task.WhenAny(connectTask, timeoutTask) != connectTask) return false;
                await connectTask;

                using var stream = client.GetStream();
                stream.ReadTimeout = 2000; stream.WriteTimeout = 2000;

                var buffer = new List<byte>(17);
                buffer.Add(0x00); buffer.Add(0x01);
                buffer.Add(0x00); buffer.Add(0x00);
                buffer.Add(0x00); buffer.Add(0x0B);
                buffer.Add(0x00);
                buffer.Add(0x10);
                buffer.Add((byte)(startRegister >> 8));
                buffer.Add((byte)(startRegister & 0xFF));
                buffer.Add(0x00); buffer.Add(0x02);
                buffer.Add(0x04);
                buffer.Add(ordered[0]); buffer.Add(ordered[1]);
                buffer.Add(ordered[2]); buffer.Add(ordered[3]);

                byte[] reqBytes = buffer.ToArray();
                await stream.WriteAsync(reqBytes, 0, reqBytes.Length);
                await stream.FlushAsync();

                byte[] response = new byte[12];
                int totalRead = 0;
                var start = DateTime.Now;
                while (totalRead < response.Length &&
                       (DateTime.Now - start).TotalMilliseconds < 2000)
                {
                    if (stream.DataAvailable)
                    {
                        int n = await stream.ReadAsync(response, totalRead, response.Length - totalRead);
                        if (n == 0) break;
                        totalRead += n;
                    }
                    else await Task.Delay(20);
                }
                return totalRead >= 12 && response[7] == 0x10;
            }
            catch { return false; }
        }

        private async void FurnaceRun_Click(object sender, RoutedEventArgs e)
        {
            FurnaceRunButton.IsEnabled = false;
            try
            {
                bool ok = await WriteFurnaceRegisterAsync(
                    FurnaceModeManual, FurnaceIp, FurnacePort, FurnaceModeRegister);
                if (ok) SetStatusSuccess("Режим работы печи изменён на «ручной»");
                else SetStatusError("Ошибка записи режима «ручной»");
            }
            finally { FurnaceRunButton.IsEnabled = true; }
        }

        private async void FurnaceStop_Click(object sender, RoutedEventArgs e)
        {
            FurnaceStopButton.IsEnabled = false;
            try
            {
                bool ok = await WriteFurnaceRegisterAsync(
                    FurnaceModeIdle, FurnaceIp, FurnacePort, FurnaceModeRegister);
                if (ok) SetStatusSuccess("Режим работы печи изменён на «ждущий»");
                else SetStatusError("Ошибка записи режима «ждущий»");
            }
            finally { FurnaceStopButton.IsEnabled = true; }
        }

        private async Task<bool> WriteFurnaceRegisterAsync(
            ushort value, string ip, int port, ushort register)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(3000);
                if (await Task.WhenAny(connectTask, timeoutTask) != connectTask) return false;
                await connectTask;

                using var stream = client.GetStream();
                stream.ReadTimeout = 2000; stream.WriteTimeout = 2000;

                var buffer = new List<byte>(12);
                buffer.Add(0x00); buffer.Add(0x01);
                buffer.Add(0x00); buffer.Add(0x00);
                buffer.Add(0x00); buffer.Add(0x06);
                buffer.Add(0x00);
                buffer.Add(0x06);
                buffer.Add((byte)(register >> 8));
                buffer.Add((byte)(register & 0xFF));
                buffer.Add((byte)(value >> 8));
                buffer.Add((byte)(value & 0xFF));

                byte[] reqBytes = buffer.ToArray();
                await stream.WriteAsync(reqBytes, 0, reqBytes.Length);
                await stream.FlushAsync();

                byte[] response = new byte[12];
                int totalRead = 0;
                var start = DateTime.Now;
                while (totalRead < response.Length &&
                       (DateTime.Now - start).TotalMilliseconds < 2000)
                {
                    if (stream.DataAvailable)
                    {
                        int n = await stream.ReadAsync(response, totalRead, response.Length - totalRead);
                        if (n == 0) break;
                        totalRead += n;
                    }
                    else await Task.Delay(20);
                }
                return totalRead >= 12 && response[7] == 0x06;
            }
            catch { return false; }
        }

        private string FormatFurnaceSetPointFileName(float setPoint)
        {
            if (Math.Abs(setPoint - Math.Round(setPoint)) < 0.001f)
            {
                int intVal = (int)Math.Round(setPoint);
                if (intVal > 0) return "+" + intVal + ".csv";
                return intVal + ".csv";
            }
            string s = setPoint.ToString("0.##", CultureInfo.InvariantCulture);
            if (setPoint > 0 && !s.StartsWith("+")) s = "+" + s;
            return s + ".csv";
        }

        // ─────────────────────────── СОХРАНЕНИЕ ───────────────────────────

        private async Task<bool> SaveDataCoreAsync(bool silent)
        {
            string saveDir = SavePathBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(saveDir))
            {
                if (!silent) MessageBox.Show("Укажите путь сохранения.");
                else SetStatusWarning("Автосохранение пропущено: не указан путь сохранения");
                return false;
            }

            if (!Directory.Exists(saveDir))
            {
                if (!silent)
                {
                    var r = MessageBox.Show($"Папка не существует:\n{saveDir}\n\nСоздать?",
                        "Папка не найдена", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r != MessageBoxResult.Yes) return false;
                }
                try { Directory.CreateDirectory(saveDir); }
                catch
                {
                    if (silent) SetStatusWarning("Автосохранение: не удалось создать папку");
                    return false;
                }
            }

            List<ConnectedClient> selected;
            lock (_clientsLock)
                selected = _connectedClients.Where(c => c.IsSelected && !c.IsOffline).ToList();

            if (selected.Count == 0)
            {
                if (!silent) MessageBox.Show("Нет отмеченных клиентов.");
                else SetStatusWarning("Автосохранение пропущено: нет отмеченных клиентов");
                return false;
            }

            float furnaceSetPoint = _lastValidFurnaceSetPoint;

            string fileName = FormatFurnaceSetPointFileName(furnaceSetPoint);
            string fullPath = Path.Combine(saveDir, fileName);

            _lastSavedFilePath = fullPath;

            if (!silent)
            {
                if (MessageBox.Show($"Сохранить в:\n{fullPath}\n\nКлиентов: {selected.Count}",
                    "Подтверждение", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return false;
            }

            lock (_saveLock) _saveResponses.Clear();
            if (!silent) SaveDataButton.IsEnabled = false;

            try
            {
                byte[] message = Encoding.UTF8.GetBytes("SAVE_DATA " + fullPath + "\n");
                foreach (var c in selected)
                {
                    try
                    {
                        c.SetStatusSaving();
                        var stream = c.TcpClient.GetStream();
                        await stream.WriteAsync(message, 0, message.Length);
                        await stream.FlushAsync();
                    }
                    catch { }
                }

                int timeoutMs = 30000;
                var start = DateTime.Now;
                while ((DateTime.Now - start).TotalMilliseconds < timeoutMs * selected.Count)
                {
                    int done;
                    lock (_saveLock)
                        done = selected.Count(c => _saveResponses.Contains(c.Sid + ":OK")
                                                || _saveResponses.Contains(c.Sid + ":FAIL"));
                    if (done >= selected.Count) break;
                    await Task.Delay(200);
                }

                int okCount;
                lock (_saveLock)
                    okCount = selected.Count(c => _saveResponses.Contains(c.Sid + ":OK"));

                if (okCount == selected.Count)
                {
                    SetStatusSuccess($"Данные сохранены во всех клиентах ({okCount}): {fileName}");
                    return true;
                }
                else
                {
                    SetStatusWarning($"Сохранено {okCount} из {selected.Count}");
                    return false;
                }
            }
            finally
            {
                if (!silent) SaveDataButton.IsEnabled = true;
            }
        }

        private async void SaveData_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning)
            {
                MessageBox.Show("Идёт градуировка/жамкать. Дождитесь завершения.");
                return;
            }
            await SaveDataCoreAsync(silent: false);
        }

        // ─────────────────────────── ОЧИСТКА ДАННЫХ ───────────────────────────

        private async Task<bool> ClearDataCoreAsync(bool silent = false)
        {
            List<ConnectedClient> selected;
            lock (_clientsLock)
                selected = _connectedClients.Where(c => c.IsSelected && !c.IsOffline).ToList();

            if (selected.Count == 0)
            {
                if (!silent) MessageBox.Show("Нет отмеченных клиентов.");
                else SetStatusWarning("Автоочистка пропущена: нет отмеченных клиентов");
                return false;
            }

            lock (_clearLock) _clearResponses.Clear();
            if (!silent) ClearDataButton.IsEnabled = false;

            try
            {
                byte[] message = Encoding.UTF8.GetBytes("CLEAR_DATA\n");
                foreach (var c in selected)
                {
                    try
                    {
                        c.SetStatusClearing();
                        var stream = c.TcpClient.GetStream();
                        await stream.WriteAsync(message, 0, message.Length);
                        await stream.FlushAsync();
                    }
                    catch { }
                }

                SetStatus($"Отправлена команда «Новая калибровка» ({selected.Count} клиент(ов)). " +
                          $"Ожидание ответа...", false);

                int timeoutPerClientMs = 30_000;
                var start = DateTime.Now;
                while ((DateTime.Now - start).TotalMilliseconds < timeoutPerClientMs * selected.Count)
                {
                    int done;
                    lock (_clearLock)
                        done = selected.Count(c => _clearResponses.Contains(c.Sid + ":OK")
                                                || _clearResponses.Contains(c.Sid + ":FAIL"));
                    if (done >= selected.Count) break;
                    await Task.Delay(200);
                }

                int okCount;
                lock (_clearLock)
                    okCount = selected.Count(c => _clearResponses.Contains(c.Sid + ":OK"));

                if (okCount == selected.Count)
                {
                    SetStatusSuccess($"Данные очищены во всех клиентах ({okCount})");
                    return true;
                }
                else
                {
                    SetStatusWarning($"Очищено {okCount} из {selected.Count}");
                    return false;
                }
            }
            finally
            {
                if (!silent) ClearDataButton.IsEnabled = true;
            }
        }

        private async void ClearData_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning)
            {
                MessageBox.Show("Идёт градуировка/жамкать. Дождитесь завершения.");
                return;
            }
            await ClearDataCoreAsync(silent: false);
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_batchRunning)
            {
                _batchStopRequested = true;
                _stopRequested = true;
                SetStatusWarning("Пакетный режим прерывается...");
                return;
            }

            _stopRequested = true;
            _timerActive = false;
            _totalTimerActive = false;
            _pauseRequested = false;
            _autoStartTriggered = false;

            ResetStepTimers();

            await SendCommandNoWaitAsync("set_mode=stop;");
            SetStatusError("АВАРИЙНАЯ ОСТАНОВКА");
            UpdateAutoStartInfo();
        }

        private async void VentButton_Click(object sender, RoutedEventArgs e)
        {
            if (_batchRunning)
            {
                if (MessageBox.Show("Прервать пакетный режим и сбросить?",
                    "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                    != MessageBoxResult.Yes) return;

                _batchStopRequested = true;
                _stopRequested = true;
                SetStatusWarning("Пакетный режим прерывается...");
                return;
            }

            if (_isRunning)
            {
                if (MessageBox.Show("Прервать и сбросить?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

                _stopRequested = true;
                _totalTimerActive = false;
                _pauseRequested = false;
                _autoStartTriggered = false;

                ResetStepTimers();
                UpdateAutoStartInfo();
            }
            string? resp = await SendCommandAsync("set_mode=vent;");
            SetStatus($"Сброс отправлен: {resp}");
        }

        private async void CalibrateStocksButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning)
            {
                MessageBox.Show("Идёт градуировка/жамкать. Дождитесь завершения.");
                return;
            }

            if (MessageBox.Show(
                    "Запустить калибровку штоков АГК?\n\n" +
                    "Во время калибровки штоки будут двигаться в крайние положения.",
                    "Калибровка штоков",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            CalibrateStocksButton.IsEnabled = false;
            try
            {
                SetStatus("Отправка команды калибровки штоков...", false);
                string? resp = await SendCommandAsync("set_cal_stocks=start;");

                if (resp != null && resp.Contains("ok", StringComparison.OrdinalIgnoreCase))
                    SetStatusSuccess($"Калибровка штоков запущена: {resp}");
                else if (resp != null)
                    SetStatusWarning($"Ответ АГК: {resp}");
                else
                    SetStatusError("Нет ответа от АГК на set_cal_stocks=start;");
            }
            finally
            {
                CalibrateStocksButton.IsEnabled = true;
            }
        }

        private async Task<bool> SendWithRetryAsync(string command, string expected)
        {
            while (true)
            {
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    if (_stopRequested) return false;
                    if (!await WaitIfPausedAsync()) return false;

                    SetStatus($"{command} — попытка {attempt}/5");
                    string? response = await SendCommandAsync(command);
                    if (response != null && response.Contains(expected, StringComparison.Ordinal))
                        return true;
                    await SafeDelayAsync(1000);
                }
                if (MessageBox.Show($"Команда «{command}» не получила ответ после 5 попыток.",
                    "Ошибка", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return false;
            }
        }

        private async Task<double?> RefreshPressureAndSetPointAsync()
        {
            double? pressure = null;

            NetworkStream? stream;
            lock (_streamLock) stream = _stream;

            if (stream == null)
            {
                return null;
            }

            string? p = await SendCommandAsync("get_press;");
            if (p != null)
            {
                string parsed = ParsePressure(p);
                string units = _currentPressureUnitsLabel;
                AgkPressureText.Text = string.IsNullOrEmpty(units)
                    ? parsed
                    : $"{parsed} {units}";
                if (double.TryParse(parsed, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double v)) pressure = v;
            }

            string? sp = await SendCommandAsync("get_point;");
            if (sp != null)
            {
                string parsedSp = ParsePressure(sp);
                string units = _currentPressureUnitsLabel;
                AgkSetPointText.Text = string.IsNullOrEmpty(units)
                    ? parsedSp
                    : $"{parsedSp} {units}";
            }

            return pressure;
        }

        private async Task<bool> WaitForStateInPointAsync()
        {
            int okCount = 0;
            while (true)
            {
                if (_stopRequested) return false;
                if (!await WaitIfPausedAsync()) return false;

                string? resp = await SendCommandAsync("get_state_in_point;");
                if (resp != null && resp.Contains("ok;", StringComparison.Ordinal))
                {
                    okCount++;
                    if (okCount >= 5) return true;
                }
                else okCount = 0;
                await RefreshPressureAndSetPointAsync();
                SetStatus($"Ожидание стабилизации: {okCount}/5");
                await SafeDelayAsync(1000);
            }
        }

        private async Task<bool> WaitUntilPressureBelowAsync(double threshold, int timeoutMs)
        {
            var start = DateTime.Now;
            var pausedTotal = TimeSpan.Zero;
            var pauseStartedAt = DateTime.MinValue;

            while (true)
            {
                if (_stopRequested) return false;

                if (_pauseRequested)
                {
                    if (pauseStartedAt == DateTime.MinValue) pauseStartedAt = DateTime.Now;
                    await Task.Delay(100);
                    continue;
                }
                else if (pauseStartedAt != DateTime.MinValue)
                {
                    pausedTotal += DateTime.Now - pauseStartedAt;
                    pauseStartedAt = DateTime.MinValue;
                }

                if ((DateTime.Now - start - pausedTotal).TotalMilliseconds >= timeoutMs)
                    return false;

                double? p = await RefreshPressureAndSetPointAsync();
                if (p.HasValue && Math.Abs(p.Value) < threshold) return true;
                await SafeDelayAsync(1000);
            }
        }

        private string GetUnitsCode(string? tp)
        {
            if (string.IsNullOrEmpty(tp)) return "kgs";
            if (tp.Contains("кгс/см²")) return "kgs";
            if (tp.Contains("МПа")) return "MPa";
            if (tp.Contains("кПа")) return "kPa";
            if (tp.Contains("бар")) return "bar";
            if (tp.Contains("PSI")) return "psi";
            return "kgs";
        }

        private string GetUnitsLabel(string? tp)
        {
            if (string.IsNullOrEmpty(tp)) return "";
            if (tp.Contains("кгс/см²")) return "кгс/см²";
            if (tp.Contains("МПа")) return "МПа";
            if (tp.Contains("кПа")) return "кПа";
            if (tp.Contains("бар")) return "бар";
            if (tp.Contains("PSI")) return "PSI";
            return "";
        }

        private double GetAtmosphereThreshold(string unitsCode) =>
            unitsCode switch
            {
                "kgs" => 1.0,
                "MPa" => 0.1,
                "kPa" => 100.0,
                "bar" => 1.0,
                "psi" => 14.7,
                _ => 1.0
            };

        private int GetStepDuration(double[] steps, int index, bool longTimers)
        {
            if (index == 0) return 10;
            double cur = steps[index];
            double prev = steps[index - 1];
            if (cur == 0) return longTimers ? 500 : 300;
            if (cur > prev) return longTimers ? 220 : 120;
            return longTimers ? 330 : 180;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (VersionText != null)
                VersionText.Text = GetAppVersion();

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

            UpdateAutoStartInfo();
            UpdateStartButtonMode();
        }

        private async Task ConnectToDeviceLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                bool needConnect;
                lock (_streamLock) needConnect = (_stream == null);
                if (!needConnect) { await Task.Delay(1000); continue; }

                try
                {
                    SetStatusWarning($"Подключение к {DeviceIp}:{DevicePort}...");
                    var client = new TcpClient();
                    var ct = client.ConnectAsync(DeviceIp, DevicePort);
                    var tt = Task.Delay(3000);
                    if (await Task.WhenAny(ct, tt) != ct)
                    {
                        try { client.Close(); } catch { }
                        SetStatusError($"Прибор недоступен. Повтор через 5 сек...");
                        await Task.Delay(5000); continue;
                    }
                    await ct;
                    var stream = client.GetStream();
                    lock (_streamLock) { _client = client; _stream = stream; }
                    SetStatusSuccess($"Подключено к {DeviceIp}:{DevicePort}");
                }
                catch
                {
                    SetStatusError($"Прибор недоступен. Повтор через 5 сек...");
                }
                await Task.Delay(5000);
            }
        }

        private void ResetDeviceConnection()
        {
            lock (_streamLock)
            {
                try { _stream?.Close(); } catch { }
                try { _client?.Close(); } catch { }
                _stream = null; _client = null;
            }
            SetStatusError("Соединение потеряно. Переподключение...");
        }

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            if (_isRunning) return;

            NetworkStream? stream;
            lock (_streamLock) stream = _stream;

            bool agkOk = false;

            if (stream != null)
            {
                try
                {
                    if (stream.CanRead && stream.CanWrite)
                    {
                        string? ur = await SendCommandAsync("get_units;");
                        if (ur != null)
                        {
                            UpdateUnits(ur);
                            await RefreshPressureAndSetPointAsync();

                            await RefreshAgkStatusAsync();

                            agkOk = true;
                        }
                    }
                    else
                    {
                        ResetDeviceConnection();
                    }
                }
                catch
                {
                    ResetDeviceConnection();
                }
            }

            if (agkOk)
            {
                _agkFailCount = 0;
                AgkStatusEllipse.Fill = Brushes.Green;

                if (!_totalTimerActive)
                    SetStatusSuccess($"Подключено к {DeviceIp}:{DevicePort}");
            }
            else
            {
                _agkFailCount++;
                if (_agkFailCount >= ConnectionLostThreshold)
                {
                    AgkPressureText.Text = "—";
                    AgkSetPointText.Text = "—";
                    AgkStatusEllipse.Fill = Brushes.Red;

                    if (AgkStatusTextBox != null)
                    {
                        AgkStatusTextBox.Text = "—";
                        AgkStatusTextBox.Foreground =
                            new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                    }
                }
            }
        }

        private void UpdateUnits(string response)
        {
            char? d = null;
            foreach (char c in response)
                if (c >= '0' && c <= '9') { d = c; break; }
            string u = d switch
            {
                '0' => "кПа",
                '1' => "МПа",
                '2' => "бар",
                '3' => "PSI",
                '4' => "кгс/см²",
                _ => "?"
            };
            _currentPressureUnitsLabel = u;
        }

        private async Task<string?> SendCommandAsync(string command)
        {
            NetworkStream? stream;
            lock (_streamLock) stream = _stream;
            if (stream == null) return null;

            try
            {
                byte[] flush = new byte[1024];
                while (stream.DataAvailable)
                {
                    await stream.ReadAsync(flush, 0, flush.Length);
                    await Task.Delay(20);
                }

                byte[] req = Encoding.ASCII.GetBytes(command);
                await stream.WriteAsync(req, 0, req.Length);

                var sb = new StringBuilder();
                byte[] one = new byte[1];
                var start = DateTime.Now;
                while ((DateTime.Now - start).TotalMilliseconds < 2000)
                {
                    if (stream.DataAvailable)
                    {
                        int n = await stream.ReadAsync(one, 0, 1);
                        if (n == 0) break;
                        char c = (char)one[0];
                        sb.Append(c);
                        if (c == ';') break;
                    }
                    else await Task.Delay(10);
                }
                return sb.Length > 0 ? sb.ToString().Trim() : null;
            }
            catch { ResetDeviceConnection(); return null; }
        }

        private async Task SendCommandNoWaitAsync(string command)
        {
            NetworkStream? stream;
            lock (_streamLock) stream = _stream;
            if (stream == null) return;
            try
            {
                byte[] req = Encoding.ASCII.GetBytes(command);
                await stream.WriteAsync(req, 0, req.Length);
            }
            catch { ResetDeviceConnection(); }
        }

        private string ParsePressure(string raw)
        {
            raw = raw.Trim().Trim('\r', '\n', '\0', ' ', ';');
            if (raw.StartsWith("E", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("e", StringComparison.OrdinalIgnoreCase))
                raw = raw.Substring(1);
            raw = raw.Replace(',', '.');
            if (double.TryParse(raw, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double v))
                return Math.Round(v, 5).ToString("F5", CultureInfo.InvariantCulture);
            return raw;
        }

        private async Task SafeDelayAsync(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end)
            {
                if (_stopRequested) return;

                while (_pauseRequested && !_stopRequested)
                    await Task.Delay(100);

                if (_stopRequested) return;

                await Task.Delay(100);
            }
        }

        private void SetStatus(string text, bool running = true)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetStatus(text, running)); return; }
            StatusText.Text = text;
            StatusText.Foreground = running ? Brushes.Green : Brushes.Gray;
        }

        private void SetStatusSuccess(string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetStatusSuccess(text)); return; }
            StatusText.Text = text; StatusText.Foreground = Brushes.Green;
        }

        private void SetStatusWarning(string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetStatusWarning(text)); return; }
            StatusText.Text = text; StatusText.Foreground = Brushes.Goldenrod;
        }

        private void SetStatusError(string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetStatusError(text)); return; }
            StatusText.Text = text; StatusText.Foreground = Brushes.OrangeRed;
        }

        private static string GetAppVersion()
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var v = asm.GetName().Version;
            if (v == null) return "v0.0.0";

            return (v.Revision == 0 && v.Build >= 0)
                ? $"v{v.Major}.{v.Minor}.{v.Build}"
                : $"v{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }

        private void SetExternalButtonsEnabled(bool enabled)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetExternalButtonsEnabled(enabled));
                return;
            }

            if (TakePointBroadcastButton != null) TakePointBroadcastButton.IsEnabled = enabled;
            if (SaveDataButton != null) SaveDataButton.IsEnabled = enabled;
            if (ClearDataButton != null) ClearDataButton.IsEnabled = enabled;
            if (RemoveOfflineButton != null) RemoveOfflineButton.IsEnabled = enabled;
        }

        protected override void OnClosed(EventArgs e)
        {
            try { SaveWindowPositionToSettings(); } catch { }
            try { SaveSettings(); } catch { }
            _stopRequested = true;
            _pauseRequested = false;
            _totalTimerActive = false;
            _batchStopRequested = true;
            _timer?.Stop();
            _clientsUiTimer?.Stop();
            _statusResetTimer?.Stop();
            _furnaceTempTimer?.Stop();
            _platinaTimer?.Stop();
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
                    _connectedClients.Clear();
                }
                try { _broadcastServer?.Stop(); } catch { }
            }
            catch { }

            base.OnClosed(e);
            Environment.Exit(0);
        }
    }
}