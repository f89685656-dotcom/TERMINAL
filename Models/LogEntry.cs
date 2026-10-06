using System;
using System.Windows.Media;

namespace TERMINAL.Models
{
    public class LogEntry
    {
        public DateTime Time { get; set; }
        public string Message { get; set; } = "";
        public Brush Color { get; set; } = Brushes.Black;

        public string TimeText => Time.ToString("HH:mm:ss");
        public string DisplayText => $"[{TimeText}] {Message}";
    }
}