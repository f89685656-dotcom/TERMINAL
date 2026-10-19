using System;
using System.Globalization;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace TERMINAL
{
    public partial class MainWindow
    {
        // ─────────────────────────────────────────────────────────────
        //  Диспетчер: вызывается таймером _platinaTimer (см. Window_Loaded).
        //  Раньше здесь была вся логика МтК @ TERMEX.
        // ─────────────────────────────────────────────────────────────
        private async Task ReadPlatinaFromMtkTermexAsync()
        {
            double? val = await TryReadPlatinaAsync();

            if (val.HasValue && !double.IsNaN(val.Value) && !double.IsInfinity(val.Value))
            {
                _platinaFailCount = 0;
                _platinaLastValue = val.Value;
                _platinaLastSuccess = DateTime.Now;

                double rounded = Math.Round(val.Value, 3);
                string sign = rounded >= 0 ? "+" : "";
                PlatinaTempText.Text =
                    $"{sign}{rounded.ToString("0.000", CultureInfo.InvariantCulture)} °C";
                PlatinaTempText.Foreground = Brushes.White;

                if (PlatinaRowEllipse != null)
                    PlatinaRowEllipse.Fill = Brushes.Green;
            }
            else
            {
                _platinaFailCount++;

                if (_platinaFailCount >= ConnectionLostThreshold)
                {
                    PlatinaTempText.Text = "— °C";
                    PlatinaTempText.Foreground =
                        new SolidColorBrush(Color.FromRgb(0xCC, 0xDD, 0xEE));

                    if (PlatinaRowEllipse != null)
                        PlatinaRowEllipse.Fill = Brushes.Red;
                }
            }
            UpdateSktbPanelVisibility();
        }

        // ─────────────────────────────────────────────────────────────
        //  МИТ 8.03 rev.2 через RS-232 — заготовка.
        //  Открывает COM-порт, читает «что пришло» и пишет в лог.
        //  Реальный протокол — после // TODO.
        // ─────────────────────────────────────────────────────────────
        private async Task ReadPlatinaFromMit803Async()
        {
            try
            {
                lock (_platinumSerialLock)
                {
                    if (_platinumSerial == null || !_platinumSerial.IsOpen ||
                        _platinumSerial.PortName != _platinumComPort)
                    {
                        try { _platinumSerial?.Close(); } catch { }
                        try { _platinumSerial?.Dispose(); } catch { }

                        _platinumSerial = new SerialPort(_platinumComPort, 9600)
                        {
                            ReadTimeout = 1000
                        };
                        _platinumSerial.Open();
                        AppendLog($"Платина: подключен {_platinumComPort}", Brushes.Gray);
                    }
                }

                // TODO: реализовать протокол МИТ 8.03 rev.2
                string response = "";
                await Task.Run(() =>
                {
                    lock (_platinumSerialLock)
                    {
                        if (_platinumSerial == null) return;
                        Thread.Sleep(100);
                        if (_platinumSerial.BytesToRead > 0)
                            response = _platinumSerial.ReadExisting();
                    }
                });

                if (!string.IsNullOrWhiteSpace(response))
                    AppendLog($"Платина МИТ 8.03 (raw): '{response.Trim()}'", Brushes.Gray);
            }
            catch (Exception ex)
            {
                AppendLog($"Платина МИТ 8.03: {ex.Message}", Brushes.OrangeRed);
            }
        }

        private async Task RefreshPlatinaAsync()
        {
            if (!_readPlatinum)
                return;

            switch (_platinumSource)
            {
                case "mit_803":
                    await ReadPlatinaFromMit803Async();
                    break;

                case "tercon":
                    // Заготовка: TERCON пока не реализован.
                    break;

                case "mtk_termex":
                default:
                    await ReadPlatinaFromMtkTermexAsync();
                    break;
            }
        }
    }
}