using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using TERMINAL.Models;
using TERMINAL.Notifications;

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
        /// </summary>
        private async Task<bool> SendPingAsync()
        {
            try
            {
                NetworkStream? stream;
                lock (_streamLock) stream = _stream;
                if (stream == null) return false;

                byte[] cmd = Encoding.ASCII.GetBytes("get_ping;");
                await stream.WriteAsync(cmd, 0, cmd.Length);
                await stream.FlushAsync();

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

        // ═══════════════════════════════════════════════════════════════
        // UI ИЗ КЭША
        // ═══════════════════════════════════════════════════════════════

        private void Timer_Tick(object? sender, EventArgs e)
        {
            // ═══════════════════════════════════════════════════════════════
            // UI ИЗ КЭША — обновляется ВСЕГДА, даже во время пакета
            // ═══════════════════════════════════════════════════════════════

            AgkSnapshot snap;
            lock (_agkStateLock) snap = _agkState;

            bool agkOk = snap.Connected;

            if (agkOk)
            {
                // ─── Единицы измерения ───
                if (!string.IsNullOrEmpty(snap.Units))
                    UpdateUnits(snap.Units);

                // ─── Режим АГК ───
                if (!string.IsNullOrEmpty(snap.Mode))
                    _currentAgkMode = snap.Mode;

                // ─── Текущее давление ───
                if (snap.Pressure.HasValue)
                {
                    string units = _currentPressureUnitsLabel;
                    string parsed = Math.Round(snap.Pressure.Value, 5)
                        .ToString("F5", CultureInfo.InvariantCulture);

                    AgkPressureText.Text = string.IsNullOrEmpty(units)
                        ? parsed
                        : $"{parsed} {units}";

                    _lastKnownPressure = snap.Pressure;
                }

                // ─── Уставка ───
                if (snap.SetPoint.HasValue)
                {
                    string units = _currentPressureUnitsLabel;
                    string parsedSp = Math.Round(snap.SetPoint.Value, 5)
                        .ToString("F5", CultureInfo.InvariantCulture);

                    AgkSetPointText.Text = string.IsNullOrEmpty(units)
                        ? parsedSp
                        : $"{parsedSp} {units}";
                }

                // ─── Статус АГК (get_state_in_point) ───
                if (AgkStatusTextBox != null)
                {
                    string display = snap.StateInPointRaw?.Trim() ?? "";

                    if (string.IsNullOrEmpty(display))
                        display = snap.Status?.Trim() ?? "";

                    if (string.IsNullOrEmpty(display))
                        display = "(нет данных)";

                    AgkStatusTextBox.Text = display;

                    if (display.Contains("ok;"))
                        AgkStatusTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0xFF, 0x80)); // Зелёный
                    else if (display.Contains("wait;"))
                        AgkStatusTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x80)); // Оранжевый
                    else
                        AgkStatusTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)); // Серый
                }

                // ★ Восстановление связи
                if (_agkFailCount >= ConnectionLostThreshold)
                {
                    if (_batchRunning)
                    {
                        _ = NotificationManager.NotifyAsync(
                            $"🟢 АГК: связь восстановлена ({_deviceIp}:{_devicePort})");

                        // ★ SMS при восстановлении
                        if (!string.IsNullOrEmpty(_smsLogin) && !string.IsNullOrEmpty(_smsPhone))
                        {
                            _ = new SmsNotificationService(_smsLogin, _smsPass, _smsPhone)
                                    .SendAsync("АГК: связь восстановлена!");
                        }
                    }
                }
                _agkFailCount = 0;
                AgkStatusEllipse.Fill = Brushes.Green;
            }
            else
            {
                _agkFailCount++;

                // ★ Потеря связи (условие ==, а не >=)
                if (_agkFailCount == ConnectionLostThreshold)
                {
                    if (_batchRunning)
                    {
                        _ = NotificationManager.NotifyAsync(
                            $"🔴 АГК: потеря связи ({_deviceIp}:{_devicePort})");

                        // ★ SMS при потере связи
                        if (!string.IsNullOrEmpty(_smsLogin) && !string.IsNullOrEmpty(_smsPhone))
                        {
                            _ = new SmsNotificationService(_smsLogin, _smsPass, _smsPhone)
                                    .SendAsync($"КРИТИЧНО: Потеря связи АГК ({_devicePort})!");
                        }
                    }
                }

                if (_agkFailCount >= ConnectionLostThreshold)
                {
                    AgkPressureText.Text = "—";
                    AgkSetPointText.Text = "—";
                    AgkStatusEllipse.Fill = Brushes.Red;
                    _currentAgkMode = "";

                    if (AgkStatusTextBox != null)
                    {
                        AgkStatusTextBox.Text = "—";
                        AgkStatusTextBox.Foreground =
                            new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                    }
                }
            }
            if (_batchRunning && _currentStepIndexForNotify >= 0)
            {
                _ = UpdatePressureTableInTelegramAsync();  // fire-and-forget, потому что Timer_Tick — void
            }

        }

            // ═══════════════════════════════════════════════════════════════
            // ★ Всё, что относится к таймерам пакета, — оставляем как есть.
            //   Но UI обновление АГК — выше, оно работает всегда.
            // ═══════════════════════════════════════════════════════════════

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

        // ═══════════════════════════════════════════════════════════════
        // ОТПРАВКА КОМАНД
        // ═══════════════════════════════════════════════════════════════

        private async Task<string?> SendCommandAsync(string command)
        {
            // ★ Ждем блокировку не более 1000 мс. Если не дождались — возвращаем null.
            if (!await _agkCommandLock.WaitAsync(1000))
            {
                return null;
            }

            try
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
                    while ((DateTime.Now - start).TotalMilliseconds < 800) // Таймаут ответа 800 мс
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
            finally
            {
                _agkCommandLock.Release();
            }
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
            var start = DateTime.Now;
            while (true)
            {
                if (_stopRequested) return;

                while (_pauseRequested && !_stopRequested)
                    await Task.Delay(50);

                if (_stopRequested) return;

                long elapsed = (long)(DateTime.Now - start).TotalMilliseconds;
                long remain = ms - elapsed;
                if (remain <= 0) return;

                int chunk = (int)Math.Min(remain, 20);
                await Task.Delay(chunk);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ОБЁРТКИ ДЛЯ ЛОГИКИ (ИЗ КЭША)
        // ═══════════════════════════════════════════════════════════════

        private Task<double?> RefreshPressureAndSetPointAsync()
        {
            AgkSnapshot snap;
            lock (_agkStateLock) snap = _agkState;

            // ★ UI и _lastKnownPressure обновляются в Timer_Tick.
            //   Здесь — только возврат данных.

            if (!string.IsNullOrEmpty(snap.Mode))
                _currentAgkMode = snap.Mode;

            return Task.FromResult(snap.Pressure);
        }

        /// <summary>
        /// Ждёт стабилизацию (5 подряд ok;) после set_point.
        /// Читает ТОЛЬКО из кэша _agkState.
        /// </summary>
        /// <summary>
        /// Ждёт стабилизацию (5 подряд ok;) после set_point.
        /// Читает из кэша. Проверяет физическое давление относительно уставки.
        /// </summary>
        private async Task<bool> WaitForStateInPointAsync(double targetPressure = 0)
        {
            var start = DateTime.Now;
            int timeoutMs = _agkPressureTimeoutMinutes * 60 * 1000;
            int okCount = 0;

            while (true)
            {
                if (_stopRequested) return false;
                if (!await WaitIfPausedAsync()) return false;

                var elapsed = DateTime.Now - start;

                // ★ Единственный таймаут — глобальный из настроек
                if (elapsed.TotalMilliseconds >= timeoutMs)
                {
                    AppendLog($"WaitForStateInPoint: таймаут {_agkPressureTimeoutMinutes} мин — " +
                              $"прибор не вышел на стабильное давление.", Brushes.OrangeRed);
                    SetStatusError($"Таймаут ожидания стабилизации давления ({_agkPressureTimeoutMinutes} мин)");
                    return false;
                }

                // ★ Читаем из кэша
                AgkSnapshot snap;
                lock (_agkStateLock) snap = _agkState;

                // ★ Защита от того, что фон умер
                if ((DateTime.Now - snap.UpdatedAt).TotalSeconds > 5)
                {
                    AppendLog("WaitForStateInPoint: КЭШ АГК НЕ ОБНОВЛЯЕТСЯ (>5 сек)!", Brushes.Red);
                    return false;
                }

                // ★ Логика:
                // - Если "ok;" -> набираем счетчик
                // - Если "error;" (атмосфера) или "wait;" -> сбрасываем счетчик и ждем
                if (snap.StateInPointOk)
                {
                    okCount++;
                    if (okCount >= 5)
                    {
                        AppendLog("Стабилизация давления завершена (5/5)", Brushes.Green);
                        SetStatus("Стабилизация завершена (5/5)");
                        return true;
                    }
                }
                else
                {
                    okCount = 0;
                }

                // Обновляем статус внизу экрана
                int elapsedMin = (int)elapsed.TotalMinutes;
                string stateText = snap.StateInPointOk ? "ok" : (snap.StateInPointWait ? "wait" : "error/нет данных");
                SetStatus($"Ожидание стабилизации: {okCount}/5 (АГК: {stateText}) " +
                          $"({elapsedMin} / {_agkPressureTimeoutMinutes} мин)", log: false);

                await Task.Delay(1000);
            }
        }

        /// <summary>
        /// Ждёт снижения давления ниже порога после vent.
        /// Прерывается по таймауту (AgkPressureTimeoutMinutes) или по переданному лимиту.
        /// </summary>
        private async Task<bool> WaitUntilPressureBelowAsync(double threshold, int timeoutMs)
        {
            // Таймаут из настроек (в минутах), но не больше переданного лимита
            int settingsTimeoutMs = _agkPressureTimeoutMinutes * 60 * 1000;
            int effectiveTimeoutMs = Math.Min(timeoutMs, settingsTimeoutMs);

            var start = DateTime.Now;
            var pausedTotal = TimeSpan.Zero;
            var pauseStartedAt = DateTime.MinValue;
            var lastVentCheck = DateTime.MinValue;
            var lastDebugLog = DateTime.MinValue;

            try
            {
                while (true)
                {
                    if (_stopRequested) return false;

                    // ─── Пауза ───
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

                    // ─── Проверка таймаута ───
                    var elapsed = DateTime.Now - start - pausedTotal;
                    if (elapsed.TotalMilliseconds >= effectiveTimeoutMs)
                    {
                        AppendLog($"WaitUntilPressureBelow: таймаут {_agkPressureTimeoutMinutes} мин — " +
                                  $"давление не снизилось ниже {threshold}", Brushes.OrangeRed);
                        SetStatusError($"Таймаут ожидания сброса давления ({_agkPressureTimeoutMinutes} мин)");
                        return false;
                    }

                    // ─── Раз в 1 секунду проверяем, что прибор всё ещё в vent ───
                    if ((DateTime.Now - lastVentCheck).TotalMilliseconds >= 1000)
                    {
                        lastVentCheck = DateTime.Now;

                        AgkSnapshot snap;
                        lock (_agkStateLock) snap = _agkState;

                        // Если режим не vent и не пустой — прибор вышел из vent
                        if (!string.IsNullOrEmpty(snap.Mode) && snap.Mode != "vent")
                        {
                            AppendLog($"WaitUntilPressureBelow: прибор вышел из vent " +
                                      $"(mode={snap.Mode}) — повторно отправляем set_mode=vent;",
                                      Brushes.OrangeRed);

                            // ★ Отправляем без ожидания ответа, чтобы не блокировать фон
                            await SendCommandNoWaitAsync("set_mode=vent;");
                        }

                        // Отладка: раз в 5 секунд пишем режим и давление
                        if ((DateTime.Now - lastDebugLog).TotalSeconds >= 5)
                        {
                            lastDebugLog = DateTime.Now;
                        }
                    }

                    // ─── Проверка давления ИЗ КЭША (без прямых запросов) ───
                    AgkSnapshot snap2;
                    lock (_agkStateLock) snap2 = _agkState;

                    double? p = snap2.Pressure;
                    if (p.HasValue && Math.Abs(p.Value) < threshold)
                    {
                        AppendLog($"WaitUntilPressureBelow: давление достигло {p.Value:F3}, vent завершён",
                                  Brushes.Green);

                        // ★ Обнуляем уставку БЕЗ ожидания ответа
                        //await Task.Delay(2000);
                        //await SendCommandNoWaitAsync("set_point=0;");
                        //AppendLog("WaitUntilPressureBelow: уставка обнулена (set_point=0)", Brushes.Gray);

                        return true;
                    }

                    await Task.Delay(1000);
                }
            }
            finally
            {
                // Сбрасываем флаг в любом случае
                _agkInVentMode = false;
                AppendLog("WaitUntilPressureBelow: _agkInVentMode = false", Brushes.Gray);
            }
        }

        private async Task<bool> SendWithRetryAsync(string command, string expected)
        {
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                if (_stopRequested) return false;
                if (!await WaitIfPausedAsync()) return false;

                SetStatus($"{command} — попытка {attempt}/5");

                // Вызываем SendCommandAsync, но он уже с таймаутом внутри
                string? response = await SendCommandAsync(command);

                if (response != null && response.Contains(expected, StringComparison.Ordinal))
                    return true;

                await SafeDelayAsync(1000);
            }

            AppendLog($"SendWithRetry: команда «{command}» не получила «{expected}» после 5 попыток",
                      Brushes.OrangeRed);
            return false;
        }

        private async Task<bool> WaitIfPausedAsync()
        {
            while (_pauseRequested && !_stopRequested)
                await Task.Delay(100);
            return !_stopRequested;
        }

        // ═══════════════════════════════════════════════════════════════
        // КЭШ СОСТОЯНИЯ
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Обновляет кэш состояния АГК. Вызывается раз в секунду.
        /// </summary>
        private async Task RefreshAgkStateAsync()
        {
            // ★ Запускаем секундомер перед опросом
            var sw = System.Diagnostics.Stopwatch.StartNew();

            NetworkStream? stream;
            lock (_streamLock) stream = _stream;

            var snap = new AgkSnapshot { UpdatedAt = DateTime.Now };

            if (stream == null || !stream.CanRead || !stream.CanWrite)
            {
                snap.Connected = false;
                lock (_agkStateLock) _agkState = snap;

                sw.Stop();
                AddTickTiming("agk-poll", sw.ElapsedMilliseconds);
                return;
            }

            try
            {
                AgkSnapshot prev;
                lock (_agkStateLock) prev = _agkState;

                // 1. Давление
                var pressureResp = await SendCommandAsync("get_press;");
                snap.Pressure = pressureResp != null
                    ? ParseDoubleOrNull(pressureResp)
                    : prev.Pressure;

                // 2. Статус в точке
                var stateResp = await SendCommandAsync("get_state_in_point;");
                if (stateResp != null)
                {
                    snap.StateInPointRaw = stateResp;
                    snap.StateInPointOk = stateResp.IndexOf("ok;", StringComparison.OrdinalIgnoreCase) >= 0;
                    snap.StateInPointWait = stateResp.IndexOf("wait;", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                else
                {
                    snap.StateInPointRaw = prev.StateInPointRaw;
                    snap.StateInPointOk = prev.StateInPointOk;
                    snap.StateInPointWait = prev.StateInPointWait;
                }

                // 3. Уставка
                var pointResp = await SendCommandAsync("get_point;");
                snap.SetPoint = pointResp != null ? ParseDoubleOrNull(pointResp) : prev.SetPoint;

                // 4. Режим
                var modeResp = await SendCommandAsync("get_mode;");
                snap.Mode = modeResp != null
                    ? modeResp.Trim().TrimEnd(';').ToLower()
                    : prev.Mode;

                // 5. Единицы
                var unitsResp = await SendCommandAsync("get_units;");
                snap.Units = unitsResp ?? prev.Units;

                // 6. Общий статус
                var statusResp = await SendCommandAsync("get_status;");
                snap.Status = statusResp ?? prev.Status;

                snap.Connected = true;
            }
            catch (Exception ex)
            {
                snap.Connected = false;
                AppendLog($"ОШИБКА ОПРОСА АГК: {ex.Message}", Brushes.Red);
            }
            finally
            {
                // ★ Останавливаем секундомер и записываем результат
                sw.Stop();
                AddTickTiming("agk-poll", sw.ElapsedMilliseconds);
            }

            lock (_agkStateLock) _agkState = snap;
        }

        /// <summary>
        /// Обновляет кэш состояния печи. Одно TCP-соединение — два Modbus-запроса.
        /// </summary>private async Task RefreshAgkStateAsync
        /// 
        private async Task RefreshFurnaceStateAsync()
        {
            var snap = new FurnaceSnapshot { UpdatedAt = DateTime.Now };
            long elapsedMs = 0; // ★ Переменная для замера

            try
            {
                var (temp, setPoint, ms) = await Task.Run(() =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew(); // ★ Запускаем внутри потока
                    Socket? sock = null;
                    try
                    {
                        sock = new Socket(AddressFamily.InterNetwork,
                                          SocketType.Stream, ProtocolType.Tcp);
                        sock.ReceiveTimeout = 1000;
                        sock.SendTimeout = 1000;

                        var endpoint = new IPEndPoint(IPAddress.Parse(FurnaceIp), FurnacePort);
                        var ar = sock.BeginConnect(endpoint, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(3000))
                            return ((float?)null, (float?)null, sw.ElapsedMilliseconds);
                        sock.EndConnect(ar);

                        float? t = ReadModbusFloat(sock, 0x00CD, 0x04);   // температура
                        float? s = ReadModbusFloat(sock, 0x00C9, 0x03);   // уставка

                        sw.Stop();
                        return (t, s, sw.ElapsedMilliseconds);
                    }
                    catch
                    {
                        sw.Stop();
                        return ((float?)null, (float?)null, sw.ElapsedMilliseconds);
                    }
                    finally { try { sock?.Close(); } catch { } }
                });

                snap.CurrentTemp = temp;
                snap.SetPoint = setPoint;
                snap.Connected = temp.HasValue || setPoint.HasValue;
                elapsedMs = ms; // ★ Сохраняем замер
            }
            catch
            {
                snap.Connected = false;
            }

            lock (_furnaceStateLock) _furnaceState = snap;

            // ★ Записываем время
            AddTickTiming("furnace", elapsedMs);
        }

        /// <summary>
        /// Читает float из регистра Modbus в уже открытом socket.
        /// </summary>
        private static float? ReadModbusFloat(Socket sock, ushort register, byte functionCode = 0x03)
        {
            try
            {
                byte[] req = new byte[]
                {
            0x00, 0x01,
            0x00, 0x00,
            0x00, 0x06,
            0x00,
            functionCode,                  // ← 0x03 или 0x04
            (byte)(register >> 8),
            (byte)(register & 0xFF),
            0x00, 0x02
                };

                sock.Send(req);

                byte[] buf = new byte[20];
                int n = sock.Receive(buf);

                if (n >= 13 && buf[7] == functionCode)
                {
                    byte b1 = buf[9], b2 = buf[10], b3 = buf[11], b4 = buf[12];
                    float val = BitConverter.ToSingle(new byte[] { b2, b1, b4, b3 }, 0);

                    if (!float.IsNaN(val) && !float.IsInfinity(val))
                        return val;
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Парсит число из ответа АГК.
        /// </summary>
        private static double? ParseDoubleOrNull(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            string s = raw.Trim().Trim('\r', '\n', '\0', ' ', ';');
            if (s.StartsWith("E", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("e", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(1);
            s = s.Replace(',', '.');

            if (double.TryParse(s, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double v))
                return v;

            return null;
        }
    }
}