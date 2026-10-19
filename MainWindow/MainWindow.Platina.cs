using System;
using System.Globalization;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Text.RegularExpressions;

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

        private async Task ReadPlatinaFromMit803Async()
        {
            string portName = _platinumComPort;
            const int baudRate = 9600;
            const int readTimeoutMs = 1000;
            const int maxWaitMs = 3000;

            string response = "";

            // Чтение — в фоне, чтобы не блокировать UI
            await Task.Run(() =>
            {
                SerialPort? sp = null;
                try
                {
                    // Открыть/переоткрыть порт (вне lock, чтобы не держать lock на всё время чтения)
                    lock (_platinumSerialLock)
                    {
                        if (_platinumSerial == null || !_platinumSerial.IsOpen ||
                            _platinumSerial.PortName != portName)
                        {
                            try { _platinumSerial?.Close(); } catch { }
                            try { _platinumSerial?.Dispose(); } catch { }

                            _platinumSerial = new SerialPort(portName, baudRate)
                            {
                                ReadTimeout = readTimeoutMs,
                                WriteTimeout = readTimeoutMs
                            };
                            _platinumSerial.Open();
                        }
                        sp = _platinumSerial;
                    }

                    if (sp == null) return;

                    // ★ Логика макроса: читаем до 3000 мс, копим, ждём 'B' или длину > 20
                    int waitTime = 0;
                    while (waitTime < maxWaitMs)
                    {
                        int bytesToRead;
                        try { bytesToRead = sp.BytesToRead; }
                        catch { break; }   // порт закрылся извне

                        if (bytesToRead > 0)
                        {
                            string chunk;
                            try { chunk = sp.ReadExisting(); }
                            catch { break; }

                            response += chunk;

                            if ((response.Contains("B") || response.Length > 20)
                                && response.Length > 5)
                            {
                                Thread.Sleep(50);
                                try
                                {
                                    string more = sp.ReadExisting();
                                    if (!string.IsNullOrEmpty(more)) response += more;
                                }
                                catch { }
                                break;
                            }

                            Thread.Sleep(50);
                            waitTime += 50;
                        }
                        else
                        {
                            Thread.Sleep(50);
                            waitTime += 50;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Платина МИТ 8.03: {ex.Message}", Brushes.OrangeRed);
                }
                finally
                {
                    // Порт не закрываем — он кэшируется в _platinumSerial,
                    // закроется в ApplyDeviceSettings/OnClosed.
                }
            });

            // ─── Парсинг (на UI-потоке, т.к. после await) ───
            string cleaned = response
                .Replace(" ", "")
                .Replace("\r", "")
                .Replace("\n", "")
                .Trim();

            double value = double.NaN;

            if (cleaned.Length > 0 && cleaned.Contains(":") && cleaned.Contains("B"))
            {
                var parts = cleaned.Split(':');
                if (parts.Length == 2)
                {
                    string valueWithUnit = parts[1];

                    string valueStr;
                    if (valueWithUnit.EndsWith("B"))
                    {
                        valueStr = valueWithUnit.Substring(0, valueWithUnit.Length - 1);
                    }
                    else
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(
                            valueWithUnit, @"(\d+\.?\d*E[+-]?\d+)");
                        valueStr = m.Success ? m.Groups[1].Value : valueWithUnit;
                    }

                    if (double.TryParse(valueStr,
                            NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                        && !double.IsNaN(v) && v != 0 && v > 10)
                    {
                        value = v;
                        _mit803LastGoodValue = v;
                    }
                    else
                    {
                        value = _mit803LastGoodValue;   // последнее хорошее
                    }
                }
                else
                {
                    value = _mit803LastGoodValue;
                }
            }
            else
            {
                value = _mit803LastGoodValue;
            }

            // ─── Обновление UI и полей состояния ───
            if (!double.IsNaN(value) && !double.IsInfinity(value))
            {
                _platinaFailCount = 0;
                _platinaLastValue = value;
                _platinaLastSuccess = DateTime.Now;

                double rounded = Math.Round(value, 3);
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