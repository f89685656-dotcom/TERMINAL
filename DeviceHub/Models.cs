using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;

namespace TERMINAL.DeviceHub
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ★ ИНТЕРФЕЙС (RS-485, I2C) ★
    public class InterfaceNode : ViewModelBase
    {
        private bool _isExpanded = true;
        private bool _isSelected = true;

        public string Name { get; set; }
        public ObservableCollection<DeviceNode> Devices { get; set; } = new ObservableCollection<DeviceNode>();

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();

                    // ★ КАСКАДНОЕ ОБНОВЛЕНИЕ УСТРОЙСТВ (БЕЗ ЛИШНИХ УВЕДОМЛЕНИЙ) ★
                    foreach (var dev in Devices)
                    {
                        dev.SetSelectedSilent(value);
                    }
                }
            }
        }
    }

    // ★ УСТРОЙСТВО (C002, DBA0, DBA2, DB80, DB81, I2C) ★
    public class DeviceNode : ViewModelBase
    {
        private bool _isExpanded = true;
        private bool _isSelected = true;
        private bool _showOnlySelected = false;
        private bool _suppressNotify = false;

        public string PortName { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }

        public ObservableCollection<ChannelNode> Channels { get; set; } = new ObservableCollection<ChannelNode>();

        public DeviceNode()
        {
            Channels.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (ChannelNode ch in e.NewItems)
                    {
                        ch.PropertyChanged += Channel_PropertyChanged;
                    }
                }
                if (e.OldItems != null)
                {
                    foreach (ChannelNode ch in e.OldItems)
                    {
                        ch.PropertyChanged -= Channel_PropertyChanged;
                    }
                }
            };
        }

        private void Channel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChannelNode.IsSelected))
            {
                if (!_suppressNotify)
                {
                    ChannelsView.Refresh();
                }
            }
        }

        private ICollectionView _channelsView;
        public ICollectionView ChannelsView
        {
            get
            {
                if (_channelsView == null)
                {
                    _channelsView = CollectionViewSource.GetDefaultView(Channels);
                    _channelsView.Filter = FilterChannels;
                }
                return _channelsView;
            }
        }

        private bool FilterChannels(object item)
        {
            if (item is ChannelNode channel)
            {
                if (ShowOnlySelected)
                    return channel.IsSelected;
                return true;
            }
            return false;
        }

        public bool ShowOnlySelected
        {
            get => _showOnlySelected;
            set
            {
                _showOnlySelected = value;
                OnPropertyChanged();
                ChannelsView.Refresh();
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();

                    // ★ МАССОВОЕ ОБНОВЛЕНИЕ КАНАЛОВ (БЕЗ УВЕДОМЛЕНИЙ) ★
                    _suppressNotify = true;
                    foreach (var ch in Channels)
                    {
                        ch.IsSelected = value;
                    }
                    _suppressNotify = false;

                    // ★ ОБНОВЛЯЕМ UI ОДИН РАЗ ПОСЛЕ ЦИКЛА ★
                    ChannelsView.Refresh();
                }
            }
        }

        // ★ МЕТОД ДЛЯ ТИХОГО ОБНОВЛЕНИЯ (БЕЗ UI) ★
        public void SetSelectedSilent(bool value)
        {
            _isSelected = value;
            OnPropertyChanged(); // Обновляем галочку устройства

            _suppressNotify = true;
            foreach (var ch in Channels)
            {
                ch.IsSelected = value;
            }
            _suppressNotify = false;

            ChannelsView.Refresh();
        }
    }

    // ★ КАНАЛ (Давление, Температура, Частоты) ★
    public class ChannelNode : ViewModelBase
    {
        private bool _isSelected = true;
        private double _value;
        private string _unit;
        private bool _isVisibleOnPlot = false;
        private bool _isValueSet = false;

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
                    _isValueSet = true;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsValueSet
        {
            get => _isValueSet;
            set { _isValueSet = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

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
        public bool IsDouble { get; set; } = false;
        public double LastValue { get; set; }

        public ushort RegisterAddress { get; set; }
        public double Multiplier { get; set; } = 1.0;

        public event Action<ChannelNode, double> ValueUpdated;
    }
}