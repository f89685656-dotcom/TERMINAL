using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace PressureMonitor
{
    public class ConnectedClient : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private Brush _statusColor;
        private string _statusText = "Готов";
        private bool _isOffline = false;

        // volatile-поля для потокобезопасности
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

        public bool IsWaiting
        {
            get => _isWaiting;
            set => _isWaiting = value;
        }

        public bool LastDoneOk
        {
            get => _lastDoneOk;
            set => _lastDoneOk = value;
        }

        public bool LastDoneFail
        {
            get => _lastDoneFail;
            set => _lastDoneFail = value;
        }

        public bool IsOffline => _isOffline;

        public DateTime? DisconnectedAt { get; set; } = null;
        public DateTime LastActivityAt { get; set; } = DateTime.Now;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private static void RunOnUI(Action action)
        {
            var app = Application.Current;
            if (app == null) { action(); return; }
            if (app.Dispatcher.CheckAccess())
                action();
            else
                app.Dispatcher.Invoke(action);
        }

        public string DisplayText => $"SID: {Sid}";

        public void SetStatusReady()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Green);
                StatusText = "Готов";
                IsWaiting = false;
                LastDoneOk = true;
                DisconnectedAt = null;
            });
        }

        public void SetStatusWaiting()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = "Занят съёмом";
                IsWaiting = true;
                LastDoneOk = false;
            });
        }

        public void SetStatusOffline()
        {
            RunOnUI(() =>
            {
                _isOffline = true;
                StatusColor = CreateFrozenBrush(Colors.Red);
                StatusText = "Не отвечает";
                IsWaiting = false;
                LastDoneOk = false;
                if (DisconnectedAt == null)
                    DisconnectedAt = DateTime.Now;
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
                IsWaiting = false;
                LastDoneOk = true;
            });
        }

        public void SetStatusDoneFail()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = "Ошибка съёма";
                IsWaiting = false;
                LastDoneFail = true;
            });
        }
    }

    public class StepItem
    {
        public string IndexText { get; set; } = "";
        public string ValueText { get; set; } = "";
        public string CheckMark { get; set; } = "☐";
    }

    public partial class MainWindow : Window
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly object _streamLock = new object();

        private DispatcherTimer? _timer;
        private string? _selectedTargetPressure;
        private int _currentStepIndex = 0;
        private int _completedSteps = 0;

        private bool _isRunning = false;
        private bool _stopRequested = false;

        private DateTime _timerEnd = DateTime.MinValue;
        private bool _timerActive = false;

        private TcpListener? _broadcastServer;
        private readonly List<ConnectedClient> _connectedClients = new();
        private readonly object _clientsLock = new();
        private readonly CancellationTokenSource _cts = new();

        private DispatcherTimer? _clientsUiTimer;

        private const int OfflineRetentionSeconds = 60;
        private const int ClientDeadThresholdSeconds = 120;

        private const string DeviceIp = "192.168.0.159";
        private const int DevicePort = 8888;

        // ─── Печь (ST-351, Modbus TCP) ───
        private const string FurnaceIp = "192.168.0.167";
        private const int FurnacePort = 502;
        private const ushort FurnaceSetPointRegister = 201;

        /// <summary>
        /// Последняя записанная уставка печи (°C). Используется для имени файла.
        /// </summary>
        private float _lastFurnaceSetPoint = 0f;

        /// <summary>
        /// Кто из клиентов уже ответил SAVED_OK / SAVED_FAIL в текущей операции сохранения.
        /// </summary>
        private readonly HashSet<string> _saveResponses = new();
        private readonly object _saveLock = new();

        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
                "PressureMonitor");

        private static readonly string SettingsFile =
            Path.Combine(SettingsDir, "settings.json");

        public MainWindow()
        {
            InitializeComponent();
            LoadTargetPressures();
            LoadTimerSets();
            LoadSettings();
        }

        // ─────────────────────────── ЗАПОЛНЕНИЕ СПИСКОВ ───────────────────────────

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
            public string CycleCount { get; set; } = "10";
            public string SavePath { get; set; } = "";
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var s = JsonSerializer.Deserialize<Settings>(json);
                    if (s != null)
                    {
                        if (s.TargetPressureIndex >= 0 &&
                            s.TargetPressureIndex < TargetPressureCombo.Items.Count)
                            TargetPressureCombo.SelectedIndex = s.TargetPressureIndex;

                        if (s.TimerSetIndex >= 0 &&
                            s.TimerSetIndex < TimerSetCombo.Items.Count)
                            TimerSetCombo.SelectedIndex = s.TimerSetIndex;

                        if (!string.IsNullOrWhiteSpace(s.CycleCount))
                            CycleCountBox.Text = s.CycleCount;

                        if (!string.IsNullOrWhiteSpace(s.SavePath))
                            SavePathBox.Text = s.SavePath;
                    }
                }

                if (string.IsNullOrWhiteSpace(SavePathBox.Text))
                {
                    string docs = Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments);
                    SavePathBox.Text = Path.Combine(docs, "PressureMonitor");
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                var s = new Settings
                {
                    TargetPressureIndex = TargetPressureCombo.SelectedIndex,
                    TimerSetIndex = TimerSetCombo.SelectedIndex,
                    CycleCount = CycleCountBox.Text,
                    SavePath = SavePathBox.Text
                };

                string json = JsonSerializer.Serialize(s,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }

        // ─────────────────────────── COMBOBOX ───────────────────────────

        private void TargetPressureCombo_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
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
            SaveSettings();
        }

        private void ShowSteps()
        {
            if (_selectedTargetPressure == null) return;
            if (!_stepsByTarget.TryGetValue(_selectedTargetPressure, out var values))
            {
                StepsList.ItemsSource = null;
                return;
            }

            string unitsLabel = GetUnitsLabel(_selectedTargetPressure);

            var items = new List<StepItem>();
            for (int i = 0; i < values.Length; i++)
            {
                items.Add(new StepItem
                {
                    IndexText = (i + 1) + ".",
                    ValueText = values[i] + " " + unitsLabel,
                    CheckMark = i < _completedSteps ? "☑" : "☐"
                });
            }

            StepsList.ItemsSource = items;
        }

        private void ResetStepsCheckmarks()
        {
            _completedSteps = 0;
            _currentStepIndex = 0;
            ShowSteps();
        }

        private void CycleCountBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            if (CyclesText == null) return;

            if (int.TryParse(CycleCountBox.Text.Trim(), out int cycles) && cycles > 0)
            {
                string current = CyclesText.Text;
                string donePart = "0";
                if (!string.IsNullOrEmpty(current) && current.Contains("/"))
                    donePart = current.Split('/')[0].Trim();

                CyclesText.Text = $"{donePart} / {cycles}";
            }

            SaveSettings();
        }

        private void SavePathBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            SaveSettings();
        }

        // ═══════════════════════════════════════════════════════════════════
        // BROADCAST-СЕРВЕР
        // ═══════════════════════════════════════════════════════════════════

        private void StartBroadcastServer()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[Broadcast] Запуск broadcast-сервера...");
                _broadcastServer = new TcpListener(IPAddress.Loopback, 9990);
                _broadcastServer.Start();
                System.Diagnostics.Debug.WriteLine("[Broadcast] Сервер запущен на 127.0.0.1:9990");

                _ = Task.Run(async () =>
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        try
                        {
                            var client = await _broadcastServer.AcceptTcpClientAsync();
                            System.Diagnostics.Debug.WriteLine(
                                $"[Broadcast] === Принято подключение, поток {System.Threading.Thread.CurrentThread.ManagedThreadId}");
                            _ = Task.Run(() => HandleClientAsync(client));
                        }
                        catch (ObjectDisposedException)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                "[Broadcast] Слушатель закрыт — выходим из цикла");
                            break;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[Broadcast] Ошибка в цикле AcceptTcpClientAsync: {ex.Message}");
                            break;
                        }
                    }
                    System.Diagnostics.Debug.WriteLine("[Broadcast] Цикл AcceptTcpClientAsync завершён");
                });

                _clientsUiTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _clientsUiTimer.Tick += (s, e) => RefreshClientsUi();
                _clientsUiTimer.Start();

                SetStatus("Broadcast-сервер запущен на 127.0.0.1:9990", false);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                MessageBox.Show(
                    "Порт 9990 уже занят.\n" +
                    "Возможно, запущен старый экземпляр PressureMonitor.\n" +
                    "Закройте его (через Диспетчер задач) и запустите снова.",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
            List<ConnectedClient> markedDead = new();

            lock (_clientsLock)
            {
                var now = DateTime.Now;
                foreach (var c in _connectedClients)
                {
                    // Используем IsOffline вместо чтения Brush.Color
                    if (!c.IsOffline &&
                        (now - c.LastActivityAt).TotalSeconds >= ClientDeadThresholdSeconds)
                    {
                        c.SetStatusOffline();
                        markedDead.Add(c);
                    }

                    if (c.DisconnectedAt.HasValue &&
                        (now - c.DisconnectedAt.Value).TotalSeconds >= OfflineRetentionSeconds)
                    {
                        toRemove.Add(c);
                    }
                }
                foreach (var c in toRemove)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Broadcast] Удаляем клиента {c.Sid} (offline > {OfflineRetentionSeconds} сек)");
                    _connectedClients.Remove(c);
                }

                snapshot = _connectedClients.ToList();
            }

            foreach (var c in markedDead)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Broadcast] Клиент {c.Sid} помечен offline (нет активности > {ClientDeadThresholdSeconds} сек)");
            }

            ClientsList.ItemsSource = null;
            ClientsList.ItemsSource = snapshot;

            int onlineCount = snapshot.Count(c => !c.IsOffline);
            int totalCount = snapshot.Count;

            if (totalCount == onlineCount)
                BroadcastStatusText.Text = $"Клиентов подключено: {onlineCount}";
            else
                BroadcastStatusText.Text =
                    $"Клиентов подключено: {onlineCount} (offline: {totalCount - onlineCount})";

            BroadcastStatusIcon.Text = onlineCount > 0 ? "✔" : "⛔";
            BroadcastStatusIcon.Foreground = onlineCount > 0
                ? Brushes.Green
                : Brushes.Red;
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            NetworkStream? stream = null;
            string sid = "(неизвестен)";
            ConnectedClient? connectedClient = null;
            int threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

            try
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Broadcast] === HandleClientAsync начат, поток {threadId}");

                stream = client.GetStream();
                System.Diagnostics.Debug.WriteLine(
                    $"[Broadcast] Поток {threadId}: клиент подключился, читаем HELLO...");

                var sb = new StringBuilder();
                byte[] one = new byte[1];
                while (true)
                {
                    int n = await stream.ReadAsync(one, 0, 1);
                    if (n == 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Broadcast] Поток {threadId}: ReadAsync вернул 0 при чтении HELLO");
                        return;
                    }
                    if (one[0] == 10) break;
                    sb.Append((char)one[0]);
                }

                string hello = sb.ToString().Trim();
                System.Diagnostics.Debug.WriteLine(
                    $"[Broadcast] Поток {threadId}: HELLO: [{hello}]");

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
                        System.Diagnostics.Debug.WriteLine(
                            $"[Broadcast] Поток {threadId}: клиент {sid} уже существует — обновляем TcpClient");
                        existing.TcpClient = client;
                        existing.ConnectedAt = DateTime.Now;
                        existing.LastActivityAt = DateTime.Now;
                        existing.SetStatusAlive();
                        connectedClient = existing;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Broadcast] Поток {threadId}: добавляем нового клиента {sid}");
                        _connectedClients.Add(connectedClient);
                    }
                }

                if (Dispatcher.CheckAccess())
                    RefreshClientsUi();
                else
                    Dispatcher.Invoke(() => RefreshClientsUi());

                var buffer = new StringBuilder();
                byte[] buf = new byte[512];

                while (!_cts.IsCancellationRequested)
                {
                    int n;
                    try
                    {
                        n = await stream.ReadAsync(buf, 0, buf.Length, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Broadcast] Поток {threadId}: чтение отменено для {sid}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Broadcast] Поток {threadId}: ошибка чтения {sid}: {ex.Message}");
                        break;
                    }

                    if (n == 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Broadcast] Поток {threadId}: клиент {sid} закрыл соединение (Read=0)");
                        break;
                    }

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Broadcast] Поток {threadId}: ОШИБКА клиента {sid}: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Broadcast] === finally для клиента {sid}, поток {threadId}");

                try { stream?.Close(); } catch { }
                try { client.Close(); } catch { }

                if (connectedClient != null)
                {
                    bool sameClient;
                    lock (_clientsLock)
                    {
                        sameClient = connectedClient.TcpClient == client;
                    }

                    if (sameClient)
                    {
                        connectedClient.SetStatusOffline();
                        System.Diagnostics.Debug.WriteLine(
                            $"[Broadcast] Поток {threadId}: клиент {sid} помечен offline (same TcpClient)");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Broadcast] Поток {threadId}: клиент {sid} уже переподключился — не помечаем offline");
                    }

                    if (Dispatcher.CheckAccess())
                        RefreshClientsUi();
                    else
                        Dispatcher.Invoke(() => RefreshClientsUi());
                }
            }
        }

        private void ProcessClientMessage(ConnectedClient client, string line)
        {
            System.Diagnostics.Debug.WriteLine($"[Client {client.Sid}] {line}");
            client.LastActivityAt = DateTime.Now;

            if (line.StartsWith("HELLO "))
            {
                client.SetStatusAlive();
            }
            else if (line.StartsWith("STATUS_BUSY"))
            {
                client.SetStatusWaiting();
            }
            else if (line.StartsWith("DONE_OK"))
            {
                client.LastDoneOk = true;
                client.LastDoneFail = false;
                client.SetStatusDoneSuccess();
            }
            else if (line.StartsWith("DONE_FAIL"))
            {
                client.LastDoneOk = false;
                client.LastDoneFail = true;
                client.SetStatusDoneFail();
            }
            else if (line.StartsWith("SAVED_OK"))
            {
                lock (_saveLock) { _saveResponses.Add(client.Sid + ":OK"); }
                client.SetStatusSaved();
                System.Diagnostics.Debug.WriteLine(
                    $"[Broadcast] Клиент {client.Sid} сохранил данные OK");
            }
            else if (line.StartsWith("SAVED_FAIL"))
            {
                lock (_saveLock) { _saveResponses.Add(client.Sid + ":FAIL"); }
                client.SetStatusSaveFailed();
                System.Diagnostics.Debug.WriteLine(
                    $"[Broadcast] Клиент {client.Sid} не смог сохранить: {line}");
            }
        }

        // ─────────────────────────── УДАЛЕНИЕ КЛИЕНТОВ ───────────────────────────

        private void RemoveOffline_Click(object sender, RoutedEventArgs e)
        {
            int removed = 0;
            lock (_clientsLock)
            {
                var toRemove = _connectedClients
                    .Where(c => c.IsOffline)
                    .ToList();
                foreach (var c in toRemove)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Broadcast] Вручную удаляем offline-клиента {c.Sid}");
                    try { c.TcpClient.Close(); } catch { }
                    _connectedClients.Remove(c);
                    removed++;
                }
            }
            System.Diagnostics.Debug.WriteLine(
                $"[Broadcast] Вручную удалено offline-клиентов: {removed}");
            RefreshClientsUi();
        }

        private void DisconnectClient_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi &&
                mi.Tag is ConnectedClient client)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Broadcast] Вручную отключаем клиента {client.Sid}");
                lock (_clientsLock)
                {
                    try { client.TcpClient.Close(); } catch { }
                    _connectedClients.Remove(client);
                }
                RefreshClientsUi();
            }
        }

        // ─────────────────────────── ОТПРАВКА КОМАНДЫ ───────────────────────────

        private async Task<bool> SendCommandToAllClientsAsync(string command)
        {
            List<ConnectedClient> selected;
            lock (_clientsLock)
            {
                selected = _connectedClients
                    .Where(c => c.IsSelected && !c.IsOffline)
                    .ToList();
            }

            if (selected.Count == 0)
            {
                SetStatus("Нет отмеченных клиентов");
                return false;
            }

            byte[] message = Encoding.ASCII.GetBytes(command);
            int success = 0;

            foreach (var c in selected)
            {
                try
                {
                    var stream = c.TcpClient.GetStream();
                    await stream.WriteAsync(message, 0, message.Length);
                    await stream.FlushAsync();
                    success++;
                }
                catch { }
            }

            SetStatus($"Отправлено {success}/{selected.Count}: {command.Trim()}");
            return success > 0;
        }

        // ─────────────────────────── ОЖИДАНИЕ DONE ───────────────────────────

        private async Task<bool> TakePointAndWaitAllAsync()
        {
            List<ConnectedClient> selected;
            lock (_clientsLock)
            {
                selected = _connectedClients
                    .Where(c => c.IsSelected && !c.IsOffline)
                    .ToList();
            }

            System.Diagnostics.Debug.WriteLine(
                $"[Broadcast] TakePointAndWaitAllAsync: клиентов выбрано {selected.Count}");

            if (selected.Count == 0)
            {
                SetStatus("Нет отмеченных клиентов");
                return false;
            }

            foreach (var c in selected)
            {
                c.LastDoneOk = false;
                c.LastDoneFail = false;
                c.SetStatusWaiting();
            }

            byte[] message = Encoding.ASCII.GetBytes("TAKE_POINT\n");
            foreach (var c in selected)
            {
                try
                {
                    var stream = c.TcpClient.GetStream();
                    await stream.WriteAsync(message, 0, message.Length);
                    await stream.FlushAsync();
                    System.Diagnostics.Debug.WriteLine(
                        $"[Broadcast] Отправлен TAKE_POINT клиенту {c.Sid}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Broadcast] Не удалось отправить TAKE_POINT клиенту {c.Sid}: {ex.Message}");
                }
            }

            foreach (var client in selected)
            {
                bool done = await WaitForClientDoneAsync(client);
                if (!done)
                {
                    SetStatus($"Клиент {client.Sid} не завершил съём");
                    return false;
                }
            }

            SetStatus($"Все клиенты ({selected.Count}) ответили DONE_OK. Пауза 3 сек...");
            await SafeDelayAsync(3000);
            return true;
        }

        private async Task<bool> WaitForClientDoneAsync(ConnectedClient client)
        {
            var start = DateTime.Now;
            var timeout = TimeSpan.FromMinutes(15);

            SetStatus($"Ожидание DONE от клиента {client.Sid}...", false);

            while (DateTime.Now - start < timeout)
            {
                if (_stopRequested) return false;

                if (client.LastDoneOk)
                {
                    SetStatus($"Клиент {client.Sid}: DONE_OK");
                    return true;
                }

                if (client.LastDoneFail)
                {
                    SetStatus($"Клиент {client.Sid}: DONE_FAIL");
                    return false;
                }

                if (client.IsOffline)
                {
                    SetStatus($"Клиент {client.Sid} offline — ждём...");
                }
                else if (client.IsWaiting)
                {
                    var elapsed = (int)(DateTime.Now - start).TotalSeconds;
                    SetStatus($"Клиент {client.Sid} занят съёмом ({elapsed} сек)...");
                }

                await Task.Delay(500);
            }

            SetStatus($"Клиент {client.Sid}: таймаут ожидания DONE (15 мин)");
            return false;
        }

        // ─────────────────────────── КНОПКА BROADCAST ───────────────────────────

        private async void TakePointBroadcast_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TakePointBroadcastButton.IsEnabled = false;
                await TakePointAndWaitAllAsync();
            }
            finally
            {
                TakePointBroadcastButton.IsEnabled = true;
            }
        }

        // ═══════════════════════════════════════════════════════════════════

        // ─────────────────────────── ГРАДУИРОВКА ───────────────────────────

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) { MessageBox.Show("Программа уже запущена."); return; }
            if (_selectedTargetPressure == null) { MessageBox.Show("Выберите целевое давление."); return; }
            if (!_stepsByTarget.TryGetValue(_selectedTargetPressure, out var steps))
            { MessageBox.Show($"Для «{_selectedTargetPressure}» не заданы шаги."); return; }

            bool noConnection;
            lock (_streamLock) { noConnection = (_stream == null); }
            if (noConnection) { MessageBox.Show("Нет соединения с прибором."); return; }

            if (!int.TryParse(StartStepBox.Text.Trim(), out int startStep) ||
                startStep < 1 || startStep > steps.Length)
            {
                MessageBox.Show(
                    $"Некорректный номер шага: {StartStepBox.Text}\n" +
                    $"Допустимо от 1 до {steps.Length}.",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            bool longTimers = TimerSetCombo.SelectedIndex == 1;

            _isRunning = true;
            _stopRequested = false;
            _completedSteps = startStep - 1;
            _currentStepIndex = startStep - 1;
            ShowSteps();

            StartButton.IsEnabled = false;
            PumpButton.IsEnabled = false;

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
                StartButton.IsEnabled = true;
                PumpButton.IsEnabled = true;

                if (programCompleted && !_stopRequested)
                {
                    ResetStepsCheckmarks();
                }
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
            UnitsText.Text = unitsLabel;
            SetPointUnitsText.Text = unitsLabel;

            for (int i = startIndex; i < steps.Length; i++)
            {
                if (_stopRequested) { SetStatus("Остановлено оператором"); return; }

                _currentStepIndex = i;
                ShowSteps();

                int human = i + 1;
                double v = steps[i];
                int duration = GetStepDuration(steps, i, longTimers);

                if (i == 0)
                {
                    SetStatus("Шаг 1: сброс до атмосферы (vent)");
                    bool ventOk = await SendWithRetryAsync("set_mode=vent;", "ok;");
                    if (!ventOk) { SetStatus("Шаг 1: не удалось перевести в vent"); return; }

                    SetStatus("Шаг 1: подготовка к съёму");
                    await SimpleCountdownAsync(10, "До начала съёма данных",
                        $"   |   Шаг {human}/{steps.Length}");
                    if (_stopRequested) { SetStatus("Остановлено"); return; }

                    SetStatus("Шаг 1: команда съёма");
                    bool q1 = await TakePointAndWaitAllAsync();
                    if (!q1) { SetStatus("Шаг 1: съём не удался"); return; }

                    _completedSteps = 1;
                    ShowSteps();
                    continue;
                }

                if (i == steps.Length - 1)
                {
                    SetStatus($"Шаг {human}: сброс до атмосферы (vent)");
                    bool ventOk = await SendWithRetryAsync("set_mode=vent;", "ok;");
                    if (!ventOk) return;

                    SetStatus($"Шаг {human}: ожидание снижения давления");
                    bool lowOk = await WaitUntilPressureBelowAsync(atmoThreshold, 300_000);
                    if (!lowOk) { SetStatus($"Шаг {human}: давление не снизилось"); return; }

                    if (v == 0)
                    {
                        await SimpleCountdownAsync(duration, "До начала съёма данных",
                            $"   |   Шаг {human}/{steps.Length}");
                    }
                    else
                    {
                        SetStatus($"Шаг {human}: измерение {duration} сек");
                        await RunTimerAsync(duration, $"   |   Шаг {human}/{steps.Length}");
                    }

                    SetStatus($"Шаг {human}: команда съёма");
                    bool qLast = await TakePointAndWaitAllAsync();
                    if (!qLast) { SetStatus($"Шаг {human}: съём не удался"); return; }

                    _completedSteps = i + 1;
                    ShowSteps();
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
                if (!setOk) { SetStatus($"Шаг {human}: не удалось установить"); return; }

                SetPointText.Text = v.ToString("F5", CultureInfo.InvariantCulture);

                SetStatus($"Шаг {human}: ожидание стабилизации давления");
                bool stable = await WaitForStateInPointAsync();
                if (!stable) { SetStatus($"Шаг {human}: прервано"); return; }

                SetStatus($"Шаг {human}: измерение {duration} сек");
                await RunTimerAsync(duration, $"   |   Шаг {human}/{steps.Length}");

                SetStatus($"Шаг {human}: команда съёма");
                bool q = await TakePointAndWaitAllAsync();
                if (!q) { SetStatus($"Шаг {human}: съём не удался"); return; }

                _completedSteps = i + 1;
                ShowSteps();
            }

            SetStatus("Программа завершена");
        }

        // ─────────────────────────── ЖАМКАТЬ ───────────────────────────

        private async void PumpButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) { MessageBox.Show("Программа уже запущена."); return; }
            if (_selectedTargetPressure == null) { MessageBox.Show("Выберите целевое давление."); return; }
            if (!_stepsByTarget.TryGetValue(_selectedTargetPressure, out var steps))
            { MessageBox.Show($"Шаги для «{_selectedTargetPressure}» не заданы."); return; }

            bool noConnection;
            lock (_streamLock) { noConnection = (_stream == null); }
            if (noConnection) { MessageBox.Show("Нет соединения с прибором."); return; }

            if (!int.TryParse(CycleCountBox.Text.Trim(), out int cycles) || cycles <= 0)
            { MessageBox.Show("Введите корректное количество циклов."); return; }

            double maxPressure = steps.Max();
            string unitsCode = GetUnitsCode(_selectedTargetPressure!);
            string unitsLabel = GetUnitsLabel(_selectedTargetPressure);
            double atmoThreshold = GetAtmosphereThreshold(unitsCode);

            _isRunning = true;
            _stopRequested = false;
            StartButton.IsEnabled = false;
            PumpButton.IsEnabled = false;

            try { await RunPumpSequenceAsync(maxPressure, cycles, unitsCode, unitsLabel, atmoThreshold); }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
                StatusText.Foreground = Brushes.Red;
            }
            finally
            {
                _isRunning = false;
                StartButton.IsEnabled = true;
                PumpButton.IsEnabled = true;
            }
        }

        private async Task RunPumpSequenceAsync(double maxPressure, int totalCycles,
            string unitsCode, string unitsLabel, double atmoThreshold)
        {
            MinValueText.Text = "0";
            MinUnitsText.Text = unitsLabel;
            MaxValueText.Text = maxPressure.ToString(CultureInfo.InvariantCulture);
            MaxUnitsText.Text = unitsLabel;
            CyclesText.Text = $"0 / {totalCycles}";

            SetStatus($"Установка единиц: {unitsCode}");
            bool unitsOk = await SendWithRetryAsync($"set_units={unitsCode};", "ok;");
            if (!unitsOk) { SetStatus("Не удалось установить единицы"); return; }

            UnitsText.Text = unitsLabel;
            SetPointUnitsText.Text = unitsLabel;

            for (int cycle = 1; cycle <= totalCycles; cycle++)
            {
                if (_stopRequested) { SetStatus("Остановлено оператором"); return; }

                MinCheck.Text = "☐";
                MaxCheck.Text = "☐";
                CyclesCheck.Text = "☑";
                CyclesText.Text = $"{cycle} / {totalCycles}";

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
                if (!setOk) { SetStatus($"[Цикл {cycle}]: не удалось"); return; }

                SetPointText.Text = maxPressure.ToString("F5", CultureInfo.InvariantCulture);
                MaxCheck.Text = "☑";

                SetStatus($"[Цикл {cycle}] ожидание стабилизации давления");
                bool stable = await WaitForStateInPointAsync();
                if (!stable) { SetStatus($"[Цикл {cycle}]: прервано"); return; }

                SetStatus($"[Цикл {cycle}] Выдерживаем максимальное давление");
                await SimpleCountdownAsync(300, "Выдерживаем максимальное давление",
                    $"   |   Цикл {cycle}/{totalCycles}");

                SetStatus($"[Цикл {cycle}] сброс (vent)");
                bool ventOk = await SendWithRetryAsync("set_mode=vent;", "ok;");
                if (!ventOk) return;

                SetStatus($"[Цикл {cycle}] ожидание снижения давления < {atmoThreshold}");
                bool lowOk = await WaitUntilPressureBelowAsync(atmoThreshold, 300_000);
                if (!lowOk) { SetStatus($"[Цикл {cycle}]: давление не снизилось"); return; }

                MinCheck.Text = "☑";

                SetStatus($"[Цикл {cycle}] Выдерживаем атмосферное давление");
                await SimpleCountdownAsync(300, "Выдерживаем атмосферное давление",
                    $"   |   Цикл {cycle}/{totalCycles}");
            }

            SetStatus($"«Жамкать» завершено ({totalCycles} циклов)");
        }

        // ─────────────────────────── ТЕСТ: ПЕЧЬ (Modbus TCP) ───────────────────────────

        private async void TestWriteFurnace_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                MessageBox.Show("Программа занята. Дождитесь завершения или нажмите СТОП.");
                return;
            }

            string input = InputDialog.Show(
                "Введите уставку температуры (°C):",
                "12.3");

            if (string.IsNullOrWhiteSpace(input))
                return;

            if (!float.TryParse(input.Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out float temp))
            {
                MessageBox.Show("Некорректное значение. Введите число, например 12.3");
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
                    _lastFurnaceSetPoint = temp;
                    SetStatusSuccess($"Уставка печи изменена на {temp} °C");
                }
                else
                {
                    SetStatusError($"Ошибка записи уставки печи ({temp} °C)");
                }
            }
            catch (Exception ex)
            {
                SetStatusError($"Ошибка: {ex.Message}");
            }
            finally
            {
                TestFurnaceButton.IsEnabled = true;
            }
        }

        private async Task<bool> WriteFurnaceSetPointAsync(
            float value, string ip, int port, ushort startRegister)
        {
            try
            {
                byte[] floatBytes = BitConverter.GetBytes(value);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(floatBytes);

                byte[] ordered = new[]
                {
                    floatBytes[2], floatBytes[3],
                    floatBytes[0], floatBytes[1]
                };

                System.Diagnostics.Debug.WriteLine(
                    $"[Furnace] Запись {value} °C (CDAB), байты: {BitConverter.ToString(ordered)}");

                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(3000);
                var completed = await Task.WhenAny(connectTask, timeoutTask);

                if (completed != connectTask)
                {
                    System.Diagnostics.Debug.WriteLine("[Furnace] Таймаут подключения");
                    return false;
                }
                await connectTask;

                using var stream = client.GetStream();
                stream.ReadTimeout = 2000;
                stream.WriteTimeout = 2000;

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
                buffer.Add(ordered[0]);
                buffer.Add(ordered[1]);
                buffer.Add(ordered[2]);
                buffer.Add(ordered[3]);

                byte[] requestBytes = buffer.ToArray();
                await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
                await stream.FlushAsync();

                byte[] response = new byte[12];
                int totalRead = 0;
                var start = DateTime.Now;

                while (totalRead < response.Length &&
                       (DateTime.Now - start).TotalMilliseconds < 2000)
                {
                    if (stream.DataAvailable)
                    {
                        int n = await stream.ReadAsync(
                            response, totalRead, response.Length - totalRead);
                        if (n == 0) break;
                        totalRead += n;
                    }
                    else
                    {
                        await Task.Delay(20);
                    }
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[Furnace] Получено {totalRead} байт: {BitConverter.ToString(response, 0, totalRead)}");

                if (totalRead >= 12 && response[7] == 0x10)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Furnace] OK: уставка {value} (CDAB) записана");
                    return true;
                }

                if (totalRead >= 9 && response[7] == 0x90)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Furnace] Modbus exception: код {response[8]:X2}");
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Furnace] Исключение: {ex.Message}");
                return false;
            }
        }

        private string FormatFurnaceSetPointFileName(float setPoint)
        {
            if (Math.Abs(setPoint - Math.Round(setPoint)) < 0.001f)
            {
                int intVal = (int)Math.Round(setPoint);
                if (intVal > 0)
                    return "+" + intVal + ".csv";
                return intVal + ".csv";
            }

            string s = setPoint.ToString("0.###", CultureInfo.InvariantCulture);
            if (setPoint > 0 && !s.StartsWith("+"))
                s = "+" + s;
            return s + ".csv";
        }

        // ─────────────────────────── СОХРАНЕНИЕ ДАННЫХ ───────────────────────────

        private async void SaveData_Click(object sender, RoutedEventArgs e)
        {
            string saveDir = SavePathBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(saveDir))
            {
                MessageBox.Show("Укажите путь сохранения в поле выше.",
                    "Путь не задан", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(saveDir))
            {
                var result = MessageBox.Show(
                    $"Папка не существует:\n{saveDir}\n\nСоздать?",
                    "Папка не найдена",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                try
                {
                    Directory.CreateDirectory(saveDir);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось создать папку: {ex.Message}",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            List<ConnectedClient> selected;
            lock (_clientsLock)
            {
                selected = _connectedClients
                    .Where(c => c.IsSelected && !c.IsOffline)
                    .ToList();
            }

            if (selected.Count == 0)
            {
                MessageBox.Show("Нет отмеченных клиентов.",
                    "Нет клиентов", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string fileName = FormatFurnaceSetPointFileName(_lastFurnaceSetPoint);
            string fullPath = Path.Combine(saveDir, fileName);

            var confirm = MessageBox.Show(
                $"Сохранить данные в файл:\n{fullPath}\n\nКлиентов: {selected.Count}",
                "Подтверждение сохранения",
                MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK) return;

            lock (_saveLock)
            {
                _saveResponses.Clear();
            }

            SaveDataButton.IsEnabled = false;
            try
            {
                byte[] message = Encoding.UTF8.GetBytes("SAVE_DATA " + fullPath + "\n");
                int sent = 0;
                foreach (var c in selected)
                {
                    try
                    {
                        c.SetStatusSaving();
                        var stream = c.TcpClient.GetStream();
                        await stream.WriteAsync(message, 0, message.Length);
                        await stream.FlushAsync();
                        sent++;
                        System.Diagnostics.Debug.WriteLine(
                            $"[Broadcast] SAVE_DATA отправлен клиенту {c.Sid}: {fullPath}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Broadcast] Не удалось отправить SAVE_DATA клиенту {c.Sid}: {ex.Message}");
                    }
                }

                SetStatus($"Команда сохранения отправлена {sent} клиентам. Ожидание ответа...", false);

                int timeoutMs = 30000;
                var start = DateTime.Now;
                int okCount = 0;
                int failCount = 0;

                while (true)
                {
                    if ((DateTime.Now - start).TotalMilliseconds > timeoutMs * selected.Count)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "[Broadcast] Таймаут ожидания SAVED_OK/SAVED_FAIL");
                        break;
                    }

                    lock (_saveLock)
                    {
                        okCount = 0;
                        failCount = 0;
                        foreach (var c in selected)
                        {
                            if (_saveResponses.Contains(c.Sid + ":OK")) okCount++;
                            if (_saveResponses.Contains(c.Sid + ":FAIL")) failCount++;
                        }

                        if (okCount + failCount >= selected.Count)
                            break;
                    }

                    SetStatus(
                        $"Сохранение: OK={okCount}, FAIL={failCount} из {selected.Count}...",
                        false);

                    await Task.Delay(200);
                }

                if (okCount == selected.Count)
                    SetStatusSuccess($"Данные сохранены во всех клиентах ({okCount}/{selected.Count}): {fileName}");
                else if (okCount > 0)
                    SetStatusWarning($"Сохранено в {okCount} из {selected.Count} клиентов (FAIL={failCount})");
                else
                    SetStatusError($"Не удалось сохранить данные (FAIL={failCount})");
            }
            catch (Exception ex)
            {
                SetStatusError($"Ошибка сохранения: {ex.Message}");
            }
            finally
            {
                SaveDataButton.IsEnabled = true;
            }
        }

        // ─────────────────────────── СТОП / СБРОС ───────────────────────────

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _stopRequested = true;
            _timerActive = false;
            TimerText.Text = "00:00:00";

            SetStatus("Отправка команды stop на прибор");
            await SendCommandNoWaitAsync("set_mode=stop;");
            SetStatus("АВАРИЙНАЯ ОСТАНОВКА");
        }

        private async void VentButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                var result = MessageBox.Show(
                    "Идёт выполнение программы. Прервать и сбросить давление?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;

                _stopRequested = true;
                _timerActive = false;
                TimerText.Text = "00:00:00";
            }

            SetStatus("Отправка команды сброса (vent)");
            string? response = await SendCommandAsync("set_mode=vent;");
            SetStatus($"Команда сброса отправлена, ответ: {response}");
        }

        // ─────────────────────────── ОТПРАВКА С ПОВТОРАМИ ───────────────────────────

        private async Task<bool> SendWithRetryAsync(string command, string expected)
        {
            while (true)
            {
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    if (_stopRequested) return false;

                    SetStatus($"{command} — попытка {attempt}/5");
                    string? response = await SendCommandAsync(command);

                    if (response != null && response.Contains(expected, StringComparison.Ordinal))
                        return true;

                    await SafeDelayAsync(1000);
                }

                var result = MessageBox.Show(
                    $"Команда «{command}» не получила ответ «{expected}» после 5 попыток.\n" +
                    "Устраните проблему и нажмите ОК для повтора.",
                    "Ошибка прибора",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.OK) return false;
            }
        }

        // ─────────────────────────── ОЖИДАНИЯ ───────────────────────────

        private async Task<double?> RefreshPressureAndSetPointAsync()
        {
            double? pressure = null;

            string? pressResponse = await SendCommandAsync("get_press;");
            if (pressResponse != null)
            {
                string parsed = ParsePressure(pressResponse);
                PressureText.Text = parsed;
                if (double.TryParse(parsed, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double p))
                    pressure = p;
            }

            string? setPointResponse = await SendCommandAsync("get_point;");
            if (setPointResponse != null)
                SetPointText.Text = ParsePressure(setPointResponse);

            return pressure;
        }

        private async Task<bool> WaitForStateInPointAsync()
        {
            int okCount = 0;
            const int requiredOk = 5;

            while (true)
            {
                if (_stopRequested) return false;

                string? response = await SendCommandAsync("get_state_in_point;");
                if (response != null && response.Contains("ok;", StringComparison.Ordinal))
                {
                    okCount++;
                    if (okCount >= requiredOk)
                        return true;
                }
                else
                {
                    okCount = 0;
                }

                await RefreshPressureAndSetPointAsync();

                SetStatus($"Ожидание стабилизации давления: {okCount}/{requiredOk}");

                await SafeDelayAsync(1000);
            }
        }

        private async Task<bool> WaitUntilPressureBelowAsync(double threshold, int timeoutMs)
        {
            var start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                if (_stopRequested) return false;

                double? pressure = await RefreshPressureAndSetPointAsync();

                if (pressure.HasValue && Math.Abs(pressure.Value) < threshold)
                    return true;

                await SafeDelayAsync(1000);
            }
            return false;
        }

        // ─────────────────────────── ТАЙМЕР ───────────────────────────

        private async Task SimpleCountdownAsync(int seconds, string statusPrefix, string suffix = "")
        {
            int remaining = seconds;
            _timerActive = true;
            UpdateTimerDisplay(remaining);

            while (remaining > 0)
            {
                if (_stopRequested) { _timerActive = false; return; }

                await RefreshPressureAndSetPointAsync();

                SetStatus($"{statusPrefix}: {remaining} сек{suffix}");
                await SafeDelayAsync(1000);
                remaining--;
                UpdateTimerDisplay(remaining);
            }

            _timerActive = false;
            UpdateTimerDisplay(0);
        }

        private async Task RunTimerAsync(int seconds, string suffix = "")
        {
            int remaining = seconds;
            _timerActive = true;
            UpdateTimerDisplay(remaining);
            bool firstOkReceived = false;

            while (remaining > 0)
            {
                if (_stopRequested) { _timerActive = false; return; }

                await RefreshPressureAndSetPointAsync();

                string? unitsResponse = await SendCommandAsync("get_units;");
                if (unitsResponse != null) UpdateUnits(unitsResponse);

                string? stateResponse = await SendCommandAsync("get_state_in_point;");

                if (stateResponse != null && stateResponse.Contains("ok;", StringComparison.Ordinal))
                {
                    firstOkReceived = true;
                    remaining--;
                    UpdateTimerDisplay(remaining);
                }
                else if (stateResponse != null && stateResponse.Contains("wait;", StringComparison.Ordinal))
                {
                    if (firstOkReceived)
                    {
                        remaining += 30;
                        SetStatus($"Срыв! Таймер +30 сек. Осталось: {remaining}{suffix}");
                        UpdateTimerDisplay(remaining);
                    }
                    else
                    {
                        SetStatus("Ожидание стабилизации давления...");
                    }

                    while (true)
                    {
                        if (_stopRequested) { _timerActive = false; return; }
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

                SetStatus($"До начала съёма данных: {remaining} сек{suffix}");
                await SafeDelayAsync(1000);
            }

            _timerActive = false;
            UpdateTimerDisplay(0);
        }

        private void UpdateTimerDisplay(int remainingSeconds)
        {
            int h = remainingSeconds / 3600;
            int m = (remainingSeconds % 3600) / 60;
            int s = remainingSeconds % 60;
            TimerText.Text = $"{h:D2}:{m:D2}:{s:D2}";
        }

        // ─────────────────────────── ВСПОМОГАТЕЛЬНЫЕ ───────────────────────────

        private string GetUnitsCode(string targetPressure)
        {
            if (targetPressure.Contains("кгс/см²")) return "kgs";
            if (targetPressure.Contains("МПа")) return "MPa";
            if (targetPressure.Contains("кПа")) return "kPa";
            if (targetPressure.Contains("бар")) return "bar";
            if (targetPressure.Contains("PSI")) return "psi";
            return "kgs";
        }

        private string GetUnitsLabel(string targetPressure)
        {
            if (targetPressure.Contains("кгс/см²")) return "кгс/см²";
            if (targetPressure.Contains("МПа")) return "МПа";
            if (targetPressure.Contains("кПа")) return "кПа";
            if (targetPressure.Contains("бар")) return "бар";
            if (targetPressure.Contains("PSI")) return "PSI";
            return "";
        }

        private double GetAtmosphereThreshold(string unitsCode)
        {
            return unitsCode switch
            {
                "kgs" => 1.0,
                "MPa" => 0.1,
                "kPa" => 100.0,
                "bar" => 1.0,
                "psi" => 14.7,
                _ => 1.0
            };
        }

        private int GetStepDuration(double[] steps, int index, bool longTimers)
        {
            if (index == 0) return 0;

            double current = steps[index];
            double previous = steps[index - 1];

            if (current == 0) return longTimers ? 500 : 300;
            if (current > previous) return longTimers ? 220 : 120;
            return longTimers ? 330 : 180;
        }

        // ─────────────────────────── БАЗОВЫЕ ОПЕРАЦИИ ───────────────────────────

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            StartBroadcastServer();

            _ = Task.Run(ConnectToDeviceLoopAsync);

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            await Task.CompletedTask;
        }

        private async Task ConnectToDeviceLoopAsync()
        {
            System.Diagnostics.Debug.WriteLine("[Device] Цикл подключения запущен");

            while (!_cts.IsCancellationRequested)
            {
                bool needConnect;
                lock (_streamLock)
                {
                    needConnect = (_stream == null);
                }

                if (!needConnect)
                {
                    await Task.Delay(1000);
                    continue;
                }

                try
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Device] Попытка подключения к {DeviceIp}:{DevicePort}...");

                    SetStatusWarning(
                        $"Пытаемся подключиться к прибору {DeviceIp}:{DevicePort}...");

                    var client = new TcpClient();
                    var connectTask = client.ConnectAsync(DeviceIp, DevicePort);
                    var timeoutTask = Task.Delay(3000);
                    var completed = await Task.WhenAny(connectTask, timeoutTask);

                    if (completed != connectTask)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "[Device] Таймаут подключения");
                        try { client.Close(); } catch { }
                        SetStatusError(
                            $"Прибор недоступен ({DeviceIp}:{DevicePort}). Повтор через 5 сек...");
                        await Task.Delay(5000);
                        continue;
                    }

                    await connectTask;

                    var stream = client.GetStream();

                    if (_cts.IsCancellationRequested)
                    {
                        try { stream.Close(); } catch { }
                        try { client.Close(); } catch { }
                        return;
                    }

                    lock (_streamLock)
                    {
                        _client = client;
                        _stream = stream;
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"[Device] Подключено к {DeviceIp}:{DevicePort}");
                    SetStatusSuccess($"Подключено к {DeviceIp}:{DevicePort}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Device] Ошибка подключения: {ex.GetType().Name}: {ex.Message}");
                    SetStatusError(
                        $"Прибор недоступен ({DeviceIp}:{DevicePort}). Повтор через 5 сек...");
                }

                await Task.Delay(5000);
            }

            System.Diagnostics.Debug.WriteLine("[Device] Цикл подключения завершён");
        }

        private void ResetDeviceConnection()
        {
            lock (_streamLock)
            {
                try { _stream?.Close(); } catch { }
                try { _client?.Close(); } catch { }
                _stream = null;
                _client = null;
            }
            System.Diagnostics.Debug.WriteLine(
                "[Device] Соединение сброшено, ждём переподключения...");
            SetStatusError("Соединение с прибором потеряно. Переподключение...");
        }

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            if (_isRunning) return;

            NetworkStream? stream;
            lock (_streamLock)
            {
                stream = _stream;
            }

            if (stream == null) return;

            try
            {
                if (!stream.CanRead || !stream.CanWrite)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[Device] Соединение потеряно (CanRead/CanWrite = false)");
                    ResetDeviceConnection();
                    return;
                }
            }
            catch
            {
                ResetDeviceConnection();
                return;
            }

            SetStatusSuccess($"Подключено к {DeviceIp}:{DevicePort}");

            string? unitsResponse = await SendCommandAsync("get_units;");
            if (unitsResponse != null) UpdateUnits(unitsResponse);

            string? pressResponse = await SendCommandAsync("get_press;");
            if (pressResponse != null) PressureText.Text = ParsePressure(pressResponse);

            string? setPointResponse = await SendCommandAsync("get_point;");
            if (setPointResponse != null) SetPointText.Text = ParsePressure(setPointResponse);
        }

        private void UpdateUnits(string response)
        {
            char? digit = null;
            foreach (char c in response)
            {
                if (c >= '0' && c <= '9') { digit = c; break; }
            }

            string units = digit switch
            {
                '0' => "кПа",
                '1' => "МПа",
                '2' => "бар",
                '3' => "PSI",
                '4' => "кгс/см²",
                _ => "?"
            };

            UnitsText.Text = units;
            SetPointUnitsText.Text = units;
        }

        private async Task<string?> SendCommandAsync(string command)
        {
            NetworkStream? stream;
            lock (_streamLock)
            {
                stream = _stream;
            }

            if (stream == null) return null;

            try
            {
                byte[] flush = new byte[1024];
                while (stream.DataAvailable)
                {
                    await stream.ReadAsync(flush, 0, flush.Length);
                    await Task.Delay(20);
                }

                byte[] request = Encoding.ASCII.GetBytes(command);
                await stream.WriteAsync(request, 0, request.Length);

                var sb = new StringBuilder();
                byte[] one = new byte[1];
                var start = DateTime.Now;

                while ((DateTime.Now - start).TotalMilliseconds < 2000)
                {
                    if (stream.DataAvailable)
                    {
                        int n = await stream.ReadAsync(one, 0, one.Length);
                        if (n == 0) break;

                        char c = (char)one[0];
                        sb.Append(c);

                        if (c == ';') break;
                    }
                    else
                    {
                        await Task.Delay(10);
                    }
                }

                if (sb.Length == 0) return null;
                return sb.ToString().Trim();
            }
            catch
            {
                ResetDeviceConnection();
                return null;
            }
        }

        private async Task SendCommandNoWaitAsync(string command)
        {
            NetworkStream? stream;
            lock (_streamLock)
            {
                stream = _stream;
            }

            if (stream == null) return;
            try
            {
                byte[] request = Encoding.ASCII.GetBytes(command);
                await stream.WriteAsync(request, 0, request.Length);
            }
            catch
            {
                ResetDeviceConnection();
            }
        }

        private string ParsePressure(string raw)
        {
            raw = raw.Trim().Trim('\r', '\n', '\0', ' ', ';');

            if (raw.StartsWith("E", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("e", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw.Substring(1);
            }

            raw = raw.Replace(',', '.');

            if (double.TryParse(raw,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value))
            {
                return Math.Round(value, 5).ToString("F5", CultureInfo.InvariantCulture);
            }

            return raw;
        }

        private async Task SafeDelayAsync(int milliseconds)
        {
            var end = DateTime.Now.AddMilliseconds(milliseconds);
            while (DateTime.Now < end)
            {
                if (_stopRequested) return;
                await Task.Delay(100);
            }
        }

        private void SetStatus(string text, bool running = true)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetStatus(text, running));
                return;
            }
            StatusText.Text = text;
            StatusText.Foreground = running ? Brushes.Green : Brushes.Gray;
        }

        private void SetStatusSuccess(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetStatusSuccess(text));
                return;
            }
            StatusText.Text = text;
            StatusText.Foreground = Brushes.Green;
        }

        private void SetStatusWarning(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetStatusWarning(text));
                return;
            }
            StatusText.Text = text;
            StatusText.Foreground = Brushes.Goldenrod;
        }

        private void SetStatusError(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetStatusError(text));
                return;
            }
            StatusText.Text = text;
            StatusText.Foreground = Brushes.OrangeRed;
        }

        protected override void OnClosed(EventArgs e)
        {
            _stopRequested = true;
            _timer?.Stop();
            _clientsUiTimer?.Stop();
            _cts.Cancel();

            lock (_streamLock)
            {
                try { _stream?.Close(); } catch { }
                try { _client?.Close(); } catch { }
                _stream = null;
                _client = null;
            }

            try
            {
                lock (_clientsLock)
                {
                    foreach (var c in _connectedClients)
                    {
                        try { c.TcpClient.Close(); } catch { }
                    }
                    _connectedClients.Clear();
                }

                try { _broadcastServer?.Stop(); } catch { }
            }
            catch { }

            base.OnClosed(e);

            Environment.Exit(0);
        }
    }

    // ─────────────────────────── ДИАЛОГ ВВОДА ───────────────────────────

    public static class InputDialog
    {
        public static string Show(string prompt, string defaultValue = "")
        {
            var window = new Window
            {
                Title = "Ввод значения",
                Width = 380,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(15)
            };
            grid.RowDefinitions.Add(
                new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(
                new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(
                new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var label = new System.Windows.Controls.TextBlock
            {
                Text = prompt,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            };
            System.Windows.Controls.Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var textBox = new System.Windows.Controls.TextBox
            {
                Text = defaultValue,
                Height = 30,
                FontSize = 14,
                Padding = new Thickness(6, 4, 6, 4)
            };
            System.Windows.Controls.Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            string result = null;

            var okButton = new System.Windows.Controls.Button
            {
                Content = "ОК",
                Width = 90,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true,
                FontSize = 14
            };
            okButton.Click += (s, e) =>
            {
                result = textBox.Text;
                window.Close();
            };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "Отмена",
                Width = 90,
                Height = 30,
                IsCancel = true,
                FontSize = 14
            };
            cancelButton.Click += (s, e) => { window.Close(); };
            buttonPanel.Children.Add(cancelButton);

            System.Windows.Controls.Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            window.Content = grid;

            window.Loaded += (s, e) =>
            {
                textBox.Focus();
                textBox.SelectAll();
            };

            window.ShowDialog();

            return result;
        }
    }
}