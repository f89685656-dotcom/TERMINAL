using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TERMINAL.Models;

namespace TERMINAL
{
    public partial class MainWindow
    {
        private void StartBroadcastServer()
        {
            try
            {
                _broadcastServer = new TcpListener(IPAddress.Loopback, BroadcastPort);
                _broadcastServer.Start();

                _ = Task.Run(async () =>
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        try
                        {
                            var client = await _broadcastServer.AcceptTcpClientAsync();
                            _ = Task.Run(() => HandleClientAsync(client));
                        }
                        catch { break; }
                    }
                });

                // ─── Таймер обновления UI клиентов ───
                _clientsUiTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _clientsUiTimer.Tick += (s, e) =>
                {
                    var start = DateTime.Now;
                    try { RefreshClientsUi(); }
                    finally
                    {
                        long ms = (long)(DateTime.Now - start).TotalMilliseconds;
                        AddTickTiming("clientsUI", ms);
                    }
                };
                _clientsUiTimer.Start();

                string mode = BroadcastPort == 9990 ? "рабочая" : "тестовая";
                SetStatus($"Broadcast-сервер ({mode}) запущен на 127.0.0.1:{BroadcastPort}", false);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                string which = BroadcastPort == 9990 ? "рабочая" : "тестовая";
                MessageBox.Show(
                    $"Порт {BroadcastPort} уже занят.\n\n" +
                    $"Похоже, {which} версия уже запущена (или не закрыт старый экземпляр).\n" +
                    $"Закройте другую копию или выберите другой режим при следующем запуске.",
                    "Порт занят", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось запустить broadcast-сервер: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private void RefreshClientsUi()
        {
            List<ConnectedClient> toMarkOffline = new();
            List<ConnectedClient> toRemove = new();

            lock (_clientsLock)
            {
                var now = DateTime.Now;
                foreach (var c in _connectedClients)
                {
                    if (!c.IsOffline &&
                        (now - c.LastActivityAt).TotalSeconds >= ClientDeadThresholdSeconds)
                    {
                        toMarkOffline.Add(c);
                    }

                    if (c.DisconnectedAt.HasValue &&
                        (now - c.DisconnectedAt.Value).TotalSeconds >= OfflineRetentionSeconds)
                    {
                        toRemove.Add(c);
                    }
                }

                foreach (var c in toRemove)
                    _connectedClients.Remove(c);
            }

            foreach (var c in toMarkOffline)
                c.SetStatusOffline();
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            NetworkStream? stream = null;
            string sid = "(неизвестен)";
            ConnectedClient? connectedClient = null;

            try
            {
                stream = client.GetStream();

                var sb = new StringBuilder();
                byte[] one = new byte[1];
                while (true)
                {
                    int n = await stream.ReadAsync(one, 0, 1);
                    if (n == 0) return;
                    if (one[0] == 10) break;
                    sb.Append((char)one[0]);
                }

                string hello = sb.ToString().Trim();
                string sidParsed = "(неизвестен)";
                var capsParsed = new List<string>();

                if (hello.StartsWith("HELLO "))
                {
                    string rest = hello[6..].Trim();
                    int capsIdx = rest.IndexOf(" CAPS ", StringComparison.Ordinal);
                    if (capsIdx >= 0)
                    {
                        sidParsed = rest.Substring(0, capsIdx).Trim();
                        string capsPart = rest.Substring(capsIdx + 6).Trim();
                        capsParsed = capsPart
                            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim())
                            .Where(x => x.Length > 0)
                            .ToList();
                    }
                    else
                    {
                        sidParsed = rest;
                    }
                }

                sid = sidParsed;

                connectedClient = new ConnectedClient
                {
                    Sid = sid,
                    TcpClient = client,
                    IsSelected = true,
                    ConnectedAt = DateTime.Now,
                    LastActivityAt = DateTime.Now
                };
                if (capsParsed.Count > 0)
                    connectedClient.SetCapabilities(capsParsed);
                if (connectedClient.IsSensorClient)
                    connectedClient.IsSelected = false;
                connectedClient.SetStatusReady();

                ConnectedClient? existing;
                lock (_clientsLock)
                {
                    existing = _connectedClients.FirstOrDefault(c => c.Sid == sid);
                    if (existing != null)
                    {
                        existing.TcpClient = client;
                        existing.ConnectedAt = DateTime.Now;
                        existing.LastActivityAt = DateTime.Now;
                        connectedClient = existing;
                    }
                }

                if (existing != null)
                {
                    existing.SetStatusAlive();
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        lock (_clientsLock)
                        {
                            if (!_connectedClients.Any(c => c.Sid == sid))
                            {
                                _connectedClients.Add(connectedClient);
                            }
                        }
                    });
                }

                var buffer = new StringBuilder();
                byte[] buf = new byte[512];

                while (!_cts.IsCancellationRequested)
                {
                    int n;
                    try
                    {
                        n = await stream.ReadAsync(buf, 0, buf.Length, _cts.Token);
                    }
                    catch { break; }

                    if (n == 0) break;

                    string chunk = Encoding.UTF8.GetString(buf, 0, n);
                    buffer.Append(chunk);

                    while (true)
                    {
                        string full = buffer.ToString();
                        int idx = full.IndexOf('\n');
                        if (idx < 0) break;

                        string line = full.Substring(0, idx).Trim();
                        buffer.Remove(0, idx + 1);

                        if (string.IsNullOrEmpty(line)) continue;
                        ProcessClientMessage(connectedClient, line);
                    }
                }
            }
            catch { }
            finally
            {
                try { stream?.Close(); } catch { }
                try { client.Close(); } catch { }

                if (connectedClient != null)
                {
                    bool sameClient;
                    lock (_clientsLock)
                        sameClient = connectedClient.TcpClient == client;

                    if (sameClient)
                        connectedClient.SetStatusOffline();
                }
            }
        }

        private void ProcessClientMessage(ConnectedClient client, string line)
        {
            client.LastActivityAt = DateTime.Now;

            if (line.StartsWith("HELLO "))
            {
                client.SetStatusAlive();

                int capsIdx = line.IndexOf(" CAPS ", StringComparison.Ordinal);
                if (capsIdx >= 0)
                {
                    string capsPart = line.Substring(capsIdx + 6).Trim();
                    var caps = capsPart
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => x.Length > 0);

                    client.SetCapabilities(caps);

                    if (client.IsSensorClient)
                        client.IsSelected = false;
                }
            }
            else if (line.StartsWith("NAME "))
            {
                string name = line.Substring(5).Trim();
                client.CustomName = name;
            }
            else if (line.StartsWith("CASE "))
            {
                string val = line.Substring(5).Trim();
                if (int.TryParse(val, out int caseVal) && caseVal >= 1)
                {
                    client.CaseValue = caseVal.ToString();
                }
            }
            else if (line.StartsWith("STATUS_BUSY")) client.SetStatusWaiting();
            else if (line.StartsWith("DONE_OK"))
            {
                client.LastDoneOk = true; client.LastDoneFail = false;
                client.SetStatusDoneSuccess();
            }
            else if (line.StartsWith("DONE_FAIL"))
            {
                client.LastDoneOk = false; client.LastDoneFail = true;
                client.SetStatusDoneFail();
            }
            else if (line.StartsWith("SAVED_OK"))
            {
                lock (_saveLock) { _saveResponses.Add(client.Sid + ":OK"); }
                client.SetStatusSaved();
            }
            else if (line.StartsWith("SAVED_FAIL"))
            {
                lock (_saveLock) { _saveResponses.Add(client.Sid + ":FAIL"); }
                client.SetStatusSaveFailed();
            }
            else if (line.StartsWith("CLEARED_OK"))
            {
                lock (_clearLock) { _clearResponses.Add(client.Sid + ":OK"); }
                client.SetStatusCleared();
            }
            else if (line.StartsWith("CLEARED_FAIL"))
            {
                lock (_clearLock) { _clearResponses.Add(client.Sid + ":FAIL"); }
                client.SetStatusClearFailed();
            }
            else if (line.StartsWith("ATM "))
            {
                string val = line.Substring(4).Trim().Replace(',', '.');
                if (double.TryParse(val, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double v))
                {
                    client.LastAtm = v;
                    client.LastAtmAt = DateTime.Now;
                }
            }
            else if (line.StartsWith("FURNACE_TEMP "))
            {
                string val = line.Substring(13).Trim().Replace(',', '.');
                if (double.TryParse(val, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double v))
                {
                    client.LastFurnaceFromClient = v;
                    client.LastFurnaceFromClientAt = DateTime.Now;
                }
            }
        }

        private void RemoveOffline_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning) return;

            lock (_clientsLock)
            {
                var toRemove = _connectedClients.Where(c => c.IsOffline).ToList();
                foreach (var c in toRemove)
                {
                    try { c.TcpClient.Close(); } catch { }
                    _connectedClients.Remove(c);
                }
            }
        }

        private void DisconnectClient_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi &&
                mi.Tag is ConnectedClient client)
            {
                lock (_clientsLock)
                {
                    try { client.TcpClient.Close(); } catch { }
                    _connectedClients.Remove(client);
                }
            }
        }

        private async Task<bool> TakePointAndWaitAllAsync()
        {
            List<ConnectedClient> selected;
            lock (_clientsLock)
            {
                selected = _connectedClients
                    .Where(c => c.IsSelected && !c.IsOffline && !c.IsSensorClient).ToList();
            }

            if (selected.Count == 0)
            {
                SetStatus("Нет отмеченных клиентов");
                return false;
            }

            foreach (var c in selected)
            {
                c.LastDoneOk = false; c.LastDoneFail = false;
                c.SetStatusWaiting();
            }

            byte[] message = Encoding.ASCII.GetBytes("TAKE_POINT\n");

            var sendTasks = selected.Select(async c =>
            {
                try
                {
                    var stream = c.TcpClient.GetStream();
                    await stream.WriteAsync(message, 0, message.Length);
                    await stream.FlushAsync();
                }
                catch { }
            }).ToList();

            await Task.WhenAll(sendTasks);

            var waitTasks = selected.Select(c => WaitForClientDoneAsync(c)).ToList();
            bool[] results = await Task.WhenAll(waitTasks);

            for (int i = 0; i < selected.Count; i++)
            {
                if (!results[i])
                {
                    SetStatus($"Клиент {selected[i].Sid} не завершил съём");
                    return false;
                }
            }

            SetStatus($"Все клиенты ({selected.Count}) ответили DONE_OK. Пауза 3 сек...");
            var pauseStart = DateTime.Now;
            while ((DateTime.Now - pauseStart).TotalSeconds < 3)
            {
                if (_stopRequested) return false;
                await SafeDelayAsync(500);
            }

            foreach (var c in selected)
            {
                if (!c.IsOffline)
                    c.SetStatusReady();
            }

            return true;
        }

        private async Task<bool> WaitForClientDoneAsync(ConnectedClient client)
        {
            var start = DateTime.Now;
            var timeout = TimeSpan.FromMinutes(15);
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

                if (DateTime.Now - start - pausedTotal >= timeout) return false;
                if (client.LastDoneOk) return true;
                if (client.LastDoneFail) return false;

                if (client.IsWaiting)
                {
                    var elapsed = (int)(DateTime.Now - start - pausedTotal).TotalSeconds;
                    client.SetStatusWaitingWithTime(elapsed);
                }

                await Task.Delay(1000);
            }
        }

        private async void TakePointBroadcast_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning)
            {
                MessageBox.Show("Идёт градуировка/Бароциклирование. Дождитесь завершения.");
                return;
            }

            try
            {
                TakePointBroadcastButton.IsEnabled = false;
                await TakePointAndWaitAllAsync();
            }
            finally { TakePointBroadcastButton.IsEnabled = true; }
        }

        private async Task<bool> TakePointForStepAsync(int stepIndex, bool waitForState)
        {
            if (!_batchKosaEnabled)
            {
                if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                    _stepItems[stepIndex].KosaCounterText = "1/1";

                return await TakePointAndWaitAllAsync();
            }

            SetStatus("КОСА: съём 1/3");
            if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                _stepItems[stepIndex].KosaCounterText = "1/3";
            if (!await TakePointKosaAllAsync()) return false;

            for (int i = 0; i < 2; i++)
            {
                if (_stopRequested) return false;

                SetStatus($"КОСА: пауза перед съёмом {i + 2}/3 ({_batchKosaTimerSeconds} сек)");
                await KosaWaitAsync(_batchKosaTimerSeconds, waitForState, stepIndex);
                if (_stopRequested) return false;

                SetStatus($"КОСА: съём {i + 2}/3");
                if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                    _stepItems[stepIndex].KosaCounterText = $"{i + 2}/3";
                if (!await TakePointKosaAllAsync()) return false;
            }

            return true;
        }

        private async Task<bool> TakePointKosaAllAsync()
        {
            List<ConnectedClient> selected;
            lock (_clientsLock)
            {
                selected = _connectedClients
                    .Where(c => c.IsSelected && !c.IsOffline && !c.IsSensorClient).ToList();
            }

            if (selected.Count == 0)
            {
                SetStatus("Нет отмеченных клиентов");
                return false;
            }

            var tasks = selected.Select(c => TakePointUntilOkAsync(c)).ToList();
            bool[] results = await Task.WhenAll(tasks);

            for (int i = 0; i < selected.Count; i++)
            {
                if (!results[i]) return false;
            }

            SetStatus($"КОСА: все клиенты ({selected.Count}) ответили DONE_OK. Пауза 3 сек...");
            await SafeDelayAsync(3000);

            foreach (var c in selected)
            {
                if (!c.IsOffline)
                    c.SetStatusReady();
            }

            return true;
        }

        private async Task<bool> TakePointUntilOkAsync(ConnectedClient client)
        {
            while (!_stopRequested)
            {
                if (!await WaitIfPausedAsync()) return false;

                client.LastDoneOk = false;
                client.LastDoneFail = false;
                client.SetStatusWaiting();

                byte[] msg = Encoding.ASCII.GetBytes("TAKE_POINT\n");
                try
                {
                    var s = client.TcpClient.GetStream();
                    await s.WriteAsync(msg, 0, msg.Length);
                    await s.FlushAsync();
                }
                catch
                {
                    await Task.Delay(1000);
                    continue;
                }

                bool done = await WaitForClientDoneAsync(client);
                if (done && client.LastDoneOk) return true;

                if (_stopRequested) return false;

                await Task.Delay(500);
            }
            return false;
        }

        private async Task KosaWaitAsync(int seconds, bool waitForState, int stepIndex)
        {
            int remaining = seconds;

            if (!waitForState)
            {
                while (remaining > 0)
                {
                    if (!await WaitIfPausedAsync()) return;

                    var cycleStart = DateTime.Now;

                    SetStatus($"КОСА: пауза {remaining} сек", running: true, log: false);
                    if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                        _stepItems[stepIndex].TimerText = remaining.ToString();

                    long pureMs = (long)(DateTime.Now - cycleStart).TotalMilliseconds;
                    AddTickTiming("kosa-idle", pureMs);

                    int sleep = 1000 - (int)pureMs;
                    if (sleep > 0) await SafeDelayAsync(sleep);

                    remaining--;
                }

                if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                    _stepItems[stepIndex].TimerText = "0";

                return;
            }

            // ─── waitForState == true ───
            bool firstOkReceived = false;

            while (!firstOkReceived)
            {
                await RefreshPressureAndSetPointAsync();

                AgkSnapshot snap;
                lock (_agkStateLock) snap = _agkState;

                if (snap.StateInPointOk)
                    firstOkReceived = true;
                else
                {
                    SetStatus("КОСА: ожидание стабилизации давления...",
                              running: true, log: false);
                    await SafeDelayAsync(1000);
                }
            }

            while (remaining > 0)
            {
                var cycleStart = DateTime.Now;

                long refreshMs = 0;
                long stateMs = 0;
                long uiMs = 0;

                // ─── RefreshPressure (из кэша, мгновенно) ───
                var t1 = DateTime.Now;
                await RefreshPressureAndSetPointAsync();
                refreshMs = (long)(DateTime.Now - t1).TotalMilliseconds;

                // ─── get_state_in_point — из кэша ───
                var t2 = DateTime.Now;
                AgkSnapshot snap;
                lock (_agkStateLock) snap = _agkState;
                bool stateOk = snap.StateInPointOk;
                bool stateWait = snap.StateInPointWait;
                stateMs = (long)(DateTime.Now - t2).TotalMilliseconds;

                // ─── UI и логика ───
                var t3 = DateTime.Now;

                if (stateOk)
                {
                    remaining--;
                    SetStatus($"КОСА: пауза {remaining} сек", running: true, log: false);
                    if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                        _stepItems[stepIndex].TimerText = remaining.ToString();
                }
                else if (stateWait)
                {
                    remaining += 30;
                    SetStatus($"КОСА: срыв! Таймер +30 сек. Осталось: {remaining}",
                              running: true, log: true);

                    if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                        _stepItems[stepIndex].TimerText = remaining.ToString();

                    // Ждём, пока StateInPointOk снова не станет true
                    while (true)
                    {
                        await SafeDelayAsync(1000);

                        lock (_agkStateLock) snap = _agkState;
                        if (snap.StateInPointOk) break;
                    }
                }
                // Если ни ok, ни wait — ничего не делаем, ждём следующий тик.

                uiMs = (long)(DateTime.Now - t3).TotalMilliseconds;

                long pureMs = (long)(DateTime.Now - cycleStart).TotalMilliseconds;
                AddTickTiming("kosa", pureMs);

                if (pureMs > 1000)
                {
                    AppendLog($"⚠ Долгий такт kosa: {pureMs} мс " +
                              $"(refr {refreshMs}, state {stateMs}, ui {uiMs})",
                              Brushes.OrangeRed);
                }

                int sleep = 1000 - (int)pureMs;
                if (sleep > 0) await SafeDelayAsync(sleep);
            }

            if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                _stepItems[stepIndex].TimerText = "0";
        }
    }
}