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
        public event Action<ChannelNode, double> NewDataArrived;

        public ConcurrentQueue<Action> UiUpdates { get; } = new ConcurrentQueue<Action>();
        public ConcurrentQueue<string> LogQueue { get; } = new ConcurrentQueue<string>();

        // ★ ДОЛГОЖИВУЩИЙ ПОРТ ★
        private SerialPort _port;
        private IModbusMaster _master;
        private bool _isPortOpen = false;

        // --- МЕТОД СКАНИРОВАНИЯ ---

        public async Task<List<DeviceNode>> ScanAllAsync(ObservableCollection<InterfaceNode> interfaces, bool isRs485, List<PortSetting> rs485Settings, bool isI2c)
        {
            var foundDevices = new List<DeviceNode>();
            var scanTasks = new List<Task>();

            foreach (var iface in interfaces)
            {
                if (!iface.IsSelected) continue;

                if (iface.Name.Contains("RS-485") && isRs485)
                {
                    foreach (var setting in rs485Settings)
                    {
                        scanTasks.Add(Task.Run(async () =>
                        {
                            var devices = await ScanModbusAsync(iface, setting.PortName, setting.BaudRate);
                            lock (foundDevices)
                            {
                                foundDevices.AddRange(devices);
                            }
                        }));
                    }
                }
                else if (iface.Name.Contains("I2C") && isI2c)
                {
                    // I2C заглушка (если нужна)
                }
            }

            // ★ ЖДЕМ ЗАВЕРШЕНИЯ ВСЕХ ЗАДАЧ (без таймаута) ★
            await Task.WhenAll(scanTasks);

            return foundDevices;
        }

        private async Task<List<DeviceNode>> ScanModbusAsync(InterfaceNode iface, string portName, int baudRate)
        {
            var foundDevices = new List<DeviceNode>();
            try
            {
                LogQueue.Enqueue($"[Scan] Попытка открыть {portName} на {baudRate} бод...");

                // ★ ОТКРЫТИЕ ПОРТА (ЭТО НЕ КОММЕНТАРИЙ, А КОД!) ★
                if (_port == null || !_port.IsOpen)
                {
                    _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                    _port.Open();
                    var factory = new ModbusFactory();
                    _master = factory.CreateRtuMaster(_port);
                    _master.Transport.ReadTimeout = 300;
                    _isPortOpen = true;
                    LogQueue.Enqueue($"[Scan] Порт {portName} успешно открыт.");
                }
                else
                {
                    LogQueue.Enqueue($"[Scan] Порт {portName} уже открыт. Используем существующий.");
                }

                LogQueue.Enqueue($"[Scan] Порт {portName} открыт. Начинаем перебор адресов...");

                for (byte address = 1; address < 248; address++)
                {
                    try
                    {
                        ushort[] regs = _master.ReadInputRegisters(address, 0, 2);

                        LogQueue.Enqueue($"[Scan] [{portName}] Найдено устройство по адресу {address}! regs=[{regs[0]}, {regs[1]}]");

                        var newDev = new DeviceNode { Name = "Датчик давления СКТБ ЭЛПА", Address = address.ToString("D3") };

                        // ★ ДОБАВЛЕНИЕ КАНАЛОВ ★
                        newDev.Channels.Add(new ChannelNode { Name = "Давление", Unit = "бар", RegisterAddress = 0x0000, Is32Bit = true });
                        newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0002, Is32Bit = true });
                        newDev.Channels.Add(new ChannelNode { Name = "Частота давления", Unit = "Гц", RegisterAddress = 0x0008, Is32Bit = true });
                        newDev.Channels.Add(new ChannelNode { Name = "Частота температуры", Unit = "Гц", RegisterAddress = 0x000A, Is32Bit = true });
                        newDev.Channels.Add(new ChannelNode { Name = "Давление (фикс)", Unit = "", RegisterAddress = 0x0010, Is32Bit = true, IsInt32 = true });
                        newDev.Channels.Add(new ChannelNode { Name = "Температура (фикс)", Unit = "", RegisterAddress = 0x0012, Is32Bit = true, IsInt32 = true });
                        newDev.Channels.Add(new ChannelNode { Name = "Температура процессора", Unit = "°C", RegisterAddress = 0x0020, Is32Bit = true });
                        newDev.Channels.Add(new ChannelNode { Name = "Напряжение питания", Unit = "В", RegisterAddress = 0x0022, Is32Bit = true });

                        foundDevices.Add(newDev);

                        // ★ ПРЕРЫВАЕМ ПЕРЕБОР, ТАК КАК УСТРОЙСТВО НАЙДЕНО ★
                        break;
                    }
                    catch { /* Таймаут */ }
                }

                LogQueue.Enqueue($"[Scan] Сканирование {portName} завершено. Найдено устройств: {foundDevices.Count}");
            }
            catch (Exception ex)
            {
                LogQueue.Enqueue($"[Scan] Ошибка порта {portName}: {ex.Message}");
            }
            return foundDevices;
        }

        private async Task ScanI2CAsync(InterfaceNode iface)
        {
            await Task.Delay(1000);
            // I2C заглушка
        }

        // --- МЕТОД ОПРОСА ---

        public void StartPolling(ObservableCollection<InterfaceNode> interfaces, bool isRs485, List<PortSetting> rs485Settings, bool isI2c, int intervalMs = 500)
        {
            Stop();
            Thread.Sleep(1000);
            _cts = new CancellationTokenSource();
            _pollingTasks.Clear();

            foreach (var iface in interfaces)
            {
                if (!iface.IsSelected) continue;

                if (iface.Name.Contains("RS-485") && isRs485)
                {
                    foreach (var setting in rs485Settings)
                    {
                        var devices = iface.Devices.Where(d => d.IsSelected).ToList();
                        if (devices.Any())
                        {
                            _pollingTasks.Add(Task.Run(() => PollModbusLoop(setting.PortName, setting.BaudRate, devices, intervalMs, _cts.Token)));
                        }
                    }
                }
            }
        }

        private async Task PollModbusLoop(string portName, int baudRate, List<DeviceNode> devices, int intervalMs, CancellationToken token)
        {
            try
            {
                LogQueue.Enqueue($"[Poll] Опрос {portName} ({baudRate} бод) запущен...");

                while (!token.IsCancellationRequested)
                {
                    foreach (var dev in devices)
                    {
                        if (token.IsCancellationRequested) break;

                        try
                        {
                            byte id = byte.Parse(dev.Address);

                            foreach (var ch in dev.Channels)
                            {
                                if (!ch.IsSelected) continue;

                                try
                                {
                                    // Читаем 2 регистра (Input Registers)
                                    ushort[] regs = _master.ReadInputRegisters(id, ch.RegisterAddress, 2);

                                    if (ch.IsInt32)
                                    {
                                        // ФИКС. ТОЧКА (Int32)
                                        int raw = (regs[1] << 16) | regs[0];
                                        double val = Math.Round((double)raw);

                                        ch.LastValue = val;
                                        UiUpdates.Enqueue(() => ch.Value = val);
                                    }
                                    else
                                    {
                                        // FLOAT (Давление, Температура)
                                        byte[] bytes = new byte[4];
                                        bytes[0] = (byte)(regs[0] & 0xFF);
                                        bytes[1] = (byte)(regs[0] >> 8);
                                        bytes[2] = (byte)(regs[1] & 0xFF);
                                        bytes[3] = (byte)(regs[1] >> 8);

                                        float val = BitConverter.ToSingle(bytes, 0);

                                        ch.LastValue = val;
                                        UiUpdates.Enqueue(() => ch.Value = val);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogQueue.Enqueue($"[Debug] Ошибка чтения {ch.Name}: {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogQueue.Enqueue($"[Debug] Ошибка устройства {dev.Address}: {ex.Message}");
                        }
                    }
                    await Task.Delay(intervalMs, token);
                }
            }
            catch (Exception ex)
            {
                LogQueue.Enqueue($"[Poll] Ошибка {portName}: {ex.Message}");
            }
            finally
            {
                LogQueue.Enqueue($"[Poll] Опрос {portName} остановлен.");
            }
        }

        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                try { Task.WaitAll(_pollingTasks.ToArray(), 3000); } catch { }
                _cts.Dispose();
                _cts = null;
            }
            _pollingTasks.Clear();
            Thread.Sleep(200);
        }

        // ★ МЕТОД ЗАКРЫТИЯ ПОРТА (ВЫЗЫВАТЬ ПРИ ВЫХОДЕ) ★
        public void ClosePort()
        {
            if (_port != null)
            {
                _master?.Dispose();
                if (_port.IsOpen)
                {
                    try { _port.DiscardInBuffer(); _port.DiscardOutBuffer(); } catch { }
                    _port.Close();
                }
                _port.Dispose();
                _port = null;
                _isPortOpen = false;
            }
        }
    }
}