using System;
using System.Windows;
using System.Windows.Controls;
using KalibratorGUI_WPF.MainWindow;

namespace TERMINAL
{
    public partial class KalibratorControl : UserControl
    {
        public KalibratorControl()
        {
            InitializeComponent();

            // 1. Инициализируем конфигурацию
            try
            {
                Kalibrator.Core.Configuration.OpenConfig();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка конфигурации: " + ex.Message);
            }

            // 2. Загружаем словари ресурсов KalibratorGUI в ГЛОБАЛЬНЫЕ ресурсы приложения
            try
            {
                var appResources = Application.Current.Resources;

                void AddDict(string uri)
                {
                    var dict = new ResourceDictionary { Source = new Uri(uri) };
                    if (!appResources.MergedDictionaries.Contains(dict))
                    {
                        appResources.MergedDictionaries.Add(dict);
                    }
                }

                // Базовые стили MahApps
                AddDict("pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml");
                AddDict("pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml");
                AddDict("pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Blue.xaml");

                // Локализация (решит ошибку "res было null")
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Res/i18n/lang.xaml");

                // Общие ресурсы KalibratorGUI
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Res/common.xaml");

                // Стили KalibratorGUI
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/FlatComboBox.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/RoundButton.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/QuadraticSlider.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/CheckBoxStyle.xaml");
                AddDict("pack://application:,,,/KalibratorGUI_WPF;component/Styles/TabControlFontSize18.xaml");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка загрузки ресурсов KalibratorGUI: " + ex.Message);
            }

            // 3. Создаем окно
            var kalibratorWindow = new KalibratorGUI_WPF.MainWindow.MainWindow();

            // Извлекаем содержимое
            ContentHost.Content = kalibratorWindow.Content;

            Unloaded += (s, e) =>
            {
                if (ContentHost.Content == kalibratorWindow.Content)
                {
                    ContentHost.Content = null;
                }
            };
        }
    }
}