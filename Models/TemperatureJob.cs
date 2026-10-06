using System.ComponentModel;
using System.Windows.Media;

namespace TERMINAL.Models
{
    public class TemperatureJob : INotifyPropertyChanged
    {
        private string _setPointText = "25";
        private string _stabilizationMinutesText = "120";
        private string _statusText = "ожидает";
        private Brush _statusColor = Brushes.Gray;
        private bool _isEnabled = true;
        private bool _isEditable = true;
        private bool _isCalibrationMode = true;
        private string _cyclesText = "10";
        private string _startStepText = "1";

        public string IndexText { get; set; } = "";

        public string SetPointText
        {
            get => _setPointText;
            set { _setPointText = value; OnPropertyChanged(nameof(SetPointText)); }
        }

        public string StabilizationMinutesText
        {
            get => _stabilizationMinutesText;
            set { _stabilizationMinutesText = value; OnPropertyChanged(nameof(StabilizationMinutesText)); }
        }

        public string CyclesText
        {
            get => _cyclesText;
            set { _cyclesText = value; OnPropertyChanged(nameof(CyclesText)); }
        }

        public string StartStepText
        {
            get => _startStepText;
            set { _startStepText = value; OnPropertyChanged(nameof(StartStepText)); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public Brush StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(nameof(StatusColor)); }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        public bool IsEditable
        {
            get => _isEditable;
            set { _isEditable = value; OnPropertyChanged(nameof(IsEditable)); }
        }

        public bool IsCalibrationMode
        {
            get => _isCalibrationMode;
            set
            {
                if (_isCalibrationMode == value) return;
                _isCalibrationMode = value;
                OnPropertyChanged(nameof(IsCalibrationMode));
                OnPropertyChanged(nameof(ModeLabel));
                OnPropertyChanged(nameof(ModeIndex));
            }
        }

        public int ModeIndex
        {
            get => _isCalibrationMode ? 0 : 1;
            set
            {
                bool newVal = value == 0;
                if (_isCalibrationMode != newVal)
                {
                    _isCalibrationMode = newVal;
                    OnPropertyChanged(nameof(IsCalibrationMode));
                    OnPropertyChanged(nameof(ModeLabel));
                    OnPropertyChanged(nameof(ModeIndex));
                }
            }
        }

        public string ModeLabel => _isCalibrationMode ? "Градуировка" : "Жамкать";

        public float SetPoint { get; set; }
        public int StabilizationMinutes { get; set; } = 120;
        public int Cycles { get; set; } = 10;
        public int StartStep { get; set; } = 1;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}