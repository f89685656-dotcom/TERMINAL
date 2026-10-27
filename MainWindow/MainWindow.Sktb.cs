using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using HidSharp;
using TERMINAL.Models;
using TERMINAL.Notifications;

namespace TERMINAL
{
    public partial class MainWindow
    {
        private async Task<double?> TryReadPlatinaAsync()
        {
            return await Task.Run(() =>
            {
                HidStream? stream = null;
                try
                {
                    var devices = DeviceList.Local.GetHidDevices(PlatinaVid, PlatinaPid);

                    int deviceCount = 0;
                    foreach (var d in devices) deviceCount++;

                    if (deviceCount == 0)
                        return (double?)null;

                    // ★ Команда из настроек + обязательные \n\0
                    string cmd = _platinumMtkCommand;
                    if (string.IsNullOrEmpty(cmd)) cmd = "pass2 meas2? 2";
                    if (!cmd.EndsWith("\n\0"))
                        cmd += "\n\0";

                    byte[] payload = Encoding.ASCII.GetBytes(cmd);

                    foreach (var device in devices)
                    {
                        try
                        {
                            stream = device.Open();
                            stream.ReadTimeout = 1000;
                            stream.WriteTimeout = 1000;

                            for (int off = 0; off < payload.Length; off += 64)
                            {
                                byte[] report = new byte[65];
                                int chunk = Math.Min(64, payload.Length - off);
                                Array.Copy(payload, off, report, 1, chunk);
                                stream.Write(report, 0, report.Length);
                            }

                            var data = new List<byte>();
                            var start = DateTime.Now;

                            const int MaxReadMs = 2000;
                            while ((DateTime.Now - start).TotalMilliseconds < MaxReadMs)
                            {
                                byte[] buf = new byte[65];
                                int n = stream.Read(buf, 0, buf.Length);
                                if (n <= 1) continue;

                                for (int i = 1; i < n; i++) data.Add(buf[i]);

                                for (int i = 0; i < data.Count - 1; i++)
                                {
                                    if (data[i] == 0x0A && data[i + 1] == 0x00)
                                    {
                                        string s = Encoding.ASCII.GetString(
                                            data.GetRange(0, i).ToArray());

                                        string first = s.Split(new[] { ' ', '\t' },
                                            StringSplitOptions.RemoveEmptyEntries)
                                            .FirstOrDefault() ?? "";
                                        first = first.Replace(',', '.');

                                        if (double.TryParse(first, NumberStyles.Float,
                                            CultureInfo.InvariantCulture, out double val))
                                            return (double?)val;
                                        return (double?)null;
                                    }
                                }
                            }

                            return (double?)null;
                        }
                        catch { }
                        finally
                        {
                            try { stream?.Close(); } catch { }
                            stream = null;
                        }
                    }
                    return (double?)null;
                }
                catch
                {
                    return (double?)null;
                }
            });
        }

        private void RefreshSensorUiFromClients()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(RefreshSensorUiFromClients);
                return;
            }

            var now = DateTime.Now;

            // ── ПДА ──
            ConnectedClient? atmClient;
            lock (_clientsLock)
            {
                atmClient = _connectedClients
                    .Where(c => !c.IsOffline && c.HasCapability("ATM"))
                    .OrderByDescending(c => c.LastAtmAt)
                    .FirstOrDefault();
            }

            bool pdaFresh = atmClient != null &&
                (now - atmClient.LastAtmAt).TotalSeconds <= SensorStaleSeconds &&
                !double.IsNaN(atmClient.LastAtm);

            if (pdaFresh)
            {
                _pdaLastValue = atmClient!.LastAtm;
                _pdaLastSuccess = atmClient.LastAtmAt;
                _pdaSourceSid = atmClient.Sid;

                if (PdaStatusEllipse != null)
                    PdaStatusEllipse.Fill = Brushes.Green;

                if (PdaPressureText != null)
                {
                    PdaPressureText.Text =
                        _pdaLastValue.ToString("0.00", CultureInfo.InvariantCulture) +
                        " мм рт. ст.";
                    PdaPressureText.Foreground = Brushes.White;
                }
            }
            else
            {
                if (PdaPressureText != null &&
                    _pdaLastSuccess != DateTime.MinValue &&
                    (now - _pdaLastSuccess).TotalSeconds > SensorStaleSeconds)
                {
                    PdaPressureText.Text = "— мм рт. ст.";
                    PdaPressureText.Foreground =
                        new SolidColorBrush(Color.FromRgb(0xCC, 0xDD, 0xEE));
                }
                if (PdaStatusEllipse != null)
                    PdaStatusEllipse.Fill = Brushes.Red;
            }

            // ── ПТК ──
            ConnectedClient? ptkClient;
            lock (_clientsLock)
            {
                ptkClient = _connectedClients
                    .Where(c => !c.IsOffline && c.HasCapability("FURNACE_TEMP"))
                    .OrderByDescending(c => c.LastFurnaceFromClientAt)
                    .FirstOrDefault();
            }

            bool ptkFresh = ptkClient != null &&
                (now - ptkClient.LastFurnaceFromClientAt).TotalSeconds <= SensorStaleSeconds &&
                !double.IsNaN(ptkClient.LastFurnaceFromClient);

            if (ptkFresh)
            {
                _ptkLastValue = ptkClient!.LastFurnaceFromClient;
                _ptkLastSuccess = ptkClient.LastFurnaceFromClientAt;
                _ptkSourceSid = ptkClient.Sid;

                if (PtkStatusEllipse != null)
                    PtkStatusEllipse.Fill = Brushes.Green;

                if (PtkTempText != null)
                {
                    string sign = _ptkLastValue >= 0 ? "+" : "";
                    PtkTempText.Text =
                        sign +
                        _ptkLastValue.ToString("0.000", CultureInfo.InvariantCulture) +
                        " °C";
                    PtkTempText.Foreground = Brushes.White;
                }
            }
            else
            {
                if (PtkTempText != null &&
                    _ptkLastSuccess != DateTime.MinValue &&
                    (now - _ptkLastSuccess).TotalSeconds > SensorStaleSeconds)
                {
                    PtkTempText.Text = "— °C";
                    PtkTempText.Foreground =
                        new SolidColorBrush(Color.FromRgb(0xCC, 0xDD, 0xEE));
                }
                if (PtkStatusEllipse != null)
                    PtkStatusEllipse.Fill = Brushes.Red;
            }
            UpdateSktbPanelVisibility();
        }

        private void UpdateSktbPanelVisibility()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(UpdateSktbPanelVisibility);
                return;
            }

            var now = DateTime.Now;

            bool platinaAlive =
                _platinaLastSuccess != DateTime.MinValue &&
                (now - _platinaLastSuccess).TotalSeconds <= SktbAllDeadSeconds;

            if (PlatinaRowBorder != null)
                PlatinaRowBorder.Visibility = platinaAlive
                    ? Visibility.Visible : Visibility.Collapsed;

            bool pdaAlive =
                _pdaLastSuccess != DateTime.MinValue &&
                (now - _pdaLastSuccess).TotalSeconds <= SktbAllDeadSeconds;

            if (PdaRowBorder != null)
                PdaRowBorder.Visibility = pdaAlive
                    ? Visibility.Visible : Visibility.Collapsed;

            bool ptkAlive =
                _ptkLastSuccess != DateTime.MinValue &&
                (now - _ptkLastSuccess).TotalSeconds <= SktbAllDeadSeconds;

            if (PtkRowBorder != null)
                PtkRowBorder.Visibility = ptkAlive
                    ? Visibility.Visible : Visibility.Collapsed;

            bool anyAlive = platinaAlive || pdaAlive || ptkAlive;

            if (PlatinaStatusEllipse != null)
                PlatinaStatusEllipse.Fill = anyAlive ? Brushes.Green : Brushes.Red;
        }

    }
}