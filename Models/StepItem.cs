using System.ComponentModel;

namespace TERMINAL.Models
{
    public class StepItem : INotifyPropertyChanged
    {
        private string _timerText = "";
        private string _arrowText = "  ";
        private string _statusMark = "☐";
        private string _kosaCounterText = "";
        public DateTime? CompletedAt { get; set; }

        public string IndexText { get; set; } = "";
        public string ValueNumber { get; set; } = "";
        public string ValueUnits { get; set; } = "";

        public string TimerText
        {
            get => _timerText;
            set { _timerText = value; OnPropertyChanged(nameof(TimerText)); }
        }

        public string ArrowText
        {
            get => _arrowText;
            set { _arrowText = value; OnPropertyChanged(nameof(ArrowText)); }
        }

        public string StatusMark
        {
            get => _statusMark;
            set { _statusMark = value; OnPropertyChanged(nameof(StatusMark)); }
        }

        public string KosaCounterText
        {
            get => _kosaCounterText;
            set { _kosaCounterText = value; OnPropertyChanged(nameof(KosaCounterText)); }
        }

        public int PlannedDuration { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}