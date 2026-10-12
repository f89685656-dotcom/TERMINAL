using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
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

            // Показать путь в окне
            DataFolderText.Text = $"Папка: {_dataFolder}";

            // Начальное состояние — пусто
            BatchNameText.Text = "Партия: —";
            SummaryGrid.ItemsSource = null;
            DetailHeaderText.Text = "Детали: —";
            DetailContent.Content = new TextBlock
            {
                Text = "Нажмите «Обновить» для загрузки данных из .xlsm\n" +
                       "или «Открыть CSV» для просмотра сохранённой партии.",
                Margin = new Thickness(20),
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.Gray
            };
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
                MessageBox.Show("Обновление недоступно для архивных данных.\n" +
                                "Сначала закройте окно и откройте заново.",
                                "Обновление", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            RefreshFromXlsx();
        }

        // ─── Печать ───
        //private void Print_Click(object sender, RoutedEventArgs e)
        //{
        //    if (_currentBatch == null || _currentBatch.Sensors.Count == 0)
        //    {
        //        MessageBox.Show("Нет данных для печати.");
        //        return;
        //    }

        //    bool printAll = PrintAllCheckBox.IsChecked == true;

        //    if (!printAll && SummaryGrid.SelectedItem is not SummaryRow)
        //    {
        //        MessageBox.Show("Выберите датчик или включите «Печать всех датчиков».");
        //        return;
        //    }

        //    var pd = new PrintDialog();
        //    if (pd.ShowDialog() != true) return;

        //    try
        //    {
        //        double pageWidth = pd.PrintableAreaWidth;
        //        double pageHeight = pd.PrintableAreaHeight;

        //        // Собираем страницы
        //        var pages = new List<FixedPage>();

        //        if (printAll)
        //        {
        //            foreach (var sensor in _currentBatch.Sensors.OrderBy(s => s.Number))
        //            {
        //                pages.Add(BuildPageForSensor(sensor, pageWidth, pageHeight));
        //            }
        //        }
        //        else
        //        {
        //            var row = (SummaryRow)SummaryGrid.SelectedItem;
        //            pages.Add(BuildPageForSensor(row.Source, pageWidth, pageHeight));
        //        }

        //        // Свой пагинатор — прижимает содержимое к верхнему левому углу
        //        var paginator = new PrintPaginator(pages, new Size(pageWidth, pageHeight));

        //        var writer = System.Printing.PrintQueue.CreateXpsDocumentWriter(pd.PrintQueue);
        //        writer.Write(paginator, pd.PrintTicket);

        //        MessageBox.Show($"Отправлено на печать: {(printAll ? "все датчики" : "выбранный датчик")}.");
        //    }
        //    catch (Exception ex)
        //    {
        //        MessageBox.Show($"Ошибка печати:\n{ex.Message}");
        //    }
        //}

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBatch == null || _currentBatch.Sensors.Count == 0)
            {
                MessageBox.Show("Нет данных для печати.");
                return;
            }

            bool printAll = PrintAllCheckBox.IsChecked == true;

            if (!printAll && SummaryGrid.SelectedItem is not SummaryRow)
            {
                MessageBox.Show("Выберите датчик или включите «Печать всех датчиков».");
                return;
            }

            var pd = new PrintDialog();
            if (pd.ShowDialog() != true) return;

            try
            {
                FixedDocument doc;

                if (printAll)
                {
                    doc = BuildPrintDocumentAll(pd);
                }
                else
                {
                    var row = (SummaryRow)SummaryGrid.SelectedItem;
                    doc = BuildPrintDocument(row, pd);
                }

                pd.PrintDocument(
                    ((IDocumentPaginatorSource)doc).DocumentPaginator,
                    printAll ? "Сводка всех датчиков" : "Сводка датчика");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка печати:\n{ex.Message}");
            }
        }

        /// <summary>
        /// Формирует документ для печати всех датчиков.
        /// Каждый датчик — на отдельной странице.
        /// </summary>
        private FixedDocument BuildPrintDocumentAll(PrintDialog pd)
        {
            double pageWidth = pd.PrintableAreaWidth;
            double pageHeight = pd.PrintableAreaHeight;

            var doc = new FixedDocument();
            doc.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

            var sensors = _currentBatch!.Sensors.OrderBy(s => s.Number).ToList();

            foreach (var sensor in sensors)
            {
                var page = BuildPageForSensor(sensor, pageWidth, pageHeight);
                var content = new PageContent();
                ((IAddChild)content).AddChild(page);
                doc.Pages.Add(content);
            }

            return doc;
        }

        private FixedPage BuildPageForSensor(SensorSummary sensor,
                                     double pageWidth, double pageHeight)
        {
            // ─── Содержимое ───
            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = pageWidth - 20
            };

            // Шапка
            stack.Children.Add(new TextBlock
            {
                Text = $"Партия: {_currentBatch?.BatchName ?? "—"}",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"Датчик №{sensor.Number}    Опис.град.: {sensor.GradDescription?.ToString("0.0000") ?? "—"}    " +
                       $"Дата: {DateTime.Now:dd.MM.yyyy HH:mm}",
                FontSize = 10,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 6)
            });

            // Таблица 1
            stack.Children.Add(new TextBlock
            {
                Text = "Гистерезис градуировки",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2)
            });
            stack.Children.Add(BuildTableGrid(sensor.GradHysteresis, forPrint: true));

            // Таблица 2
            stack.Children.Add(new TextBlock
            {
                Text = "Гистерезис проверки",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 6, 0, 2)
            });
            stack.Children.Add(BuildTableGrid(sensor.CheckHysteresis, forPrint: true));

            // ТЧХ (если есть)
            //if (sensor.Tchx != null && sensor.Tchx.MainTables.Count > 0)
            //{
            //    stack.Children.Add(new TextBlock
            //    {
            //        Text = $"ТЧХ — {sensor.Tchx.ChannelCount}",
            //        FontSize = 11,
            //        FontWeight = FontWeights.Bold,
            //        Margin = new Thickness(0, 6, 0, 2)
            //    });

            //    for (int i = 0; i < sensor.Tchx.MainTables.Count; i++)
            //    {
            //        var t = sensor.Tchx.MainTables[i];
            //        stack.Children.Add(new TextBlock
            //        {
            //            Text = t.Title,
            //            FontSize = 10,
            //            FontWeight = FontWeights.Bold,
            //            Margin = new Thickness(0, 2, 0, 1)
            //        });
            //        stack.Children.Add(BuildTchxTableForPrint(t));
            //    }

            // Таблица 3
            stack.Children.Add(new TextBlock
            {
                Text = "Погрешность",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 6, 0, 2)
            });
            stack.Children.Add(BuildTableGrid(sensor.Errors, forPrint: true));

            // ─── Canvas — точное позиционирование ───
            var canvas = new Canvas
            {
                Width = pageWidth,
                Height = pageHeight
            };

            // Сдвигаем содержимое вверх и вправо
            Canvas.SetLeft(stack, 10);
            Canvas.SetTop(stack, -60);   // ← подбери это значение

            canvas.Children.Add(stack);

            // ─── Страница ───
            var page = new FixedPage
            {
                Width = pageWidth,
                Height = pageHeight
            };
            page.Children.Add(canvas);

            return page;
        }
        private TextBlock MakeTableHeader(string text, double topMargin = 0)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, topMargin, 0, 2)
            };
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
        private UIElement BuildTableGrid(TableData table, bool forPrint = false)
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

            // Максимум по модулю
            double maxAbs = 0;
            for (int r = 0; r < table.Values.GetLength(0); r++)
            {
                for (int c = 0; c < table.Values.GetLength(1); c++)
                {
                    var v = table.Values[r, c];
                    if (v.HasValue && Math.Abs(v.Value) > maxAbs)
                        maxAbs = Math.Abs(v.Value);
                }
            }
            if (maxAbs < 0.0001) maxAbs = 1.0;
            maxAbs *= 1.05;

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserSortColumns = false,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeight = forPrint ? 16 : 22,   // ← ИЗМЕНЕНО
                FontSize = forPrint ? 10 : 12,    // ← ИЗМЕНЕНО
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // Колонка P
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "P, кгс/см²",
                Binding = new System.Windows.Data.Binding("P"),
                Width = forPrint ? 80 : 110
            });

            // Колонки температур
            for (int c = 0; c < table.ColumnHeaders.Count; c++)
            {
                if (!hasData[c]) continue;

                int colIndex = c;
                string bindingPath = $"C{colIndex}";

                var template = new DataTemplate();
                var gridFactory = new FrameworkElementFactory(typeof(Grid));

                // Полоска
                var barFactory = new FrameworkElementFactory(typeof(Border));
                barFactory.SetBinding(Border.WidthProperty,
                    new System.Windows.Data.Binding(bindingPath)
                    {
                        Converter = new TERMINAL.Converters.ValueToBarWidthConverter
                        {
                            MaxWidth = forPrint ? 40 : 60,
                            MaxAbsValue = maxAbs
                        }
                    });
                barFactory.SetBinding(Border.BackgroundProperty,
                    new System.Windows.Data.Binding(bindingPath)
                    {
                        Converter = new TERMINAL.Converters.ValueToBarBrushConverter()
                    });
                barFactory.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
                barFactory.SetValue(Border.MarginProperty, new Thickness(2));
                barFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
                gridFactory.AppendChild(barFactory);

                // Текст
                var textFactory = new FrameworkElementFactory(typeof(TextBlock));
                textFactory.SetBinding(TextBlock.TextProperty,
                    new System.Windows.Data.Binding(bindingPath)
                    {
                        StringFormat = "0.000"
                    });
                textFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
                textFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
                textFactory.SetValue(TextBlock.FontSizeProperty, forPrint ? 10.0 : 12.0);
                gridFactory.AppendChild(textFactory);

                template.VisualTree = gridFactory;

                var column = new DataGridTemplateColumn
                {
                    Header = table.ColumnHeaders[c].ToString("0.##"),
                    CellTemplate = template,
                    Width = forPrint ? 55 : 70
                };

                grid.Columns.Add(column);
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
                    dict[$"C{c}"] = table.Values[r, c];
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

            if (tabIndex == 0)
            {
                DetailContent.Content = BuildHysteresisPanel(row);    // Гистерезис
            }
            else if (tabIndex == 1)
            {
                DetailContent.Content = BuildTchxPanel(row);          // ТЧХ
            }
            else if (tabIndex == 2)
            {
                DetailContent.Content = BuildSingleTablePanel(row.Source.Errors);  // Погрешность
            }
        }
        private UIElement BuildTchxPanel(SummaryRow row)
        {
            var tchx = row.Source.Tchx;
            if (tchx == null || tchx.MainTables.Count == 0)
            {
                return new TextBlock
                {
                    Text = "Термоканал отсутствует",
                    Margin = new Thickness(10),
                    FontStyle = FontStyles.Italic,
                    Foreground = Brushes.Gray
                };
            }

            var stack = new StackPanel { Orientation = Orientation.Vertical };

            stack.Children.Add(new TextBlock
            {
                Text = $"Режим: {tchx.ChannelCount}",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Основные таблицы
            for (int i = 0; i < tchx.MainTables.Count; i++)
            {
                if (tchx.MainTables.Count > 1)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = $"Термоканал {i + 1}",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, i == 0 ? 0 : 10, 0, 2)
                    });
                }

                stack.Children.Add(BuildTchxTable(tchx.MainTables[i], "Основная"));
            }

            // Проверочные таблицы
            for (int i = 0; i < tchx.CheckTables.Count; i++)
            {
                if (tchx.CheckTables.Count > 1)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = $"Термоканал {i + 1} — проверка",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 10, 0, 2)
                    });
                }
                else
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = "Проверка",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 10, 0, 2)
                    });
                }

                stack.Children.Add(BuildTchxTable(tchx.CheckTables[i], "Проверка"));
            }

            return stack;
        }

        private UIElement BuildTchxTable(TchxTable tchx, string defaultTitle, bool forPrint = false)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical };

            // Заголовок таблицы
            stack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(tchx.Title) ? defaultTitle : tchx.Title,
                FontSize = forPrint ? 10 : 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2)
            });

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserSortColumns = false,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeight = forPrint ? 16 : 22,
                FontSize = forPrint ? 10 : 12,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // ─── Максимум по модулю для последнего столбца (для полоски) ───
            double maxAbs = 0;
            int lastCol = 3;
            for (int r = 0; r < tchx.Values.GetLength(0); r++)
            {
                var v = tchx.Values[r, lastCol];
                if (v.HasValue && Math.Abs(v.Value) > maxAbs)
                    maxAbs = Math.Abs(v.Value);
            }
            if (maxAbs < 0.0001) maxAbs = 1.0;
            maxAbs *= 1.05;

            // ─── Колонки 0..2 — обычные ───
            for (int c = 0; c < 3; c++)
            {
                var col = new DataGridTextColumn
                {
                    Header = tchx.ColumnHeaders[c],
                    Binding = new System.Windows.Data.Binding($"C{c}")
                    {
                        StringFormat = "0.000"
                    },
                    Width = c == 0 ? 130 : 150
                };
                grid.Columns.Add(col);
            }

            // ─── Колонка 3 — с полоской и цветом ───
            {
                int colIndex = 3;
                string bindingPath = $"C{colIndex}";

                var template = new DataTemplate();
                var gridFactory = new FrameworkElementFactory(typeof(Grid));

                // Полоска
                var barFactory = new FrameworkElementFactory(typeof(Border));
                barFactory.SetBinding(Border.WidthProperty,
                    new System.Windows.Data.Binding(bindingPath)
                    {
                        Converter = new TERMINAL.Converters.ValueToBarWidthConverter
                        {
                            MaxWidth = forPrint ? 40 : 60,
                            MaxAbsValue = maxAbs
                        }
                    });
                barFactory.SetBinding(Border.BackgroundProperty,
                    new System.Windows.Data.Binding(bindingPath)
                    {
                        Converter = new TERMINAL.Converters.ValueToBarBrushConverter()
                    });
                barFactory.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
                barFactory.SetValue(Border.MarginProperty, new Thickness(2));
                barFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
                gridFactory.AppendChild(barFactory);

                // Текст
                var textFactory = new FrameworkElementFactory(typeof(TextBlock));
                textFactory.SetBinding(TextBlock.TextProperty,
                    new System.Windows.Data.Binding(bindingPath)
                    {
                        StringFormat = "0.000"
                    });
                textFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
                textFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
                textFactory.SetValue(TextBlock.FontSizeProperty, forPrint ? 10.0 : 12.0);
                gridFactory.AppendChild(textFactory);

                template.VisualTree = gridFactory;

                var column = new DataGridTemplateColumn
                {
                    Header = tchx.ColumnHeaders[colIndex],
                    CellTemplate = template,
                    Width = 150
                };
                grid.Columns.Add(column);
            }

            // ─── Строки ───
            var rows = new System.Collections.Generic.List<object>();
            for (int r = 0; r < tchx.Values.GetLength(0); r++)
            {
                // Проверяем, есть ли хоть одно значение в строке
                bool hasAny = false;
                for (int c = 0; c < 4; c++)
                {
                    if (tchx.Values[r, c].HasValue)
                    {
                        hasAny = true;
                        break;
                    }
                }
                if (!hasAny) continue;   // ← пропускаем пустые строки

                dynamic expando = new System.Dynamic.ExpandoObject();
                var dict = (System.Collections.Generic.IDictionary<string, object>)expando;

                for (int c = 0; c < 4; c++)
                {
                    dict[$"C{c}"] = tchx.Values[r, c];
                }
                rows.Add(expando);
            }

            grid.ItemsSource = rows;

            stack.Children.Add(grid);
            return stack;
        }

        private FixedDocument BuildPrintDocument(SummaryRow row, PrintDialog pd)
        {
            // ─── Размеры страницы ───
            double pageWidth = pd.PrintableAreaWidth;
            double pageHeight = pd.PrintableAreaHeight;

            var doc = new FixedDocument();
            doc.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

            // ─── Содержимое ───
            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0)   // ← убираем все отступы
            };

            // Шапка — 2 строки, без пустот
            stack.Children.Add(new TextBlock
            {
                Text = $"Партия: {_currentBatch?.BatchName ?? "—"}",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"Датчик №{row.Number}    Опис.град.: {row.GradDescription?.ToString("0.0000") ?? "—"}    " +
                       $"Дата: {DateTime.Now:dd.MM.yyyy HH:mm}",
                FontSize = 10,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 0)
            });

            // Таблица 1
            stack.Children.Add(new TextBlock
            {
                Text = "Гистерезис градуировки",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2)
            });
            stack.Children.Add(BuildTableGrid(row.Source.GradHysteresis));

            // Таблица 2
            stack.Children.Add(new TextBlock
            {
                Text = "Гистерезис проверки",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 6, 0, 2)   // ← 6 вместо 8
            });
            stack.Children.Add(BuildTableGrid(row.Source.CheckHysteresis));

            // Таблица 3
            stack.Children.Add(new TextBlock
            {
                Text = "Погрешность",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 6, 0, 2)
            });
            stack.Children.Add(BuildTableGrid(row.Source.Errors));

            // ─── Viewbox ───
            var viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Width = pageWidth,
                Height = pageHeight,
                StretchDirection = StretchDirection.DownOnly   // ← не растягивать, если влезает
            };
            viewbox.Child = stack;

            // ─── Одна страница ───
            var page = new FixedPage
            {
                Width = pageWidth,
                Height = pageHeight
            };
            page.Children.Add(viewbox);

            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            doc.Pages.Add(content);

            return doc;
        }
        public class PrintPaginator : System.Windows.Documents.DocumentPaginator
        {
            private readonly List<FixedPage> _pages;
            private Size _pageSize;

            public PrintPaginator(List<FixedPage> pages, Size pageSize)
            {
                _pages = pages;
                _pageSize = pageSize;
            }

            public override System.Windows.Documents.DocumentPage GetPage(int pageNumber)
            {
                var page = _pages[pageNumber];

                var paperSize = new Size(_pageSize.Width, _pageSize.Height);
                var bleedBox = new Rect(0, 0, _pageSize.Width, _pageSize.Height);
                var contentBox = new Rect(0, 0, _pageSize.Width, _pageSize.Height);

                return new System.Windows.Documents.DocumentPage(
                    page,
                    paperSize,
                    bleedBox,
                    contentBox);
            }

            public override bool IsPageCountValid => true;
            public override int PageCount => _pages.Count;

            public override Size PageSize
            {
                get => _pageSize;
                set => _pageSize = value;
            }

            public override IDocumentPaginatorSource Source => null;
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