using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TERMINAL.DeviceHub
{
    public partial class PollingTabView : UserControl
    {
        private PollingEngine _engine = new PollingEngine();
        private DispatcherTimer _uiQueueTimer;
        private PlotModel _plotModel;
        private Dictionary<ChannelNode, LineSeries> _seriesMap = new Dictionary<ChannelNode, LineSeries>();
        private DateTime _lastPlotUpdate = DateTime.MinValue;
        public ObservableCollection<InterfaceNode> Interfaces { get; set; } = new ObservableCollection<InterfaceNode>();
        private bool _isScanning = false;
        private CancellationTokenSource _scanCts;
        private bool _isRs485Enabled = true;
        private List<PortSetting> _rs485Ports = new List<PortSetting>
        {
            new PortSetting { PortName = "COM9", BaudRate = 57600 }
        };
        private bool _isI2CEnabled = true;

        private bool _isRecording = false;

        // ★ Текущий выбранный канал для графика ★
        private ChannelNode _selectedChannelForPlot = null;

        public PollingTabView()
        {
            InitializeComponent();
            DataContext = this;

            InitializePlot();

            _engine.ScanProgressChanged += (port, current, total) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (port == "RS-485")
                    {
                        ScanProgressBarRS485.Maximum = total;
                        ScanProgressBarRS485.Value = current;
                        ScanProgressTextRS485.Text = $"RS-485: {current}/{total}";
                    }
                    else if (port == "I2C")
                    {
                        ScanProgressBarI2C.Maximum = total;
                        ScanProgressBarI2C.Value = current;
                        ScanProgressTextI2C.Text = $"I2C: {current}/{total}";
                    }
                });
            };

            _engine.DeviceFound += (dev) =>
            {
                Dispatcher.Invoke(() =>
                {
                    var rs485Iface = Interfaces.FirstOrDefault(i => i.Name.Contains("RS-485"));
                    var i2cIface = Interfaces.FirstOrDefault(i => i.Name.Contains("I2C"));

                    if (dev.PortName == "I2C" && i2cIface != null)
                    {
                        if (!i2cIface.Devices.Any(d => d.Address == dev.Address))
                        {
                            i2cIface.Devices.Add(dev);
                            LogListView.Items.Add($"[Scan] I2C устройство {dev.Name} добавлено в дерево.");
                        }
                    }
                    else if (rs485Iface != null)
                    {
                        if (!rs485Iface.Devices.Any(d => d.Address == dev.Address && d.PortName == dev.PortName))
                        {
                            rs485Iface.Devices.Add(dev);
                            LogListView.Items.Add($"[Scan] RS-485 устройство {dev.Name} добавлено в дерево.");
                        }
                    }
                });
            };

            _uiQueueTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _uiQueueTimer.Tick += UiQueueTimer_Tick;
            _uiQueueTimer.Start();

            UpdateStatusText();
        }        // ★ ОБРАБОТЧИК КЛИКА ПО КАНАЛУ В ДЕРЕВЕ (ДЛЯ ВЫБОРА ГРАФИКА) ★
        private void ChannelRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ChannelNode channel)
            {
                // Снимаем выделение со всех каналов
                foreach (var iface in Interfaces)
                    foreach (var dev in iface.Devices)
                        foreach (var ch in dev.Channels)
                            ch.IsVisibleOnPlot = false;

                // Выделяем текущий
                channel.IsVisibleOnPlot = true;
                _selectedChannelForPlot = channel;

                // ★ МЕНЯЕМ ЗАГОЛОВОК ГРАФИКА ★
                _plotModel.Title = channel.Name;

                // ★ УМНАЯ ПОДПИСЬ ОСИ Y ★
                string yAxisTitle = "Значение";
                if (channel.Name.Contains("Частота давления")) yAxisTitle = "Fp, Гц";
                else if (channel.Name.Contains("Частота температуры")) yAxisTitle = "Ft, Гц";
                else if (channel.Name.Contains("Давление")) yAxisTitle = "P, кгс/см²";
                else if (channel.Name.Contains("Температура процессора")) yAxisTitle = "t, °C";
                else if (channel.Name.Contains("Температура")) yAxisTitle = "t, °C";
                else if (channel.Name.Contains("Напряжение питания")) yAxisTitle = "U, В";

                var yAxis = _plotModel.Axes.FirstOrDefault(a => a.Position == AxisPosition.Left);
                if (yAxis != null)
                    yAxis.Title = yAxisTitle;

                _plotModel.InvalidatePlot(true);

                // Обновляем видимость серий
                UpdatePlotVisibility();
            }
        }        // ★ ОБНОВЛЕНИЕ ВИДИМОСТИ СЕРИЙ НА ГРАФИКЕ ★
        private void UpdatePlotVisibility()
        {
            foreach (var kvp in _seriesMap)
            {
                var channel = kvp.Key;
                var series = kvp.Value;
                series.IsVisible = (channel == _selectedChannelForPlot);
            }
            _plotModel.InvalidatePlot(true);
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRecording)
            {
                if (!Interfaces.Any(i => i.Devices.Any(d => d.IsSelected)))
                {
                    LogListView.Items.Add("Нет выбранных устройств для опроса. Сначала выполните поиск.");
                    return;
                }

                // ★ ПРЕРЫВАЕМ ПОИСК, ЕСЛИ ОН ИДЁТ ★
                if (_isScanning)
                {
                    _scanCts?.Cancel();
                    _scanCts?.Dispose();
                    _scanCts = null;
                    _isScanning = false;
                    ScanButtonIcon.Text = "\uE721";
                    ScanButton.ToolTip = "Поиск";
                    LogListView.Items.Add("Поиск прерван (запущен опрос).");
                }

                // ★ СБРАСЫВАЕМ ФЛАГ IsValueSet У ВСЕХ КАНАЛОВ ★
                foreach (var iface in Interfaces)
                {
                    foreach (var dev in iface.Devices)
                    {
                        foreach (var ch in dev.Channels)
                        {
                            ch.IsValueSet = false;
                        }
                    }
                }

                // ★ ПЕРЕВОДИМ СЕКУНДЫ В МИЛЛИСЕКУНДЫ ★
                int intervalMs = (int)(IntervalSlider.Value * 1000);
                _engine.StartPolling(Interfaces, _isRs485Enabled, _rs485Ports, _isI2CEnabled, intervalMs);
                _isRecording = true;

                RecordIcon.Text = "\uE71A";
                RecordIcon.Foreground = new SolidColorBrush(Colors.Red);
                LogListView.Items.Add($"Запись графика запущена. Период: {intervalMs} мс");
            }
            else
            {
                _engine.Stop();
                _isRecording = false;

                RecordIcon.Text = "\uE768";
                RecordIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4CAF50"));
                LogListView.Items.Add("Запись графика остановлена.");
            }
        }
        private void CreateSeriesForAllChannels()
        {
            _plotModel.Series.Clear();
            _seriesMap.Clear();

            var colors = new[] { OxyColors.Red, OxyColors.DeepSkyBlue, OxyColors.Lime, OxyColors.Orange, OxyColors.Yellow, OxyColors.Purple };
            int colorIndex = 0;

            foreach (var iface in Interfaces)
            {
                foreach (var dev in iface.Devices)
                {
                    foreach (var ch in dev.Channels)
                    {
                        ch.PropertyChanged -= Channel_PropertyChanged;
                        ch.PropertyChanged += Channel_PropertyChanged;

                        var series = new LineSeries
                        {
                            Title = $"{dev.Name} - {ch.Name}",
                            Color = colors[colorIndex % colors.Length],
                            StrokeThickness = 2,
                            IsVisible = false // По умолчанию скрыта
                        };
                        _plotModel.Series.Add(series);
                        _seriesMap[ch] = series;
                        colorIndex++;
                    }
                }
            }
            _plotModel.InvalidatePlot(true);
        }


        private void UiQueueTimer_Tick(object sender, EventArgs e)
        {
            // 1. Применяем обновления UI
            while (_engine.UiUpdates.TryDequeue(out var action))
            {
                action.Invoke();
            }

            // 2. Логи
            while (_engine.LogQueue.TryDequeue(out var msg))
            {
                LogListView.Items.Add(msg);
                if (LogListView.Items.Count > 0)
                    LogListView.ScrollIntoView(LogListView.Items[LogListView.Items.Count - 1]);
            }

            // 3. График (раз в intervalMs)
            if (!_isRecording) return;

            int intervalMs = (int)(IntervalSlider.Value * 1000);
            if ((DateTime.Now - _lastPlotUpdate).TotalMilliseconds < intervalMs)
                return;

            _lastPlotUpdate = DateTime.Now;

            bool needRedraw = false;
            foreach (var iface in Interfaces)
            {
                foreach (var dev in iface.Devices)
                {
                    foreach (var ch in dev.Channels)
                    {
                        if (!ch.IsSelected) continue;

                        // ★ ОБНОВЛЯЕМ ch.Value ИЗ ch.LastValue ★
                        ch.Value = ch.LastValue;

                        if (_seriesMap.TryGetValue(ch, out var series))
                        {
                            if (!ch.IsValueSet) continue;

                            series.Points.Add(new DataPoint(DateTimeAxis.ToDouble(DateTime.Now), ch.Value));
                            if (series.Points.Count > 1000) series.Points.RemoveAt(0);
                            needRedraw = true;
                        }
                    }
                }
            }

            if (needRedraw)
            {
                _plotModel.InvalidatePlot(true);
            }
        }
        private void IntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IntervalTextBox != null)
            {
                IntervalTextBox.Text = e.NewValue.ToString("F2"); // ★ Обновляем TextBox ★
            }
        }

        private void IntervalTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (double.TryParse(IntervalTextBox.Text, out double value))
            {
                if (value >= IntervalSlider.Minimum && value <= IntervalSlider.Maximum)
                {
                    IntervalSlider.Value = value; // ★ Обновляем Slider ★
                }
            }
        }

        private void UpdateStatusText()
        {
            var rs485Str = _isRs485Enabled
                ? string.Join(", ", _rs485Ports.Select(p => $"{p.PortName}@{p.BaudRate}"))
                : "Выкл";

            // ★ ОБНОВЛЯЕМ СТАТУС С НОВЫМИ НАЗВАНИЯМИ ★
            StatusText.Text = $"RS-485 (Ц1): {rs485Str} | I2C (Ц4): {(_isI2CEnabled ? "Вкл" : "Выкл")}";
        }

        private void InitializePlot()
        {
            _plotModel = new PlotModel
            {
                Title = "Показания датчиков",
                Background = OxyColors.White,
                TitleColor = OxyColors.Black,
                DefaultFont = "Times New Roman",
                TitleFont = "Times New Roman",
                TitleFontSize = 16,
                TitleFontWeight = OxyPlot.FontWeights.Bold // ★ double, а не FontWeight ★
            };

            _plotModel.Axes.Add(new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "HH:mm:ss",
                Title = "Время",
                TextColor = OxyColors.Black,
                TitleColor = OxyColors.Black,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColors.LightGray,
                MinorGridlineColor = OxyColors.Gainsboro,
                Font = "Times New Roman",
                TitleFont = "Times New Roman",
                TitleFontSize = 12,
                AxisTitleDistance = 15 // ★ Только это, без TitlePadding ★
            });

            _plotModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Значение",
                TextColor = OxyColors.Black,
                TitleColor = OxyColors.Black,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColors.LightGray,
                MinorGridlineColor = OxyColors.Gainsboro,
                Font = "Times New Roman",
                TitleFont = "Times New Roman",
                TitleFontSize = 12,
                AxisTitleDistance = 25 // ★ Отодвигаем заголовок от чисел ★
            });

            PlotView.Model = _plotModel;
        }
        private void Channel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChannelNode.IsVisibleOnPlot))
            {
                var channel = (ChannelNode)sender;
                if (_seriesMap.TryGetValue(channel, out var series))
                {
                    series.IsVisible = channel.IsVisibleOnPlot;
                    _plotModel.InvalidatePlot(false);
                }
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new HubSettingsWindow { Owner = Window.GetWindow(this) };

            if (settingsWindow.ShowDialog() == true)
            {
                _isRs485Enabled = settingsWindow.IsRs485Enabled;
                _rs485Ports = settingsWindow.SelectedPortsWithBaud;
                _isI2CEnabled = settingsWindow.IsI2CEnabled;

                UpdateStatusText();
                LogListView.Items.Add($"Настройки: Порты={string.Join(", ", _rs485Ports.Select(p => $"{p.PortName}@{p.BaudRate}"))}");
            }
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            // ★ ЕСЛИ ИДЁТ ЗАПИСЬ — ОСТАНАВЛИВАЕМ ОПРОС ★
            if (_isRecording)
            {
                _engine.Stop();
                _isRecording = false;
                RecordIcon.Text = "\uE768";
                RecordIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4CAF50"));
                LogListView.Items.Add("Опрос остановлен (запущен поиск).");
            }

            if (_isScanning)
            {
                _engine.Stop(); // на всякий случай
                _scanCts?.Cancel();
                _scanCts?.Dispose();
                _scanCts = null;
                _isScanning = false;
                ScanButtonIcon.Text = "\uE721";
                ScanButton.ToolTip = "Поиск";
                LogListView.Items.Add("Поиск отменён.");
                return;
            }

            _isScanning = true;
            _scanCts = new CancellationTokenSource();
            ScanButtonIcon.Text = "\uE711";
            ScanButton.ToolTip = "Отмена";
            LogListView.Items.Add("Запуск параллельного сканирования...");

            ScanProgressBarRS485.Visibility = Visibility.Visible;
            ScanProgressTextRS485.Visibility = Visibility.Visible;
            ScanProgressBarI2C.Visibility = Visibility.Visible;
            ScanProgressTextI2C.Visibility = Visibility.Visible;
            ScanProgressBarRS485.Value = 0;
            ScanProgressTextRS485.Text = "";
            ScanProgressBarI2C.Value = 0;
            ScanProgressTextI2C.Text = "";

            try
            {
                Interfaces.Clear();
                if (_isRs485Enabled)
                    Interfaces.Add(new InterfaceNode { Name = "RS-485 (Ц1)" });
                if (_isI2CEnabled)
                    Interfaces.Add(new InterfaceNode { Name = "I2C (Ц4)" });

                _plotModel.Series.Clear();
                _seriesMap.Clear();
                _plotModel.InvalidatePlot(true);

                var foundDevices = await _engine.ScanAllAsync(
                    Interfaces, _isRs485Enabled, _rs485Ports, _isI2CEnabled, _scanCts.Token);

                LogListView.Items.Add($"[Scan] Найдено устройств: {foundDevices.Count}");

                var rs485Iface = Interfaces.FirstOrDefault(i => i.Name.Contains("RS-485"));
                var i2cIface = Interfaces.FirstOrDefault(i => i.Name.Contains("I2C"));

                foreach (var dev in foundDevices)
                {
                    if (dev.PortName == "I2C" && i2cIface != null)
                    {
                        if (!i2cIface.Devices.Any(d => d.Address == dev.Address))
                        {
                            i2cIface.Devices.Add(dev);
                            LogListView.Items.Add($"[Scan] I2C устройство {dev.Name} добавлено в дерево.");
                        }
                    }
                    else if (rs485Iface != null)
                    {
                        if (!rs485Iface.Devices.Any(d => d.Address == dev.Address && d.PortName == dev.PortName))
                        {
                            rs485Iface.Devices.Add(dev);
                            LogListView.Items.Add($"[Scan] RS-485 устройство {dev.Name} добавлено в дерево.");
                        }
                    }
                }

                var colors = new[] { OxyColors.Red, OxyColors.DeepSkyBlue, OxyColors.Lime, OxyColors.Orange, OxyColors.Yellow, OxyColors.Purple };
                int colorIndex = 0;
                int createdCount = 0;

                foreach (var iface in Interfaces)
                {
                    foreach (var dev in iface.Devices)
                    {
                        foreach (var ch in dev.Channels)
                        {
                            ch.PropertyChanged -= Channel_PropertyChanged;
                            ch.PropertyChanged += Channel_PropertyChanged;

                            var series = new LineSeries
                            {
                                Title = $"{dev.Name} - {ch.Name}",
                                Color = colors[colorIndex % colors.Length],
                                StrokeThickness = 2,
                                IsVisible = false
                            };
                            _plotModel.Series.Add(series);
                            _seriesMap[ch] = series;
                            colorIndex++;
                            createdCount++;
                        }
                    }
                }

                LogListView.Items.Add($"[Scan] Создано серий для графика: {createdCount}");
                _plotModel.InvalidatePlot(true);
                LogListView.Items.Add("Сканирование завершено.");
            }
            catch (OperationCanceledException)
            {
                LogListView.Items.Add("Поиск отменён.");
            }
            catch (Exception ex)
            {
                LogListView.Items.Add($"Ошибка при сканировании: {ex.Message}");
            }
            finally
            {
                _isScanning = false;
                _scanCts?.Dispose();
                _scanCts = null;
                ScanButtonIcon.Text = "\uE721";
                ScanButton.ToolTip = "Поиск";

                ScanProgressBarRS485.Visibility = Visibility.Collapsed;
                ScanProgressTextRS485.Visibility = Visibility.Collapsed;
                ScanProgressBarI2C.Visibility = Visibility.Collapsed;
                ScanProgressTextI2C.Visibility = Visibility.Collapsed;
                ScanProgressBarRS485.Value = 0;
                ScanProgressTextRS485.Text = "";
                ScanProgressBarI2C.Value = 0;
                ScanProgressTextI2C.Text = "";
            }
        }
        public void ClosePort()
        {
            _engine.ClosePort();
        }

        private void CopyLogMenuItem_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedLogs();
        }

        private void CopyLogCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            CopySelectedLogs();
        }

        private void CopySelectedLogs()
        {
            if (LogListView.SelectedItems.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            foreach (var item in LogListView.SelectedItems)
            {
                sb.AppendLine(item.ToString());
            }

            try
            {
                Clipboard.SetText(sb.ToString());
            }
            catch { }
        }

        public void SetPollingInterval(double seconds)
        {
            if (seconds >= IntervalSlider.Minimum && seconds <= IntervalSlider.Maximum)
            {
                IntervalSlider.Value = seconds;
                IntervalTextBox.Text = seconds.ToString("F2");
            }
        }

        public double GetPollingInterval()
        {
            return IntervalSlider.Value;
        }
    }
}