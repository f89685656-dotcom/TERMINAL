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

        // ★ Ссылка на редактируемый скрипт (если открыт на редактирование)
        private ScriptNode _editingScript = null;

        // Конструктор для создания НОВОГО скрипта (вызов из контекстного меню)
        public ScriptEditorWindow(PollingTabView pollingView)
        {
            InitializeComponent();
            _pollingView = pollingView;

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

print('Привет, IronPython!')
";
        }

        // ★ Конструктор для РЕДАКТИРОВАНИЯ существующего скрипта (двойной клик по дереву)
        public ScriptEditorWindow(PollingTabView pollingView, ScriptNode script) : this(pollingView)
        {
            _editingScript = script;
            ScriptCodeBox.Text = script.Code; // Загружаем текущий код в редактор
            StatusTextBlock.Text = $"Редактирование: {script.Name}";
            StatusTextBlock.Foreground = Brushes.Blue;

            // Меняем заголовок окна, чтобы было понятно
            this.Title = $"Редактор скриптов - {script.Name}";
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

                string mockSessionIdCode = @"
class SessionID:
    def __init__(self):
        self.id = '00000000-0000-0000-0000-000000000000'
    def __str__(self):
        return self.id
    def __repr__(self):
        return self.id
";
                _engine.Execute(mockSessionIdCode, _scope);

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
            // ★ Добавили *.KGPY в фильтр загрузки
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
            // ★ Добавили *.KGPY в фильтр сохранения
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

        // ★ НОВЫЙ МЕТОД: Сохранение изменений в существующий скрипт (перекомпиляция на лету)
        private void SaveAndCompileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_editingScript == null) return;

            try
            {
                // Обновляем код в объекте
                _editingScript.Code = ScriptCodeBox.Text;

                // ★ Перекомпилируем скрипт на лету
                // Для этого нужно добавить в ScriptNode метод ReloadCode
                _editingScript.ReloadCode(ScriptCodeBox.Text);

                StatusTextBlock.Text = "Успешно сохранено и перекомпилировано!";
                StatusTextBlock.Foreground = Brushes.Green;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Ошибка компиляции: {ex.Message}";
                StatusTextBlock.Foreground = Brushes.Red;
            }
        }
    }
}