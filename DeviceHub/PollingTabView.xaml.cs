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

        private PlotModel _plotModelRealTime;
        private Dictionary<ChannelNode, LineSeries> _seriesMapRealTime = new Dictionary<ChannelNode, LineSeries>();
        private DateTime _lastPlotUpdate = DateTime.MinValue;

        public ObservableCollection<InterfaceNode> Interfaces { get; set; } = new ObservableCollection<InterfaceNode>();

        private bool _isScanning = false;
        private CancellationTokenSource _scanCts;

        private bool _isRs485Enabled = true;
        private List<PortSetting> _rs485Ports = new List<PortSetting> { new PortSetting { PortName = "COM9", BaudRate = 57600 } };
        private bool _isI2CEnabled = true;

        private bool _isRecording = false;
        private ChannelNode _selectedChannelForPlot = null;
        private double _historyMinTime = 0;
        private double _historyMaxTime = 0;

        public PollingTabView()
        {
            InitializeComponent();
            DataContext = this;
            InitializePlot();

            _engine.ScanProgressChanged += (port, current, total) => Dispatcher.Invoke(() =>
            {
                if (port == "RS-485") { ScanProgressBarRS485.Maximum = total; ScanProgressBarRS485.Value = current; ScanProgressTextRS485.Text = $"RS-485: {current}/{total}"; }
                else if (port == "I2C") { ScanProgressBarI2C.Maximum = total; ScanProgressBarI2C.Value = current; ScanProgressTextI2C.Text = $"I2C: {current}/{total}"; }
            });

            _engine.DeviceFound += (dev) => Dispatcher.Invoke(() =>
            {
                var rs485Iface = Interfaces.FirstOrDefault(i => i.Name.Contains("RS-485"));
                var i2cIface = Interfaces.FirstOrDefault(i => i.Name.Contains("I2C"));

                if (dev.PortName == "I2C")
                {
                    if (i2cIface != null && !i2cIface.Devices.Any(d => d.Address == dev.Address))
                        i2cIface.Devices.Add(dev);
                }
                else
                {
                    if (rs485Iface != null && !rs485Iface.Devices.Any(d => d.Address == dev.Address && d.PortName == dev.PortName))
                        rs485Iface.Devices.Add(dev);
                }
            });

            _uiQueueTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _uiQueueTimer.Tick += UiQueueTimer_Tick;
            _uiQueueTimer.Start();
            UpdateStatusText();

            // Отписываемся при выгрузке
            Unloaded += (s, e) => _uiQueueTimer.Stop();
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

                    // Отвязываем модели истории
                    HistoryItemsControl.ItemsSource = null;

                    foreach (var iface in Interfaces)
                        foreach (var dev in iface.Devices)
                            foreach (var ch in dev.Channels)
                                ch.ChartModel = null;

                    // ★ ПРИНУДИТЕЛЬНО ПЕРЕРИСОВЫВАЕМ ГРАФИК РЕАЛЬНОГО ВРЕМЕНИ ★
                    if (_plotModelRealTime != null)
                    {
                        _plotModelRealTime.InvalidatePlot(true);
                    }
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
                _selectedChannelForPlot = channel;

                string title = channel.Name;
                string yAxisTitle = "Значение";
                if (channel.Name.Contains("Частота давления")) yAxisTitle = "Fp, Гц";
                else if (channel.Name.Contains("Частота температуры")) yAxisTitle = "Ft, Гц";
                else if (channel.Name.Contains("Давление")) yAxisTitle = "P, кгс/см²";
                else if (channel.Name.Contains("Температура процессора")) yAxisTitle = "t, °C";
                else if (channel.Name.Contains("Температура")) yAxisTitle = "t, °C";
                else if (channel.Name.Contains("Напряжение питания")) yAxisTitle = "U, В";

                _plotModelRealTime.Title = title;

                var yAxisRT = _plotModelRealTime.Axes.FirstOrDefault(a => a.Position == AxisPosition.Left);
                if (yAxisRT != null) yAxisRT.Title = yAxisTitle;

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
                    Title = $"История: {channel.Name}",
                    Width = 900,
                    Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    Background = System.Windows.Media.Brushes.White
                };

                // Создаем независимую модель
                var newPlotModel = new PlotModel
                {
                    Title = channel.Name,
                    Background = OxyColors.White,
                    DefaultFont = "Times New Roman",
                    TitleFontSize = 18,
                    TitleFontWeight = OxyPlot.FontWeights.Bold
                };

                newPlotModel.Axes.Add(new LinearAxis
                {
                    Position = AxisPosition.Left,
                    MajorGridlineStyle = LineStyle.Solid,
                    MajorGridlineColor = OxyColors.LightGray,
                    Title = channel.Unit,
                    FontSize = 12
                });

                newPlotModel.Axes.Add(new DateTimeAxis
                {
                    Position = AxisPosition.Bottom,
                    StringFormat = "HH:mm:ss",
                    MajorGridlineStyle = LineStyle.Solid,
                    MajorGridlineColor = OxyColors.LightGray,
                    Title = "Время",
                    FontSize = 12
                });

                var destSeries = new LineSeries { Color = OxyColors.DeepSkyBlue, StrokeThickness = 2.5 };
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
            var result = MessageBox.Show("Вы уверены, что хотите удалить всю накопленную историю графиков?",
                                         "Очистка истории", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var iface in Interfaces)
                    foreach (var dev in iface.Devices)
                        foreach (var ch in dev.Channels)
                            ch.HistoryPoints.Clear();

                UpdateHistoryList(); // Пересоздаем графики
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

                _engine.StartPolling(Interfaces, _isRs485Enabled, _rs485Ports, _isI2CEnabled, intervalMs);
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
                _isRs485Enabled = settingsWindow.IsRs485Enabled; _rs485Ports = settingsWindow.SelectedPortsWithBaud; _isI2CEnabled = settingsWindow.IsI2CEnabled;
                UpdateStatusText(); LogListView.Items.Add($"Настройки: Порты={string.Join(", ", _rs485Ports.Select(p => $"{p.PortName}@{p.BaudRate}"))}");
            }
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecording) { _engine.Stop(); _isRecording = false; RecordIcon.Text = "\uE768"; RecordIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4CAF50")); LogListView.Items.Add("Опрос остановлен (запущен поиск)."); }
            if (_isScanning) { _engine.Stop(); _scanCts?.Cancel(); _scanCts?.Dispose(); _scanCts = null; _isScanning = false; ScanButtonIcon.Text = "\uE721"; ScanButton.ToolTip = "Поиск"; LogListView.Items.Add("Поиск отменён."); return; }

            _isScanning = true; _scanCts = new CancellationTokenSource(); ScanButtonIcon.Text = "\uE711"; ScanButton.ToolTip = "Отмена"; LogListView.Items.Add("Запуск параллельного сканирования...");
            ScanProgressBarRS485.Visibility = Visibility.Visible; ScanProgressTextRS485.Visibility = Visibility.Visible; ScanProgressBarI2C.Visibility = Visibility.Visible; ScanProgressTextI2C.Visibility = Visibility.Visible;
            ScanProgressBarRS485.Value = 0; ScanProgressTextRS485.Text = ""; ScanProgressBarI2C.Value = 0; ScanProgressTextI2C.Text = "";

            try
            {
                Interfaces.Clear();
                if (_isRs485Enabled) Interfaces.Add(new InterfaceNode { Name = "RS-485 (Ц1)" });
                if (_isI2CEnabled) Interfaces.Add(new InterfaceNode { Name = "I2C (Ц4)" });
                _plotModelRealTime.Series.Clear(); _seriesMapRealTime.Clear(); _plotModelRealTime.InvalidatePlot(true); HistoryItemsControl.ItemsSource = null;

                var foundDevices = await _engine.ScanAllAsync(Interfaces, _isRs485Enabled, _rs485Ports, _isI2CEnabled, _scanCts.Token);
                LogListView.Items.Add($"[Scan] Найдено устройств: {foundDevices.Count}");

                var rs485Iface = Interfaces.FirstOrDefault(i => i.Name.Contains("RS-485"));
                var i2cIface = Interfaces.FirstOrDefault(i => i.Name.Contains("I2C"));

                foreach (var dev in foundDevices)
                {
                    if (dev.PortName == "I2C")
                    {
                        if (i2cIface != null && !i2cIface.Devices.Any(d => d.Address == dev.Address)) { i2cIface.Devices.Add(dev); i2cIface.RecalculateSelection(); }
                    }
                    else
                    {
                        if (rs485Iface != null && !rs485Iface.Devices.Any(d => d.Address == dev.Address && d.PortName == dev.PortName)) { rs485Iface.Devices.Add(dev); rs485Iface.RecalculateSelection(); }
                    }
                }

                CreateSeriesForAllChannels(); UpdateHistoryList(); LogListView.Items.Add("Сканирование завершено.");
            }
            catch (OperationCanceledException) { LogListView.Items.Add("Поиск отменён."); }
            catch (Exception ex) { LogListView.Items.Add($"Ошибка при сканировании: {ex.Message}"); }
            finally
            {
                _isScanning = false; _scanCts?.Dispose(); _scanCts = null; ScanButtonIcon.Text = "\uE721"; ScanButton.ToolTip = "Поиск";
                ScanProgressBarRS485.Visibility = Visibility.Collapsed; ScanProgressTextRS485.Visibility = Visibility.Collapsed; ScanProgressBarI2C.Visibility = Visibility.Collapsed; ScanProgressTextI2C.Visibility = Visibility.Collapsed;
                ScanProgressBarRS485.Value = 0; ScanProgressTextRS485.Text = ""; ScanProgressBarI2C.Value = 0; ScanProgressTextI2C.Text = "";
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
            StatusText.Text = $"RS-485 (Ц1): {rs485Str} | I2C (Ц4): {(_isI2CEnabled ? "Вкл" : "Выкл")}";
        }

        private void InitializePlot()
        {
            _plotModelRealTime = new PlotModel { Title = "Показания датчиков (Реальное время)", Background = OxyColors.White, TitleColor = OxyColors.Black, DefaultFont = "Times New Roman", TitleFont = "Times New Roman", TitleFontSize = 16, TitleFontWeight = OxyPlot.FontWeights.Bold, IsLegendVisible = false };
            _plotModelRealTime.Axes.Add(new DateTimeAxis { Position = AxisPosition.Bottom, StringFormat = "HH:mm:ss", Title = "Время", MajorGridlineStyle = LineStyle.Solid, MinorGridlineStyle = LineStyle.Dot, MajorGridlineColor = OxyColors.LightGray, MinorGridlineColor = OxyColors.Gainsboro });
            _plotModelRealTime.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Значение", MajorGridlineStyle = LineStyle.Solid, MinorGridlineStyle = LineStyle.Dot, MajorGridlineColor = OxyColors.LightGray, MinorGridlineColor = OxyColors.Gainsboro });
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
                        var seriesRT = new LineSeries { Title = $"{dev.Name} - {ch.Name}", Color = colors[colorIndex % colors.Length], StrokeThickness = 2, IsVisible = false };
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
            // 1. Жестко очищаем старый список, чтобы WPF уничтожил все старые PlotView
            HistoryItemsControl.ItemsSource = null;
            HistoryItemsControl.Items.Refresh(); // Принудительно обновляем

            // 2. Создаем абсолютно новые модели для всех каналов
            var visibleChannels = Interfaces.SelectMany(i => i.Devices).SelectMany(d => d.Channels).Where(c => c.IsSelected).ToList();

            foreach (var ch in visibleChannels)
            {
                // Всегда создаем новую модель!
                ch.ChartModel = CreateMiniPlot(ch);
            }

            // 3. Привязываем заново
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
                Title = channel.Name,
                TitleFontSize = 12,
                TitleFont = "Times New Roman",
                TitleFontWeight = OxyPlot.FontWeights.Bold,
                Padding = new OxyThickness(2)
            };

            model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, MajorGridlineStyle = LineStyle.Dot, MajorGridlineColor = OxyColors.Gainsboro, TextColor = OxyColors.Black, FontSize = 10 });
            model.Axes.Add(new DateTimeAxis { Position = AxisPosition.Bottom, StringFormat = "HH:mm:ss", MajorGridlineStyle = LineStyle.Dot, MajorGridlineColor = OxyColors.Gainsboro, TextColor = OxyColors.Black, FontSize = 10 });

            var series = new LineSeries { Color = OxyColors.DeepSkyBlue, StrokeThickness = 1.5, MarkerType = MarkerType.None };

            // Берем для отрисовки только последние 5000 точек
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

            HistoryTimeSlider.Minimum = _historyMinTime;
            HistoryTimeSlider.Maximum = _historyMaxTime;
            HistoryTimeSlider.Value = _historyMinTime;
            HistoryTimeSliderEnd.Minimum = _historyMinTime;
            HistoryTimeSliderEnd.Maximum = _historyMaxTime;
            HistoryTimeSliderEnd.Value = _historyMaxTime;
        }

        private void HistoryTimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (HistoryTimeSliderEnd != null && HistoryTimeSliderEnd.Value <= e.NewValue) HistoryTimeSliderEnd.Value = e.NewValue + 1;
            ApplyHistoryTimeRange(e.NewValue, HistoryTimeSliderEnd?.Value ?? _historyMaxTime);
        }

        private void HistoryTimeSliderEnd_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (HistoryTimeSlider != null && HistoryTimeSlider.Value >= e.NewValue) HistoryTimeSlider.Value = e.NewValue - 1;
            ApplyHistoryTimeRange(HistoryTimeSlider?.Value ?? _historyMinTime, e.NewValue);
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
            // 1. Обработка очередей
            while (_engine.UiUpdates.TryDequeue(out var action)) { try { action.Invoke(); } catch { } }
            while (_engine.LogQueue.TryDequeue(out var msg)) { LogListView.Items.Add(msg); LogListView.ScrollIntoView(msg); }

            if (!_isRecording) return;

            int intervalMs = (int)(IntervalSlider.Value * 1000);
            if (intervalMs < 50) intervalMs = 50;

            // 2. Проверка интервала времени
            if ((DateTime.Now - _lastPlotUpdate).TotalMilliseconds < intervalMs) return;

            _lastPlotUpdate = DateTime.Now;
            var now = DateTime.Now;
            bool needRedrawRT = false;

            // 3. Сбор данных
            foreach (var iface in Interfaces)
            {
                foreach (var dev in iface.Devices)
                {
                    foreach (var ch in dev.Channels)
                    {
                        if (!ch.IsSelected || !ch.IsValueSet || (now - ch.LastUpdateTime).TotalSeconds > 3) continue;

                        var newPoint = new DataPoint(DateTimeAxis.ToDouble(now), ch.Value);

                        // Обновляем RealTime данные
                        if (_seriesMapRealTime.TryGetValue(ch, out var seriesRT))
                        {
                            seriesRT.Points.Add(newPoint);
                            needRedrawRT = true;
                        }

                        // Обновляем History данные
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
                }
            }

            // 4. Обрезка RealTime (2 минуты)
            var cutoffRT = DateTimeAxis.ToDouble(now.AddMinutes(-2));
            foreach (var series in _seriesMapRealTime.Values)
                while (series.Points.Count > 0 && series.Points[0].X < cutoffRT) series.Points.RemoveAt(0);

            // ★ 5. ПРИНУДИТЕЛЬНАЯ ОТРИСОВКА ★
            // Перерисовываем RealTime, если мы на этой вкладке ИЛИ если есть новые данные
            if (needRedrawRT || MainTabControl.SelectedIndex == 0)
            {
                _plotModelRealTime.InvalidatePlot(true);
            }

            // 6. Отрисовка истории (только если мы на вкладке истории)
            if (MainTabControl.SelectedIndex == 1)
            {
                foreach (var ch in Interfaces.SelectMany(i => i.Devices).SelectMany(d => d.Channels))
                {
                    ch.ChartModel?.InvalidatePlot(true);
                }
            }

            // 7. Двигаем ползунок
            if (HistoryTimeSliderEnd.Value >= _historyMaxTime - 1)
            {
                _historyMaxTime = DateTimeAxis.ToDouble(now);
                HistoryTimeSliderEnd.Maximum = _historyMaxTime;
                HistoryTimeSlider.Maximum = _historyMaxTime;
                HistoryTimeSliderEnd.Value = _historyMaxTime;
                ApplyHistoryTimeRange(HistoryTimeSlider.Value, _historyMaxTime);
            }
        }
        public void ClosePort() => _engine.ClosePort();

        public void SetPollingInterval(double seconds) { if (seconds >= IntervalSlider.Minimum && seconds <= IntervalSlider.Maximum) { IntervalSlider.Value = seconds; IntervalTextBox.Text = seconds.ToString("F2"); } }
        public double GetPollingInterval() => IntervalSlider.Value;
    }
}