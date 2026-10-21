using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TERMINAL.Models;
using TERMINAL.Notifications;

namespace TERMINAL
{
    public partial class MainWindow
    {
        private async Task<float?> TryReadCurrentTemperatureAsync()
        {
            return await Task.Run(() =>
            {
                System.Net.Sockets.Socket? sock = null;
                try
                {
                    sock = new System.Net.Sockets.Socket(
                        System.Net.Sockets.AddressFamily.InterNetwork,
                        System.Net.Sockets.SocketType.Stream,
                        System.Net.Sockets.ProtocolType.Tcp);

                    sock.ReceiveTimeout = 1000;
                    sock.SendTimeout = 1000;

                    var endpoint = new IPEndPoint(IPAddress.Parse(FurnaceIp), FurnacePort);

                    var ar = sock.BeginConnect(endpoint, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(3000))
                        return (float?)null;
                    sock.EndConnect(ar);

                    byte[] request = new byte[]
                    {
                        0x00, 0x01,
                        0x00, 0x00,
                        0x00, 0x06,
                        0x00,
                        0x04,
                        0x00, 0xCD,
                        0x00, 0x02
                    };

                    sock.Send(request);

                    byte[] buf = new byte[20];
                    int n = sock.Receive(buf);

                    if (n >= 13 && buf[7] == 0x04)
                    {
                        byte b1 = buf[9];
                        byte b2 = buf[10];
                        byte b3 = buf[11];
                        byte b4 = buf[12];

                        uint bits = (uint)((b3 << 24) | (b4 << 16) | (b1 << 8) | b2);

                        int sign = (int)((bits >> 31) & 1);
                        int exponent = (int)((bits >> 23) & 0xFF);
                        int mantissa = (int)(bits & 0x7FFFFF);

                        double temp;
                        if (exponent == 0)
                        {
                            temp = (mantissa == 0)
                                ? 0.0
                                : Math.Pow(2.0, -126) * (mantissa / Math.Pow(2.0, 23));
                        }
                        else if (exponent == 255)
                        {
                            temp = 0.0;
                        }
                        else
                        {
                            temp = Math.Pow(2.0, exponent - 127) *
                                   (1.0 + mantissa / Math.Pow(2.0, 23));
                        }

                        if (sign == 1) temp = -temp;

                        return (float?)temp;
                    }

                    return (float?)null;
                }
                catch
                {
                    return (float?)null;
                }
                finally
                {
                    try { sock?.Close(); } catch { }
                }
            });
        }

        private Task RefreshFurnaceCurrentTempAsync()
        {
            FurnaceSnapshot snap;
            lock (_furnaceStateLock) snap = _furnaceState;

            if (snap.CurrentTemp.HasValue)
            {
                // ★ Восстановление связи — шлём ОДИН раз при переходе из «плохо» в «хорошо».
                //   Проверяем ДО сброса счётчика, иначе условие никогда не сработает.
                if (_furnaceTempFailCount >= ConnectionLostThreshold)
                {
                    _ = NotificationManager.NotifyAsync(
                        $"🟢 ST-351: связь восстановлена ({_furnaceIp}:{_furnacePort})");
                }

                _furnaceTempFailCount = 0;
                double rounded = Math.Round((double)snap.CurrentTemp.Value, 3);
                string sign = rounded >= 0 ? "+" : "";
                FurnaceCurrentTempText.Text =
                    $"{sign}{rounded.ToString("0.000", CultureInfo.InvariantCulture)} °C";
                FurnaceCurrentTempText.Foreground = Brushes.White;
                if (FurnaceStatusEllipse != null)
                    FurnaceStatusEllipse.Fill = Brushes.Green;
            }
            else
            {
                _furnaceTempFailCount++;

                // ★ Потеря связи — шлём ОДИН раз в момент пересечения порога.
                //   Именно ==, а не >=, иначе будет спам каждую секунду после порога.
                if (_furnaceTempFailCount == ConnectionLostThreshold)
                {
                    _ = NotificationManager.NotifyAsync(
                        $"🔴 ST-351: потеря связи ({_furnaceIp}:{_furnacePort})");
                }

                if (_furnaceTempFailCount >= ConnectionLostThreshold)
                {
                    FurnaceCurrentTempText.Text = "— °C";
                    FurnaceCurrentTempText.Foreground =
                        new SolidColorBrush(Color.FromRgb(0xCC, 0xDD, 0xEE));
                    if (FurnaceStatusEllipse != null)
                        FurnaceStatusEllipse.Fill = Brushes.Red;
                }
            }

            return Task.CompletedTask;
        }

        private Task<bool> RefreshFurnaceSetPointAsync()
        {
            if (FurnaceSetPointBox.IsKeyboardFocused) return Task.FromResult(false);

            FurnaceSnapshot snap;
            lock (_furnaceStateLock) snap = _furnaceState;

            if (snap.SetPoint.HasValue)
            {
                _furnaceSetPointFailCount = 0;
                double rounded = Math.Round((double)snap.SetPoint.Value, 2);
                FurnaceSetPointBox.Text =
                    rounded.ToString("0.##", CultureInfo.InvariantCulture);
                _lastValidFurnaceSetPoint = snap.SetPoint.Value;
                return Task.FromResult(true);
            }

            _furnaceSetPointFailCount++;
            if (_furnaceSetPointFailCount >= ConnectionLostThreshold)
                FurnaceSetPointBox.Text = "—";

            return Task.FromResult(false);
        }
        private void FurnaceSetPointBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_uiLoaded) return;
            if (FurnaceSetPointBox == null) return;
            if (!FurnaceSetPointBox.IsKeyboardFocused) return;

            string txt = FurnaceSetPointBox.Text?.Replace(',', '.') ?? "";
            if (double.TryParse(txt, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double sp))
            {
                if (!double.IsNaN(_lastFurnaceSetPoint) &&
                    Math.Abs(sp - _lastFurnaceSetPoint) > 0.0001)
                {
                    ResetStabilizationTimer();
                }
                _lastFurnaceSetPoint = sp;
            }
        }

        private void ResetStabilizationTimer()
        {
            _stabilizationTimerActive = false;
            _stabilizationTimerStart = DateTime.MinValue;
            _stabilizationOutOfRangeSince = DateTime.MinValue;
            if (StabilizationTimerText != null)
                StabilizationTimerText.Text = "00:00:00";
        }

        private void ResetStabilizationTimer_Click(object sender, RoutedEventArgs e)
        {
            ResetStabilizationTimer();
            SetStatus("Таймер стабилизации сброшен", false);
        }

        private void UpdateStabilizationTimer()
        {
            if (StabilizationTimerText == null) return;

            string spTxt = FurnaceSetPointBox?.Text?.Replace(',', '.') ?? "";
            if (!double.TryParse(spTxt, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double setPoint))
                return;

            string tTxt = FurnaceCurrentTempText?.Text ?? "";
            int idx = tTxt.IndexOf(" °C", StringComparison.Ordinal);
            if (idx >= 0) tTxt = tTxt.Substring(0, idx);
            tTxt = tTxt.Trim().TrimStart('+').Replace(',', '.');

            if (!double.TryParse(tTxt, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double currentTemp))
                return;

            bool inRange = Math.Abs(currentTemp - setPoint) <= _furnaceStabilizationTolerance;

            if (inRange)
            {
                _stabilizationOutOfRangeSince = DateTime.MinValue;

                if (!_stabilizationTimerActive)
                {
                    _stabilizationTimerStart = DateTime.Now;
                    _stabilizationTimerActive = true;
                }
                var elapsed = DateTime.Now - _stabilizationTimerStart;
                StabilizationTimerText.Text =
                    $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            }
            else
            {
                if (_stabilizationOutOfRangeSince == DateTime.MinValue)
                    _stabilizationOutOfRangeSince = DateTime.Now;

                var outOfRange = DateTime.Now - _stabilizationOutOfRangeSince;

                if (outOfRange.TotalSeconds >= StabilizationOutOfRangeGraceSeconds)
                {
                    ResetStabilizationTimer();
                    _stabilizationOutOfRangeSince = DateTime.MinValue;
                }
                else
                {
                    if (_stabilizationTimerActive)
                    {
                        var elapsed = DateTime.Now - _stabilizationTimerStart;
                        StabilizationTimerText.Text =
                            $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
                    }
                }
            }
        }

        private async Task<bool> WaitForSavedFileAsync(string? path, int timeoutMs)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            var start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                if (File.Exists(path)) return true;
                await Task.Delay(250);
            }
            return File.Exists(path);
        }

        private async void ReadFurnaceSetPoint_Click(object sender, RoutedEventArgs e)
        {
            ReadFurnaceSetPointButton.IsEnabled = false;
            try
            {
                SetStatus("Чтение уставки из печи ST-351...", false);
                bool ok = await RefreshFurnaceSetPointAsync();
                if (ok)
                    SetStatusSuccess($"Уставка печи: {FurnaceSetPointBox.Text} °C");
                else
                    SetStatusError("Не удалось прочитать уставку из печи");
            }
            finally
            {
                ReadFurnaceSetPointButton.IsEnabled = true;
            }
        }

        private async void TestWriteFurnace_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning) { MessageBox.Show("Программа занята."); return; }

            string input = FurnaceSetPointBox.Text?.Trim() ?? "";
            if (!float.TryParse(input.Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out float temp))
            {
                MessageBox.Show("Некорректное значение.");
                return;
            }

            TestFurnaceButton.IsEnabled = false;
            try
            {
                SetStatus($"Запись уставки {temp} °C в печь...", false);
                bool ok = await WriteFurnaceSetPointAsync(
                    temp, FurnaceIp, FurnacePort, FurnaceSetPointRegister);

                if (ok)
                {
                    _lastValidFurnaceSetPoint = temp;
                    SetStatusSuccess($"Уставка печи изменена на {temp} °C");
                }
                else SetStatusError($"Ошибка записи уставки печи");
            }
            finally { TestFurnaceButton.IsEnabled = true; }
        }

        private async Task<bool> WriteFurnaceSetPointAsync(
            float value, string ip, int port, ushort startRegister)
        {
            try
            {
                byte[] floatBytes = BitConverter.GetBytes(value);
                if (BitConverter.IsLittleEndian) Array.Reverse(floatBytes);
                byte[] ordered = new[] { floatBytes[2], floatBytes[3], floatBytes[0], floatBytes[1] };

                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(3000);
                if (await Task.WhenAny(connectTask, timeoutTask) != connectTask) return false;
                await connectTask;

                using var stream = client.GetStream();
                stream.ReadTimeout = 2000; stream.WriteTimeout = 2000;

                var buffer = new List<byte>(17);
                buffer.Add(0x00); buffer.Add(0x01);
                buffer.Add(0x00); buffer.Add(0x00);
                buffer.Add(0x00); buffer.Add(0x0B);
                buffer.Add(0x00);
                buffer.Add(0x10);
                buffer.Add((byte)(startRegister >> 8));
                buffer.Add((byte)(startRegister & 0xFF));
                buffer.Add(0x00); buffer.Add(0x02);
                buffer.Add(0x04);
                buffer.Add(ordered[0]); buffer.Add(ordered[1]);
                buffer.Add(ordered[2]); buffer.Add(ordered[3]);

                byte[] reqBytes = buffer.ToArray();
                await stream.WriteAsync(reqBytes, 0, reqBytes.Length);
                await stream.FlushAsync();

                byte[] response = new byte[12];
                int totalRead = 0;
                var start = DateTime.Now;
                while (totalRead < response.Length &&
                       (DateTime.Now - start).TotalMilliseconds < 2000)
                {
                    if (stream.DataAvailable)
                    {
                        int n = await stream.ReadAsync(response, totalRead, response.Length - totalRead);
                        if (n == 0) break;
                        totalRead += n;
                    }
                    else await Task.Delay(20);
                }
                return totalRead >= 12 && response[7] == 0x10;
            }
            catch { return false; }
        }

        private async void FurnaceRun_Click(object sender, RoutedEventArgs e)
        {
            FurnaceRunButton.IsEnabled = false;
            try
            {
                bool ok = await WriteFurnaceRegisterAsync(
                    FurnaceModeManual, FurnaceIp, FurnacePort, FurnaceModeRegister);
                if (ok) SetStatusSuccess("Режим работы печи изменён на «ручной»");
                else SetStatusError("Ошибка записи режима «ручной»");
            }
            finally { FurnaceRunButton.IsEnabled = true; }
        }

        private async void FurnaceStop_Click(object sender, RoutedEventArgs e)
        {
            FurnaceStopButton.IsEnabled = false;
            try
            {
                await StopFurnaceAsync();
            }
            finally
            {
                FurnaceStopButton.IsEnabled = true;
            }
        }

        private async Task<bool> StopFurnaceAsync()
        {
            bool ok = await WriteFurnaceRegisterAsync(
                FurnaceModeIdle, FurnaceIp, FurnacePort, FurnaceModeRegister);
            if (ok) SetStatusSuccess("Режим работы камеры тепла/холода ST-351 изменён на «ждущий»");
            else SetStatusError("Ошибка записи режима «ждущий»");
            return ok;
        }

        private async Task<bool> WriteFurnaceRegisterAsync(
            ushort value, string ip, int port, ushort register)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(3000);
                if (await Task.WhenAny(connectTask, timeoutTask) != connectTask) return false;
                await connectTask;

                using var stream = client.GetStream();
                stream.ReadTimeout = 2000; stream.WriteTimeout = 2000;

                var buffer = new List<byte>(12);
                buffer.Add(0x00); buffer.Add(0x01);
                buffer.Add(0x00); buffer.Add(0x00);
                buffer.Add(0x00); buffer.Add(0x06);
                buffer.Add(0x00);
                buffer.Add(0x06);
                buffer.Add((byte)(register >> 8));
                buffer.Add((byte)(register & 0xFF));
                buffer.Add((byte)(value >> 8));
                buffer.Add((byte)(value & 0xFF));

                byte[] reqBytes = buffer.ToArray();
                await stream.WriteAsync(reqBytes, 0, reqBytes.Length);
                await stream.FlushAsync();

                byte[] response = new byte[12];
                int totalRead = 0;
                var start = DateTime.Now;
                while (totalRead < response.Length &&
                       (DateTime.Now - start).TotalMilliseconds < 2000)
                {
                    if (stream.DataAvailable)
                    {
                        int n = await stream.ReadAsync(response, totalRead, response.Length - totalRead);
                        if (n == 0) break;
                        totalRead += n;
                    }
                    else await Task.Delay(20);
                }
                return totalRead >= 12 && response[7] == 0x06;
            }
            catch { return false; }
        }

        private string FormatFurnaceSetPointFileName(float setPoint)
        {
            if (Math.Abs(setPoint - Math.Round(setPoint)) < 0.001f)
            {
                int intVal = (int)Math.Round(setPoint);
                if (intVal > 0) return "+" + intVal + ".csv";
                return intVal + ".csv";
            }
            string s = setPoint.ToString("0.##", CultureInfo.InvariantCulture);
            if (setPoint > 0 && !s.StartsWith("+")) s = "+" + s;
            return s + ".csv";
        }

    }
}