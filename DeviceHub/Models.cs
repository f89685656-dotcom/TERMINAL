using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

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
        private bool? _isSelected = true;

        public string Name { get; set; }
        public ObservableCollection<DeviceNode> Devices { get; set; } = new ObservableCollection<DeviceNode>();

        public InterfaceNode()
        {
            Devices.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (DeviceNode dev in e.NewItems) dev.SelectionChanged += Device_SelectionChanged;
                if (e.OldItems != null)
                    foreach (DeviceNode dev in e.OldItems) dev.SelectionChanged -= Device_SelectionChanged;
            };
        }

        private void Device_SelectionChanged(object sender, EventArgs e) => RecalculateSelection();

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public bool? IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    if (value.HasValue)
                        foreach (var dev in Devices) dev.SetSelectedSilent(value.Value);
                }
            }
        }

        public void RecalculateSelection()
        {
            if (Devices.Count == 0) return;
            bool allSelected = Devices.All(d => d.IsSelected == true);
            bool anySelected = Devices.Any(d => d.IsSelected != false);

            if (allSelected) IsSelected = true;
            else if (anySelected) IsSelected = null;
            else IsSelected = false;
        }
    }

    public class DeviceNode : ViewModelBase
    {
        private bool _isExpanded = true;
        private bool? _isSelected = true;
        private bool _showOnlySelected = false;
        private bool _suppressNotify = false;

        public event EventHandler SelectionChanged;

        public string PortName { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }

        public ObservableCollection<ChannelNode> Channels { get; set; } = new ObservableCollection<ChannelNode>();

        public DeviceNode()
        {
            Channels.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (ChannelNode ch in e.NewItems) ch.PropertyChanged += Channel_PropertyChanged;
                if (e.OldItems != null)
                    foreach (ChannelNode ch in e.OldItems) ch.PropertyChanged -= Channel_PropertyChanged;
            };
        }

        private void Channel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChannelNode.IsSelected))
            {
                if (!_suppressNotify)
                {
                    ChannelsView.Refresh();
                    RecalculateSelection();
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
                if (ShowOnlySelected) return channel.IsSelected;
                return true;
            }
            return false;
        }

        public bool ShowOnlySelected
        {
            get => _showOnlySelected;
            set { _showOnlySelected = value; OnPropertyChanged(); ChannelsView.Refresh(); }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public bool? IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    if (value.HasValue)
                    {
                        _suppressNotify = true;
                        foreach (var ch in Channels) ch.IsSelected = value.Value;
                        _suppressNotify = false;
                        ChannelsView.Refresh();
                        SelectionChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        public void SetSelectedSilent(bool value)
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
            _suppressNotify = true;
            foreach (var ch in Channels) ch.IsSelected = value;
            _suppressNotify = false;
            ChannelsView.Refresh();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RecalculateSelection()
        {
            if (Channels.Count == 0) return;
            bool allSelected = Channels.All(c => c.IsSelected);
            bool anySelected = Channels.Any(c => c.IsSelected);

            bool? newState = allSelected ? true : (anySelected ? (bool?)null : false);
            if (_isSelected != newState)
            {
                _isSelected = newState;
                OnPropertyChanged(nameof(IsSelected));
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public class ChannelNode : ViewModelBase
    {
        private bool _isSelected = true;
        private double _value;
        private string _unit;
        private bool _isVisibleOnPlot = false;
        private bool _isValueSet = false;
        private DateTime _lastUpdateTime = DateTime.MinValue;

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
                _value = value;
                _isValueSet = true;
                _lastUpdateTime = DateTime.Now;
                OnPropertyChanged();
            }
        }

        public DateTime LastUpdateTime => _lastUpdateTime;

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
            set { if (_isVisibleOnPlot != value) { _isVisibleOnPlot = value; OnPropertyChanged(); } }
        }

        public bool Is32Bit { get; set; } = true;
        public bool IsInt32 { get; set; } = false;
        public bool IsDouble { get; set; } = false;
        public double LastValue { get; set; }
        public ushort RegisterAddress { get; set; }
        public double Multiplier { get; set; } = 1.0;

        public event Action<ChannelNode, double> ValueUpdated;

        public void ResetValueState()
        {
            _isValueSet = false;
            _lastUpdateTime = DateTime.MinValue;
            OnPropertyChanged(nameof(IsValueSet));
        }

        // ★ ХРАНИМ ТОЧКИ ★
        public List<DataPoint> HistoryPoints { get; set; } = new List<DataPoint>();

        // ★ СВОЙСТВО ДЛЯ ПРИВЯЗКИ (вместо Tag) ★
        private PlotModel _chartModel;
        public PlotModel ChartModel
        {
            get => _chartModel;
            set { _chartModel = value; OnPropertyChanged(); }
        }
    }
}