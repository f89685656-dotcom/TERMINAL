using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

            DataFolderText.Text = $"Папка: {_dataFolder}";
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

        // ═══════════════════════════════════════════════════
        // Логирование в главное окно
        // ═══════════════════════════════════════════════════
        private void LogToMainWindow(string message, Brush color)
        {
            try
            {
                MainWindow.Current?.LogExternal(message, color);
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════
        // Обновить (перечитать .xlsm)
        // ═══════════════════════════════════════════════════
        private async void RefreshFromXlsx()
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

                var files = Directory.GetFiles(parentFolder, "*.xlsm")
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count == 0)
                {
                    MessageBox.Show($"В папке нет .xlsm файлов:\n{parentFolder}",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                LogToMainWindow($"Сводка: начало чтения {files.Count} файлов",
                                Brushes.Blue);

                // Прогресс-бар
                LoadProgressBar.Visibility = Visibility.Visible;
                LoadProgressText.Visibility = Visibility.Visible;
                LoadProgressBar.Minimum = 0;
                LoadProgressBar.Maximum = files.Count;
                LoadProgressBar.Value = 0;

                var batch = new SummaryBatch
                {
                    BatchName = batchName,
                    SavedAt = DateTime.Now
                };

                for (int i = 0; i < files.Count; i++)
                {
                    string file = files[i];
                    LoadProgressBar.Value = i;
                    LoadProgressText.Text = $"Чтение {i + 1}/{files.Count}: {Path.GetFileName(file)}";

                    // Даём UI обновиться
                    await Dispatcher.InvokeAsync(() => { },
                        System.Windows.Threading.DispatcherPriority.Background);
                    await Task.Delay(20);

                    try
                    {
                        var sensor = SummaryService.ReadSingleFile(file);
                        batch.Sensors.Add(sensor);

                        if (batch.Temperatures.Count == 0 &&
                            sensor.Errors.ColumnHeaders.Count > 0)
                        {
                            batch.Temperatures = sensor.Errors.ColumnHeaders
                                .Where(t => t != 0)
                                .ToList();
                        }

                        LogToMainWindow($"Сводка: прочитан {Path.GetFileName(file)} (№{sensor.Number})",
                                        Brushes.Gray);
                    }
                    catch (Exception ex)
                    {
                        LogToMainWindow($"Сводка: ошибка чтения {Path.GetFileName(file)}: {ex.Message}",
                                        Brushes.OrangeRed);
                    }
                }

                LoadProgressBar.Value = files.Count;
                _currentBatch = batch;

                BatchNameText.Text = $"Партия: {_currentBatch.BatchName}";
                UpdateSummaryGrid();
                UpdateDetailPanel();

                RefreshButton.IsEnabled = true;

                LogToMainWindow($"Сводка: чтение завершено, датчиков={batch.Sensors.Count}",
                                Brushes.Green);
            }
            catch (Exception ex)
            {
                LogToMainWindow($"Сводка: критическая ошибка — {ex.Message}",
                                Brushes.Red);
                MessageBox.Show($"Ошибка чтения .xlsm:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadProgressBar.Visibility = Visibility.Collapsed;
                LoadProgressText.Visibility = Visibility.Collapsed;
            }
        }

        // ═══════════════════════════════════════════════════
        // Открыть CSV
        // ═══════════════════════════════════════════════════
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

                LogToMainWindow($"Сводка: загружен CSV {Path.GetFileName(dlg.FileName)}",
                                Brushes.Blue);
            }
            catch (Exception ex)
            {
                LogToMainWindow($"Сводка: ошибка загрузки CSV — {ex.Message}",
                                Brushes.OrangeRed);
                MessageBox.Show($"Ошибка загрузки CSV:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═══════════════════════════════════════════════════
        // Сохранить CSV
        // ═══════════════════════════════════════════════════
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

                LogToMainWindow($"Сводка: сохранён CSV {fileName}",
                                Brushes.Green);

                MessageBox.Show($"Сохранено:\n{fullPath}",
                    "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogToMainWindow($"Сводка: ошибка сохранения CSV — {ex.Message}",
                                Brushes.OrangeRed);
                MessageBox.Show($"Ошибка сохранения CSV:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═══════════════════════════════════════════════════
        // Обновить (по кнопке)
        // ═══════════════════════════════════════════════════
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

        // ═══════════════════════════════════════════════════
        // Печать
        // ═══════════════════════════════════════════════════
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

                LogToMainWindow($"Сводка: отправлено на печать",
                                Brushes.Green);
            }
            catch (Exception ex)
            {
                LogToMainWindow($"Сводка: ошибка печати — {ex.Message}",
                                Brushes.OrangeRed);
                MessageBox.Show($"Ошибка печати:\n{ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════
        // Печать — все датчики
        // ═══════════════════════════════════════════════════
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

        // ═══════════════════════════════════════════════════
        // Печать — один датчик
        // ═══════════════════════════════════════════════════
        private FixedDocument BuildPrintDocument(SummaryRow row, PrintDialog pd)
        {
            double pageWidth = pd.PrintableAreaWidth;
            double pageHeight = pd.PrintableAreaHeight;

            var doc = new FixedDocument();
            doc.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

            var page = BuildPageForSensor(row.Source, pageWidth, pageHeight);

            var content = new PageContent();
            ((IAddChild)content).AddChild(page);
            doc.Pages.Add(content);

            return doc;
        }

        // ═══════════════════════════════════════════════════
        // Страница для печати
        // ═══════════════════════════════════════════════════
        private FixedPage BuildPageForSensor(SensorSummary sensor,
                                     double pageWidth, double pageHeight)
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = pageWidth - 60,
                Margin = new Thickness(30, 30, 30, 30)
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

            // Гистерезис градуировки
            stack.Children.Add(new TextBlock
            {
                Text = "Гистерезис градуировки",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2)
            });
            stack.Children.Add(BuildTableGrid(sensor.GradHysteresis, forPrint: true));

            // Гистерезис проверки
            stack.Children.Add(new TextBlock
            {
                Text = "Гистерезис проверки",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 6, 0, 2)
            });
            stack.Children.Add(BuildTableGrid(sensor.CheckHysteresis, forPrint: true));

            // ТЧХ (если есть)
            if (sensor.Tchx != null && sensor.Tchx.MainTables.Count > 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"ТЧХ — {sensor.Tchx.ChannelCount}",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 6, 0, 2)
                });

                for (int i = 0; i < sensor.Tchx.MainTables.Count; i++)
                {
                    stack.Children.Add(BuildTchxTable(sensor.Tchx.MainTables[i], "Основная", forPrint: true));
                }

                for (int i = 0; i < sensor.Tchx.CheckTables.Count; i++)
                {
                    stack.Children.Add(BuildTchxTable(sensor.Tchx.CheckTables[i], "Проверка", forPrint: true));
                }
            }

            // Погрешность
            stack.Children.Add(new TextBlock
            {
                Text = "Погрешность",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 6, 0, 2)
            });
            stack.Children.Add(BuildTableGrid(sensor.Errors, forPrint: true));

            var page = new FixedPage
            {
                Width = pageWidth,
                Height = pageHeight
            };
            page.Children.Add(stack);

            return page;
        }

        // ═══════════════════════════════════════════════════
        // Выбор датчика
        // ═══════════════════════════════════════════════════
        private void SummaryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetailPanel();
        }

        // ═══════════════════════════════════════════════════
        // Переключение вкладки
        // ═══════════════════════════════════════════════════
        private void DetailTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetailPanel();
        }

        // ═══════════════════════════════════════════════════
        // Заполнить сводку
        // ═══════════════════════════════════════════════════
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

        // ═══════════════════════════════════════════════════
        // Панель «Гистерезис» (2 таблицы)
        // ═══════════════════════════════════════════════════
        private UIElement BuildHysteresisPanel(SummaryRow row)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical };

            stack.Children.Add(new TextBlock
            {
                Text = "Гистерезис градуировки",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            stack.Children.Add(BuildTableGrid(row.Source.GradHysteresis));

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

        // ═══════════════════════════════════════════════════
        // Панель с одной таблицей
        // ═══════════════════════════════════════════════════
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

        // ═══════════════════════════════════════════════════
        // DataGrid для TableData
        // ═══════════════════════════════════════════════════
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
                RowHeight = forPrint ? 16 : 22,
                FontSize = forPrint ? 10 : 12,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // Контекстное меню
            var contextMenu = new ContextMenu();
            var miCopy = new MenuItem { Header = "Копировать" };
            miCopy.Click += (s, ev) => CopyTableToClipboard(grid);
            contextMenu.Items.Add(miCopy);
            grid.ContextMenu = contextMenu;

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

                var column = new TaggedTemplateColumn
                {
                    Header = table.ColumnHeaders[c].ToString("0.##"),
                    CellTemplate = template,
                    Width = forPrint ? 55 : 70,
                    SourceColumnIndex = colIndex
                };

                grid.Columns.Add(column);
            }

            // Строки
            var rows = new List<object>();

            for (int r = 0; r < table.Values.GetLength(0); r++)
            {
                dynamic expando = new System.Dynamic.ExpandoObject();
                var dict = (IDictionary<string, object>)expando;

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

        // ═══════════════════════════════════════════════════
        // Заполнить панель деталей
        // ═══════════════════════════════════════════════════
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
                DetailContent.Content = BuildHysteresisPanel(row);
            }
            else if (tabIndex == 1)
            {
                DetailContent.Content = BuildTchxPanel(row);
            }
            else if (tabIndex == 2)
            {
                DetailContent.Content = BuildSingleTablePanel(row.Source.Errors);
            }
        }

        // ═══════════════════════════════════════════════════
        // Панель ТЧХ
        // ═══════════════════════════════════════════════════
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

            var root = new StackPanel { Orientation = Orientation.Vertical };

            root.Children.Add(new TextBlock
            {
                Text = $"Режим: {tchx.ChannelCount}",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var columns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Top
            };

            int channelCount = tchx.MainTables.Count;

            for (int i = 0; i < channelCount; i++)
            {
                var col = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(0, 0, 20, 0)
                };

                if (channelCount > 1)
                {
                    col.Children.Add(new TextBlock
                    {
                        Text = $"Термоканал {i + 1}",
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 4)
                    });
                }

                if (i < tchx.MainTables.Count)
                {
                    col.Children.Add(BuildTchxTable(tchx.MainTables[i], "Основная"));
                }

                if (i < tchx.CheckTables.Count)
                {
                    col.Children.Add(new TextBlock
                    {
                        Text = "Проверка",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 10, 0, 2)
                    });
                    col.Children.Add(BuildTchxTable(tchx.CheckTables[i], "Проверка"));
                }

                columns.Children.Add(col);
            }

            root.Children.Add(columns);
            return root;
        }

        // ═══════════════════════════════════════════════════
        // Таблица ТЧХ
        // ═══════════════════════════════════════════════════
        private UIElement BuildTchxTable(TchxTable tchx, string defaultTitle, bool forPrint = false)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical };

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

            var contextMenu = new ContextMenu();
            var miCopy = new MenuItem { Header = "Копировать" };
            miCopy.Click += (s, ev) => CopyTchxTableToClipboard(grid, tchx);
            contextMenu.Items.Add(miCopy);
            grid.ContextMenu = contextMenu;

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

            // Колонки 0..2 — обычные
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

            // Колонка 3 — с полоской
            {
                int colIndex = 3;
                string bindingPath = $"C{colIndex}";

                var template = new DataTemplate();
                var gridFactory = new FrameworkElementFactory(typeof(Grid));

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

            // Строки
            var rows = new List<object>();
            for (int r = 0; r < tchx.Values.GetLength(0); r++)
            {
                bool hasAny = false;
                for (int c = 0; c < 4; c++)
                {
                    if (tchx.Values[r, c].HasValue)
                    {
                        hasAny = true;
                        break;
                    }
                }
                if (!hasAny) continue;

                dynamic expando = new System.Dynamic.ExpandoObject();
                var dict = (IDictionary<string, object>)expando;

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

        // ═══════════════════════════════════════════════════
        // Копировать таблицу в буфер
        // ═══════════════════════════════════════════════════
        private void CopyTableToClipboard(DataGrid grid)
        {
            if (grid == null || grid.ItemsSource == null)
                return;

            var sb = new StringBuilder();

            var headers = new List<string>();
            foreach (var col in grid.Columns)
            {
                headers.Add(col.Header?.ToString() ?? "");
            }
            sb.AppendLine(string.Join("\t", headers));

            foreach (var item in grid.Items)
            {
                var cells = new List<string>();

                for (int c = 0; c < grid.Columns.Count; c++)
                {
                    var col = grid.Columns[c];
                    string value = "";

                    if (col is DataGridTextColumn textCol)
                    {
                        var binding = textCol.Binding as System.Windows.Data.Binding;
                        if (binding != null)
                        {
                            var prop = item.GetType().GetProperty(binding.Path.Path);
                            if (prop != null)
                            {
                                var v = prop.GetValue(item);
                                value = v?.ToString() ?? "";
                            }
                        }
                    }
                    else if (col is TaggedTemplateColumn templateCol)
                    {
                        if (item is IDictionary<string, object> dict)
                        {
                            string key;
                            if (c == 0)
                            {
                                key = "P";
                            }
                            else
                            {
                                int origIdx = templateCol.SourceColumnIndex;
                                key = $"C{origIdx}";
                            }

                            if (dict.ContainsKey(key))
                            {
                                var v = dict[key];

                                if (v is double d)
                                    value = d.ToString("0.000");
                                else if (v is double?)
                                {
                                    double? nd = (double?)v;
                                    if (nd.HasValue)
                                        value = nd.Value.ToString("0.000");
                                }
                                else
                                    value = v?.ToString() ?? "";
                            }
                        }
                    }

                    cells.Add(value);
                }
                sb.AppendLine(string.Join("\t", cells));
            }

            if (!ClipboardHelper.SetText(sb.ToString()))
            {
                MessageBox.Show("Не удалось скопировать в буфер обмена.\n" +
                                "Попробуйте ещё раз через секунду.",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ═══════════════════════════════════════════════════
        // Копировать ТЧХ в буфер
        // ═══════════════════════════════════════════════════
        private void CopyTchxTableToClipboard(DataGrid grid, TchxTable table)
        {
            if (table == null) return;

            var sb = new StringBuilder();

            sb.AppendLine(string.Join("\t", table.ColumnHeaders));

            for (int r = 0; r < table.Values.GetLength(0); r++)
            {
                bool hasAny = false;
                for (int c = 0; c < table.Values.GetLength(1); c++)
                {
                    if (table.Values[r, c].HasValue)
                    {
                        hasAny = true;
                        break;
                    }
                }
                if (!hasAny) continue;

                var cells = new List<string>();
                for (int c = 0; c < table.Values.GetLength(1); c++)
                {
                    var v = table.Values[r, c];
                    cells.Add(v?.ToString("0.000") ?? "");
                }
                sb.AppendLine(string.Join("\t", cells));
            }

            if (!ClipboardHelper.SetText(sb.ToString()))
            {
                MessageBox.Show("Не удалось скопировать в буфер обмена.\n" +
                                "Попробуйте ещё раз через секунду.",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    // ═══════════════════════════════════════════════════
    // Класс колонки с оригинальным индексом
    // ═══════════════════════════════════════════════════
    public class TaggedTemplateColumn : DataGridTemplateColumn
    {
        public int SourceColumnIndex { get; set; }
    }

    // ═══════════════════════════════════════════════════
    // Модель строки сводки
    // ═══════════════════════════════════════════════════
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