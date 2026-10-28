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

        // ★ ОБРАБОТЧИКИ ДЛЯ НАШЕЙ НОВОЙ КНОПКИ ЯЗЫКА ★
        // ★ ОБРАБОТЧИКИ ДЛЯ НАШЕЙ НОВОЙ КНОПКИ ЯЗЫКА ★
        private void Lang_Russian_Click(object sender, RoutedEventArgs e)
        {
            // 1. Меняем язык (App.Language внутри KalibratorGUI обновит lang.xaml)
            KalibratorGUI_WPF.App.Language = new System.Globalization.CultureInfo("ru-RU");

            // 2. Принудительно перезагружаем контент (создаем MainWindow заново)
            ReloadKalibratorContent();

            if (sender is MenuItem mi && mi.Parent is ContextMenu cm) cm.IsOpen = false;
        }

        private void Lang_English_Click(object sender, RoutedEventArgs e)
        {
            KalibratorGUI_WPF.App.Language = new System.Globalization.CultureInfo("en-US");
            ReloadKalibratorContent();
            if (sender is MenuItem mi && mi.Parent is ContextMenu cm) cm.IsOpen = false;
        }

        // ★ НОВЫЙ МЕТОД: Перезагружает контент, чтобы обновить StaticResource
        private void ReloadKalibratorContent()
        {
            // ★ 1. ОТПИСЫВАЕМ СТАРОЕ ОКНО ОТ ВСЕХ СОБЫТИЙ ★
            if (_kalibratorWindow != null)
            {
                try
                {
                    // Находим метод, который вызывается при изменении коллекции
                    var method = typeof(KalibratorGUI_WPF.MainWindow.MainWindow).GetMethod(
                        "AbstractDevicesCollection_CollectionChanged",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (method != null)
                    {
                        var handler = (System.Collections.Specialized.NotifyCollectionChangedEventHandler)
                            Delegate.CreateDelegate(
                                typeof(System.Collections.Specialized.NotifyCollectionChangedEventHandler),
                                _kalibratorWindow,
                                method);

                        // Отписываем от CollectionChanged
                        KalibratorGUI_WPF.General.DevicesCollection.CollectionChanged -= handler;
                    }
                }
                catch { /* Игнорируем ошибки отписки */ }

                // Закрываем окно (разрешаем закрытие)
                _kalibratorWindow.Closing -= (s, ev) => { ev.Cancel = true; };
                _kalibratorWindow.Close();
                _kalibratorWindow = null;
            }

            // ★ 2. ОЧИЩАЕМ КОЛЛЕКЦИЮ УСТРОЙСТВ, ЧТОБЫ НОВОЕ ОКНО НАЧАЛО С ЧИСТОГО ЛИСТА ★
            KalibratorGUI_WPF.General.DevicesCollection.Clear();

            // ★ 3. СОЗДАЕМ НОВОЕ ОКНО ★
            _kalibratorWindow = new KalibratorGUI_WPF.MainWindow.MainWindow();
            _kalibratorWindow.Closing += (s, ev) => { ev.Cancel = true; }; // Запрещаем закрытие
            _kalibratorWindow.Width = 800;
            _kalibratorWindow.Height = 600;
            _kalibratorWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            _kalibratorWindow.Left = -10000;
            _kalibratorWindow.Top = -10000;
            _kalibratorWindow.ShowInTaskbar = false;
            _kalibratorWindow.Show();

            ContentHost.Content = _kalibratorWindow.Content;
            ContentHost.UpdateLayout();

            // ★ 4. ПРИНУДИТЕЛЬНО ПЕРЕКЛЮЧАЕМСЯ НА ВКЛАДКУ "ОПРОС" (ИЛИ ТУ, ЧТО НУЖНО) ★
            // Находим TabControl в новом окне и ставим индекс 2 (Опрос)
            var tabControl = FindChild<TabControl>(_kalibratorWindow, "WorkModeSelectionTabControl");
            if (tabControl != null)
            {
                tabControl.SelectedIndex = 2; // 0 - Edit, 1 - Calibration, 2 - Opros
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