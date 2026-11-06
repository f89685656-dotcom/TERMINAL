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

                var fontAwesomeFamily = new FontFamily("pack://application:,,,/FontAwesome.WPF;component/#FontAwesome");
                appResources.Add("FontAwesome", fontAwesomeFamily);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка загрузки ресурсов: " + ex.Message);
            }

            // 2. Открываем конфигурацию и применяем сохраненный язык
            try
            {
                Kalibrator.Core.Configuration.OpenConfig();

                // ★ ПРИМЕНЯЕМ СОХРАНЕННЫЙ ЯЗЫК ★
                string savedLang = Kalibrator.Core.Configuration.Get("DefaultLanguage");
                if (!string.IsNullOrEmpty(savedLang))
                {
                    KalibratorGUI_WPF.App.Language = new System.Globalization.CultureInfo(savedLang);
                }
            }
            catch { }

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

            ContentHost.Content = _kalibratorWindow.Content;

            // 4. Оживляем кнопки (поиск)
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
            // Сохраняем язык
            try
            {
                Kalibrator.Core.Configuration.Set("DefaultLanguage", "ru-RU");
                Kalibrator.Core.Configuration.SaveConfig();
            }
            catch { }

            // Закрываем меню
            if (sender is MenuItem mi && mi.Parent is ContextMenu cm) cm.IsOpen = false;

            // ★ ПЕРЕЗАПУСКАЕМ ПРИЛОЖЕНИЕ ★
            System.Windows.Application.Current.Shutdown();
            System.Diagnostics.Process.Start(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        private void Lang_English_Click(object sender, RoutedEventArgs e)
        {
            // Сохраняем язык
            try
            {
                Kalibrator.Core.Configuration.Set("DefaultLanguage", "en-US");
                Kalibrator.Core.Configuration.SaveConfig();
            }
            catch { }

            // Закрываем меню
            if (sender is MenuItem mi && mi.Parent is ContextMenu cm) cm.IsOpen = false;

            // ★ ПЕРЕЗАПУСКАЕМ ПРИЛОЖЕНИЕ ★
            System.Windows.Application.Current.Shutdown();
            System.Diagnostics.Process.Start(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        // ★ НОВЫЙ МЕТОД: Перезагружает ТОЛЬКО словарь, без пересоздания окна ★
        private void RefreshLanguage()
        {
            try
            {
                // 1. Загружаем словарь
                string langFileName = KalibratorGUI_WPF.App.Language.Name == "ru-RU"
                    ? "lang.ru-RU.xaml"
                    : "lang.xaml";

                var langDict = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/KalibratorGUI_WPF;component/Res/i18n/" + langFileName)
                };

                // 2. Обновляем словарь
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

                // 4. Пересоздаем TreeViewModule в OprosTabContent и CalibrationTabContent
                try
                {
                    var oprosTab = _kalibratorWindow.FindName("OprosTab") as TabItem;
                    if (oprosTab?.Content is KalibratorGUI_WPF.MainWindow.resources.Tabs.OprosTabContent oprosContent)
                    {
                        var reloadMethod = oprosContent.GetType().GetMethod("ReloadTreeModule");
                        reloadMethod?.Invoke(oprosContent, null);
                    }

                    var calibTab = _kalibratorWindow.FindName("CalibrationTab") as TabItem;
                    if (calibTab?.Content is KalibratorGUI_WPF.MainWindow.resources.Tabs.CalibrationTabContent calibContent)
                    {
                        var reloadMethod = calibContent.GetType().GetMethod("ReloadTreeModule");
                        reloadMethod?.Invoke(calibContent, null);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Ошибка перезагрузки дерева: " + ex.Message);
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