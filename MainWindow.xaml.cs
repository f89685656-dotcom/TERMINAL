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

        private readonly bool _uiLoaded = false;

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

        private readonly DateTime _lastAgkStatusRequest = DateTime.MinValue;
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
            RunExcelMacroInternal(silent: false);
        }




        /// <summary>
        /// Возвращает уставку (температуру) текущего активного задания пакета.
        /// Например, "80" для задания «80 °C».
        /// Если пакет не запущен или активного задания нет — возвращает "".
        /// </summary>
        private string GetCurrentBatchSetPoint()
        {
            if (!_batchRunning) return "";

            for (int i = 0; i < _batchJobs.Count; i++)
            {
                var job = _batchJobs[i];
                string st = job.StatusText ?? "";
                if (st.Contains("запись") || st.Contains("стабилизация") ||
                    st.Contains("проверка") || st.Contains("Идёт съём") ||
                    st.Contains("завершено"))
                {
                    // Извлекаем целое число из SetPointText ("80" или "80.5" → "80")
                    string spText = job.SetPointText ?? "";
                    spText = spText.Trim().TrimStart('+').Replace(',', '.');
                    if (spText.Contains('.')) spText = spText.Split('.')[0];
                    return spText;
                }
            }

            return "";
        }
        /// <summary>
        /// Основная логика запуска Excel-макросов.
        /// </summary>
        /// <param name="silent">
        /// true — тихий режим (автозапуск): ошибки только в журнал, без MessageBox.
        /// false — обычный режим (кнопка): ошибки показываются MessageBox.
        /// </param>
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
            // Сначала пробуем из активного задания пакета (надёжнее).
            string setpoint = GetCurrentBatchSetPoint();

            // Если пакет не запущен — берём из печи.
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
                // Отрицательные температуры: имя без плюса → "-40.csv"
                candidates = new[]
                {
            Path.Combine(dataFolder, setpoint + ".csv"),
        };
            }
            else
            {
                // Положительные: сначала с плюсом, потом без
                candidates = new[]
                {
            Path.Combine(dataFolder, "+" + setpoint + ".csv"),
            Path.Combine(dataFolder, setpoint + ".csv"),
        };
            }

            string? csvPath = candidates.FirstOrDefault(File.Exists);

            if (csvPath == null)
            {
                // Fallback: самый свежий CSV в папке
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

            // ─── 4. Количество датчиков (Точка) для первого макроса ────────
            int sensorCount = GetSelectedClientCase();
            if (sensorCount < 1 || sensorCount > 10)
            {
                string msg = "Количество датчиков (Точка) должно быть от 1 до 10.\n\n" +
                             "Отметьте клиента галочкой и введите Точку рядом с SID.";
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
                      $"(Точка={sensorCount}, CASE={caseForCopy})", Brushes.Blue);

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
                xl.Visible = false;     // ← true для отладки; в релизе false
                xl.DisplayAlerts = false;

                // Создаём временную книгу — обход бага с AddIns
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

                // Открываем CSV
                csvBook = xl.Workbooks.Open(csvPath);
                dynamic wb = csvBook;
                wb.Activate();

                // ─── Пишем параметры в скрытые ячейки Z1, Z2, Z3 ─────────
                dynamic sheet = wb.ActiveSheet;
                sheet.Range("Z1").Value = sensorCount;
                sheet.Range("Z2").Value = caseForCopy;
                sheet.Range("Z3").Value = parentFolder;

                // ─── Макрос 1: Фильтр КОСА ────────────────────────────────
                AppendLog("Excel: запуск макроса «Фильтр КОСА»", Brushes.Blue);
                xl.Run("Filter_KOSA_GUI_Auto");
                AppendLog("Excel: макрос «Фильтр КОСА» выполнен", Brushes.Green);

                // ─── ВАЖНО: первый макрос делает SaveAs,
                //     активная книга меняется — пишем Z1/Z2/Z3 ЗАНОВО ────
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

                // ─── Макрос 2: Universal Insert ───────────────────────────
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

                // Закрываем CSV
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
        /// Возвращает CASE текущего активного задания пакета.
        /// Берёт значение из поля CaseText активного задания.
        /// Если пакет не запущен или активного нет — возвращает 0.
        /// </summary>
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
                    return i + 1;   // fallback — реальный номер задания
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

    }
}