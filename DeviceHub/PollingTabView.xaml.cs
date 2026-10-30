using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TERMINAL.DeviceHub
{
    public partial class PollingTabView : UserControl
    {
        private PollingEngine _engine = new PollingEngine();
        private DispatcherTimer _uiTimer;
        private DispatcherTimer _uiQueueTimer;
        private PlotModel _plotModel;
        private Dictionary<ChannelNode, LineSeries> _seriesMap = new Dictionary<ChannelNode, LineSeries>();

        public ObservableCollection<InterfaceNode> Interfaces { get; set; } = new ObservableCollection<InterfaceNode>();

        // Настройки
        private bool _isRs485Enabled = true;
        private string _rs485Port = "COM4";
        private int _rs485Baud = 9600;
        private bool _isI2CEnabled = true;

        public PollingTabView()
        {
            InitializeComponent();
            DataContext = this;

            InitializeTestTree();
            InitializePlot();

            // Таймер для обновления графика
            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _uiTimer.Tick += UpdateUiFromQueue;

            // Таймер для обработки очереди UiUpdates из движка
            // В конструкторе, где настраивается _uiQueueTimer:
            _uiQueueTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _uiQueueTimer.Tick += (s, e) =>
            {
                while (_engine.UiUpdates.TryDequeue(out var action))
                {
                    action.Invoke();
                }
                while (_engine.LogQueue.TryDequeue(out var msg))
                {
                    LogListView.Items.Add(msg);
                    if (LogListView.Items.Count > 0)
                        LogListView.ScrollIntoView(LogListView.Items[LogListView.Items.Count - 1]);
                }
            };
            _uiQueueTimer.Start();

            // Инициализация статуса
            UpdateStatusText();
        }

        private void UpdateStatusText()
        {
            StatusText.Text = $"RS-485: {(_isRs485Enabled ? _rs485Port : "Выкл")} | I2C: {(_isI2CEnabled ? "Вкл" : "Выкл")}";
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

            var colors = new[] { OxyColors.Red, OxyColors.DeepSkyBlue, OxyColors.Lime, OxyColors.Orange };
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
                            StrokeThickness = 2
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
                _rs485Port = settingsWindow.SelectedPort;
                _rs485Baud = settingsWindow.SelectedBaud; // <-- Вот здесь берется 57600
                _isI2CEnabled = settingsWindow.IsI2CEnabled;

                UpdateStatusText();
                LogListView.Items.Add($"Настройки: Порт={_rs485Port}, Скорость={_rs485Baud}");
            }
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            ScanButton.IsEnabled = false;
            LogListView.Items.Add("Запуск параллельного сканирования...");

            // Очищаем дерево перед поиском
            foreach (var iface in Interfaces)
                iface.Devices.Clear();

            await _engine.ScanAllAsync(Interfaces, _isRs485Enabled, _rs485Port, _rs485Baud, _isI2CEnabled);

            LogListView.Items.Add("Сканирование завершено.");
            ScanButton.IsEnabled = true;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Interfaces.Any(i => i.Devices.Any(d => d.IsSelected)))
            {
                LogListView.Items.Add("Нет выбранных устройств для опроса. Сначала выполните поиск.");
                return;
            }

            // Запускаем опрос
            _engine.StartPolling(Interfaces, _isRs485Enabled, _rs485Port, _rs485Baud, _isI2CEnabled, 500);

            // Таймер для обновления графика
            _uiTimer.Start();

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            LogListView.Items.Add("Запущен параллельный опрос.");
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _engine.Stop();
            _uiTimer.Stop();

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            LogListView.Items.Add("Остановлено.");
        }

        private void UpdateUiFromQueue(object sender, EventArgs e)
        {
            foreach (var iface in Interfaces)
            {
                foreach (var dev in iface.Devices)
                {
                    foreach (var ch in dev.Channels)
                    {
                        if (ch.IsSelected && _seriesMap.TryGetValue(ch, out var series))
                        {
                            series.Points.Add(new DataPoint(DateTimeAxis.ToDouble(DateTime.Now), ch.Value));
                            if (series.Points.Count > 1000) series.Points.RemoveAt(0);
                        }
                    }
                }
            }
            _plotModel.InvalidatePlot(true);
        }
    }
}