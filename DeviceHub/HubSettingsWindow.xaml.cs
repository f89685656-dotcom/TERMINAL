using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
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
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public int BaudRate
        {
            get => _baudRate;
            set
            {
                _baudRate = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BaudRate)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    // ★ КЛАСС ДЛЯ I2C-УСТРОЙСТВ (ДОБАВИЛИ INotifyPropertyChanged и IsSelected) ★
    public class I2CDeviceItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string Name { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public override string ToString() => Name;
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class HubSettingsWindow : Window
    {
        public bool IsRs485Enabled { get; private set; }
        public bool IsI2CEnabled { get; private set; }
        public List<PortSetting> SelectedPortsWithBaud { get; private set; } = new List<PortSetting>();

        // ★ ВОТ ЭТО СВОЙСТВО БУДЕТ ХРАНИТЬ СПИСОК ВЫБРАННЫХ ПЕРЕХОДНИКОВ ★
        public List<string> SelectedI2CDevices { get; private set; } = new List<string>();

        public ObservableCollection<PortItem> Ports { get; set; } = new ObservableCollection<PortItem>();
        public ObservableCollection<I2CDeviceItem> I2CDevices { get; set; } = new ObservableCollection<I2CDeviceItem>();

        public HubSettingsWindow()
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
                Ports.Add(new PortItem { Name = port, IsSelected = false, BaudRate = 57600 });
            }

            var lastPort = "COM39";
            var item = Ports.FirstOrDefault(p => p.Name == lastPort);
            if (item != null) item.IsSelected = true;
            else if (Ports.Any()) Ports[0].IsSelected = true;

            Rs485PortsList.ItemsSource = Ports;

            // ★ ПРИВЯЗЫВАЕМ СПИСОК I2C
            I2CDevicesList.ItemsSource = I2CDevices;

            // ★ ЗАГРУЖАЕМ СПИСОК FT260 ★
            LoadI2CDevices();
        }

        // ★ МЕТОД ЗАГРУЗКИ СПИСКА FT260 ★
        private void LoadI2CDevices()
        {
            try
            {
                I2CDevices.Clear();

                // ★ ЗДЕСЬ ДОЛЖЕН БЫТЬ ВЫЗОВ РЕАЛЬНОГО МЕТОДА ИЗ FT260_I2CDotNet ★
                // Например: var devices = FT260_I2CDotNet.FT260Device.GetDevices();
                // foreach (var d in devices) I2CDevices.Add(new I2CDeviceItem { Name = d, IsSelected = true });

                // ★ ВРЕМЕННАЯ ЗАГЛУШКА (2 устройства для теста) ★
                I2CDevices.Add(new I2CDeviceItem { Name = "FT260 [7&5fcd268&0&0000]", IsSelected = true });
                I2CDevices.Add(new I2CDeviceItem { Name = "FT260 [7&1234567&0&0001]", IsSelected = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка поиска FT260: {ex.Message}");
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            IsRs485Enabled = Rs485CheckBox.IsChecked == true;
            IsI2CEnabled = I2CCheckBox.IsChecked == true;

            SelectedPortsWithBaud.Clear();
            foreach (var p in Ports.Where(x => x.IsSelected))
            {
                SelectedPortsWithBaud.Add(new PortSetting { PortName = p.Name, BaudRate = p.BaudRate });
            }

            if (SelectedPortsWithBaud.Count == 0 && IsRs485Enabled)
            {
                MessageBox.Show("Выберите хотя бы один COM-порт для RS-485.");
                return;
            }

            // ★ СОХРАНЯЕМ СПИСОК ВЫБРАННЫХ I2C
            SelectedI2CDevices.Clear();
            if (IsI2CEnabled)
            {
                foreach (var dev in I2CDevices.Where(d => d.IsSelected))
                {
                    SelectedI2CDevices.Add(dev.Name);
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