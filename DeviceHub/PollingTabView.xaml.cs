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
        private List<PortSetting> _rs485Ports = new List<PortSetting>
        {
            new PortSetting { PortName = "COM9", BaudRate = 57600 }
        };
        private bool _isI2CEnabled = true;

        private bool _isRecording = false;
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

        private void UpdatePlotVisibility()
        {
            foreach (var kvp in _seriesMapRealTime)
            {
                kvp.Value.IsVisible = (kvp.Key == _selectedChannelForPlot);
            }
            _plotModelRealTime.InvalidatePlot(true);

            // Обновляем список истории (скрываем/показываем графики)
            UpdateHistoryList();
        }

        // ★ ОБНОВЛЕНИЕ СПИСКА ИСТОРИИ ★
        private void UpdateHistoryList()
        {
            var allChannels = Interfaces.SelectMany(i => i.Devices).SelectMany(d => d.Channels).ToList();

            // Берем только те каналы, которые отмечены галочкой
            var visibleChannels = allChannels.Where(c => c.IsSelected).ToList();

            // Привязываем к ItemsControl (который должен быть в XAML)
            HistoryItemsControl.ItemsSource = visibleChannels;
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRecording)
            {
                if (!Interfaces.Any(i => i.Devices.Any(d => d.IsSelected != false)))
                {
                    LogListView.Items.Add("Нет выбранных устройств для опроса. Сначала выполните поиск.");
                    return;
                }

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

                foreach (var iface in Interfaces)
                {
                    foreach (var dev in iface.Devices)
                    {
                        foreach (var ch in dev.Channels) ch.ResetValueState();
                    }
                }

                int intervalMs = (int)(IntervalSlider.Value * 1000);
                if (intervalMs < 50) intervalMs = 50;

                _engine.StartPolling(Interfaces, _isRs485Enabled, _rs485Ports, _isI2CEnabled, intervalMs);
                _isRecording = true;

                UpdateHistoryList(); // Обновляем список при старте

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
            _plotModelRealTime.Series.Clear();
            _seriesMapRealTime.Clear();

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

                        var seriesRT = new LineSeries
                        {
                            Title = $"{dev.Name} - {ch.Name}",
                            Color = colors[colorIndex % colors.Length],
                            StrokeThickness = 2,
                            IsVisible = false
                        };
                        _plotModelRealTime.Series.Add(seriesRT);
                        _seriesMapRealTime[ch] = seriesRT;
                        colorIndex++;
                    }
                }
            }
            _plotModelRealTime.InvalidatePlot(true);
        }

        private void UiQueueTimer_Tick(object sender, EventArgs e)
        {
            while (_engine.UiUpdates.TryDequeue(out var action))
            {
                try { action.Invoke(); }
                catch (Exception ex) { LogListView.Items.Add($"[UI] {ex.Message}"); }
            }

            while (_engine.LogQueue.TryDequeue(out var msg))
            {
                LogListView.Items.Add(msg);
                if (LogListView.Items.Count > 0)
                    LogListView.ScrollIntoView(LogListView.Items[LogListView.Items.Count - 1]);
            }

            if (!_isRecording) return;

            int intervalMs = (int)(IntervalSlider.Value * 1000);
            if (intervalMs < 50) intervalMs = 50;

            if ((DateTime.Now - _lastPlotUpdate).TotalMilliseconds < intervalMs) return;

            _lastPlotUpdate = DateTime.Now;
            bool needRedrawRT = false;
            var now = DateTime.Now;

            foreach (var iface in Interfaces)
            {
                foreach (var dev in iface.Devices)
                {
                    foreach (var ch in dev.Channels)
                    {
                        if (!ch.IsSelected || !ch.IsValueSet) continue;
                        if ((now - ch.LastUpdateTime).TotalSeconds > 3) continue;

                        // 1. Пишем в RealTime
                        if (_seriesMapRealTime.TryGetValue(ch, out var seriesRT))
                        {
                            seriesRT.Points.Add(new DataPoint(DateTimeAxis.ToDouble(now), ch.Value));
                            needRedrawRT = true;
                        }

                        // 2. Пишем в мини-график Истории
                        if (ch.MiniPlotModel.Series.Count > 0 && ch.MiniPlotModel.Series[0] is LineSeries miniSeries)
                        {
                            miniSeries.Points.Add(new DataPoint(DateTimeAxis.ToDouble(now), ch.Value));

                            // Ограничиваем историю последними 2 минутами
                            var cutoff = now.AddMinutes(-2);
                            double cutoffX = DateTimeAxis.ToDouble(cutoff);
                            while (miniSeries.Points.Count > 0 && miniSeries.Points[0].X < cutoffX)
                            {
                                miniSeries.Points.RemoveAt(0);
                            }

                            ch.MiniPlotModel.InvalidatePlot(true);
                        }
                    }
                }
            }

            // Обрезаем RealTime
            var cutoffRT = now.AddMinutes(-2);
            double cutoffXRT = DateTimeAxis.ToDouble(cutoffRT);
            foreach (var series in _seriesMapRealTime.Values)
            {
                while (series.Points.Count > 0 && series.Points[0].X < cutoffXRT)
                {
                    series.Points.RemoveAt(0);
                }
            }

            if (needRedrawRT) _plotModelRealTime.InvalidatePlot(true);
        }

        private void IntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IntervalTextBox != null) IntervalTextBox.Text = e.NewValue.ToString("F2");
        }

        private void IntervalTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (double.TryParse(IntervalTextBox.Text, out double value))
            {
                if (value >= IntervalSlider.Minimum && value <= IntervalSlider.Maximum) IntervalSlider.Value = value;
            }
        }

        private void UpdateStatusText()
        {
            var rs485Str = _isRs485Enabled ? string.Join(", ", _rs485Ports.Select(p => $"{p.PortName}@{p.BaudRate}")) : "Выкл";
            StatusText.Text = $"RS-485 (Ц1): {rs485Str} | I2C (Ц4): {(_isI2CEnabled ? "Вкл" : "Выкл")}";
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
                IsLegendVisible = false
            };

            _plotModelRealTime.Axes.Add(new DateTimeAxis
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
                AxisTitleDistance = 15
            });

            _plotModelRealTime.Axes.Add(new LinearAxis
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
                AxisTitleDistance = 25
            });

            PlotViewRealTime.Model = _plotModelRealTime;
        }

        private void Channel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChannelNode.IsVisibleOnPlot))
            {
                var channel = (ChannelNode)sender;
                if (_seriesMapRealTime.TryGetValue(channel, out var seriesRT))
                {
                    seriesRT.IsVisible = channel.IsVisibleOnPlot;
                    _plotModelRealTime.InvalidatePlot(false);
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
                if (_isRs485Enabled) Interfaces.Add(new InterfaceNode { Name = "RS-485 (Ц1)" });
                if (_isI2CEnabled) Interfaces.Add(new InterfaceNode { Name = "I2C (Ц4)" });

                _plotModelRealTime.Series.Clear();
                _seriesMapRealTime.Clear();
                _plotModelRealTime.InvalidatePlot(true);
                HistoryItemsControl.ItemsSource = null;

                var foundDevices = await _engine.ScanAllAsync(Interfaces, _isRs485Enabled, _rs485Ports, _isI2CEnabled, _scanCts.Token);
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
                            i2cIface.RecalculateSelection();
                        }
                    }
                    else if (rs485Iface != null)
                    {
                        if (!rs485Iface.Devices.Any(d => d.Address == dev.Address && d.PortName == dev.PortName))
                        {
                            rs485Iface.Devices.Add(dev);
                            rs485Iface.RecalculateSelection();
                        }
                    }
                }

                CreateSeriesForAllChannels();
                UpdateHistoryList();

                LogListView.Items.Add("Сканирование завершено.");
            }
            catch (OperationCanceledException) { LogListView.Items.Add("Поиск отменён."); }
            catch (Exception ex) { LogListView.Items.Add($"Ошибка при сканировании: {ex.Message}"); }
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

        private void MiniPlot_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ChannelNode channel)
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

                if (channel.MiniPlotModel.Series[0] is LineSeries sourceSeries)
                {
                    var destSeries = new LineSeries
                    {
                        Color = sourceSeries.Color,
                        StrokeThickness = 2.5
                    };
                    foreach (var pt in sourceSeries.Points) destSeries.Points.Add(pt);
                    newPlotModel.Series.Add(destSeries);
                }

                var plotView = new OxyPlot.Wpf.PlotView { Model = newPlotModel };
                historyWindow.Content = plotView;
                historyWindow.Show();
            }
        }

        public void ClosePort() => _engine.ClosePort();

        private void CopyLogMenuItem_Click(object sender, RoutedEventArgs e) => CopySelectedLogs();
        private void CopyLogCommand_Executed(object sender, ExecutedRoutedEventArgs e) => CopySelectedLogs();

        private void CopySelectedLogs()
        {
            if (LogListView.SelectedItems.Count == 0) return;
            var sb = new System.Text.StringBuilder();
            foreach (var item in LogListView.SelectedItems) sb.AppendLine(item.ToString());
            try { Clipboard.SetText(sb.ToString()); } catch { }
        }

        public void SetPollingInterval(double seconds)
        {
            if (seconds >= IntervalSlider.Minimum && seconds <= IntervalSlider.Maximum)
            {
                IntervalSlider.Value = seconds;
                IntervalTextBox.Text = seconds.ToString("F2");
            }
        }

        public double GetPollingInterval() => IntervalSlider.Value;
    }
}