using System.IO.Ports;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TERMINAL.DeviceHub
{
    public partial class HubSettingsWindow : Window
    {
        public bool IsRs485Enabled { get; private set; }
        public string SelectedPort { get; private set; }
        public int SelectedBaud { get; private set; }
        public bool IsI2CEnabled { get; private set; }
        public string SelectedI2CAdapter { get; private set; }

        public HubSettingsWindow()
        {
            InitializeComponent();

            // Заполняем список COM-портов
            var ports = SerialPort.GetPortNames();
            Rs485PortComboBox.ItemsSource = ports;
            if (ports.Any()) Rs485PortComboBox.SelectedIndex = 0;

            // Заполняем список FT260
            I2CAdapterComboBox.Items.Add("FT260 [7&5fcd268&0&0000]");
            I2CAdapterComboBox.SelectedIndex = 0;

            // ★ ПРИНУДИТЕЛЬНО УСТАНАВЛИВАЕМ СКОРОСТЬ 57600 ★
            Rs485BaudComboBox.SelectedIndex = 3; // 0-9600, 1-19200, 2-38400, 3-57600, 4-115200
            SelectedBaud = 57600;
        }

        private void Rs485BaudComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Rs485BaudComboBox.SelectedItem is ComboBoxItem item)
            {
                if (int.TryParse(item.Content.ToString(), out int baud))
                {
                    SelectedBaud = baud;
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            IsRs485Enabled = Rs485CheckBox.IsChecked == true;
            SelectedPort = Rs485PortComboBox.SelectedItem?.ToString() ?? "COM1";
            IsI2CEnabled = I2CCheckBox.IsChecked == true;
            SelectedI2CAdapter = I2CAdapterComboBox.SelectedItem?.ToString() ?? "";

            // На всякий случай еще раз читаем скорость
            if (Rs485BaudComboBox.SelectedItem is ComboBoxItem baudItem)
            {
                if (int.TryParse(baudItem.Content.ToString(), out int b))
                    SelectedBaud = b;
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}