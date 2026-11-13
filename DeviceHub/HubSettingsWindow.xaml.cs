using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace TERMINAL.DeviceHub
{
    public class PortSetting
    {
        public string PortName { get; set; }
        public int BaudRate { get; set; }
    }

    public class PortItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private int _baudRate = 57600;

        public string Name { get; set; }
        public List<int> AvailableBaudRates { get; } = new List<int> { 9600, 19200, 38400, 57600, 115200 };

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public int BaudRate
        {
            get => _baudRate;
            set { _baudRate = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BaudRate))); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class I2CDeviceItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string Name { get; set; }
        public string DevicePath { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public override string ToString() => Name;
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class HubSettingsWindow : Window
    {
        public bool IsRs485Enabled { get; private set; }
        public bool IsUartEnabled { get; private set; }
        public bool IsI2CEnabled { get; private set; }

        public List<PortSetting> SelectedPortsWithBaud { get; private set; } = new List<PortSetting>();
        public List<PortSetting> SelectedUartPortsWithBaud { get; private set; } = new List<PortSetting>();
        public List<string> SelectedI2CDevices { get; private set; } = new List<string>();

        public ObservableCollection<PortItem> Ports { get; set; } = new ObservableCollection<PortItem>();
        public ObservableCollection<PortItem> UartPorts { get; set; } = new ObservableCollection<PortItem>();
        public ObservableCollection<I2CDeviceItem> I2CDevices { get; set; } = new ObservableCollection<I2CDeviceItem>();

        // ★ НОВЫЙ КОНСТРУКТОР С ПАРАМЕТРАМИ (для загрузки сохраненных настроек) ★
        public HubSettingsWindow(bool isRs485, List<PortSetting> rs485Ports, bool isUart, List<PortSetting> uartPorts, List<string> i2cDevices)
        {
            InitializeComponent();

            var ports = SerialPort.GetPortNames().ToList();
            ports = ports.OrderBy(p =>
            {
                var match = Regex.Match(p, @"\d+");
                return match.Success ? int.Parse(match.Value) : 0;
            }).ToList();

            foreach (var port in ports)
            {
                // Восстанавливаем галочки для RS-485
                var rs485Setting = rs485Ports?.FirstOrDefault(p => p.PortName == port);
                Ports.Add(new PortItem
                {
                    Name = port,
                    IsSelected = rs485Setting != null,
                    BaudRate = rs485Setting?.BaudRate ?? 57600
                });

                // Восстанавливаем галочки для UART
                var uartSetting = uartPorts?.FirstOrDefault(p => p.PortName == port);
                UartPorts.Add(new PortItem
                {
                    Name = port,
                    IsSelected = uartSetting != null,
                    BaudRate = uartSetting?.BaudRate ?? 57600
                });
            }

            Rs485CheckBox.IsChecked = isRs485;
            UartCheckBox.IsChecked = isUart;

            Rs485PortsList.ItemsSource = Ports;
            UartPortsList.ItemsSource = UartPorts;
            I2CDevicesList.ItemsSource = I2CDevices;

            LoadI2CDevices(i2cDevices);
        }

        // ★ ОБНОВЛЕННЫЙ МЕТОД (принимает сохраненные I2C пути) ★
        private void LoadI2CDevices(List<string> savedDevices)
        {
            try
            {
                I2CDevices.Clear();

                FT260Native.FT260_CreateDeviceList(out uint deviceCount);
                int count = (int)deviceCount;

                if (count == 0)
                {
                    // Не показываем MessageBox при старте, если ничего не найдено, чтобы не мешать
                    return;
                }

                int displayIndex = 1;
                for (uint i = 0; i < count; i++)
                {
                    var sbPath = new StringBuilder(256);
                    int res = FT260Native.FT260_GetDevicePath(sbPath, (uint)sbPath.Capacity, i);

                    if (res == FT260Native.FT260_OK)
                    {
                        string path = sbPath.ToString();
                        string lowerPath = path.ToLower();

                        if (!lowerPath.Contains("vid_0403") || !lowerPath.Contains("pid_6030"))
                            continue;

                        if (lowerPath.Contains("mi_01") && !lowerPath.Contains("mi_00"))
                            continue;

                        string displayName = "Unknown";
                        var match = Regex.Match(path, @"#([0-9a-fA-F&]+)#");
                        if (match.Success)
                        {
                            displayName = match.Groups[1].Value;
                        }
                        else if (path.Length > 8)
                        {
                            displayName = path.Substring(path.Length - 8);
                        }

                        // Если сохраненных путей нет (первый запуск), выбираем всё по умолчанию.
                        // Если есть — выбираем только те, что были сохранены.
                        bool isSelected = savedDevices == null || savedDevices.Count == 0 || savedDevices.Contains(path);

                        I2CDevices.Add(new I2CDeviceItem
                        {
                            Name = $"FT260 [{displayIndex}] ({displayName})",
                            IsSelected = isSelected,
                            DevicePath = path
                        });
                        displayIndex++;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка поиска FT260: {ex.Message}");
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            IsRs485Enabled = Rs485CheckBox.IsChecked == true;
            IsUartEnabled = UartCheckBox.IsChecked == true;
            IsI2CEnabled = I2CCheckBox.IsChecked == true;

            SelectedPortsWithBaud.Clear();
            SelectedUartPortsWithBaud.Clear();

            if (IsRs485Enabled)
            {
                foreach (var p in Ports.Where(x => x.IsSelected))
                {
                    SelectedPortsWithBaud.Add(new PortSetting { PortName = p.Name, BaudRate = p.BaudRate });
                }
            }

            if (IsUartEnabled)
            {
                foreach (var p in UartPorts.Where(x => x.IsSelected))
                {
                    SelectedUartPortsWithBaud.Add(new PortSetting { PortName = p.Name, BaudRate = p.BaudRate });
                }
            }

            // Проверка на дубли
            if (SelectedPortsWithBaud.Any(p => SelectedUartPortsWithBaud.Any(u => u.PortName == p.PortName)))
            {
                MessageBox.Show("Один и тот же порт не может быть одновременно в RS-485 и UART.");
                return;
            }

            SelectedI2CDevices.Clear();
            if (IsI2CEnabled)
            {
                foreach (var dev in I2CDevices.Where(d => d.IsSelected))
                {
                    SelectedI2CDevices.Add(dev.DevicePath);
                }

                if (SelectedI2CDevices.Count == 0)
                {
                    MessageBox.Show("Выберите хотя бы один I2C переходник.");
                    return;
                }
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}