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

    // ★ КЛАСС ДЛЯ I2C-УСТРОЙСТВ ★
    public class I2CDeviceItem
    {
        public string Name { get; set; }
        public override string ToString() => Name;
    }

    public partial class HubSettingsWindow : Window
    {
        public bool IsRs485Enabled { get; private set; }
        public bool IsI2CEnabled { get; private set; }
        public List<PortSetting> SelectedPortsWithBaud { get; private set; } = new List<PortSetting>();
        public ObservableCollection<PortItem> Ports { get; set; } = new ObservableCollection<PortItem>();

        // ★ СВОЙСТВО ДЛЯ ВЫБРАННОГО I2C-ПЕРЕХОДНИКА ★
        public string SelectedI2CDevice { get; private set; } = string.Empty;

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

            // ★ ЗАГРУЖАЕМ СПИСОК FT260 ★
            LoadI2CDevices();
        }

        // ★ МЕТОД ЗАГРУЗКИ СПИСКА FT260 ★
        private void LoadI2CDevices()
        {
            try
            {
                // ★ ЗДЕСЬ ВЫЗЫВАЕМ МЕТОД ИЗ FT260_I2CDotNet ★
                // Например: var devices = FT260_I2CDotNet.FT260Device.GetDevices();
                // I2CDeviceComboBox.ItemsSource = devices;

                // ЗАГЛУШКА (пока нет реального метода):
                var devices = new List<I2CDeviceItem>
                {
                    new I2CDeviceItem { Name = "FT260 [7&5fcd268&0&0000]" }
                };

                I2CDeviceComboBox.ItemsSource = devices;
                if (devices.Any()) I2CDeviceComboBox.SelectedIndex = 0;
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

            // ★ СОХРАНЯЕМ ВЫБРАННЫЙ I2C-ПЕРЕХОДНИК ★
            if (IsI2CEnabled && I2CDeviceComboBox.SelectedItem is I2CDeviceItem selectedI2C)
            {
                SelectedI2CDevice = selectedI2C.Name;
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}