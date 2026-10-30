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

        // Список доступных скоростей для ComboBox
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

    public partial class HubSettingsWindow : Window
    {
        public bool IsRs485Enabled { get; private set; }
        public bool IsI2CEnabled { get; private set; }
        public List<PortSetting> SelectedPortsWithBaud { get; private set; } = new List<PortSetting>();
        public ObservableCollection<PortItem> Ports { get; set; } = new ObservableCollection<PortItem>();

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

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}