using HidSharp;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TERMINAL.Models;
using TERMINAL.Notifications;

namespace TERMINAL
{
    public partial class MainWindow
    {
        private void AppendLog(string message, Brush color)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendLog(message, color));
                return;
            }

            if (string.IsNullOrEmpty(message)) return;

            if (_logEntries.Count > 0)
            {
                var last = _logEntries[_logEntries.Count - 1];
                if (last.Message == message
                    && (DateTime.Now - last.Time).TotalSeconds < 1.0)
                {
                    last.Time = DateTime.Now;
                    OnPropertyChangedForLogList();
                    return;
                }
            }

            _logEntries.Add(new LogEntry
            {
                Time = DateTime.Now,
                Message = message,
                Color = color
            });

            while (_logEntries.Count > LogMaxEntries)
                _logEntries.RemoveAt(0);

            if (_logListBox != null && _logListBox.Items.Count > 0)
            {
                try
                {
                    _logListBox.ScrollIntoView(
                        _logListBox.Items[_logListBox.Items.Count - 1]);
                }
                catch { }
            }
        }

        public void LogExternal(string message, Brush color)
        {
            AppendLog(message, color);
        }
        private void OnPropertyChangedForLogList()
        {
            if (_logListBox == null) return;
            try
            {
                var items = _logListBox.Items;
                if (items.Count > 0)
                    _logListBox.ScrollIntoView(items[items.Count - 1]);
            }
            catch { }
        }

        private void ShowLogButton_Click(object sender, RoutedEventArgs e)
        {
            if (_logWindow != null && _logWindow.IsVisible)
            {
                _logWindow.Activate();
                return;
            }

            _logWindow = new Window
            {
                Title = "Журнал событий — T.E.R.M.I.N.A.L.",
                Width = 900,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.CanResize,
                Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7)),
                MinWidth = 480,
                MinHeight = 300
            };

            var root = new System.Windows.Controls.Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var topPanel = new System.Windows.Controls.DockPanel { Margin = new Thickness(0, 0, 0, 8) };

            var btnClear = new System.Windows.Controls.Button
            {
                Content = "Очистить",
                Width = 100,
                Height = 26,
                Margin = new Thickness(6, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0xB0, 0xA0, 0x50)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };
            System.Windows.Controls.DockPanel.SetDock(btnClear, System.Windows.Controls.Dock.Right);
            topPanel.Children.Add(btnClear);

            var btnSave = new System.Windows.Controls.Button
            {
                Content = "Сохранить в файл",
                Width = 150,
                Height = 26,
                Margin = new Thickness(6, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x50, 0x90, 0xD0)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };
            System.Windows.Controls.DockPanel.SetDock(btnSave, System.Windows.Controls.Dock.Right);
            topPanel.Children.Add(btnSave);

            var btnCopyAll = new System.Windows.Controls.Button
            {
                Content = "Копировать всё",
                Width = 130,
                Height = 26,
                Margin = new Thickness(6, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x80, 0xB0, 0x80)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };
            System.Windows.Controls.DockPanel.SetDock(btnCopyAll, System.Windows.Controls.Dock.Right);
            topPanel.Children.Add(btnCopyAll);

            var filterPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };

            var filterLabel = new System.Windows.Controls.TextBlock
            {
                Text = "Фильтр:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12
            };
            filterPanel.Children.Add(filterLabel);

            _logFilterBox = new System.Windows.Controls.TextBox
            {
                Width = 260,
                Height = 26,
                FontSize = 12,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Показать только строки, содержащие этот текст"
            };
            filterPanel.Children.Add(_logFilterBox);

            var filterHint = new System.Windows.Controls.TextBlock
            {
                Text = "(пусто — показывать всё)",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = 11,
                Foreground = Brushes.Gray
            };
            filterPanel.Children.Add(filterHint);

            System.Windows.Controls.DockPanel.SetDock(filterPanel, System.Windows.Controls.Dock.Left);
            topPanel.Children.Add(filterPanel);

            System.Windows.Controls.Grid.SetRow(topPanel, 0);
            root.Children.Add(topPanel);

            _logListBox = new System.Windows.Controls.ListBox
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            System.Windows.Controls.ScrollViewer.SetVerticalScrollBarVisibility(
                _logListBox, System.Windows.Controls.ScrollBarVisibility.Auto);
            System.Windows.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(
                _logListBox, System.Windows.Controls.ScrollBarVisibility.Auto);

            // ── Контекстное меню ──
            var ctxMenu = new System.Windows.Controls.ContextMenu();

            var miCopyLine = new System.Windows.Controls.MenuItem
            {
                Header = "Копировать строку"
            };
            miCopyLine.Click += (s, ev) =>
            {
                if (_logListBox?.SelectedItem is LogEntry entry)
                {
                    try
                    {
                        Clipboard.SetText(entry.DisplayText);
                        if (_logCounterText != null) _logCounterText.Text = "Строка скопирована";
                    }
                    catch
                    {
                        MessageBox.Show("Не удалось скопировать в буфер обмена.");
                    }
                }
            };
            ctxMenu.Items.Add(miCopyLine);

            var miCopyContext = new System.Windows.Controls.MenuItem
            {
                Header = "Копировать с контекстом ±5"
            };
            miCopyContext.Click += (s, ev) =>
            {
                if (_logListBox?.SelectedItem is LogEntry entry)
                {
                    int idx = _logEntries.IndexOf(entry);
                    if (idx < 0) return;

                    int from = Math.Max(0, idx - 5);
                    int to = Math.Min(_logEntries.Count - 1, idx + 5);

                    var sb = new StringBuilder();
                    for (int i = from; i <= to; i++)
                        sb.AppendLine(_logEntries[i].DisplayText);

                    try
                    {
                        Clipboard.SetText(sb.ToString());
                        if (_logCounterText != null)
                            _logCounterText.Text = $"Скопировано строк: {to - from + 1}";
                    }
                    catch
                    {
                        MessageBox.Show("Не удалось скопировать в буфер обмена.");
                    }
                }
            };
            ctxMenu.Items.Add(miCopyContext);

            ctxMenu.Items.Add(new System.Windows.Controls.Separator());

            var miCopyAll = new System.Windows.Controls.MenuItem
            {
                Header = "Копировать всё"
            };
            miCopyAll.Click += (s, ev) =>
            {
                var sb = new StringBuilder();
                foreach (var entry in _logEntries)
                    sb.AppendLine(entry.DisplayText);

                try
                {
                    Clipboard.SetText(sb.ToString());
                    if (_logCounterText != null)
                        _logCounterText.Text = $"Скопировано строк: {_logEntries.Count}";
                }
                catch
                {
                    MessageBox.Show("Не удалось скопировать в буфер обмена.");
                }
            };
            ctxMenu.Items.Add(miCopyAll);

            _logListBox.ContextMenu = ctxMenu;
            _logListBox.PreviewMouseRightButtonDown += (s, ev) =>
            {
                if (ItemsControl.ContainerFromElement(_logListBox, (DependencyObject)ev.OriginalSource) is ListBoxItem item)
                {
                    item.IsSelected = true;
                }
            };

            // ── ItemTemplate с подсветкой ──
            var itemTemplate = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            factory.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
                new System.Windows.Data.Binding(nameof(LogEntry.DisplayText)));
            factory.SetBinding(System.Windows.Controls.TextBlock.ForegroundProperty,
                new System.Windows.Data.Binding(nameof(LogEntry.Color)));
            factory.SetBinding(System.Windows.Controls.TextBlock.BackgroundProperty,
                new System.Windows.Data.Binding(nameof(LogEntry.Background)));
            factory.SetValue(System.Windows.Controls.TextBlock.FontFamilyProperty,
                new FontFamily("Consolas"));
            factory.SetValue(System.Windows.Controls.TextBlock.FontSizeProperty, 12.0);
            factory.SetValue(System.Windows.Controls.TextBlock.TextWrappingProperty,
                TextWrapping.NoWrap);
            factory.SetValue(System.Windows.Controls.TextBlock.PaddingProperty,
                new Thickness(2, 1, 2, 1));

            var dimTrigger = new DataTrigger
            {
                Binding = new System.Windows.Data.Binding(nameof(LogEntry.IsDimmed)),
                Value = true
            };

            dimTrigger.Setters.Add(new Setter(
                System.Windows.Controls.TextBlock.OpacityProperty, 0.35));
            itemTemplate.Triggers.Add(dimTrigger);

            itemTemplate.VisualTree = factory;

            _logListBox.ItemTemplate = itemTemplate;
            _logListBox.ItemsSource = _logEntries;

            System.Windows.Controls.Grid.SetRow(_logListBox, 1);
            root.Children.Add(_logListBox);

            // ── Нижняя панель со счётчиком ──
            var bottomPanel = new System.Windows.Controls.DockPanel { Margin = new Thickness(0, 6, 0, 0) };

            _logCounterText = new System.Windows.Controls.TextBlock
            {
                FontSize = 11,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center
            };

            void UpdateCounter()
            {
                if (_logCounterText != null)
                    _logCounterText.Text = $"Записей: {_logEntries.Count} (максимум {LogMaxEntries})";
            }

            _logEntries.CollectionChanged += (s, ev) => UpdateCounter();
            UpdateCounter();

            System.Windows.Controls.DockPanel.SetDock(_logCounterText, System.Windows.Controls.Dock.Left);
            bottomPanel.Children.Add(_logCounterText);

            var closeBtn = new System.Windows.Controls.Button
            {
                Content = "Закрыть",
                Width = 100,
                Height = 26,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };
            System.Windows.Controls.DockPanel.SetDock(closeBtn, System.Windows.Controls.Dock.Right);
            bottomPanel.Children.Add(closeBtn);

            System.Windows.Controls.Grid.SetRow(bottomPanel, 2);
            root.Children.Add(bottomPanel);

            btnCopyAll.Click += (s, ev) =>
            {
                var sb = new StringBuilder();
                foreach (var entry in _logEntries)
                    sb.AppendLine(entry.DisplayText);

                try
                {
                    Clipboard.SetText(sb.ToString());
                    if (_logCounterText != null)
                        _logCounterText.Text = "Скопировано в буфер обмена";
                }
                catch
                {
                    MessageBox.Show("Не удалось скопировать в буфер обмена.");
                }
            };

            btnSave.Click += (s, ev) =>
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
                    FileName = $"TERMINAL_log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt",
                    Title = "Сохранить журнал"
                };

                if (sfd.ShowDialog() != true) return;

                try
                {
                    var sb = new StringBuilder();
                    foreach (var entry in _logEntries)
                        sb.AppendLine(entry.DisplayText);

                    File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                    if (_logCounterText != null)
                        _logCounterText.Text = $"Сохранено: {Path.GetFileName(sfd.FileName)}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения: {ex.Message}");
                }
            };

            btnClear.Click += (s, ev) =>
            {
                if (MessageBox.Show("Очистить журнал?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                _logEntries.Clear();
            };

            closeBtn.Click += (s, ev) => _logWindow.Close();

            _logFilterBox.TextChanged += (s, ev) => ApplyLogFilter();

            _logWindow.Content = root;
            _logWindow.Closed += (s, ev) =>
            {
                _logWindow = null;
                _logListBox = null;
                _logFilterBox = null;
                _logCounterText = null;
            };

            _logWindow.Show();
            ApplyLogFilter();
        }

        private void ApplyLogFilter()
        {
            if (_logListBox == null) return;

            string filter = _logFilterBox?.Text?.Trim() ?? "";

            // Сбросим подсветку и приглушение
            foreach (var entry in _logEntries)
            {
                entry.Background = Brushes.Transparent;
                entry.IsDimmed = false;
            }

            if (!string.IsNullOrEmpty(filter))
            {
                int matchCount = 0;
                LogEntry? firstMatch = null;

                foreach (var entry in _logEntries)
                {
                    bool match = entry.Message.Contains(filter, StringComparison.OrdinalIgnoreCase)
                              || entry.DisplayText.Contains(filter, StringComparison.OrdinalIgnoreCase);

                    if (match)
                    {
                        entry.Background = new SolidColorBrush(
                            Color.FromRgb(0xFF, 0xF4, 0x80));
                        matchCount++;
                        if (firstMatch == null) firstMatch = entry;
                    }
                    else
                    {
                        entry.IsDimmed = true;
                    }
                }

                UpdateLogCounterText($"Найдено: {matchCount} из {_logEntries.Count}");

                if (firstMatch != null)
                {
                    try { _logListBox.ScrollIntoView(firstMatch); }
                    catch { }
                }
            }
            else
            {
                UpdateLogCounterText(
                    $"Записей: {_logEntries.Count} (максимум {LogMaxEntries})");

                if (_logListBox.Items.Count > 0)
                {
                    try
                    {
                        _logListBox.ScrollIntoView(_logListBox.Items[^1]);
                    }
                    catch { }
                }
            }
        }

        private void UpdateLogCounterText(string text)
        {
            if (_logCounterText == null) return;
            _logCounterText.Text = text;
        }
        private static string GetAppVersion()
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var v = asm.GetName().Version;
            if (v == null) return "v0.0.0";

            return (v.Revision == 0 && v.Build >= 0)
                ? $"v{v.Major}.{v.Minor}.{v.Build}"
                : $"v{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }

        private void VersionText_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ShowChangelogWindow();
        }

        private void ShowChangelogWindow()
        {
            var dlg = new Window
            {
                Title = "История изменений — T.E.R.M.I.N.A.L.",
                Width = 640,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.CanResize,
                WindowStyle = WindowStyle.SingleBorderWindow,
                Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7)),
                MinWidth = 420,
                MinHeight = 320
            };

            var root = new System.Windows.Controls.Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var header = new System.Windows.Controls.TextBlock
            {
                Text = "История изменений",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            System.Windows.Controls.Grid.SetRow(header, 0);
            root.Children.Add(header);

            var scroller = new System.Windows.Controls.ScrollViewer
            {
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                Padding = new Thickness(4),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                BorderThickness = new Thickness(1)
            };

            var changelogText = new System.Windows.Controls.TextBox
            {
                Text = GetChangelogText(),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12.5,
                Padding = new Thickness(8),
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                IsReadOnlyCaretVisible = false,
                Cursor = System.Windows.Input.Cursors.Arrow
            };

            changelogText.PreviewMouseDown += (s, ev) =>
            {
                if (ev.ChangedButton == System.Windows.Input.MouseButton.Left)
                    ev.Handled = true;
            };

            scroller.Content = changelogText;
            System.Windows.Controls.Grid.SetRow(scroller, 1);
            root.Children.Add(scroller);

            var closeBtn = new System.Windows.Controls.Button
            {
                Content = "Закрыть",
                Width = 110,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x50, 0x90, 0xD0)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };
            closeBtn.Click += (s, ev) => dlg.Close();

            System.Windows.Controls.Grid.SetRow(closeBtn, 2);
            root.Children.Add(closeBtn);

            dlg.Content = root;
            dlg.ShowDialog();
        }

        private static string GetChangelogText()
        {
            return
        @"═══════════════════════════════════════════════════════════════
         T.E.R.M.I.N.A.L. — история изменений
        ═══════════════════════════════════════════════════════════════
v1.6.0

Улучшения (интерфейс и логика):
  - В настройках появилась вкладка «Уведомления».
  - Добавлены уведомления для Telegram, VK, Email (Yandex, Mail.ru, Gmail, 
    «Другой») и SMS.
  - Добавлено сохранение настроек для каждого метода уведомлений отдельно.
  - На главном экране появилась отдельная кнопка для настроек уведомлений.
  - Добавлен метод уведомлений МАКС (реализован алгоритм, но протестировать 
    нельзя — токен доступен только бизнес-аккаунтам, ИП и самозанятым; 
    оставлена заготовка на будущее).
  - Реализован метод уведомлений для ВК через сообщество (создать группу, 
    получить токен, разрешить сообщения, написать с личной страницы).
  - Добавлена и отлажена тёмная тема для главного окна.
  - Добавлена тёмная тема для окна «Сводка данных».
  - Добавлена тёмная тема для окна «Настройки приборов».
  - Добавлено сохранение выбранной темы (светлая/тёмная) между запусками.
  - Добавлено шифрование .json-файла с настройками.
  - Доработаны уведомления: вывод таймера, количества датчиков, номера заказа 
    и другой полезной информации.
  - Настроена и отлажена таблица в Telegram: живая таблица, обновляется 
    по мере прохождения шагов.
  - Отлажена массовая рассылка (несколько почт + VK + Telegram одновременно).
  - Адаптирован макрос фильтрации для РЧ-24.
  - В Telegram добавлен таймер стабилизации (обновляется каждые 5 секунд), 
    а не просто статичное сообщение «Ожидание стабилизации 120 мин...». 
    Сообщение о стабилизации теперь приходит раньше таблицы давлений, 
    а таблица — последняя.
  - Если включить уведомления в Telegram во время уже запущенного пакетного 
    режима, таблица или таймер стабилизации придут сразу, а не только 
    при старте градуировки. Раньше таблица появлялась только при старте.
  - Статус «пропущено» теперь ставится сразу при активации пакетного режима 
    для шагов, не отмеченных галочками. Раньше они висели как «ожидает» 
    до момента, когда программа до них доходила.
  - Теперь при сохранении настроек, если сетевые параметры (IP, порт, 
    COM-порт, источник платины) не изменились, переподключение к приборам 
    не выполняется — экономит время и не сбрасывает связь.
  - Закомментирован отладочный код для АГК, который выводил сырой ответ 
    get_state_in_point; и содержимое кэша каждую секунду — он заваливал журнал.

Исправленные ошибки (баги):
  - Исправлено смещение вводимых значений в пакетном режиме при переключении 
    темы: в светлой теме текст был по центру, а в тёмной — прилипал влево. 
    Теперь выравнивание одинаковое в обеих темах.
  - В тёмной теме не была видна стрелка шага (была чёрной на тёмном фоне). 
    Теперь стрелка белая и хорошо читается.
  - Исправлена проблема с невидимым текстом в пакетном режиме в тёмной теме: 
    теперь весь текст белый и контрастный.
  - Проверены и исправлены замеры длительности операций: agk-poll (всегда 
    был 0) и furnace_poll. Теперь они корректно отображаются в плашке «такт».
  - Исправлен порядок сообщений в Email и VK: раньше сначала приходило 
    «Шаг 14 начат», а только потом «Шаг 13 завершён». Теперь порядок 
    строго последовательный (завершение предыдущего шага → начало следующего).
  - Исправлен баг в выпадающем списке выбора каналов уведомлений: при клике 
    на «VK» текст улетал в название поля, а не ставил галочку. Теперь 
    все пункты ведут себя одинаково — клик в любом месте строки ставит/снимает 
    галочку и не закрывает список.
  - Исправлена проблема с Gmail: если были выбраны все почты (Mail.ru, 
    Yandex, Gmail), то на Gmail письма не доходили. При выборе только Gmail 
    тест работал. Теперь каждая почта получает свои данные корректно, 
    а Gmail не «банится» за частые подключения (переиспользуется SmtpClient).
  - Исправлен баг с дублированием таблицы в Telegram: раньше приходило 
    две таблицы — одна до градуировки, вторая после начала (она уже 
    редактировалась). Теперь добавлена проверка, что градуировка началась, 
    и таблица создаётся только одна.
  - Исправлено отображение названий пресетов: раньше в выпадающем списке 
    показывалось «TERMINAL.Models...» вместо названия пресета. Теперь 
    отображается корректное имя (через DisplayMemberPath).
  - Исправлена проблема с отображением активной вкладки в тёмной теме: 
    текст сливался с белым фоном. Теперь активная вкладка имеет тёмный 
    фон и белый текст, как в окне настроек.
  - Устранена проблема с отсутствием рамки у выпадающих списков (ComboBox) 
    в тёмной теме, в том числе в заблокированном состоянии (например, 
    во время пакетного режима). Теперь рамка видна всегда.
  - Исправлен баг с невидимым паролем в тёмной теме: поле PasswordBox 
    теперь имеет тёмный фон и белый текст.

v1.5.0
Известные проблемы:
  - Проблема с отображением уставки 0 при сбросе: 
    неверная последовательность команд приводит к переходу АГК в режим «стоп»
    пока что закомментирован код для уставки = 0, происходит обычный сброс
    без изменения уставки

Исправленные ошибки (баги):
  - Исправлен баг в сводке данных: если все значения проверки равны 0, 
    вместо «мусора» (например, -2146826273) теперь корректно отображается «—».
  - Исправлена ошибка, из-за которой при запуске пакетного режима 
    не читалась стабилизация на 2-м шаге (требовался перезапуск программы). 
    Теперь фоновый опрос АГК работает всегда, а кэш обновляется независимо 
    от пакетного режима.
  - Исправлена логика выдержки атмосферного давления при «Бароциклировании»: 
    программа больше не ждет статус get_state_in_point; и ориентируется 
    на физическое падение давления.
  - Исправлен грубый баг: если в шагах запускались «Бароциклирование» и «Градуировка», 
    программа не считала количество циклов и после первого цикла переходила 
    в режим «Градуировка».
  - Устранена проблема с зависанием статуса «Отправлена команда 
    «Новая калибровка», ожидание ответа...» после успешной очистки данных. 
    Теперь статус корректно меняется на «Данные успешно очищены».
  - Исправлена проблема с отображением уставки 0 при сбросе: 
    неверная последовательность команд приводила к переходу АГК в режим «стоп».
  - В уставку АГК писали несколько методов, поэтому при сбросе один метод пытался
    записать 0, другой читал реальную уставку 100 и значение «мигало», теперь пишет
    только Timer_Tick, остальные методы должны брать значения из кэша

Улучшения интерфейса:
  - Плашка «Длинные/Короткие таймеры» увеличена, текст больше не обрезается.
  - В окно настроек добавлена вертикальная прокрутка (ScrollViewer) для 
    всех вкладок. Теперь при большом количестве полей (например, на вкладке 
    ST-351) настройки не обрезаются.
  - Добавлена вкладка «Общие» с галочкой «Тактирование» и логированием 
    медленных тактов (для отладки).
  - Настройки из пакетного режима (q2view, таймер для q2view, автозаполнение Excel) 
    перенесены в новое окно настроек на вкладку «Общие».
  - Переименован режим «Жамки» в «Бароциклирование».
  - При выбранном режиме «Бароциклирование» поле «точка» автоматически скрывается
    т.к. оно относится только к режиму «градуировка».
  - Убран лишний лог «Стабилизация 0/5» из журнала событий, теперь пишется только
    последний шаг 5/5.

Улучшения производительности и стабильности:
  - Реализовано асинхронное выполнение макросов Excel в фоне, чтобы клиент 
    TERMINAL не подвисал во время обработки.
  - Реализовано асинхронное чтение ГСТ, чтобы интерфейс не зависал 
    при загрузке графиков.
  - Реализовано асинхронное заполнение сводки данных, чтобы клиент 
    TERMINAL не подвисал при чтении файлов .xlsm.
  - Улучшена работа таймера: теперь он привязан к системному времени. 
    Если тик длится 5 секунд, таймер сверится с системой и сдвинется 
    тоже на 5 секунд, а не на 1, как было раньше.

Работа с Excel:
  - На вкладке СКТБ ЭлПА добавлен выбор макросов для фильтрации данных под разные интерфейсы
  - Исправлен баг, когда макросы хватали мусорные данные и тёрли нужные диапазоны

Для ST-351:
  - На вкладку ST-351 добавлена настройка автовыключения камеры тепла/холода после 
    прохождения всех шагов пакетного режима.
  - В настройки вынесен параметр «Допуск стабилизации, °C» (по умолчанию 
    +- 0.1 °C), по которому считается стабилизация камеры тепла/холода ST-351.
  - Переименована галочка автовыключения (текст сокращен, полное описание вынесено 
    в ToolTip).

Для АГК:
  - Улучшена работа с АГК - добавлено кэширование значений, метод Timer_Tick
    читает все значения раз в секунду и пишет в кэш, уже из кэша они уходят во
    все остальные методы. Раньше каждый метод опрашивал АГК отдельно, отсюда
    один тик набегал 1,5 - 2 секунды, теперь 100-200 мс.
  - На вкладку АГК добавлено поле таймаута. Если за это время прибор 
    не сможет набрать давление, программа остановится и сбросит давление.

СКТБ ЭлПА:
  - Добавлена галочка «Читать платину сейчас» — опрос платины теперь 
    выполняется только если она активна (для снижения нагрузки программы).
  - Добавлен выбор источника платины (МтК @ TERMEX, МИТ 8.03, TERCON), TERCON -
    пока что только заглушка, нужно реализовать скрипт с опросом, прибор пока занят
    на градуировках.
  - Добавлена возможность редактирования команды, отправляемой в МтК @ TERMEX.
  - При выборе платины MIT в настройках теперь отображается список только 
    активных COM-портов в системе, а не фиксированный список 1–20.
  - Текст ввода для команды МтК @ TERMEX выровнен по вертикали 

Для Сводки данных:
  - Перекомпановка листа печати 
  - Донастройка массовой печати
  - Добавление гистограмм в таблицы, как в Excel
  - Смещение оси гистограмм, как в Excel
  - Убрана сортировка при заполнении сводки данных (теперь порядок 
    точно как в папке), а также при сохранении и загрузке.
  - 


v1.4.4
Тайминги и мониторинг:
  - Добавлена плашка «такт» с суммой времени за секунду и
    разбивкой по операциям (main, furnace, platina, sensorUI,
    clientsUI, kosa, timer-step, batch-stab и т.д.).
  - Цветовая индикация: зелёный < 300 мс, оранжевый 300–600 мс,
    красный > 600 мс.
  - Автоматическая запись в журнал «⚠ Медленный такт» при
    превышении 500 мс.
  - Все циклические таймеры переведены на «1000 − elapsed»:
    остаток секунды вычисляется от начала такта.
  - SafeDelayAsync: шаг ожидания уменьшен с 100 до 20 мс —
    точность выросла с ~15% до ~2%.
  - Timer_Tick: паузы между командами АГК уменьшены
    с 200 до 50 мс (экономия ~600 мс на такт).
  - KosaWaitAsync: убран лишний SafeDelay поверх операций,
    счётчик теперь ровный.
  - RunTimerWithStepAsync и RunTimerWithRightBlockAsync:
    переведены на точный интервал.
  - WaitForStateInPointAsync и WaitUntilPressureBelowAsync:
    переведены на точный интервал.
  - WaitForBatchStabilizationAsync и
    WaitForBatchTemperatureInToleranceAsync: переведены
    на точный интервал.


v1.4.3
Excel и макросы:
  - Убрано ограничение Case 1..10 — теперь принимается любое
    число ≥ 1.
  - Обновлён скрипт клиента v19: CLIENT_CASE может быть
    до 10000 (защита от мусора).
  - Скрипт клиента теперь отправляет CASE <N> в TERMINAL
    строкой протокола (после NAME).
  - Сообщение об ошибке в RunExcelMacroInternal обновлено
    на «Количество датчиков должно быть не меньше 1».
  - RunCalibrationModeAsync корректно работает с новым
    диапазоном Case.

CSV и графики ГСТ:
  - CSV сохраняет PNG-графики ГСТ в Base64 (секция
    «# --- ГСТ ---»).
  - Метаданные ГСТ (Резонатор, Модель, Файл) сохраняются
    рядом с Base64.
  - LoadFromCsv декодирует Base64 обратно в ChartPng.
  - Обратная совместимость: старые CSV без секции ГСТ
    загружаются корректно.

Парсер ГСТ:
  - Универсальный парсер папок диапазона на регулярном
    выражении (\d+)[._](\d+).
  - Поддержка формата 2025: 188.25-214.25(...).
  - Поддержка формата 2026: 151.26_174.26(...).
  - Поддержка смешанных форматов: 42.26_51.26(...).
  - Поиск файлов с точками внутри номеров
    (103.26_104.26_...xlsx).
  - Убрано сохранение CaseValue по SID/CustomName/индексу —
    значение теперь приходит от клиента.

Прочее:
  - Исправлены дубликаты класса Settings (были в двух файлах).
  - Удалён класс ClientSetting и связанные методы
    SaveClientsSnapshot / ApplySavedSettingsToClient.
  - Добавлен аккумулятор таймингов:
    _tickTimingAccum, _tickTimingLock, _tickTimingTotalMs.
  - В SummaryWindow.xaml: вкладки подняты выше, график ГСТ
    увеличен (MaxHeight 800, растягивание на всё окно).
  - В SummaryWindow.xaml.cs: BuildGstPanel переведён
    с StackPanel на Grid с Height=""*"" для растягивания.
  - В MainWindow.xaml: добавлен TextBlock TickTimingText
    в ROW 3.
  - ToolTip у поля CaseValue у клиента обновлён
    на «Количество датчиков в текущей загрузке».

Совместимость:
  - Старые settings.json корректно загружаются.
  - Старые CSV без секции ГСТ корректно загружаются.
  - Старые скрипты клиента (без CASE) работают, но
    CaseValue останется значением по умолчанию.

v1.3.1
  • Доведено до ума чтение .xlsm файлов для сводки данных
  • Реализована вкладка гистерезис (2 таблицы)
  • Реализована вкладка ТЧХ (по 2 таблицы на каждый термоканал)
  • Реализована вкладка Погрешность
  • Автоскрытие пустых колонок и строк в сводке
  • Цветовая маркировка + гистограммы как в Excel
  • Реализовано сохранение сводки данных в .csv
  • Реализована выгрузка сводки данных из сохраненного .csv
  • Реализовано копирование таблиц в буфер (ПКМ)
  • Реализовано логирование в журнал при чтении Excel
  • Добавлен прогресс-бар при чтении Excel

v1.2.2
  • Добавлено автозаполнение данных после окончания съёма
  • Добавлено поле для ввода номера точки для заполнения данных
  • Добавлена тестовая кнопка Открыть в 'Excel' для отладки автозаполнения
  • Для сохранения файла теперь данные берутся по уставке в пакетном режиме,
    а не из текущей прочитанной уставки печки, так надёжнее и не нужно округлять.

v1.1.21
  • Добавлен Changelog с описанием изменений в версиях программы
  • Добавлен Журнал с логами всех действий программы
  • В пакетный режим на каждую температуру добавлен выбор 
    Градуировка/Жамкать и выбор таймеров
  • Добавлено поле с какого шага начать и количество жамков 
    (смотря какой режим выбран) для каждой температуры
  • Теперь 0 во времени стабилизации - запуск без стабилизации 
    (Как будто нажали кнопку Старт старую)
  • Глобальные настройки (диапазон, таймеры, КОСА) перенесены
    в шапку пакетного режима и применяются ко всем активным
    заданиям.
  • Удалён обычный СТАРТ — кнопка СТАРТ всегда запускает пакет.
  • Удалён автоСтарт — его роль выполняет задание в пакете с
    нужным временем стабилизации.
  • У каждого задания своё время стабилизации (0 = без выдержки,
    но с проверкой допуска).
  • добавлена подпись SID из макроса
  • Перекомпановка внешнего вида программы
  • Код разбит на partial-классы, модели вынесены
  • Добавлена цветная плашка КОСА в пакетном режиме, которую
    четко видно, если включена галочка КОСА
  • Добавлена плашка с дельтой температуры при выходе на уставку
    (нужно потестить)
  • Улучшена работа с логом, добавлена возможность копировать
    отдельные строки
  • Подсветка совпадений при поиске в журнале
  • Добавлены пресеты для пакетного режима

v1.0.79
  • Добавлен Changelog с описанием изменений в каждой версии программы
  • Добавлен журнал операций, в котором можно посмотреть все логи событий
  • Добавлены подписи SID клиентов через макрос, можно подписать каждый клиент либо вручную, либо через макрос

v1.0.78
  • СКТБ ЭлПа: каждая строка (Платина / ПДА / ПТК) скрывается
    отдельно, если соответствующий источник не отвечает дольше
    5 секунд.
  • СКТБ ЭлПа: общий индикатор в заголовке теперь зелёный, если
    активен хотя бы один прибор (Платина / ПДА / ПТК), и
    красный, если молчат все три.
  • Удалено неиспользуемое поле _timerActive.

v1.0.77
  • Пакетный режим по температурам для «Градуировки»:
    список заданий (уставка °C + время стабилизации, мин),
    кнопки «+ Добавить» / «Очистить», сохранение в settings.json.
  • Кнопка СТАРТ автоматически превращается в «СТАРТ (пакет)»,
    если есть активные задания.
  • АвтоСтарт отключён в пакетном режиме.

v1.0.76
  • Добавлен автоСтарт: запуск «Градуировка» после 120 мин
    стабилизации, «Жамкать» — после 30 мин.
  • Таймер стабилизации с грейс-периодом 60 сек при выходе
    температуры за допуск ±0.1 °C.

v1.0.75
  • Поддержка клиентов-датчиков KalibratorGUI с CAPS ATM и
    FURNACE_TEMP (ПДА и ПТК в карточке СКТБ ЭлПа).
  • Роли клиентов отображаются в списке подключённых.

v1.0.74
  • Считывание платины по USB HID (VID 0xFFFF, PID 0x0009).
  • Отображение температуры платины в карточке СКТБ ЭлПа.

v1.0.73
  • Подключение к АГК (192.168.0.159:8888), опрос
    get_units / get_press / get_point / get_status.
  • Управление штоками: set_mode=control / vent / stop,
    set_point, set_cal_stocks=start.

v1.0.72
  • Подключение к печи ST-351 по Modbus TCP
    (192.168.0.167:502).
  • Запись/чтение уставки (рег. 201/202), чтение текущей
    температуры (рег. 205), режим manual/idle (рег. 200).

v1.0.71
  • Broadcast-сервер для KalibratorGUI (порты 9990 — рабочая
    версия, 9999 — тестовая).
  • Ожидание DONE_OK/DONE_FAIL от клиентов, пауза 3 сек
    после каждого съёма.

v1.0.70
  • Режим «Жамкать»: циклическая накачка до максимума и сброс
    до атмосферы, счётчик циклов.

v1.0.60
  • Режим «Градуировка»: проход по точкам диапазона с
    ожиданием стабилизации давления и съёмом данных.
  • КОСА: три съёма на точку с паузой (сек) и продлением
    таймера на +30 сек при срыве давления.

v1.0.50
  • Выбор диапазона давлений (кгс/см² и МПа), таблица шагов,
    короткие и длинные таймеры.
  • Выбор единиц измерения на АГК, парсинг ответов.

v1.0.00
  • Первый запуск. Базовый каркас окна, подключение к АГК,
    СТАРТ/ПАУЗА/СТОП, сохранение и очистка данных.
";
        }

        private void SetStatus(string text, bool running = true, bool log = true)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetStatus(text, running, log)); return; }

            StatusText.Text = text;
            StatusText.Foreground = running ? Brushes.Green : Brushes.Gray;

            if (log)
                AppendLog(text, running ? Brushes.Green : Brushes.Gray);
        }

        private void SetStatusSuccess(string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetStatusSuccess(text)); return; }
            StatusText.Text = text; StatusText.Foreground = Brushes.Green;
            AppendLog(text, Brushes.Green);
        }

        private void SetStatusWarning(string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetStatusWarning(text)); return; }
            StatusText.Text = text; StatusText.Foreground = Brushes.Goldenrod;
            AppendLog(text, Brushes.Goldenrod);
        }

        private void SetStatusError(string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetStatusError(text)); return; }
            StatusText.Text = text; StatusText.Foreground = Brushes.OrangeRed;
            AppendLog(text, Brushes.OrangeRed);
        }

        private void SetExternalButtonsEnabled(bool enabled)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetExternalButtonsEnabled(enabled));
                return;
            }

            if (TakePointBroadcastButton != null) TakePointBroadcastButton.IsEnabled = enabled;
            if (SaveDataButton != null) SaveDataButton.IsEnabled = enabled;
            if (ClearDataButton != null) ClearDataButton.IsEnabled = enabled;
            if (RemoveOfflineButton != null) RemoveOfflineButton.IsEnabled = enabled;
        }



    }
}