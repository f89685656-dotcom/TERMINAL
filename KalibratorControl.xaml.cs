using System;
using System.Windows;
using System.Windows.Controls;
using KalibratorGUI_WPF.MainWindow;
using System.Reflection;

namespace TERMINAL
{
    public partial class KalibratorControl : UserControl
    {
        private KalibratorGUI_WPF.MainWindow.MainWindow _kalibratorWindow;

        public KalibratorControl()
        {
            InitializeComponent();

            // 1. Загружаем ресурсы
            try
            {
                var appResources = Application.Current.Resources;
                void AddDict(string uri)
                {
                    var dict = new ResourceDictionary { Source = new Uri(uri) };
                    if (!appResources.MergedDictionaries.Contains(dict))
                        appResources.MergedDictionaries.Add(dict);
                }
                AddDict("pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml");
                AddDict("pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml");
                AddDict("pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Blue.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Res/i18n/lang.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Res/common.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/FlatComboBox.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/RoundButton.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/QuadraticSlider.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/CheckBoxStyle.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/TabControlFontSize18.xaml");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка загрузки ресурсов: " + ex.Message);
            }

            try { Kalibrator.Core.Configuration.OpenConfig(); } catch { }

            // 3. Создаем окно и показываем его за экраном
            _kalibratorWindow = new KalibratorGUI_WPF.MainWindow.MainWindow();
            _kalibratorWindow.Closing += (s, e) => { e.Cancel = true; };

            _kalibratorWindow.Width = 800;
            _kalibratorWindow.Height = 600;
            _kalibratorWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            _kalibratorWindow.Left = -10000;
            _kalibratorWindow.Top = -10000;
            _kalibratorWindow.ShowInTaskbar = false;
            _kalibratorWindow.Show();

            ContentHost.Content = _kalibratorWindow.Content;
        }

        // ★ МЕТОД ДЛЯ РУЧНОГО ПОИСКА УСТРОЙСТВ ★
        public void SearchBus()
        {
            var scanMethod = typeof(KalibratorGUI_WPF.MainWindow.MainWindow).GetMethod("ScanBus", BindingFlags.NonPublic | BindingFlags.Instance);
            if (scanMethod != null)
            {
                scanMethod.Invoke(_kalibratorWindow, null);
            }
        }

        // ★ МЕТОД ДЛЯ ЗАПУСКА ОПРОСА ★
        public void StartPolling()
        {
            var rtpTab = _kalibratorWindow.FindName("RTPTabContent") as KalibratorGUI_WPF.MainWindow.resources.Tabs.RTPTabContent;
            if (rtpTab != null)
            {
                // 1. Запускаем опрос (StartOprosExecute)
                var startMethod = typeof(KalibratorGUI_WPF.MainWindow.resources.Tabs.RTPTabContent).GetMethod("StartOprosExecute", BindingFlags.NonPublic | BindingFlags.Instance);
                startMethod?.Invoke(rtpTab, new object[] { null, null });
            }
        }

        // ★ МЕТОД ДЛЯ ОСТАНОВКИ ОПРОСА ★
        public void StopPolling()
        {
            var rtpTab = _kalibratorWindow.FindName("RTPTabContent") as KalibratorGUI_WPF.MainWindow.resources.Tabs.RTPTabContent;
            if (rtpTab != null)
            {
                var stopMethod = typeof(KalibratorGUI_WPF.MainWindow.resources.Tabs.RTPTabContent).GetMethod("StopOprosExecute", BindingFlags.NonPublic | BindingFlags.Instance);
                stopMethod?.Invoke(rtpTab, new object[] { null, null });
            }
        }
    }
}