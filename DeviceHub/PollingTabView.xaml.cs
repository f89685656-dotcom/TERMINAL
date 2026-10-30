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
using System.Windows.Threading;

namespace TERMINAL.DeviceHub
{
    public partial class PollingTabView : UserControl
    {
        private PollingEngine _engine = new PollingEngine();
        private DispatcherTimer _uiQueueTimer; // Единый таймер для UI и графика
        private PlotModel _plotModel;
        private Dictionary<ChannelNode, LineSeries> _seriesMap = new Dictionary<ChannelNode, LineSeries>();

        public ObservableCollection<InterfaceNode> Interfaces { get; set; } = new ObservableCollection<InterfaceNode>();

        private bool _isRs485Enabled = true;
        private List<PortSetting> _rs485Ports = new List<PortSetting>
        {
            new PortSetting { PortName = "COM9", BaudRate = 57600 }
        };
        private bool _isI2CEnabled = true;

        public PollingTabView()
        {
            InitializeComponent();
            DataContext = this;

            InitializeTestTree();
            InitializePlot();

            // Единый таймер для обработки очереди, обновления UI и отрисовки графика
            _uiQueueTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _uiQueueTimer.Tick += UiQueueTimer_Tick;
            _uiQueueTimer.Start();

            UpdateStatusText();
        }

        private void UiQueueTimer_Tick(object sender, EventArgs e)
        {
            bool needRedraw = false;

            // 1. Применяем обновления данных из очереди
            while (_engine.UiUpdates.TryDequeue(out var action))
            {
                action.Invoke();
                needRedraw = true;
            }

            // 2. Выводим логи
            while (_engine.LogQueue.TryDequeue(out var msg))
            {
                LogListView.Items.Add(msg);
                if (LogListView.Items.Count > 0)
                    LogListView.ScrollIntoView(LogListView.Items[LogListView.Items.Count - 1]);
            }

            // 3. Добавляем точки на график для выбранных каналов
            if (needRedraw)
            {
                foreach (var iface in Interfaces)
                {
                    foreach (var dev in iface.Devices)
                    {
                        foreach (var ch in dev.Channels)
                        {
                            // Добавляем точку только если канал выбран и для него есть серия
                            if (ch.IsSelected && _seriesMap.TryGetValue(ch, out var series))
                            {
                                // Если это заглушка I2C и значение не менялось (0), можно пропустить,
                                // но пока просто добавляем. Если линия не нужна — снимите галочку.
                                series.Points.Add(new DataPoint(DateTimeAxis.ToDouble(DateTime.Now), ch.Value));

                                if (series.Points.Count > 1000)
                                    series.Points.RemoveAt(0);
                            }
                        }
                    }
                }
                _plotModel.InvalidatePlot(true);
            }
        }

        private void UpdateStatusText()
        {
            var rs485Str = _isRs485Enabled
                ? string.Join(", ", _rs485Ports.Select(p => $"{p.PortName}@{p.BaudRate}"))
                : "Выкл";

            StatusText.Text = $"RS-485: {rs485Str} | I2C: {(_isI2CEnabled ? "Вкл" : "Выкл")}";
        }

        private void InitializeTestTree()
        {
            var rs485 = new InterfaceNode { Name = "RS-485 (Modbus)" };
            var dev1 = new DeviceNode { Name = "Датчик давления", Address = "001" };
            dev1.Channels.Add(new ChannelNode { Name = "Давление", Unit = "бар" });
            dev1.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C" });
            rs485.Devices.Add(dev1);

            var i2c = new InterfaceNode { Name = "I2C (FT260)" };
            var dev2 = new DeviceNode { Name = "Датчик температуры", Address = "017" };
            dev2.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C" });
            i2c.Devices.Add(dev2);

            Interfaces.Add(rs485);
            Interfaces.Add(i2c);

            // Подписка на события для тестовых каналов
            foreach (var iface in Interfaces)
                foreach (var dev in iface.Devices)
                    foreach (var ch in dev.Channels)
                        ch.PropertyChanged += Channel_PropertyChanged;
        }

        private void InitializePlot()
        {
            _plotModel = new PlotModel { Title = "Показания датчиков", Background = OxyColors.Transparent, TitleColor = OxyColors.White };
            _plotModel.Axes.Add(new DateTimeAxis { Position = AxisPosition.Bottom, StringFormat = "HH:mm:ss", Title = "Время", TextColor = OxyColors.White, TitleColor = OxyColors.White });
            _plotModel.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Значение", TextColor = OxyColors.White, TitleColor = OxyColors.White });

            var colors = new[] { OxyColors.Red, OxyColors.DeepSkyBlue, OxyColors.Lime, OxyColors.Orange, OxyColors.Yellow, OxyColors.Purple };
            int colorIndex = 0;

            foreach (var iface in Interfaces)
            {
                foreach (var dev in iface.Devices)
                {
                    foreach (var ch in dev.Channels)
                    {
                        var series = new LineSeries
                        {
                            Title = $"{dev.Name} - {ch.Name}",
                            Color = colors[colorIndex % colors.Length],
                            StrokeThickness = 2,
                            IsVisible = ch.IsSelected // Видимость по галочке
                        };
                        _plotModel.Series.Add(series);
                        _seriesMap[ch] = series;
                        colorIndex++;
                    }
                }
            }
            PlotView.Model = _plotModel;
        }

        private void Channel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChannelNode.IsSelected))
            {
                var channel = (ChannelNode)sender;
                if (_seriesMap.TryGetValue(channel, out var series))
                {
                    series.IsVisible = channel.IsSelected;
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
            ScanButton.IsEnabled = false;
            LogListView.Items.Add("Запуск параллельного сканирования...");

            try
            {
                foreach (var iface in Interfaces)
                    iface.Devices.Clear();

                _plotModel.Series.Clear();
                _seriesMap.Clear();
                _plotModel.InvalidatePlot(true);

                await _engine.ScanAllAsync(Interfaces, _isRs485Enabled, _rs485Ports, _isI2CEnabled);

                var colors = new[] { OxyColors.Red, OxyColors.DeepSkyBlue, OxyColors.Lime, OxyColors.Orange, OxyColors.Yellow, OxyColors.Purple };
                int colorIndex = 0;

                foreach (var iface in Interfaces)
                {
                    foreach (var dev in iface.Devices)
                    {
                        foreach (var ch in dev.Channels)
                        {
                            ch.PropertyChanged += Channel_PropertyChanged;

                            var series = new LineSeries
                            {
                                Title = $"{dev.Name} - {ch.Name}",
                                Color = colors[colorIndex % colors.Length],
                                StrokeThickness = 2,
                                IsVisible = ch.IsSelected
                            };
                            _plotModel.Series.Add(series);
                            _seriesMap[ch] = series;
                            colorIndex++;
                        }
                    }
                }

                _plotModel.InvalidatePlot(true);
                LogListView.Items.Add("Сканирование завершено.");
            }
            catch (Exception ex)
            {
                LogListView.Items.Add($"Ошибка при сканировании: {ex.Message}");
            }
            finally
            {
                ScanButton.IsEnabled = true;
                LogListView.Items.Add("Кнопка 'Поиск' снова активна.");
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Interfaces.Any(i => i.Devices.Any(d => d.IsSelected)))
            {
                LogListView.Items.Add("Нет выбранных устройств для опроса. Сначала выполните поиск.");
                return;
            }

            _engine.StartPolling(Interfaces, _isRs485Enabled, _rs485Ports, _isI2CEnabled, 500);

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            LogListView.Items.Add("Запущен параллельный опрос.");
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _engine.Stop();

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            LogListView.Items.Add("Остановлено.");
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
    }
}