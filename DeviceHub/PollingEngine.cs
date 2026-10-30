using NModbus;
using NModbus.Serial;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TERMINAL.DeviceHub
{
    public class PollingEngine
    {
        private CancellationTokenSource _cts;
        private List<Task> _pollingTasks = new List<Task>();

        public ConcurrentQueue<Action> UiUpdates { get; } = new ConcurrentQueue<Action>();
        public ConcurrentQueue<string> LogQueue { get; } = new ConcurrentQueue<string>();

        public async Task ScanAllAsync(ObservableCollection<InterfaceNode> interfaces, bool isRs485, string rs485Port, int rs485Baud, bool isI2c)
        {
            var scanTasks = new List<Task>();

            foreach (var iface in interfaces)
            {
                if (!iface.IsSelected) continue;

                if (iface.Name.Contains("RS-485") && isRs485)
                {
                    scanTasks.Add(Task.Run(() => ScanModbusAsync(iface, rs485Port, rs485Baud)));
                }
                else if (iface.Name.Contains("I2C") && isI2c)
                {
                    scanTasks.Add(Task.Run(() => ScanI2CAsync(iface)));
                }
            }

            await Task.WhenAll(scanTasks);
        }

        private async Task ScanModbusAsync(InterfaceNode iface, string portName, int baudRate)
        {
            SerialPort port = null;
            IModbusMaster master = null;

            try
            {
                LogQueue.Enqueue($"[Scan] Попытка открыть {portName} на {baudRate} бод...");

                port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                port.Open();
                LogQueue.Enqueue($"[Scan] Порт {portName} открыт. Начинаем перебор адресов...");

                var factory = new ModbusFactory();
                master = factory.CreateRtuMaster(port);
                master.Transport.ReadTimeout = 300; // Быстрый таймаут для перебора

                for (byte address = 1; address < 248; address++)
                {
                    try
                    {
                        master.ReadInputRegisters(address, 0, 1);

                        LogQueue.Enqueue($"[Scan] Найдено устройство по адресу {address}!");

                        var newDev = new DeviceNode
                        {
                            Name = $"Modbus датчик",
                            Address = address.ToString("D3")
                        };
                        newDev.Channels.Add(new ChannelNode { Name = "Давление", Unit = "бар" });
                        newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C" });

                        UiUpdates.Enqueue(() =>
                        {
                            if (!iface.Devices.Any(d => d.Address == newDev.Address))
                                iface.Devices.Add(newDev);
                        });
                    }
                    catch
                    {
                        // Таймаут — нормально, просто идем дальше
                    }
                }
                LogQueue.Enqueue($"[Scan] Сканирование {portName} завершено.");
            }
            catch (Exception ex)
            {
                LogQueue.Enqueue($"[Scan] Ошибка порта {portName}: {ex.Message}");
            }
            finally
            {
                master?.Dispose();
                if (port != null && port.IsOpen) port.Close();
            }
        }

        private async Task ScanI2CAsync(InterfaceNode iface)
        {
            await Task.Delay(1000); // Имитация поиска

            LogQueue.Enqueue("[Scan] I2C: Поиск завершен (заглушка).");
            UiUpdates.Enqueue(() =>
            {
                var dev = new DeviceNode { Name = "I2C датчик", Address = "017" };
                dev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C" });
                iface.Devices.Add(dev);
            });
        }

        public void StartPolling(ObservableCollection<InterfaceNode> interfaces, bool isRs485, string rs485Port, int rs485Baud, bool isI2c, int intervalMs = 500)
        {
            Stop();
            _cts = new CancellationTokenSource();
            _pollingTasks.Clear();

            foreach (var iface in interfaces)
            {
                if (!iface.IsSelected) continue;

                if (iface.Name.Contains("RS-485") && isRs485)
                {
                    var devices = iface.Devices.Where(d => d.IsSelected).ToList();
                    if (devices.Any())
                        _pollingTasks.Add(Task.Run(() => PollModbusLoop(rs485Port, rs485Baud, devices, intervalMs, _cts.Token)));
                }
            }
        }

        private async Task PollModbusLoop(string portName, int baudRate, List<DeviceNode> devices, int intervalMs, CancellationToken token)
        {
            SerialPort port = null;
            IModbusMaster master = null;

            try
            {
                port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                port.Open();
                var factory = new ModbusFactory();
                master = factory.CreateRtuMaster(port);
                master.Transport.ReadTimeout = 1000;

                LogQueue.Enqueue($"[Poll] Опрос {portName} запущен...");

                while (!token.IsCancellationRequested)
                {
                    foreach (var dev in devices)
                    {
                        if (token.IsCancellationRequested) break;
                        try
                        {
                            byte id = byte.Parse(dev.Address);
                            ushort[] regs = master.ReadInputRegisters(id, 0, 2);

                            if (regs.Length >= 2)
                            {
                                UiUpdates.Enqueue(() =>
                                {
                                    dev.Channels[0].Value = regs[0];
                                    dev.Channels[1].Value = regs[1];
                                });
                            }
                        }
                        catch { /* Пропускаем ошибку датчика */ }
                    }
                    await Task.Delay(intervalMs, token);
                }
            }
            catch (Exception ex)
            {
                LogQueue.Enqueue($"[Poll] Ошибка: {ex.Message}");
            }
            finally
            {
                master?.Dispose();
                if (port != null && port.IsOpen) port.Close();
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { Task.WaitAll(_pollingTasks.ToArray(), 1000); } catch { }
            _pollingTasks.Clear();
        }
    }
}