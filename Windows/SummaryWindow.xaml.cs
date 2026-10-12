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

            Loaded += (s, e) => RefreshFromXlsx();
        }

        // ─── Перечитать .xlsm из папки ───
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
                string parentFolder = Directory.GetParent(_dataFolder)?.FullName ?? _dataFolder;
                string batchName = new DirectoryInfo(parentFolder).Name;

                _currentBatch = SummaryService.ReadAllSensorsFromFolder(parentFolder, batchName);

                BatchNameText.Text = $"Партия: {_currentBatch.BatchName}";
                UpdateSummaryGrid();
                UpdateDetailPanel();

                RefreshButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка чтения .xlsm:\n{ex.Message}",
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

        // ─── Обновить ───
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

        // ─── Выбор датчика ───
        private void SummaryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetailPanel();
        }

        // ─── Переключение вкладки ───
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


        // ─── Построить панель «Гистерезис» (2 таблицы) ───
        private UIElement BuildHysteresisPanel(SummaryRow row)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical };

            // Таблица 1: Гистерезис градуировки
            stack.Children.Add(new TextBlock
            {
                Text = "Гистерезис градуировки",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            stack.Children.Add(BuildTableGrid(row.Source.GradHysteresis));

            // Таблица 2: Гистерезис проверки
            stack.Children.Add(new TextBlock
            {
                Text = "Гистерезис проверки",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 16, 0, 4)
            });

            stack.Children.Add(BuildTableGrid(row.Source.CheckHysteresis));

            return stack;
        }

        // ─── Построить панель с одной таблицей ───
        private UIElement BuildSingleTablePanel(TableData? table)
        {
            if (table == null || table.Values.Length == 0)
            {
                return new TextBlock
                {
                    Text = "Нет данных",
                    Margin = new Thickness(10),
                    FontStyle = FontStyles.Italic
                };
            }

            return BuildTableGrid(table);
        }

        // ─── Построить DataGrid для одной таблицы ───
        private UIElement BuildTableGrid(TableData table)
        {
            if (table == null || table.Values.Length == 0)
            {
                return new TextBlock
                {
                    Text = "Нет данных",
                    Margin = new Thickness(10),
                    FontStyle = FontStyles.Italic
                };
            }

            // Определяем заполненные столбцы
            bool[] hasData = new bool[table.ColumnHeaders.Count];
            for (int c = 0; c < table.ColumnHeaders.Count; c++)
            {
                for (int r = 0; r < table.Values.GetLength(0); r++)
                {
                    if (table.Values[r, c].HasValue)
                    {
                        hasData[c] = true;
                        break;
                    }
                }
            }

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserSortColumns = false,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeight = 22,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // Колонка P
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "P, кгс/см²",
                Binding = new System.Windows.Data.Binding("P"),
                Width = 110
            });

            // Колонки температур — только заполненные
            for (int c = 0; c < table.ColumnHeaders.Count; c++)
            {
                if (!hasData[c]) continue;

                int colIndex = c;
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = table.ColumnHeaders[c].ToString("0.##"),
                    Binding = new System.Windows.Data.Binding($"C{colIndex}"),
                    Width = 70
                });
            }

            // Строки
            var rows = new System.Collections.Generic.List<object>();

            for (int r = 0; r < table.Values.GetLength(0); r++)
            {
                dynamic expando = new System.Dynamic.ExpandoObject();
                var dict = (System.Collections.Generic.IDictionary<string, object>)expando;

                dict["P"] = table.RowHeaders.Count > r
                    ? table.RowHeaders[r].ToString("0.###")
                    : "";

                for (int c = 0; c < table.Values.GetLength(1); c++)
                {
                    var val = table.Values[r, c];
                    dict[$"C{c}"] = val?.ToString("0.000") ?? "";
                }

                rows.Add(expando);
            }

            grid.ItemsSource = rows;

            return grid;
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

            // Вкладка 0 — Гистерезис (две таблицы: градуировка + проверка)
            // Вкладка 1 — Погрешность (одна таблица)
            if (tabIndex == 0)
            {
                DetailContent.Content = BuildHysteresisPanel(row);
            }
            else
            {
                DetailContent.Content = BuildSingleTablePanel(row.Source.Errors);
            }
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