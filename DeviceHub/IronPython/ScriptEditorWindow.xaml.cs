using Microsoft.Scripting.Hosting;
using IronPython.Hosting;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using TERMINAL.DeviceHub;

namespace TERMINAL.Windows
{
    public partial class ScriptEditorWindow : Window
    {
        private ScriptEngine _engine;
        private ScriptScope _scope;
        private PollingTabView _pollingView;
        private ScriptNode _editingScript = null;
        private System.Windows.Threading.DispatcherTimer _compileTimer;

        public ScriptEditorWindow(PollingTabView pollingView)
        {
            InitializeComponent();
            _pollingView = pollingView;

            ConfigureSyntaxHighlighting();

            _engine = Python.CreateEngine();
            _scope = _engine.CreateScope();
            _scope.SetVariable("terminal", _pollingView);

            ScriptCodeBox.Text =
        @"# Доступные переменные:
# terminal -> ссылка на PollingTabView (можно брать Interfaces)
# print(...) -> вывод в панель справа
#
# Пример: вывести все устройства на шине
# for iface in terminal.Interfaces:
#     for dev in iface.Devices:
#         print(dev.DisplayName)

def Evaluate():
    return 0.0
";

            // ★ Инициализация таймера для авто-компиляции
            _compileTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _compileTimer.Tick += CompileTimer_Tick;

            // Подписываемся на изменение текста
            ScriptCodeBox.TextChanged += ScriptCodeBox_TextChanged;
        }
        public ScriptEditorWindow(PollingTabView pollingView, ScriptNode script) : this(pollingView)
        {
            _editingScript = script;
            ScriptCodeBox.Text = script.Code;
            StatusTextBlock.Text = $"Редактирование: {script.Name}";
            StatusTextBlock.Foreground = Brushes.Blue;
            this.Title = $"Редактор скриптов - {script.Name}";

            // Отписываемся от автокомпиляции для начального текста, чтобы не спамить в лог
            ScriptCodeBox.TextChanged -= ScriptCodeBox_TextChanged;
            ScriptCodeBox.TextChanged += ScriptCodeBox_TextChanged;
        }

        // ★ Автокомпиляция при вводе (как в Kalibrator)
        private void ScriptCodeBox_TextChanged(object sender, EventArgs e)
        {
            // ★ При каждом нажатии клавиши сбрасываем таймер
            _compileTimer.Stop();
            _compileTimer.Start();

            // Визуально показываем, что идет ввод
            StatusTextBlock.Text = "Ввод...";
            StatusTextBlock.Foreground = Brushes.Gray;
        }

        private async void CompileTimer_Tick(object sender, EventArgs e)
        {
            _compileTimer.Stop();

            if (_editingScript == null) return;

            string code = ScriptCodeBox.Text;
            string currentScopeText = "";

            try
            {
                // Компилируем в фоновом потоке, чтобы не вешать UI
                await Task.Run(() =>
                {
                    _editingScript.ReloadCode(code);
                });

                StatusTextBlock.Text = "Код скомпилирован без ошибок";
                StatusTextBlock.Foreground = Brushes.Green;
                OutputTextBlock.Text = "";
                ScriptCodeBox.Select(0, 0); // Убираем выделение ошибки, если было
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Ошибка синтаксиса!";
                StatusTextBlock.Foreground = Brushes.Red;

                var operations = _engine.GetService<ExceptionOperations>();
                string errorMessage = operations.FormatException(ex);
                OutputTextBlock.Text = errorMessage;

                // ★ Извлекаем номер строки из ошибки IronPython
                int lineNumber = -1;
                var match = System.Text.RegularExpressions.Regex.Match(errorMessage, @"line (\d+)");
                if (match.Success)
                {
                    lineNumber = int.Parse(match.Groups[1].Value);
                }

                if (lineNumber > 0 && lineNumber <= ScriptCodeBox.LineCount)
                {
                    var line = ScriptCodeBox.Document.GetLineByNumber(lineNumber);
                    ScriptCodeBox.Select(line.Offset, line.Length);
                    ScriptCodeBox.ScrollToLine(lineNumber);
                    ScriptCodeBox.Focus();
                }
            }
        }


        // ★ Метод для подсветки синтаксиса Python
        private void ConfigureSyntaxHighlighting()
        {
            try
            {
                // Используем встроенный в AvalonEdit подсветчик Python (если он есть в сборке)
                // Если нет, можно создать файл Python.xshd и загрузить его.
                // Для простоты используем стандартный из HighlightingManager.
                var highlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("Python");
                if (highlighting != null)
                {
                    ScriptCodeBox.SyntaxHighlighting = highlighting;
                }
                else
                {
                    // Если стандартного нет, скачиваем/создаем. 
                    // Пока оставим без подсветки, если не найдено.
                }
            }
            catch
            {
                // Игнорируем ошибки подсветки
            }
        }

        private void RunScriptButton_Click(object sender, RoutedEventArgs e)
        {
            OutputTextBlock.Text = "";
            StatusTextBlock.Text = "Выполнение...";
            StatusTextBlock.Foreground = Brushes.Blue;

            try
            {
                var stream = new MemoryStream();
                _engine.Runtime.IO.SetOutput(stream, System.Text.Encoding.UTF8);
                _engine.Runtime.IO.SetErrorOutput(stream, System.Text.Encoding.UTF8);

                var source = _engine.CreateScriptSourceFromString(ScriptCodeBox.Text);
                source.Execute(_scope);

                stream.Position = 0;
                using var reader = new StreamReader(stream);
                OutputTextBlock.Text = reader.ReadToEnd();

                StatusTextBlock.Text = "Успешно выполнено!";
                StatusTextBlock.Foreground = Brushes.Green;
            }
            catch (Exception ex)
            {
                OutputTextBlock.Text = $"ОШИБКА:\n{ex.Message}";
                StatusTextBlock.Text = "Ошибка выполнения";
                StatusTextBlock.Foreground = Brushes.Red;
            }
        }

        private void LoadScriptButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Скрипты (*.py;*.KGPY)|*.py;*.KGPY|Python файлы (*.py)|*.py|KGPY файлы (*.KGPY)|*.KGPY|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                ScriptCodeBox.Text = File.ReadAllText(dlg.FileName);
                StatusTextBlock.Text = $"Загружен: {Path.GetFileName(dlg.FileName)}";
            }
        }

        private void SaveScriptButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Скрипты (*.py;*.KGPY)|*.py;*.KGPY|Python файлы (*.py)|*.py|KGPY файлы (*.KGPY)|*.KGPY|Все файлы (*.*)|*.*",
                DefaultExt = ".py"
            };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, ScriptCodeBox.Text);
                StatusTextBlock.Text = $"Сохранен: {Path.GetFileName(dlg.FileName)}";
            }
        }

        private void SaveAndCompileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_editingScript == null)
            {
                SaveScriptButton_Click(sender, e);
                return;
            }

            _compileTimer.Stop(); // Останавливаем таймер, если он еще тикает

            try
            {
                _editingScript.ReloadCode(ScriptCodeBox.Text);
                StatusTextBlock.Text = "Успешно сохранено и перекомпилировано!";
                StatusTextBlock.Foreground = Brushes.Green;
                OutputTextBlock.Text = "";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Ошибка компиляции!";
                StatusTextBlock.Foreground = Brushes.Red;

                var operations = _engine.GetService<ExceptionOperations>();
                string errorMessage = operations.FormatException(ex);
                OutputTextBlock.Text = errorMessage;

                int lineNumber = -1;
                var match = System.Text.RegularExpressions.Regex.Match(errorMessage, @"line (\d+)");
                if (match.Success)
                {
                    lineNumber = int.Parse(match.Groups[1].Value);
                }

                if (lineNumber > 0 && lineNumber <= ScriptCodeBox.LineCount)
                {
                    var line = ScriptCodeBox.Document.GetLineByNumber(lineNumber);
                    ScriptCodeBox.Select(line.Offset, line.Length);
                    ScriptCodeBox.ScrollToLine(lineNumber);
                    ScriptCodeBox.Focus();
                }
            }
        }
    }
}