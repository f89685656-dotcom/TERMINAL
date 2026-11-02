using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TERMINAL.DeviceHub
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class InterfaceNode : ViewModelBase
    {
        private bool _isExpanded = true;
        private bool _isSelected = true;
        public string Name { get; set; }
        public ObservableCollection<DeviceNode> Devices { get; set; } = new ObservableCollection<DeviceNode>();
        public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    }

    public class DeviceNode : ViewModelBase
    {
        private bool _isExpanded = true;
        private bool _isSelected = true;
        public string PortName { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }

        // ОЧЕНЬ ВАЖНО: Именно ObservableCollection
        public ObservableCollection<ChannelNode> Channels { get; set; } = new ObservableCollection<ChannelNode>();

        public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    }

    public class ChannelNode : ViewModelBase
    {
        private bool _isSelected = true;
        private double _value;
        private string _unit;

        public string Name { get; set; }

        public string Unit
        {
            get => _unit;
            set { _unit = value; OnPropertyChanged(); }
        }

        public double Value
        {
            get => _value;
            set
            {
                if (Math.Abs(_value - value) > 0.0001)
                {
                    _value = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        // ★ НОВОЕ СВОЙСТВО: Выбран ли канал для отображения на графике ★
        private bool _isVisibleOnPlot = false;
        public bool IsVisibleOnPlot
        {
            get => _isVisibleOnPlot;
            set
            {
                if (_isVisibleOnPlot != value)
                {
                    _isVisibleOnPlot = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool Is32Bit { get; set; } = true;
        public bool IsInt32 { get; set; } = false;
        public double LastValue { get; set; }

        public ushort RegisterAddress { get; set; }
        public double Multiplier { get; set; } = 1.0;

        // ★ ДОБАВЛЕНО: Событие для графика ★
        public event Action<ChannelNode, double> ValueUpdated;
    }
}