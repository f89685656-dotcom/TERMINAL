using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Scripting.Hosting;
using IronPython.Hosting;
using System.Windows.Media;
using OxyPlot;
using System.Threading.Tasks;

namespace TERMINAL.DeviceHub
{
    public class ScriptNode : ViewModelBase
    {
        // Один общий движок на всё приложение (создается 1 раз)
        private static ScriptEngine _engine = Python.CreateEngine();

        private ScriptScope _scope;
        private double _value = double.NaN;

        public string Name { get; set; }
        public string FullName => Name;
        public string Unit => "";

        // ★ Флаг, показывающий, что в скрипте есть ошибка синтаксиса
        public bool HasError { get; private set; } = false;

        private string _code;
        public string Code
        {
            get => _code;
            set
            {
                if (_code != value)
                {
                    _code = value;
                    OnPropertyChanged(nameof(Code));
                }
            }
        }

        public List<DataPoint> HistoryPoints { get; } = new List<DataPoint>();

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                    OnPropertyChanged(nameof(DisplayValue));
                }
            }
        }

        private bool _isVisibleOnPlot = false;
        public bool IsVisibleOnPlot
        {
            get => _isVisibleOnPlot;
            set
            {
                if (_isVisibleOnPlot != value)
                {
                    _isVisibleOnPlot = value;
                    OnPropertyChanged(nameof(IsVisibleOnPlot));
                }
            }
        }

        private OxyPlot.PlotModel _chartModel;
        public OxyPlot.PlotModel ChartModel
        {
            get => _chartModel;
            set
            {
                if (_chartModel != value)
                {
                    _chartModel = value;
                    OnPropertyChanged(nameof(ChartModel));
                }
            }
        }

        public double Value
        {
            get => _value;
            set
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
                OnPropertyChanged(nameof(DisplayValue));
            }
        }

        public string DisplayValue => !IsSelected ? "—" : (double.IsNaN(Value) ? "—" : Value.ToString("F5"));

        public ScriptNode(string name, string code)
        {
            Name = name;
            Code = code;

            // ★ ЗАПУСКАЕМ ИНИЦИАЛИЗАЦИЮ В ФОНЕ (чтобы не вешать UI при загрузке)
            Task.Run(() => InitializeEngine(code));
        }

        private void InitializeEngine(string code)
        {
            try
            {
                var newScope = _engine.CreateScope();

                _engine.Execute(@"
class SessionID:
    def __init__(self): pass
", newScope);

                // Компилируем и выполняем
                var source = _engine.CreateScriptSourceFromString(code, Microsoft.Scripting.SourceCodeKind.Statements);
                var compiledCode = source.Compile();
                compiledCode.Execute(newScope);

                // Если все ок, сохраняем скоуп
                _scope = newScope;
                HasError = false;
            }
            catch (Exception ex)
            {
                _scope = null; // Ломаем скоуп, чтобы скрипт не считался
                HasError = true;
                Value = double.NaN;
                MainWindow.Current?.LogExternal($"[Script {Name}] Ошибка при инициализации: {ex.Message}", Brushes.Red);
            }
        }

        public void ReloadCode(string newCode)
        {
            Code = newCode;

            var newScope = _engine.CreateScope();
            _engine.Execute(@"
class SessionID:
    def __init__(self): pass
", newScope);

            // ★ КОМПИЛИРУЕМ КОД (при ошибке вылетит исключение)
            var source = _engine.CreateScriptSourceFromString(newCode, Microsoft.Scripting.SourceCodeKind.Statements);
            var compiledCode = source.Compile();

            // Выполняем, чтобы объявить Evaluate
            compiledCode.Execute(newScope);

            // Если все ок — заменяем рабочий скоуп
            _scope = newScope;
            HasError = false;
        }

        public void UpdateValue()
        {
            // ★ Защита: если скоуп сломан (ошибка) или галочка снята — ничего не делаем
            if (!IsSelected || _scope == null) return;

            try
            {
                dynamic evalFunc = _scope.GetVariable("Evaluate");

                if (evalFunc != null)
                {
                    object result = evalFunc();

                    if (result is double d) Value = d;
                    else if (result is int i) Value = i;
                    else if (result is float f) Value = f;
                    else if (result != null && double.TryParse(result.ToString(), out double parsed)) Value = parsed;
                    else
                    {
                        Value = double.NaN;
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.Current?.LogExternal($"[Script {Name}] Ошибка Evaluate: {ex.Message}", Brushes.Red);
                Value = double.NaN;
            }
        }
    }
}