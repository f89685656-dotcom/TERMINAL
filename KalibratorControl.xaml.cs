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

            // 1. Загружаем ресурсы KalibratorGUI
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

            // 4. Забираем контент (КАК ЕСТЬ)
            ContentHost.Content = _kalibratorWindow.Content;

            // 5. Оживляем кнопку поиска и скрываем оригинальную кнопку языка
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                var searchButton = FindChild<Button>(_kalibratorWindow, "BeginSearchOnBusButton");
                if (searchButton != null)
                {
                    searchButton.Command = ApplicationCommands.Find;
                    this.CommandBindings.Add(new CommandBinding(ApplicationCommands.Find,
                        (s, e) => SearchBus(),
                        (s, e) => { e.CanExecute = true; }));
                }

                // Скрываем оригинальную кнопку языка (она все равно глючит)
                var originalLangButton = FindChild<Button>(_kalibratorWindow, "Language_selector");
                if (originalLangButton != null)
                {
                    originalLangButton.Visibility = Visibility.Collapsed;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void Lang_Russian_Click(object sender, RoutedEventArgs e)
        {
            KalibratorGUI_WPF.App.Language = new System.Globalization.CultureInfo("ru-RU");
            RefreshLanguage();
            if (sender is MenuItem mi && mi.Parent is ContextMenu cm) cm.IsOpen = false;
        }

        private void Lang_English_Click(object sender, RoutedEventArgs e)
        {
            KalibratorGUI_WPF.App.Language = new System.Globalization.CultureInfo("en-US");
            RefreshLanguage();
            if (sender is MenuItem mi && mi.Parent is ContextMenu cm) cm.IsOpen = false;
        }

        // ★ НОВЫЙ МЕТОД: Перезагружает ТОЛЬКО словарь, без пересоздания окна ★
        private void RefreshLanguage()
        {
            try
            {
                // 1. Формируем правильный путь к словарю
                string langFileName;
                if (KalibratorGUI_WPF.App.Language.Name == "ru-RU")
                {
                    langFileName = "lang.ru-RU.xaml";
                }
                else
                {
                    langFileName = "lang.xaml";
                }

                var langDict = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/KalibratorGUI_WPF;component/Res/i18n/" + langFileName)
                };

                // 2. Удаляем старый и добавляем новый словарь
                var oldDict = Application.Current.Resources.MergedDictionaries
                    .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("i18n/lang"));

                if (oldDict != null)
                {
                    int index = Application.Current.Resources.MergedDictionaries.IndexOf(oldDict);
                    Application.Current.Resources.MergedDictionaries.Remove(oldDict);
                    Application.Current.Resources.MergedDictionaries.Insert(index, langDict);
                }
                else
                {
                    Application.Current.Resources.MergedDictionaries.Add(langDict);
                }

                // 3. Обновляем визуальное дерево
                ContentHost.UpdateLayout();

                // 4. Пересоздаем OprosTabContent, чтобы обновить StaticResource (Devices/Expressions)
                var oprosTab = _kalibratorWindow.FindName("OprosTab") as TabItem;
                if (oprosTab != null)
                {
                    // Запоминаем текущую вкладку
                    var tabControl = FindChild<TabControl>(_kalibratorWindow, "WorkModeSelectionTabControl");
                    int savedIndex = tabControl?.SelectedIndex ?? 0;

                    // Пересоздаем контент
                    var newContent = new KalibratorGUI_WPF.MainWindow.resources.Tabs.OprosTabContent();
                    oprosTab.Content = newContent;

                    // Возвращаем вкладку на место
                    if (tabControl != null)
                    {
                        tabControl.SelectedIndex = savedIndex;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка обновления языка: " + ex.Message);
            }
        }


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

        public void SearchBus()
        {
            var scanMethod = typeof(KalibratorGUI_WPF.MainWindow.MainWindow).GetMethod("ScanBus", BindingFlags.NonPublic | BindingFlags.Instance);
            scanMethod?.Invoke(_kalibratorWindow, null);
        }

        private void FakeLangButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                btn.ContextMenu.IsOpen = true;
            }
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
    }
}