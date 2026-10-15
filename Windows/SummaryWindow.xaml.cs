using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
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
using System.Windows.Media.Imaging;
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
            catch (Exception ex)
            {
                // ✅ ИСПРАВЛЕНО: не глотаем исключение молча
                Debug.WriteLine($"[SummaryWindow] LogToMainWindow: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════
        // Обновить (перечитать .xlsm)
        // ═══════════════════════════════════════════════════
        // ✅ ИСПРАВЛЕНО: async Task вместо async void
        private async Task RefreshFromXlsxAsync()
        {
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

                    await Dispatcher.InvokeAsync(() => { },
                        System.Windows.Threading.DispatcherPriority.Background);
                    await Task.Yield();

                    try
                    {
                        var sensor = SummaryService.ReadSingleFile(file);

                        // ─── Чтение ГСТ ───
                        try
                        {
                            if (!string.IsNullOrEmpty(sensor.ResonatorNumber) &&
                                !string.IsNullOrEmpty(sensor.ResonatorModel))
                            {
                                sensor.Gst = GstService.ReadGst(
                                    sensor.ResonatorNumber,
                                    sensor.ResonatorModel);
                            }
                            else
                            {
                                sensor.Gst = new GstInfo
                                {
                                    Error = "Не заданы номер резонатора или модель"
                                };
                            }
                        }
                        catch (Exception ex)
                        {
                            sensor.Gst = new GstInfo { Error = $"Ошибка ГСТ: {ex.Message}" };
                        }

                        batch.Sensors.Add(sensor);
                    }
                    catch (Exception ex)
                    {
                        LogToMainWindow($"Сводка: ошибка чтения {Path.GetFileName(file)} — {ex.Message}",
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
        // ✅ ИСПРАВЛЕНО: async void только для обработчика события (это нормально),
        // внутри await на async Task
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadedFromCsv)
            {
                MessageBox.Show("Обновление недоступно для архивных данных.\n" +
                                "Сначала закройте окно и откройте заново.",
                                "Обновление", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            RefreshButton.IsEnabled = false;
            try
            {
                await RefreshFromXlsxAsync();
            }
            finally
            {
                // ✅ ИСПРАВЛЕНО: возвращаем кнопку в строй
                RefreshButton.IsEnabled = !_isLoadedFromCsv;
            }
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
                // ─── Принудительно альбомная ориентация ───
                try
                {
                    var ticket = pd.PrintTicket;
                    ticket.PageOrientation = System.Printing.PageOrientation.Landscape;
                    pd.PrintTicket = ticket;
                }
                catch (Exception ex)
                {
                    LogToMainWindow($"Сводка: не удалось установить альбомную ориентацию — {ex.Message}",
                                    Brushes.OrangeRed);
                    // Продолжаем с тем, что есть
                }

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
        private FixedPage BuildPageForSensor(SensorSummary sensor, double pageWidth, double pageHeight)
        {
            var root = new Grid
            {
                Width = pageWidth - 20,
                Margin = new Thickness(10, 8, 10, 8),
                RowDefinitions =
        {
            new RowDefinition { Height = GridLength.Auto },   // 0: шапка
            new RowDefinition { Height = GridLength.Auto },   // 1: гистерезисы
            new RowDefinition { Height = GridLength.Auto },   // 2: ТЧХ
            new RowDefinition { Height = new GridLength(280) }, // 3: погрешность + ГСТ (фиксированная высота)
        }
            };

            // ─── 0. Шапка ───
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

            header.Children.Add(new TextBlock
            {
                Text = $"Партия: {_currentBatch?.BatchName ?? "—"}",
                FontSize = 11,
                FontWeight = FontWeights.Bold
            });

            header.Children.Add(new TextBlock
            {
                Text = $"Датчик №{sensor.Number}    " +
                       $"Опис.град.: {sensor.GradDescription?.ToString("0.0000") ?? "—"}    " +
                       $"Дата: {DateTime.Now:dd.MM.yyyy HH:mm}",
                FontSize = 9,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 1, 0, 0)
            });

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ─── 1. Гистерезисы (2 колонки) ───
            var hystGrid = new Grid
            {
                ColumnDefinitions =
        {
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            new ColumnDefinition { Width = new GridLength(15) },
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
        },
                Margin = new Thickness(0, 0, 0, 4)
            };

            var gradHyst = new StackPanel();
            gradHyst.Children.Add(new TextBlock
            {
                Text = "Гистерезис градуировки",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2)
            });
            gradHyst.Children.Add(BuildTableGrid(sensor.GradHysteresis, forPrint: true));
            Grid.SetColumn(gradHyst, 0);
            hystGrid.Children.Add(gradHyst);

            var checkHyst = new StackPanel();
            checkHyst.Children.Add(new TextBlock
            {
                Text = "Гистерезис проверки",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2)
            });
            checkHyst.Children.Add(BuildTableGrid(sensor.CheckHysteresis, forPrint: true));
            Grid.SetColumn(checkHyst, 2);
            hystGrid.Children.Add(checkHyst);

            Grid.SetRow(hystGrid, 1);
            root.Children.Add(hystGrid);

            // ─── 2. ТЧХ (1-3 колонки) ───
            var tchxHost = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            tchxHost.Children.Add(new TextBlock
            {
                Text = $"ТЧХ — {sensor.Tchx?.ChannelCount ?? "—"}",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2)
            });

            if (sensor.Tchx != null && sensor.Tchx.MainTables.Count > 0)
            {
                int channelCount = sensor.Tchx.MainTables.Count;
                var tchxGrid = new Grid();

                for (int i = 0; i < channelCount; i++)
                {
                    if (i > 0)
                        tchxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });

                    tchxGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(1, GridUnitType.Star)
                    });
                }

                for (int i = 0; i < channelCount; i++)
                {
                    var col = new StackPanel();

                    if (channelCount > 1)
                    {
                        col.Children.Add(new TextBlock
                        {
                            Text = $"Термоканал {i + 1}",
                            FontSize = 9,
                            FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 0, 0, 1)
                        });
                    }

                    if (i < sensor.Tchx.MainTables.Count)
                        col.Children.Add(BuildTchxTable(sensor.Tchx.MainTables[i], "Основная", forPrint: true));

                    if (i < sensor.Tchx.CheckTables.Count)
                    {
                        // без заголовка "Проверка" — просто вторая таблица с отступом
                        col.Children.Add(BuildTchxTable(sensor.Tchx.CheckTables[i], "Проверка", forPrint: true));
                    }

                    Grid.SetColumn(col, i * 2);
                    tchxGrid.Children.Add(col);
                }

                tchxHost.Children.Add(tchxGrid);
            }
            else
            {
                tchxHost.Children.Add(new TextBlock
                {
                    Text = "Термоканал отсутствует",
                    FontSize = 9,
                    FontStyle = FontStyles.Italic,
                    Foreground = Brushes.Gray
                });
            }

            Grid.SetRow(tchxHost, 2);
            root.Children.Add(tchxHost);

            // ─── 3. Погрешность + ГСТ (2 колонки 50/50) ───
            var bottomGrid = new Grid
            {
                ColumnDefinitions =
        {
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            new ColumnDefinition { Width = new GridLength(15) },
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
        }
            };

            // 3.1 Погрешность
            var errHost = new StackPanel();
            errHost.Children.Add(new TextBlock
            {
                Text = "Погрешность",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2)
            });
            errHost.Children.Add(BuildTableGrid(sensor.Errors, forPrint: true));
            Grid.SetColumn(errHost, 0);
            bottomGrid.Children.Add(errHost);

            // 3.2 ГСТ
            var gstHost = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            if (sensor.Gst != null && sensor.Gst.IsOk &&
                sensor.Gst.ChartPng != null && sensor.Gst.ChartPng.Length > 0)
            {
                gstHost.Children.Add(new TextBlock
                {
                    Text = $"ГСТ (резонатор {sensor.Gst.ResonatorModel} - № {sensor.Gst.ResonatorNumber})",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,   // ← по центру
                    Margin = new Thickness(0, 0, 0, 2)
                });

                var bitmap = new BitmapImage();
                using (var ms = new MemoryStream(sensor.Gst.ChartPng))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                }
                bitmap.Freeze();

                var image = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.Both
                };

                var viewbox = new Viewbox
                {
                    Child = image,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,   // ← не растягивать больше
                    MaxHeight = 250,                                 // ← ограничение
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Top
                };

                gstHost.Children.Add(viewbox);
            }
            else
            {
                gstHost.Children.Add(new TextBlock
                {
                    Text = "ГСТ отсутствует",
                    FontSize = 9,
                    FontStyle = FontStyles.Italic,
                    Foreground = Brushes.Gray
                });
            }

            Grid.SetColumn(gstHost, 2);
            bottomGrid.Children.Add(gstHost);

            Grid.SetRow(bottomGrid, 3);
            root.Children.Add(bottomGrid);

            // ─── Сборка FixedPage ───
            var page = new FixedPage
            {
                Width = pageWidth,
                Height = pageHeight,
                Background = Brushes.White
            };
            page.Children.Add(root);

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
                .Select(s => new SummaryRow
                {
                    Number = s.Number,
                    GradDescription = s.GradDescription,
                    Check1 = GetCheck(s.CheckDescriptions, 0),
                    Check2 = GetCheck(s.CheckDescriptions, 1),
                    Check3 = GetCheck(s.CheckDescriptions, 2),
                    Check4 = GetCheck(s.CheckDescriptions, 3),
                    Check5 = GetCheck(s.CheckDescriptions, 4),
                    Source = s
                })
                .ToList();

            SummaryGrid.ItemsSource = rows;

            if (rows.Count > 0)
                SummaryGrid.SelectedIndex = 0;
        }

        // ✅ ДОБАВЛЕНО: хелпер для безопасного получения проверки по индексу
        private static double? GetCheck(double?[]? arr, int index)
        {
            if (arr == null || index < 0 || index >= arr.Length)
                return null;
            return arr[index];
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
                // ✅ ИСПРАВЛЕНО: без dynamic, сразу IDictionary
                IDictionary<string, object> dict = new ExpandoObject();

                dict["P"] = table.RowHeaders.Count > r
                    ? table.RowHeaders[r].ToString("0.###")
                    : "";

                for (int c = 0; c < table.Values.GetLength(1); c++)
                {
                    dict[$"C{c}"] = table.Values[r, c];
                }

                rows.Add(dict);
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
            else if (tabIndex == 3)
            {
                DetailContent.Content = BuildGstPanel(row);
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
                    col.Children.Add(BuildTchxTable(tchx.CheckTables[i], "Проверка"));
                }

                columns.Children.Add(col);
            }

            root.Children.Add(columns);
            return root;
        }


        private UIElement BuildGstPanel(SummaryRow row)
        {
            var gst = row.Source.Gst;

            // ─── Grid вместо StackPanel: картинка занимает всё доступное место ───
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 0: шапка
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 1: путь
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // 2: картинка
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 3: кнопка

            if (gst == null || !gst.IsOk)
            {
                // ... сюда вставьте код для случая ошибки как раньше ...
                return root;
            }

            // Шапка
            var header = new TextBlock
            {
                Text = $"Резонатор: {gst.ResonatorNumber}    Модель: {gst.ResonatorModel}",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Путь к файлу
            var pathText = new TextBlock
            {
                Text = gst.FilePath,
                FontSize = 10,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(pathText, 1);
            root.Children.Add(pathText);

            // Картинка — в Viewbox, растягивается по всей доступной высоте
            if (gst.ChartPng != null && gst.ChartPng.Length > 0)
            {
                var bitmap = new BitmapImage();
                using (var ms = new MemoryStream(gst.ChartPng))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                }
                bitmap.Freeze();

                var image = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.Both
                };

                var viewbox = new Viewbox
                {
                    Child = image,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.Both,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 10)
                };

                Grid.SetRow(viewbox, 2);
                root.Children.Add(viewbox);
            }

            // Кнопка
            var openBtn = new Button
            {
                Content = "Открыть источник в Excel",
                Width = 200,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(0x50, 0x90, 0xD0)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            openBtn.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = gst.FilePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SummaryWindow] OpenGstSource: {ex.Message}");
                }
            };
            Grid.SetRow(openBtn, 3);
            root.Children.Add(openBtn);

            return root;
        }

        // ═══════════════════════════════════════════════════
        // Таблица ТЧХ
        // ═══════════════════════════════════════════════════
        // ═══════════════════════════════════════════════════
        // Таблица ТЧХ
        // ═══════════════════════════════════════════════════
        private UIElement BuildTchxTable(TchxTable tchx, string defaultTitle, bool forPrint = false)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical };

            string displayTitle = string.IsNullOrEmpty(tchx.Title)
                ? defaultTitle
                : ShortenTchxTitle(tchx.Title);

            stack.Children.Add(new TextBlock
            {
                Text = displayTitle,
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

            // ─── Колонки 0..2 — обычные (с сокращением заголовков) ───
            for (int c = 0; c < 3; c++)
            {
                var col = new DataGridTextColumn
                {
                    Header = ShortenTchxHeader(tchx.ColumnHeaders[c]),
                    Binding = new System.Windows.Data.Binding($"C{c}")
                    {
                        StringFormat = "0.000"
                    },
                    Width = DataGridLength.Auto
                };
                grid.Columns.Add(col);
            }

            // ─── Колонка 3 — с полоской ───
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
                    Header = ShortenTchxHeader(tchx.ColumnHeaders[colIndex]),
                    CellTemplate = template,
                    Width = DataGridLength.Auto
                };
                grid.Columns.Add(column);
            }

            // ─── Строки ───
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

                IDictionary<string, object> dict = new ExpandoObject();

                for (int c = 0; c < 4; c++)
                {
                    dict[$"C{c}"] = tchx.Values[r, c];
                }
                rows.Add(dict);
            }

            grid.ItemsSource = rows;

            stack.Children.Add(grid);
            return stack;
        }


        /// <summary>
        /// Сокращает длинные заголовки ТЧХ для компактного отображения.
        /// </summary>
        /// <summary>
        /// Сокращает длинные заголовки таблиц ТЧХ.
        /// Примеры:
        ///   "Таблица 3 - Расчет ТЧХ степень полинома 2 первого термоканала. Банк № 1187"
        ///   → "ТЧХ². Банк № 1187 - термоканал №1"
        ///   "Таблица 8 - Расчет ТЧХ степень полинома 3 третьего термоканала. Банк № 1187"
        ///   → "ТЧХ³. Банк № 1187 - термоканал №3"
        /// </summary>
        private static string ShortenTchxTitle(string original)
        {
            if (string.IsNullOrWhiteSpace(original)) return original;

            string s = original.Trim();

            // ─── 1. Убираем префикс "Таблица N - Расчет" ───
            s = System.Text.RegularExpressions.Regex.Replace(
                s, @"^Таблица\s+\d+\s*-\s*Расчет\s+", "");

            // ─── 2. Определяем степень полинома и заменяем на значок ───
            string power = null;
            var m = System.Text.RegularExpressions.Regex.Match(
                s, @"степень\s+полинома\s+(\d+)");
            if (m.Success)
            {
                string deg = m.Groups[1].Value;
                power = deg switch
                {
                    "2" => "²",
                    "3" => "³",
                    "4" => "⁴",
                    "5" => "⁵",
                    _ => "^" + deg
                };
                s = s.Remove(m.Index, m.Length);
            }

            // ─── 3. Убираем слово "Расчет" и "ТЧХ" (пока оставляем одно ТЧХ) ───
            s = s.Replace("Расчет ТЧХ", "").Replace("ТЧХ", "");

            // ─── 4. Заменяем длинные названия термоканалов на короткие ───
            s = s.Replace("первого термоканала", "термоканал №1");
            s = s.Replace("второго термоканала", "термоканал №2");
            s = s.Replace("третьего термоканала", "термоканал №3");
            s = s.Replace("четвертого термоканала", "термоканал №4");

            // ─── 5. Убираем лишние пробелы и точки-дубли ───
            s = s.Replace("  ", " ").Trim();
            s = s.Replace(". .", ".").Replace("..", ".");
            s = s.Trim('.', ' ', ',');

            // ─── 6. Собираем заново в нужном порядке ───
            // Ищем "Банк № NNN" в строке
            var bankMatch = System.Text.RegularExpressions.Regex.Match(s, @"Банк\s+№\s*(\d+)");
            string bankPart = bankMatch.Success ? $"Банк № {bankMatch.Groups[1].Value}" : "";

            // Ищем "термоканал №N" в строке
            var chMatch = System.Text.RegularExpressions.Regex.Match(s, @"термоканал\s+№\s*(\d+)");
            string channelPart = chMatch.Success ? $"термоканал №{chMatch.Groups[1].Value}" : "";

            // Собираем: "ТЧХ<степень>. Банк № N - термоканал №N"
            string result = $"ТЧХ{power ?? ""}";
            if (!string.IsNullOrEmpty(bankPart))
                result += $". {bankPart}";
            if (!string.IsNullOrEmpty(channelPart))
                result += $" - {channelPart}";

            return result;
        }

        /// <summary>
        /// Сокращает длинные заголовки столбцов ТЧХ
        /// (T экспериментальная → T эксп., F расчетная → F расч.).
        /// </summary>
        private static string ShortenTchxHeader(string original)
        {
            if (string.IsNullOrWhiteSpace(original)) return original;

            string s = original.Trim();

            // ─── Замены длинных названий на короткие ───
            s = s.Replace("T экспериментальная", "T эксп.");
            s = s.Replace("F экспериментальная", "F эксп.");
            s = s.Replace("t экспериментальная", "t эксп.");
            s = s.Replace("F расчетная", "F расч.");
            s = s.Replace("f расчетная", "f расч.");
            s = s.Replace("t расчетная", "t расч.");
            s = s.Replace("Разность частот", "ΔF");
            s = s.Replace("Разность температур", "Δt");

            // ─── Убираем лишние пробелы ───
            s = s.Replace(" ,", ",").Replace(",  ", ", ");
            s = s.Trim();

            return s;
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
                            // ✅ ИСПРАВЛЕНО: для ExpandoObject через IDictionary, иначе через рефлексию
                            if (item is IDictionary<string, object> dict)
                            {
                                if (dict.TryGetValue(binding.Path.Path, out var v))
                                    value = v?.ToString() ?? "";
                            }
                            else
                            {
                                var prop = item.GetType().GetProperty(binding.Path.Path);
                                if (prop != null)
                                {
                                    var v = prop.GetValue(item);
                                    value = v?.ToString() ?? "";
                                }
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

                            if (dict.TryGetValue(key, out var v))
                            {
                                value = v switch
                                {
                                    double d => d.ToString("0.000"),
                                    _ => v?.ToString() ?? ""
                                };
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