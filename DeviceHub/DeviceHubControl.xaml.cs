using System.Windows.Controls;

namespace TERMINAL.DeviceHub
{
    public partial class DeviceHubControl : UserControl
    {
        public DeviceHubControl()
        {
            InitializeComponent();
        }

        // ★ МЕТОД ВНУТРИ КЛАССА ★
        public void CloseAllPorts()
        {
            // Если PollingTabView является частью DeviceHubControl:
            var pollingView = this.FindName("PollingView") as PollingTabView;
            if (pollingView != null)
            {
                pollingView.ClosePort();
            }
        }
    }
}