using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TERMINAL.Models;
using TERMINAL.Services;

namespace TERMINAL.Windows
{
    public partial class SummaryWindow : Window
    {
        private SummaryBatch? _currentBatch;
        private bool _isLoadedFromCsv = false;

        public SummaryWindow()
        {
            InitializeComponent();
        }

        // ─── Открыть CSV прошлой партии ───
        private void OpenCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "CSV файлы (*.csv)|*.csv|Все файлы (*.*)|*.*",
                Title = "Открыть сводку"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                _currentBatch = CsvService.LoadFromCsv(dlg.FileName);
                _isLoadedFromCsv = true;

                // Блокируем «Обновить» — данные из архива
                RefreshButton.IsEnabled = false;

                BatchNameText.Text = $"Партия: {_currentBatch.BatchName}";
                UpdateSummaryGrid();
                UpdateDetailPanel();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки CSV:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─── Сохранить CSV ───
        private void SaveCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBatch == null)
            {
                MessageBox.Show("Нет данных для сохранения.");
                return;
            }

            // Путь: та же папка, что SavePathBox + имя партии
            // TODO: получить SavePathBox из MainWindow — см. ниже
            MessageBox.Show("Сохранение CSV — TODO (нужно получить путь из MainWindow).");
        }

        // ─── Обновить (перечитать .xlsx) ───
        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadedFromCsv)
            {
                MessageBox.Show("Обновление недоступно для архивных данных.");
                return;
            }

            // TODO: перечитать .xlsx — вызов из MainWindow
            MessageBox.Show("Обновление — TODO.");
        }

        // ─── Печать ───
        private void Print_Click(object sender, RoutedEventArgs e)
        {
            var pd = new System.Windows.Controls.PrintDialog();
            if (pd.ShowDialog() == true)
            {
                // TODO: сформировать документ для печати
                MessageBox.Show("Печать — TODO.");
            }
        }

        // ─── Выбор датчика в сводке ───
        private void SummaryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetailPanel();
        }

        // ─── Переключение вкладки деталей ───
        private void DetailTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetailPanel();
        }

        // ─── Вспомогательные ───
        private void UpdateSummaryGrid()
        {
            if (_currentBatch == null)
            {
                SummaryGrid.ItemsSource = null;
                return;
            }

            var rows = _currentBatch.Sensors
                .OrderBy(s => s.Number)
                .Select(s => new SummaryRow
                {
                    Number = s.Number,
                    GradDescription = s.GradDescription,
                    Check1 = s.CheckDescriptions.Length > 0 ? s.CheckDescriptions[0] : null,
                    Check2 = s.CheckDescriptions.Length > 1 ? s.CheckDescriptions[1] : null,
                    Check3 = s.CheckDescriptions.Length > 2 ? s.CheckDescriptions[2] : null,
                    Check4 = s.CheckDescriptions.Length > 3 ? s.CheckDescriptions[3] : null,
                    Check5 = s.CheckDescriptions.Length > 4 ? s.CheckDescriptions[4] : null,
                    Source = s
                })
                .ToList();

            SummaryGrid.ItemsSource = rows;

            // Автовыбор первой строки
            if (rows.Count > 0)
                SummaryGrid.SelectedIndex = 0;
        }

        private void UpdateDetailPanel()
        {
            if (SummaryGrid.SelectedItem is not SummaryRow row)
            {
                DetailHeaderText.Text = "Детали: —";
                DetailContent.Content = null;
                return;
            }

            DetailHeaderText.Text = $"Детали: Датчик №{row.Number}    " +
                                    $"Опис.град.: {row.GradDescription?.ToString("0.0000") ?? "—"}";

            // Какую вкладку смотрим
            int tabIndex = DetailTabs.SelectedIndex;

            TableData? table = tabIndex switch
            {
                0 => row.Source.GradHysteresis,   // Гистерезис
                1 => row.Source.CheckHysteresis,  // Проверка
                2 => row.Source.Errors,           // Погрешность
                _ => null
            };

            if (table == null)
            {
                DetailContent.Content = new TextBlock
                {
                    Text = "Нет данных",
                    Margin = new Thickness(10),
                    FontStyle = FontStyles.Italic
                };
                return;
            }

            // Заглушка — просто показываем размеры
            DetailContent.Content = new TextBlock
            {
                Text = $"Таблица: {table.Values.GetLength(0)} × {table.Values.GetLength(1)}\n" +
                       $"Температуры: {string.Join(", ", table.ColumnHeaders)}\n" +
                       $"Давления: {string.Join(", ", table.RowHeaders)}",
                Margin = new Thickness(10),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            };
        }

        public class SummaryRow
        {
            public int Number { get; set; }
            public double? GradDescription { get; set; }
            public double? Check1 { get; set; }
            public double? Check2 { get; set; }
            public double? Check3 { get; set; }
            public double? Check4 { get; set; }
            public double? Check5 { get; set; }

            // Ссылка на оригинальный датчик — для деталей
            public SensorSummary Source { get; set; } = null!;
        }
    }
}