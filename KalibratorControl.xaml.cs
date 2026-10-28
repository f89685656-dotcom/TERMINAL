using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

                // Стили MahApps
                AddDict("pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml");
                AddDict("pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml");
                AddDict("pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Blue.xaml");

                // Локализация и стили KalibratorGUI
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Res/i18n/lang.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Res/common.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/FlatComboBox.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/RoundButton.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/QuadraticSlider.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/CheckBoxStyle.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/TabControlFontSize18.xaml");

                // ИСПРАВЛЕНИЕ ДЛЯ ИКОНОК
                var fontAwesomeFamily = new FontFamily("pack://application:,,,/FontAwesome.WPF;component/#FontAwesome");
                appResources.Add("FontAwesome", fontAwesomeFamily);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка загрузки ресурсов: " + ex.Message);
            }

            // 2. Конфигурация
            try { Kalibrator.Core.Configuration.OpenConfig(); } catch { }

            // 3. Создаем окно
            _kalibratorWindow = new KalibratorGUI_WPF.MainWindow.MainWindow();
            _kalibratorWindow.Closing += (s, e) => { e.Cancel = true; };

            _kalibratorWindow.Width = 800;
            _kalibratorWindow.Height = 600;
            _kalibratorWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            _kalibratorWindow.Left = -10000;
            _kalibratorWindow.Top = -10000;
            _kalibratorWindow.ShowInTaskbar = false;
            _kalibratorWindow.Show();

            // 4. Забираем контент
            ContentHost.Content = _kalibratorWindow.Content;

            // 5. Оживляем кнопку языка (перебиваем DropDownButtonBehavior)
            var langButton = FindChild<Button>(_kalibratorWindow, "Language_selector");
            if (langButton != null)
            {
                var contextMenu = langButton.ContextMenu;
                if (contextMenu != null)
                {
                    langButton.Click += (s, e) =>
                    {
                        contextMenu.PlacementTarget = langButton;
                        contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                        contextMenu.IsOpen = true;
                    };
                }
            }
        } // <-- ЗДЕСЬ ЗАКРЫВАЕТСЯ КОНСТРУКТОР

        private static T FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            if (parent == null) return null;
            T foundChild = null;
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                T childType = child as T;
                if (childType == null)
                {
                    foundChild = FindChild<T>(child, childName);
                    if (foundChild != null) break;
                }
                else if (!string.IsNullOrEmpty(childName))
                {
                    var frameworkElement = child as FrameworkElement;
                    if (frameworkElement != null && frameworkElement.Name == childName)
                    {
                        foundChild = (T)child;
                        break;
                    }
                }
                else
                {
                    foundChild = (T)child;
                    break;
                }
            }
            return foundChild;
        }

        // --- Методы для управления ---
        public void SearchBus()
        {
            var scanMethod = typeof(KalibratorGUI_WPF.MainWindow.MainWindow).GetMethod("ScanBus", BindingFlags.NonPublic | BindingFlags.Instance);
            scanMethod?.Invoke(_kalibratorWindow, null);
        }

        public void StartPolling()
        {
            var rtpTab = _kalibratorWindow.FindName("RTPTabContent") as KalibratorGUI_WPF.MainWindow.resources.Tabs.RTPTabContent;
            if (rtpTab != null)
            {
                var startMethod = typeof(KalibratorGUI_WPF.MainWindow.resources.Tabs.RTPTabContent).GetMethod("StartOprosExecute", BindingFlags.NonPublic | BindingFlags.Instance);
                startMethod?.Invoke(rtpTab, new object[] { null, null });
            }
        }

        public void StopPolling()
        {
            var rtpTab = _kalibratorWindow.FindName("RTPTabContent") as KalibratorGUI_WPF.MainWindow.resources.Tabs.RTPTabContent;
            if (rtpTab != null)
            {
                var stopMethod = typeof(KalibratorGUI_WPF.MainWindow.resources.Tabs.RTPTabContent).GetMethod("StopOprosExecute", BindingFlags.NonPublic | BindingFlags.Instance);
                stopMethod?.Invoke(rtpTab, new object[] { null, null });
            }
        }
    } // <-- ЗДЕСЬ ЗАКРЫВАЕТСЯ КЛАСС
} // <-- ЗДЕСЬ ЗАКРЫВАЕТСЯ ПРОСТРАНСТВО ИМЕН