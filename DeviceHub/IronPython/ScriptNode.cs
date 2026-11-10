using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Scripting.Hosting;
using IronPython.Hosting;
using System.Windows.Media;
using OxyPlot;

namespace TERMINAL.DeviceHub
{
    public class ScriptNode : ViewModelBase
    {
        private static ScriptEngine _engine = Python.CreateEngine();
        private ScriptScope _scope;

        private double _value = double.NaN;

        public string Name { get; set; }
        public string FullName => Name;
        public string Unit => "";

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

            _scope = _engine.CreateScope();

            // Заглушка для SessionID
            _engine.Execute(@"
class SessionID:
    def __init__(self): pass
", _scope);

            try
            {
                _engine.Execute(code, _scope);

                // ★ Проверяем, есть ли функция Evaluate сразу после загрузки
                if (_scope.GetVariable("Evaluate") == null)
                {
                    MainWindow.Current?.LogExternal($"[Script {name}] ВНИМАНИЕ: Функция Evaluate не найдена!", Brushes.Red);
                }
            }
            catch (Exception ex)
            {
                MainWindow.Current?.LogExternal($"[Script {name}] Ошибка при загрузке: {ex.Message}", Brushes.Red);
            }
        }

        public void ReloadCode(string newCode)
        {
            Code = newCode;
            _scope = _engine.CreateScope();

            _engine.Execute(@"
class SessionID:
    def __init__(self): pass
", _scope);

            try
            {
                _engine.Execute(newCode, _scope);
            }
            catch (Exception ex)
            {
                MainWindow.Current?.LogExternal($"[Script {Name}] Ошибка при перезагрузке: {ex.Message}", Brushes.Red);
            }
        }

        public void UpdateValue()
        {
            if (!IsSelected) return;

            try
            {
                // Ищем функцию Evaluate
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
                else
                {
                    // Если функция пропала (например, скрипт был перезаписан)
                    MainWindow.Current?.LogExternal($"[Script {Name}] Функция Evaluate не найдена!", Brushes.Red);
                }
            }
            catch (Exception ex)
            {
                // ★ Это покажет реальную ошибку, почему скрипт не работает
                MainWindow.Current?.LogExternal($"[Script {Name}] Ошибка Evaluate: {ex.Message}", Brushes.Red);
                Value = double.NaN;
            }
        }
    }
}