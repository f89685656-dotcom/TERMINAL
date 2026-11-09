using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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

        public ObservableCollection<ScanProgressInfo> ScanProgresses { get; set; } = new();

        private PlotModel _plotModelRealTime;
        private Dictionary<ChannelNode, LineSeries> _seriesMapRealTime = new Dictionary<ChannelNode, LineSeries>();
        private DateTime _lastPlotUpdate = DateTime.MinValue;

        public ObservableCollection<InterfaceNode> Interfaces { get; set; } = new ObservableCollection<InterfaceNode>();

        private bool _isScanning = false;
        private CancellationTokenSource _scanCts;

        private bool _isRs485Enabled = true;
        private List<PortSetting> _rs485Ports = new List<PortSetting> { new PortSetting { PortName = "COM9", BaudRate = 57600 } };

        // ★ ТЕПЕРЬ ХРАНИМ СПИСОК ID ВЫБРАННЫХ ПЕРЕХОДНИКОВ I2C (А НЕ ПРОСТО ФЛАГ)
        private List<string> _selectedI2CDevices = new List<string> { "FT260 [7&5fcd268&0&0000]" };

        private bool _isRecording = false;
        private ChannelNode _selectedChannelForPlot = null;
        private double _historyMinTime = 0;
        private double _historyMaxTime = 0;

        public PollingTabView()
        {
            InitializeComponent();
            DataContext = this;
            InitializePlot();

            // ★ Привязываем ItemsControl к коллекции
            ScanProgressList.ItemsSource = ScanProgresses;

            // ★ Обновленный обработчик прогресса (поддерживает несколько портов одновременно)
            _engine.ScanProgressChanged += (progressInfo) => Dispatcher.Invoke(() =>
            {
                var existing = ScanProgresses.FirstOrDefault(p => p.PortName == progressInfo.PortName);
                if (existing == null)
                {
                    ScanProgresses.Add(progressInfo);
                }
                else
                {
                    existing.Current = progressInfo.Current;
                    existing.Total = progressInfo.Total;
                    existing.StatusText = progressInfo.StatusText;
                }
            });

            _engine.DeviceFound += (dev) => Dispatcher.Invoke(() =>
            {
                var rs485Iface = Interfaces.FirstOrDefault(i => i.Name.Contains("RS-485"));
                var i2cIface = Interfaces.FirstOrDefault(i => i.Name.Contains("I2C"));

                // ★ Проверяем, что PortName начинается с "I2C" (так как теперь он может быть "I2C_FT260...")
                if (dev.PortName.StartsWith("I2C"))
                {
                    if (i2cIface != null && !i2cIface.Devices.Any(d => d.Address == dev.Address && d.PortName == dev.PortName))
                    {
                        dev.ParentInterface = i2cIface;
                        i2cIface.Devices.Add(dev);
                    }
                }
                else
                {
                    if (rs485Iface != null && !rs485Iface.Devices.Any(d => d.Address == dev.Address && d.PortName == dev.PortName))
                    {
                        dev.ParentInterface = rs485Iface;
                        rs485Iface.Devices.Add(dev);
                    }
                }
            });

            _uiQueueTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _uiQueueTimer.Tick += UiQueueTimer_Tick;
            _uiQueueTimer.Start();
            UpdateStatusText();
        }

        // ================= ОБРАБОТЧИКИ ИНТЕРФЕЙСА =================

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tabControl && tabControl == MainTabControl)
            {
                if (RecordButton == null || RefreshHistoryButton == null || ClearHistoryButton == null) return;

                if (tabControl.SelectedIndex == 0) // "Реальное время"
                {
                    RecordButton.Visibility = Visibility.Visible;
                    RefreshHistoryButton.Visibility = Visibility.Collapsed;
                    ClearHistoryButton.Visibility = Visibility.Collapsed;

                    HistoryItemsControl.ItemsSource = null;
                    foreach (var iface in Interfaces)
                        foreach (var dev in iface.Devices)
                            foreach (var ch in dev.Channels)
                                ch.ChartModel = null;

                    if (_plotModelRealTime != null) _plotModelRealTime.InvalidatePlot(true);
                }
                else if (tabControl.SelectedIndex == 1) // "История"
                {
                    RecordButton.Visibility = Visibility.Collapsed;
                    RefreshHistoryButton.Visibility = Visibility.Visible;
                    ClearHistoryButton.Visibility = Visibility.Visible;
                    UpdateHistoryList();
                }
            }
        }

        private void ChannelRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ChannelNode channel)
            {
                foreach (var iface in Interfaces)
                    foreach (var dev in iface.Devices)
                        foreach (var ch in dev.Channels)
                            ch.IsVisibleOnPlot = false;

                channel.IsVisibleOnPlot = true;
                _selectedChannelForPlot = channel;

                string channelName = channel.Name;
                string devInfo = channel.ParentDevice?.DisplayName?.Replace("[", "").Replace("]", "") ?? "?";
                string address = channel.ParentDevice?.Address ?? "?";

                string ifaceName = channel.ParentInterface?.Name;
                if (string.IsNullOrEmpty(ifaceName))
                {
                    var parentIface = Interfaces.FirstOrDefault(i => i.Devices.Contains(channel.ParentDevice));
                    ifaceName = parentIface?.Name ?? "?";
                }

                if (devInfo.StartsWith("I2C|"))
                    devInfo = devInfo.Replace("I2C|", "FT260|");

                _plotModelRealTime.Title = $"{channelName}[адрес: {address}|{devInfo}|{ifaceName}]";

                string yAxisTitle = "Значение";
                if (channelName.Contains("Частота давления")) yAxisTitle = "Fp, Гц";
                else if (channelName.Contains("Частота температуры")) yAxisTitle = "Ft, Гц";
                else if (channelName.Contains("Давление")) yAxisTitle = "P, кгс/см²";
                else if (channelName.Contains("Температура процессора")) yAxisTitle = "t, °C";
                else if (channelName.Contains("Температура")) yAxisTitle = "t, °C";
                else if (channelName.Contains("Напряжение питания")) yAxisTitle = "U, В";

                var yAxisRT = _plotModelRealTime.Axes.FirstOrDefault(a => a.Position == AxisPosition.Left) as LinearAxis;
                if (yAxisRT != null)
                {
                    yAxisRT.Title = yAxisTitle;
                    yAxisRT.StringFormat = channel.IsInt32 ? "0" : "0.000";
                }

                _plotModelRealTime.InvalidatePlot(true);
                UpdatePlotVisibility();
            }
        }

        private void MiniPlot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element && element.DataContext is ChannelNode channel)
            {
                var historyWindow = new Window
                {
                    Title = $"История: {channel.FullName}",
                    Width = 900,
                    Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    Background = System.Windows.Media.Brushes.White
                };

                var newPlotModel = new PlotModel
                {
                    Title = channel.FullName,
                    Background = OxyColors.White,
                    DefaultFont = "Times New Roman",
                    TitleFontSize = 18,
                    TitleFontWeight = OxyPlot.FontWeights.Bold
                };

                string yAxisFormat = channel.IsInt32 ? "0" : "0.000";

                newPlotModel.Axes.Add(new LinearAxis { Position = AxisPosition.Left, MajorGridlineStyle = LineStyle.Solid, MajorGridlineColor = OxyColors.LightGray, Title = channel.Unit, FontSize = 12, StringFormat = yAxisFormat, AxisTitleDistance = 30 });
                newPlotModel.Axes.Add(new DateTimeAxis { Position = AxisPosition.Bottom, StringFormat = "HH:mm:ss", MajorGridlineStyle = LineStyle.Solid, MajorGridlineColor = OxyColors.LightGray, Title = "Время", FontSize = 12, AxisTitleDistance = 15 });

                var destSeries = new LineSeries { Color = OxyColors.DeepSkyBlue, StrokeThickness = 2.5, TrackerFormatString = $"{channel.FullName}\nВремя: {{2:HH:mm:ss}}\nЗначение: {{4:{yAxisFormat}}}" };
                foreach (var pt in channel.HistoryPoints) destSeries.Points.Add(pt);
                newPlotModel.Series.Add(destSeries);

                historyWindow.Content = new OxyPlot.Wpf.PlotView { Model = newPlotModel };
                historyWindow.Show();
                e.Handled = true;
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateHistoryList();
            LogListView.Items.Add("[UI] Список графиков истории обновлён.");
        }

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Вы уверены, что хотите удалить всю накопленную историю графиков?", "Очистка истории", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                foreach (var iface in Interfaces)
                    foreach (var dev in iface.Devices)
                        foreach (var ch in dev.Channels)
                            ch.HistoryPoints.Clear();

                UpdateHistoryList();
                ResetHistorySliders();
                LogListView.Items.Add("[UI] История графиков полностью очищена.");
            }
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRecording)
            {
                if (!Interfaces.Any(i => i.Devices.Any(d => d.IsSelected != false))) { LogListView.Items.Add("Нет выбранных устройств."); return; }
                if (_isScanning) { _scanCts?.Cancel(); _scanCts?.Dispose(); _scanCts = null; _isScanning = false; ScanButtonIcon.Text = "\uE721"; ScanButton.ToolTip = "Поиск"; }

                foreach (var iface in Interfaces)
                    foreach (var dev in iface.Devices)
                        foreach (var ch in dev.Channels)
                            ch.ResetValueState();

                int intervalMs = (int)(IntervalSlider.Value * 1000);
                if (intervalMs < 50) intervalMs = 50;

                // ★ Передаем список выбранных I2C-переходников вместо bool
                _engine.StartPolling(Interfaces, _isRs485Enabled, _rs485Ports, _selectedI2CDevices, intervalMs);
                _isRecording = true;
                RecordIcon.Text = "\uE71A";
                RecordIcon.Foreground = new SolidColorBrush(Colors.Red);
                LogListView.Items.Add($"Запись графика запущена. Период: {intervalMs} мс");
                ResetHistorySliders();
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

        private void IntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IntervalTextBox != null) IntervalTextBox.Text = e.NewValue.ToString("F2");
        }

        private void IntervalTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (double.TryParse(IntervalTextBox.Text, out double value) && value >= IntervalSlider.Minimum && value <= IntervalSlider.Maximum)
                IntervalSlider.Value = value;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new HubSettingsWindow { Owner = Window.GetWindow(this) };
            if (settingsWindow.ShowDialog() == true)
            {
                _isRs485Enabled = settingsWindow.IsRs485Enabled;
                _rs485Ports = settingsWindow.SelectedPortsWithBaud;

                // ★ Получаем список выбранных I2C-переходников
                _selectedI2CDevices = settingsWindow.SelectedI2CDevices;

                UpdateStatusText();
                LogListView.Items.Add($"Настройки: Порты={string.Join(", ", _rs485Ports.Select(p => $"{p.PortName}@{p.BaudRate}"))} | I2C={string.Join(", ", _selectedI2CDevices)}");
            }
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
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
                _engine.Stop();
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

            ScanProgresses.Clear();

            try
            {
                Interfaces.Clear();
                if (_isRs485Enabled) Interfaces.Add(new InterfaceNode { Name = "RS-485 (Ц1)" });
                if (_selectedI2CDevices.Count > 0) Interfaces.Add(new InterfaceNode { Name = "I2C (Ц4)" });

                _plotModelRealTime.Series.Clear();
                _seriesMapRealTime.Clear();
                _plotModelRealTime.InvalidatePlot(true);
                HistoryItemsControl.ItemsSource = null;

                var foundDevices = await _engine.ScanAllAsync(Interfaces, _isRs485Enabled, _rs485Ports, _selectedI2CDevices, _scanCts.Token);
                LogListView.Items.Add($"[Scan] Найдено устройств: {foundDevices.Count}");

                var rs485Iface = Interfaces.FirstOrDefault(i => i.Name.Contains("RS-485"));
                var i2cIface = Interfaces.FirstOrDefault(i => i.Name.Contains("I2C"));

                foreach (var dev in foundDevices)
                {
                    // Определяем, к какому интерфейсу относится устройство
                    if (dev.PortName.Contains("hid#") || dev.Name.Contains("I2C"))
                    {
                        if (i2cIface != null)
                        {
                            dev.ParentInterface = i2cIface;

                            // Проверяем, нет ли дубликата (по адресу и пути)
                            bool exists = i2cIface.Devices.Any(d => d.Address == dev.Address && d.PortName == dev.PortName);
                            if (!exists)
                            {
                                i2cIface.Devices.Add(dev);
                            }
                        }
                    }
                    else
                    {
                        if (rs485Iface != null)
                        {
                            dev.ParentInterface = rs485Iface;

                            bool exists = rs485Iface.Devices.Any(d => d.Address == dev.Address && d.PortName == dev.PortName);
                            if (!exists)
                            {
                                rs485Iface.Devices.Add(dev);
                            }
                        }
                    }
                }

                // Пересчитываем галочки (чтобы родительские узлы обновились)
                rs485Iface?.RecalculateSelection();
                i2cIface?.RecalculateSelection();

                CreateSeriesForAllChannels();
                UpdateHistoryList();
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
            }
        }
        private void CopyLogMenuItem_Click(object sender, RoutedEventArgs e) => CopySelectedLogs();
        private void CopyLogCommand_Executed(object sender, ExecutedRoutedEventArgs e) => CopySelectedLogs();

        private void CopySelectedLogs()
        {
            if (LogListView.SelectedItems.Count == 0) return;
            var sb = new System.Text.StringBuilder();
            foreach (var item in LogListView.SelectedItems) sb.AppendLine(item.ToString());
            try { Clipboard.SetText(sb.ToString()); } catch { }
        }

        // ================= ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ И ГРАФИКИ =================

        private void UpdateStatusText()
        {
            var rs485Str = _isRs485Enabled ? string.Join(", ", _rs485Ports.Select(p => $"{p.PortName}@{p.BaudRate}")) : "Выкл";
            var i2cStr = _selectedI2CDevices.Count > 0 ? $"{_selectedI2CDevices.Count} шт." : "Выкл";
            StatusText.Text = $"RS-485 (Ц1): {rs485Str} | I2C (Ц4): {i2cStr}";
        }

        private void InitializePlot()
        {
            _plotModelRealTime = new PlotModel
            {
                Title = "Показания датчиков (Реальное время)",
                Background = OxyColors.White,
                TitleColor = OxyColors.Black,
                DefaultFont = "Times New Roman",
                TitleFont = "Times New Roman",
                TitleFontSize = 16,
                TitleFontWeight = OxyPlot.FontWeights.Bold,
                IsLegendVisible = false,
                Padding = new OxyThickness(0, 5, 0, 0)
            };

            _plotModelRealTime.Axes.Add(new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "HH:mm:ss",
                Title = "Время",
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColors.LightGray,
                MinorGridlineColor = OxyColors.Gainsboro,
                AxisTitleDistance = 5,
                TitleFont = "Times New Roman",
                TitleFontSize = 14,
                TitleFontWeight = OxyPlot.FontWeights.Bold
            });

            _plotModelRealTime.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Значение",
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColors.LightGray,
                MinorGridlineColor = OxyColors.Gainsboro,
                StringFormat = "0.000",
                AxisTitleDistance = 10,
                TitleFont = "Times New Roman",
                TitleFontSize = 14,
                TitleFontWeight = OxyPlot.FontWeights.Bold
            });

            PlotViewRealTime.Model = _plotModelRealTime;
        }

        private void CreateSeriesForAllChannels()
        {
            _plotModelRealTime.Series.Clear();
            _seriesMapRealTime.Clear();
            var colors = new[] { OxyColors.Red, OxyColors.DeepSkyBlue, OxyColors.Lime, OxyColors.Orange, OxyColors.Yellow, OxyColors.Purple };
            int colorIndex = 0;

            foreach (var iface in Interfaces)
                foreach (var dev in iface.Devices)
                    foreach (var ch in dev.Channels)
                    {
                        ch.PropertyChanged -= Channel_PropertyChanged;
                        ch.PropertyChanged += Channel_PropertyChanged;
                        var seriesRT = new LineSeries { Title = ch.FullName, Color = colors[colorIndex % colors.Length], StrokeThickness = 2, IsVisible = false };
                        _plotModelRealTime.Series.Add(seriesRT);
                        _seriesMapRealTime[ch] = seriesRT;
                        colorIndex++;
                    }
            _plotModelRealTime.InvalidatePlot(true);
        }

        private void Channel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChannelNode.IsVisibleOnPlot))
            {
                var channel = (ChannelNode)sender;
                if (_seriesMapRealTime.TryGetValue(channel, out var seriesRT)) { seriesRT.IsVisible = channel.IsVisibleOnPlot; _plotModelRealTime.InvalidatePlot(false); }
            }
        }

        private void UpdatePlotVisibility()
        {
            foreach (var kvp in _seriesMapRealTime)
            {
                kvp.Value.IsVisible = (kvp.Key == _selectedChannelForPlot);
            }
            _plotModelRealTime.InvalidatePlot(true);
        }

        private void UpdateHistoryList()
        {
            HistoryItemsControl.ItemsSource = null;

            var visibleChannels = Interfaces.SelectMany(i => i.Devices).SelectMany(d => d.Channels).Where(c => c.IsSelected).ToList();

            foreach (var ch in visibleChannels)
            {
                if (ch.ParentInterface == null && ch.ParentDevice != null)
                {
                    var parentIface = Interfaces.FirstOrDefault(i => i.Devices.Contains(ch.ParentDevice));
                    if (parentIface != null)
                    {
                        ch.ParentInterface = parentIface;
                    }
                }

                ch.ChartModel = CreateMiniPlot(ch);
            }

            HistoryItemsControl.ItemsSource = visibleChannels;
            ResetHistorySliders();
        }

        private PlotModel CreateMiniPlot(ChannelNode channel)
        {
            var model = new PlotModel
            {
                Background = OxyColors.White,
                PlotAreaBorderColor = OxyColors.LightGray,
                IsLegendVisible = false,
                Title = channel.FullName,
                TitleFontSize = 12,
                TitleFont = "Times New Roman",
                TitleFontWeight = OxyPlot.FontWeights.Bold,
                Padding = new OxyThickness(0, 5, 0, 0)
            };

            string yAxisFormat = channel.IsInt32 ? "0" : "0.000";

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColors.Gainsboro,
                TextColor = OxyColors.Black,
                FontSize = 10,
                StringFormat = yAxisFormat,
                AxisTitleDistance = 10,
                TitleFont = "Times New Roman",
                TitleFontSize = 12,
                TitleFontWeight = OxyPlot.FontWeights.Bold
            });

            model.Axes.Add(new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "HH:mm:ss",
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColors.Gainsboro,
                TextColor = OxyColors.Black,
                FontSize = 10,
                AxisTitleDistance = 5,
                TitleFont = "Times New Roman",
                TitleFontSize = 12,
                TitleFontWeight = OxyPlot.FontWeights.Bold
            });

            var series = new LineSeries
            {
                Color = OxyColors.DeepSkyBlue,
                StrokeThickness = 1.5,
                MarkerType = MarkerType.None,
                TrackerFormatString = $"{channel.FullName}\nВремя: {{2:HH:mm:ss}}\nЗначение: {{4:{yAxisFormat}}}"
            };

            var pointsToDraw = channel.HistoryPoints.Count > 5000
                ? channel.HistoryPoints.Skip(channel.HistoryPoints.Count - 5000)
                : channel.HistoryPoints;

            foreach (var pt in pointsToDraw) series.Points.Add(pt);

            model.Series.Add(series);
            return model;
        }

        private void ResetHistorySliders()
        {
            if (!_isRecording) return;
            var allPoints = Interfaces.SelectMany(i => i.Devices).SelectMany(d => d.Channels).SelectMany(c => c.HistoryPoints).ToList();

            if (allPoints.Any())
            {
                _historyMinTime = allPoints.Min(p => p.X);
                _historyMaxTime = allPoints.Max(p => p.X);
            }
            else
            {
                _historyMinTime = DateTimeAxis.ToDouble(DateTime.Now.AddMinutes(-5));
                _historyMaxTime = DateTimeAxis.ToDouble(DateTime.Now);
            }

            HistoryRangeSlider.Minimum = _historyMinTime;
            HistoryRangeSlider.Maximum = _historyMaxTime;
            HistoryRangeSlider.Value = _historyMinTime;

            HistoryStartText.Text = $"С: {DateTimeAxis.ToDateTime(_historyMinTime):HH:mm:ss}";
            HistoryEndText.Text = $"По: {DateTimeAxis.ToDateTime(_historyMaxTime):HH:mm:ss}";
        }

        private void HistoryRangeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (HistoryItemsControl.ItemsSource == null) return;

            var allPoints = Interfaces.SelectMany(i => i.Devices).SelectMany(d => d.Channels).SelectMany(c => c.HistoryPoints).ToList();
            if (!allPoints.Any()) return;

            double minTime = allPoints.Min(p => p.X);
            double maxTime = allPoints.Max(p => p.X);
            double duration = maxTime - minTime;

            if (duration <= 0) return;

            double ratio = (e.NewValue - HistoryRangeSlider.Minimum) / (HistoryRangeSlider.Maximum - HistoryRangeSlider.Minimum);
            if (double.IsNaN(ratio) || ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;

            double windowDuration = duration * ratio;
            double newMax = maxTime;
            double newMin = newMax - windowDuration;

            ApplyHistoryTimeRange(newMin, newMax);

            HistoryStartText.Text = $"С: {DateTimeAxis.ToDateTime(newMin):HH:mm:ss}";
            HistoryEndText.Text = $"По: {DateTimeAxis.ToDateTime(newMax):HH:mm:ss}";
        }

        private void ApplyHistoryTimeRange(double minX, double maxX)
        {
            if (HistoryItemsControl.ItemsSource == null) return;

            foreach (var channel in HistoryItemsControl.ItemsSource.Cast<ChannelNode>())
            {
                var model = channel.ChartModel;
                if (model != null)
                {
                    var xAxis = model.Axes.FirstOrDefault(a => a.Position == AxisPosition.Bottom) as DateTimeAxis;
                    if (xAxis != null)
                    {
                        xAxis.Minimum = minX;
                        xAxis.Maximum = maxX;
                        model.InvalidatePlot(false);
                    }
                }
            }
        }

        private void UiQueueTimer_Tick(object sender, EventArgs e)
        {
            while (_engine.UiUpdates.TryDequeue(out var action)) { try { action.Invoke(); } catch { } }
            while (_engine.LogQueue.TryDequeue(out var msg)) { LogListView.Items.Add(msg); LogListView.ScrollIntoView(msg); }

            if (!_isRecording) return;

            int intervalMs = (int)(IntervalSlider.Value * 1000);
            if (intervalMs < 50) intervalMs = 50;

            if ((DateTime.Now - _lastPlotUpdate).TotalMilliseconds < intervalMs) return;

            _lastPlotUpdate = DateTime.Now;
            var now = DateTime.Now;
            bool needRedrawRT = false;

            foreach (var iface in Interfaces)
                foreach (var dev in iface.Devices)
                    foreach (var ch in dev.Channels)
                    {
                        if (!ch.IsSelected || !ch.IsValueSet || (now - ch.LastUpdateTime).TotalSeconds > 3) continue;

                        var newPoint = new DataPoint(DateTimeAxis.ToDouble(now), ch.Value);

                        if (_seriesMapRealTime.TryGetValue(ch, out var seriesRT))
                        {
                            seriesRT.Points.Add(newPoint);
                            needRedrawRT = true;
                        }

                        ch.HistoryPoints.Add(newPoint);

                        if (ch.ChartModel != null)
                        {
                            var miniSeries = ch.ChartModel.Series.OfType<LineSeries>().FirstOrDefault();
                            if (miniSeries != null)
                            {
                                miniSeries.Points.Add(newPoint);
                                if (miniSeries.Points.Count > 5000) miniSeries.Points.RemoveAt(0);
                            }
                        }
                    }

            var cutoffRT = DateTimeAxis.ToDouble(now.AddMinutes(-2));
            foreach (var series in _seriesMapRealTime.Values)
                while (series.Points.Count > 0 && series.Points[0].X < cutoffRT) series.Points.RemoveAt(0);

            if (needRedrawRT || MainTabControl.SelectedIndex == 0) _plotModelRealTime.InvalidatePlot(true);

            if (MainTabControl.SelectedIndex == 1)
                foreach (var ch in Interfaces.SelectMany(i => i.Devices).SelectMany(d => d.Channels))
                    ch.ChartModel?.InvalidatePlot(true);

            if (HistoryRangeSlider.Value >= HistoryRangeSlider.Maximum - 1)
            {
                _historyMaxTime = DateTimeAxis.ToDouble(now);
                HistoryRangeSlider.Maximum = _historyMaxTime;
                HistoryRangeSlider.Value = _historyMaxTime;
                HistoryRangeSlider_ValueChanged(null, new RoutedPropertyChangedEventArgs<double>(HistoryRangeSlider.Value, HistoryRangeSlider.Value));
            }
        }

        public void ClosePort() => _engine.ClosePort();

        public void SetPollingInterval(double seconds) { if (seconds >= IntervalSlider.Minimum && seconds <= IntervalSlider.Maximum) { IntervalSlider.Value = seconds; IntervalTextBox.Text = seconds.ToString("F2"); } }
        public double GetPollingInterval() => IntervalSlider.Value;

        private void HistoryCard_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border && border.DataContext is ChannelNode channel)
            {
                channel.IsHovered = true;
            }
        }

        private void HistoryCard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border && border.DataContext is ChannelNode channel)
            {
                channel.IsHovered = false;
            }
        }
    }
}