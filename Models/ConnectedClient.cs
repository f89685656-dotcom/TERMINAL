using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Media;

namespace TERMINAL.Models
{
    public class ConnectedClient : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private Brush _statusColor;
        private string _statusText = "Готов";
        private bool _isOffline = false;

        private volatile bool _isWaiting = false;
        private volatile bool _lastDoneOk = false;
        private volatile bool _lastDoneFail = false;

        public ConnectedClient()
        {
            _statusColor = CreateFrozenBrush(Colors.Gray);
        }

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public Brush StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(nameof(StatusColor)); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public string Sid { get; set; } = "";
        public TcpClient TcpClient { get; set; } = null!;
        public DateTime ConnectedAt { get; set; } = DateTime.Now;

        public bool IsWaiting { get => _isWaiting; set => _isWaiting = value; }
        public bool LastDoneOk { get => _lastDoneOk; set => _lastDoneOk = value; }
        public bool LastDoneFail { get => _lastDoneFail; set => _lastDoneFail = value; }

        public bool IsOffline => _isOffline;
        public DateTime? DisconnectedAt { get; set; } = null;
        public DateTime LastActivityAt { get; set; } = DateTime.Now;

        private readonly HashSet<string> _capabilities = new HashSet<string>();
        public HashSet<string> Capabilities => _capabilities;

        public double LastAtm { get; set; } = double.NaN;
        public DateTime LastAtmAt { get; set; } = DateTime.MinValue;

        public double LastFurnaceFromClient { get; set; } = double.NaN;
        public DateTime LastFurnaceFromClientAt { get; set; } = DateTime.MinValue;

        public bool HasCapability(string cap)
        {
            lock (_capabilities) return _capabilities.Contains(cap);
        }

        public bool IsSensorClient =>
            HasCapability("ATM") || HasCapability("FURNACE_TEMP");

        public string RoleLabel
        {
            get
            {
                bool atm = HasCapability("ATM");
                bool ptk = HasCapability("FURNACE_TEMP");

                if (atm && ptk) return "Атм.давл/Темп. в печке";
                if (atm) return "Атм.давл";
                if (ptk) return "Темп. в печке";
                return "";
            }
        }

        public void SetCapabilities(IEnumerable<string> caps)
        {
            lock (_capabilities)
            {
                _capabilities.Clear();
                foreach (var c in caps) _capabilities.Add(c);
            }
            OnPropertyChanged(nameof(RoleLabel));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static void RunOnUI(Action action)
        {
            var app = Application.Current;
            if (app == null) { action(); return; }
            if (app.Dispatcher.CheckAccess()) action();
            else app.Dispatcher.Invoke(action);
        }

        public string DisplayText => $"SID: {Sid}";

        private string _customName = "";
        public string CustomName
        {
            get => _customName;
            set
            {
                if (_customName == value) return;
                _customName = value ?? "";
                OnPropertyChanged(nameof(CustomName));
            }
        }

        public void SetStatusReady()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Green);
                StatusText = "Готов";
                IsWaiting = false; LastDoneOk = true; DisconnectedAt = null;
            });
        }

        public void SetStatusWaiting()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = "Занят съёмом";
                IsWaiting = true; LastDoneOk = false;
            });
        }

        public void SetStatusWaitingWithTime(int seconds)
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = $"Занят съёмом ({seconds} сек)";
            });
        }

        public void SetStatusOffline()
        {
            RunOnUI(() =>
            {
                _isOffline = true;
                StatusColor = CreateFrozenBrush(Colors.Red);
                StatusText = "Не отвечает";
                IsWaiting = false; LastDoneOk = false;
                if (DisconnectedAt == null) DisconnectedAt = DateTime.Now;
            });
        }

        public void SetStatusAlive()
        {
            if (_isOffline)
            {
                RunOnUI(() =>
                {
                    _isOffline = false;
                    StatusColor = CreateFrozenBrush(Colors.Green);
                    StatusText = "Готов";
                    DisconnectedAt = null;
                });
            }
        }

        public void SetStatusSaving()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = "Сохраняет данные";
            });
        }

        public void SetStatusSaved()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Green);
                StatusText = "Сохранил данные";
            });
        }

        public void SetStatusSaveFailed()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = "Ошибка сохранения";
            });
        }

        public void SetStatusDoneSuccess()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Green);
                StatusText = "Съём завершён";
                IsWaiting = false; LastDoneOk = true;
            });
        }

        public void SetStatusDoneFail()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = "Ошибка съёма";
                IsWaiting = false; LastDoneFail = true;
            });
        }

        public void SetStatusClearing()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = "Ожидает очистки";
            });
        }

        public void SetStatusCleared()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Green);
                StatusText = "Очищено";
            });
        }

        public void SetStatusClearFailed()
        {
            RunOnUI(() =>
            {
                _isOffline = false;
                StatusColor = CreateFrozenBrush(Colors.Orange);
                StatusText = "Очистка не удалась";
            });
        }
    }
}