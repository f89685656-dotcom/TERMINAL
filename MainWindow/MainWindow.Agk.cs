using System;
using System.IO;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using TERMINAL.Models;

namespace TERMINAL
{
    public partial class MainWindow
    {
        private async Task ConnectToDeviceLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                bool needConnect;
                lock (_streamLock) needConnect = (_stream == null);
                if (!needConnect) { await Task.Delay(1000); continue; }

                try
                {
                    SetStatusWarning($"Подключение к {_deviceIp}:{_devicePort}...");

                    var client = new TcpClient();
                    var ct = client.ConnectAsync(_deviceIp, _devicePort);
                    var tt = Task.Delay(3000);

                    if (await Task.WhenAny(ct, tt) != ct)
                    {
                        try { client.Close(); } catch { }
                        SetStatusError($"Прибор недоступен. Повтор через 5 сек...");
                        await Task.Delay(5000);
                        continue;
                    }

                    await ct;

                    var stream = client.GetStream();

                    lock (_streamLock)
                    {
                        _client = client;
                        _stream = stream;
                    }

                    SetStatus($"Проверка связи (get_ping;)...", false);

                    // ─── Проверка ping ───
                    bool pingOk = await SendPingAsync();

                    if (!pingOk)
                    {
                        AppendLog($"АГК не отвечает на get_ping;, переподключение через 5 сек...",
                                  Brushes.OrangeRed);

                        lock (_streamLock)
                        {
                            try { _stream?.Close(); } catch { }
                            try { _client?.Close(); } catch { }
                            _stream = null;
                            _client = null;
                        }

                        SetStatusError($"Нет ответа на get_ping;. Повтор через 5 сек...");
                        await Task.Delay(5000);
                        continue;
                    }

                    SetStatusSuccess($"Подключено к {_deviceIp}:{_devicePort} (ping ok)");
                    AppendLog($"АГК {_deviceIp}:{_devicePort} готов (get_ping; → ok;)", Brushes.Green);
                }
                catch
                {
                    SetStatusError($"Прибор недоступен. Повтор через 5 сек...");
                }

                await Task.Delay(5000);
            }
        }

        /// <summary>
        /// Проверка связи с АГК: отправляет get_ping; и ждёт ответ "ok;".
        /// Возвращает true, если ответ получен, иначе false.
        /// </summary>
        private async Task<bool> SendPingAsync()
        {
            try
            {
                NetworkStream? stream;
                lock (_streamLock) stream = _stream;
                if (stream == null) return false;

                // Отправляем get_ping;
                byte[] cmd = Encoding.ASCII.GetBytes("get_ping;");
                await stream.WriteAsync(cmd, 0, cmd.Length);
                await stream.FlushAsync();

                // Читаем ответ до ';' с таймаутом
                var sb = new StringBuilder();
                var buffer = new byte[1];
                var start = DateTime.Now;
                var timeout = TimeSpan.FromSeconds(3);

                while ((DateTime.Now - start) < timeout)
                {
                    if (!stream.DataAvailable)
                    {
                        await Task.Delay(20);
                        continue;
                    }

                    int n = await stream.ReadAsync(buffer, 0, 1);
                    if (n <= 0) return false;

                    char c = (char)buffer[0];
                    sb.Append(c);

                    if (c == ';')
                        break;
                }

                string response = sb.ToString().Trim();

                AppendLog($"АГК ping ответ: '{response}'", Brushes.Gray);

                return response.Contains("ok", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                AppendLog($"АГК ping ошибка: {ex.Message}", Brushes.OrangeRed);
                return false;
            }
        }

        private void ResetDeviceConnection()
        {
            lock (_streamLock)
            {
                try { _stream?.Close(); } catch { }
                try { _client?.Close(); } catch { }
                _stream = null; _client = null;
            }
            SetStatusError("Соединение потеряно. Переподключение...");
        }

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            if (_isRunning) return;
            if (_agkBatchBusy) return;

            NetworkStream? stream;
            lock (_streamLock) stream = _stream;

            bool agkOk = false;

            if (stream != null)
            {
                _agkBatchBusy = true;
                try
                {
                    if (stream.CanRead && stream.CanWrite)
                    {
                        string? unitsResp = await SendCommandAsync("get_units;");
                        await Task.Delay(200);
                        string? pressResp = await SendCommandAsync("get_press;");
                        await Task.Delay(200);
                        string? pointResp = await SendCommandAsync("get_point;");
                        await Task.Delay(200);
                        string? statusResp = await SendCommandAsync("get_status;");

                        if (unitsResp != null) UpdateUnits(unitsResp);

                        if (pressResp != null)
                        {
                            string parsed = ParsePressure(pressResp);
                            string units = _currentPressureUnitsLabel;
                            AgkPressureText.Text = string.IsNullOrEmpty(units)
                                ? parsed : $"{parsed} {units}";

                            if (double.TryParse(parsed, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out double v))
                                _lastKnownPressure = v;
                        }

                        if (pointResp != null)
                        {
                            string parsedSp = ParsePressure(pointResp);
                            string units = _currentPressureUnitsLabel;
                            AgkSetPointText.Text = string.IsNullOrEmpty(units)
                                ? parsedSp : $"{parsedSp} {units}";
                        }

                        if (statusResp != null)
                        {
                            string display = statusResp.Trim();
                            if (string.IsNullOrEmpty(display)) display = "(пусто)";

                            if (AgkStatusTextBox != null)
                            {
                                AgkStatusTextBox.Text = display;
                                AgkStatusTextBox.Foreground =
                                    (display == "0;" || display == "0")
                                        ? new SolidColorBrush(Color.FromRgb(0x80, 0xFF, 0x80))
                                        : new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x80));
                            }
                        }

                        if (unitsResp != null && pressResp != null)
                            agkOk = true;
                    }
                    else
                    {
                        ResetDeviceConnection();
                    }
                }
                catch
                {
                    ResetDeviceConnection();
                }
                finally
                {
                    _agkBatchBusy = false;
                }
            }

            if (agkOk)
            {
                _agkFailCount = 0;
                AgkStatusEllipse.Fill = Brushes.Green;
            }
            else
            {
                _agkFailCount++;
                if (_agkFailCount >= ConnectionLostThreshold)
                {
                    AgkPressureText.Text = "—";
                    AgkSetPointText.Text = "—";
                    AgkStatusEllipse.Fill = Brushes.Red;

                    if (AgkStatusTextBox != null)
                    {
                        AgkStatusTextBox.Text = "—";
                        AgkStatusTextBox.Foreground =
                            new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                    }
                }
            }
        }

        private void UpdateUnits(string response)
        {
            char? d = null;
            foreach (char c in response)
                if (c >= '0' && c <= '9') { d = c; break; }

            string? u = d switch
            {
                '0' => "кПа",
                '1' => "МПа",
                '2' => "бар",
                '3' => "PSI",
                '4' => "кгс/см²",
                _ => null
            };

            if (u != null)
                _currentPressureUnitsLabel = u;
        }

        private async Task<string?> SendCommandAsync(string command)
        {
            NetworkStream? stream;
            lock (_streamLock) stream = _stream;
            if (stream == null) return null;

            try
            {
                byte[] flush = new byte[1024];
                while (stream.DataAvailable)
                {
                    await stream.ReadAsync(flush, 0, flush.Length);
                    await Task.Delay(20);
                }

                byte[] req = Encoding.ASCII.GetBytes(command);
                await stream.WriteAsync(req, 0, req.Length);

                var sb = new StringBuilder();
                byte[] one = new byte[1];
                var start = DateTime.Now;
                while ((DateTime.Now - start).TotalMilliseconds < 2000)
                {
                    if (stream.DataAvailable)
                    {
                        int n = await stream.ReadAsync(one, 0, 1);
                        if (n == 0) break;
                        char c = (char)one[0];
                        sb.Append(c);
                        if (c == ';') break;
                    }
                    else await Task.Delay(10);
                }
                return sb.Length > 0 ? sb.ToString().Trim() : null;
            }
            catch { ResetDeviceConnection(); return null; }
        }

        private async Task SendCommandNoWaitAsync(string command)
        {
            NetworkStream? stream;
            lock (_streamLock) stream = _stream;
            if (stream == null) return;
            try
            {
                byte[] req = Encoding.ASCII.GetBytes(command);
                await stream.WriteAsync(req, 0, req.Length);
            }
            catch { ResetDeviceConnection(); }
        }

        private string ParsePressure(string raw)
        {
            raw = raw.Trim().Trim('\r', '\n', '\0', ' ', ';');
            if (raw.StartsWith("E", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("e", StringComparison.OrdinalIgnoreCase))
                raw = raw.Substring(1);
            raw = raw.Replace(',', '.');
            if (double.TryParse(raw, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double v))
                return Math.Round(v, 5).ToString("F5", CultureInfo.InvariantCulture);
            return raw;
        }

        private async Task SafeDelayAsync(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end)
            {
                if (_stopRequested) return;

                while (_pauseRequested && !_stopRequested)
                    await Task.Delay(100);

                if (_stopRequested) return;

                await Task.Delay(100);
            }
        }

        private async Task<double?> RefreshPressureAndSetPointAsync()
        {
            double? pressure = null;

            NetworkStream? stream;
            lock (_streamLock) stream = _stream;

            if (stream == null) return null;

            string? p = await SendCommandAsync("get_press;");
            await Task.Delay(200);
            string? sp = await SendCommandAsync("get_point;");

            if (p != null)
            {
                string parsed = ParsePressure(p);
                string units = _currentPressureUnitsLabel;
                AgkPressureText.Text = string.IsNullOrEmpty(units)
                    ? parsed : $"{parsed} {units}";

                if (double.TryParse(parsed, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double v))
                {
                    pressure = v;
                    _lastKnownPressure = v;
                }
            }

            if (sp != null)
            {
                string parsedSp = ParsePressure(sp);
                string units = _currentPressureUnitsLabel;
                AgkSetPointText.Text = string.IsNullOrEmpty(units)
                    ? parsedSp : $"{parsedSp} {units}";
            }

            return pressure;
        }

        private async Task<bool> WaitForStateInPointAsync()
        {
            int okCount = 0;
            while (true)
            {
                if (_stopRequested) return false;
                if (!await WaitIfPausedAsync()) return false;

                string? resp = await SendCommandAsync("get_state_in_point;");
                if (resp != null && resp.Contains("ok;", StringComparison.Ordinal))
                {
                    okCount++;
                    if (okCount >= 5) return true;
                }
                else okCount = 0;
                await RefreshPressureAndSetPointAsync();
                SetStatus($"Ожидание стабилизации: {okCount}/5");
                await SafeDelayAsync(1000);
            }
        }

        private async Task<bool> WaitUntilPressureBelowAsync(double threshold, int timeoutMs)
        {
            var start = DateTime.Now;
            var pausedTotal = TimeSpan.Zero;
            var pauseStartedAt = DateTime.MinValue;

            while (true)
            {
                if (_stopRequested) return false;

                if (_pauseRequested)
                {
                    if (pauseStartedAt == DateTime.MinValue) pauseStartedAt = DateTime.Now;
                    await Task.Delay(100);
                    continue;
                }
                else if (pauseStartedAt != DateTime.MinValue)
                {
                    pausedTotal += DateTime.Now - pauseStartedAt;
                    pauseStartedAt = DateTime.MinValue;
                }

                if ((DateTime.Now - start - pausedTotal).TotalMilliseconds >= timeoutMs)
                    return false;

                double? p = await RefreshPressureAndSetPointAsync();
                if (p.HasValue && Math.Abs(p.Value) < threshold) return true;
                await SafeDelayAsync(1000);
            }
        }

        private async Task<bool> SendWithRetryAsync(string command, string expected)
        {
            while (true)
            {
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    if (_stopRequested) return false;
                    if (!await WaitIfPausedAsync()) return false;

                    SetStatus($"{command} — попытка {attempt}/5");
                    string? response = await SendCommandAsync(command);
                    if (response != null && response.Contains(expected, StringComparison.Ordinal))
                        return true;
                    await SafeDelayAsync(1000);
                }
                if (MessageBox.Show($"Команда «{command}» не получила ответ после 5 попыток.",
                    "Ошибка", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return false;
            }
        }

        private async Task<bool> WaitIfPausedAsync()
        {
            while (_pauseRequested && !_stopRequested)
                await Task.Delay(100);
            return !_stopRequested;
        }



    }
}