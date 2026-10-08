using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
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
        private readonly string _dataFolder;

        public SummaryWindow(string dataFolder)
        {
            InitializeComponent();

            _dataFolder = dataFolder;

            // При открытии сразу читаем .xlsx из папки
            Loaded += (s, e) => RefreshFromXlsx();
        }

        // ─── Перечитать .xlsx из папки ───
        private void RefreshFromXlsx()
        {
            if (_isLoadedFromCsv) return;

            if (string.IsNullOrWhiteSpace(_dataFolder) || !Directory.Exists(_dataFolder))
            {
                MessageBox.Show($"Папка не найдена:\n{_dataFolder}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Имя партии — из родительской папки
                string batchName = new DirectoryInfo(
                    Directory.GetParent(_dataFolder)?.FullName ?? _dataFolder).Name;

                _currentBatch = SummaryService.ReadAllSensorsFromFolder(
                    _dataFolder, batchName);

                BatchNameText.Text = $"Партия: {_currentBatch.BatchName}";
                UpdateSummaryGrid();
                UpdateDetailPanel();

                RefreshButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка чтения .xlsx:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

            try
            {
                string batchName = _currentBatch.BatchName;
                string fileName = $"{batchName}_сводка.csv";
                string fullPath = Path.Combine(_dataFolder, fileName);

                CsvService.SaveToCsv(_currentBatch, fullPath);

                MessageBox.Show($"Сохранено:\n{fullPath}",
                    "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения CSV:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─── Обновить (перечитать .xlsx) ───
        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadedFromCsv)
            {
                MessageBox.Show("Обновление недоступно для архивных данных.");
                return;
            }

            RefreshFromXlsx();
        }

        // ─── Печать ───
        private void Print_Click(object sender, RoutedEventArgs e)
        {
            var pd = new System.Windows.Controls.PrintDialog();
            if (pd.ShowDialog() == true)
            {
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

        // ─── Заполнить сводку ───
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

            if (rows.Count > 0)
                SummaryGrid.SelectedIndex = 0;
        }

        // ─── Заполнить панель деталей ───
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

            int tabIndex = DetailTabs.SelectedIndex;

            TableData? table = tabIndex switch
            {
                0 => row.Source.GradHysteresis,
                1 => row.Source.CheckHysteresis,
                2 => row.Source.Errors,
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

            // ВРЕМЕННАЯ заглушка — показываем размеры и заголовки
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
    }

    // ─── Модель строки для сводки ───
    public class SummaryRow
    {
        public int Number { get; set; }
        public double? GradDescription { get; set; }
        public double? Check1 { get; set; }
        public double? Check2 { get; set; }
        public double? Check3 { get; set; }
        public double? Check4 { get; set; }
        public double? Check5 { get; set; }
        public SensorSummary Source { get; set; } = null!;
    }
}