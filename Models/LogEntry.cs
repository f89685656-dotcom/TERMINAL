using System;
using System.ComponentModel;
using System.Windows.Media;

namespace TERMINAL.Models
{
    public class LogEntry : INotifyPropertyChanged
    {
        public DateTime Time { get; set; }
        public string Message { get; set; } = "";
        public Brush Color { get; set; } = Brushes.Black;

        public string TimeText => Time.ToString("HH:mm:ss");
        public string DisplayText => $"[{TimeText}] {Message}";

        private Brush _background = Brushes.Transparent;
        public Brush Background
        {
            get => _background;
            set
            {
                if (ReferenceEquals(_background, value)) return;
                _background = value;
                OnPropertyChanged(nameof(Background));
            }
        }

        private bool _isDimmed = false;
        public bool IsDimmed
        {
            get => _isDimmed;
            set
            {
                if (_isDimmed == value) return;
                _isDimmed = value;
                OnPropertyChanged(nameof(IsDimmed));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}