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

        public event Action<string, int, int> ScanProgressChanged;
        public event Action<DeviceNode> DeviceFound;

        private SerialPort _port;
        private IModbusMaster _master;
        private bool _isPortOpen = false;

        // --- МЕТОД СКАНИРОВАНИЯ ---

        public async Task<List<DeviceNode>> ScanAllAsync(
            ObservableCollection<InterfaceNode> interfaces,
            bool isRs485,
            List<PortSetting> rs485Settings,
            bool isI2c,
            CancellationToken token)
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
                            var devices = await ScanModbusAsync(iface, setting.PortName, setting.BaudRate, token);
                            lock (foundDevices) { foundDevices.AddRange(devices); }
                        }, token));
                    }
                }
                else if (iface.Name.Contains("I2C") && isI2c)
                {
                    scanTasks.Add(Task.Run(async () =>
                    {
                        var devices = await ScanI2CAsync(iface, token);
                        lock (foundDevices) { foundDevices.AddRange(devices); }
                    }, token));
                }
            }

            await Task.WhenAll(scanTasks);
            return foundDevices;
        }

        private async Task<List<DeviceNode>> ScanModbusAsync(InterfaceNode iface, string portName, int baudRate, CancellationToken token)
        {
            var foundDevices = new List<DeviceNode>();
            SerialPort port = null;

            try
            {
                LogQueue.Enqueue($"[Scan] {portName}: Попытка открыть на {baudRate} бод...");

                port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                port.ReadTimeout = 200;
                port.Open();
                var factory = new ModbusFactory();
                var master = factory.CreateRtuMaster(port);
                master.Transport.ReadTimeout = 200;
                master.Transport.Retries = 0;

                LogQueue.Enqueue($"[Scan] {portName}: Порт открыт. Начинаем перебор адресов...");

                int maxAddress = 247;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                for (byte address = 1; address <= maxAddress; address++)
                {
                    if (token.IsCancellationRequested) break;

                    sw.Restart();
                    ScanProgressChanged?.Invoke("RS-485", address, maxAddress);

                    try
                    {
                        // ★ СНАЧАЛА ЧИТАЕМ ID (Holding 0x00) ★
                        ushort[] idRegs = master.ReadHoldingRegisters(address, 0x00, 1);
                        ushort id = idRegs[0];
                        LogQueue.Enqueue($"[Scan] {portName}: Адрес {address}, ID = 0x{id:X4} (за {sw.ElapsedMilliseconds} мс)");

                        var newDev = new DeviceNode
                        {
                            Address = address.ToString("D3"),
                            PortName = portName
                        };

                        // ★ ОПРЕДЕЛЯЕМ ТИП ПО ID ★
                        if (id == 0xDB80 || id == 0xDB81)
                        {
                            // ★ DB80/DB81 ★
                            newDev.Name = $"[{portName}|DB80/DB81]";
                            int channelCount = 24;
                            if (id == 0xDB81) channelCount = 12;

                            for (int i = 0; i < channelCount; i++)
                            {
                                newDev.Channels.Add(new ChannelNode
                                {
                                    Name = $"Частота канала {i:D2}",
                                    Unit = "Гц",
                                    RegisterAddress = (ushort)(i * 4),
                                    Is32Bit = true,
                                    IsDouble = true
                                });
                            }
                        }
                        else if (id == 0xC002)
                        {
                            // ★ C002 ★
                            newDev.Name = $"[{portName}|C002]";
                            newDev.Channels.Add(new ChannelNode { Name = "Давление", Unit = "бар", RegisterAddress = 0x0000, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0002, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Частота давления", Unit = "Гц", RegisterAddress = 0x0008, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Частота температуры", Unit = "Гц", RegisterAddress = 0x000A, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Давление (фикс)", Unit = "", RegisterAddress = 0x0010, Is32Bit = true, IsInt32 = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Температура (фикс)", Unit = "", RegisterAddress = 0x0012, Is32Bit = true, IsInt32 = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Температура процессора", Unit = "°C", RegisterAddress = 0x0020, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Напряжение питания", Unit = "В", RegisterAddress = 0x0022, Is32Bit = true });
                        }
                        else if (id == 0xDBA0 || id == 0xDBA2)
                        {
                            // ★ DBA0/DBA2 ★
                            if (address == 1)
                            {
                                newDev.Name = $"[{portName}|DBA2]";
                                newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0002, Is32Bit = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Частота температуры", Unit = "Гц", RegisterAddress = 0x0006, Is32Bit = true });
                            }
                            else if (address == 2)
                            {
                                newDev.Name = $"[{portName}|DBA0]";
                                newDev.Channels.Add(new ChannelNode { Name = "Давление", Unit = "бар", RegisterAddress = 0x0000, Is32Bit = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0002, Is32Bit = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Частота давления", Unit = "Гц", RegisterAddress = 0x0004, Is32Bit = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Частота температуры", Unit = "Гц", RegisterAddress = 0x0006, Is32Bit = true });
                            }
                            else
                            {
                                newDev.Name = $"[{portName}|DBA{address}]";
                                newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0002, Is32Bit = true });
                            }
                        }
                        else
                        {
                            LogQueue.Enqueue($"[Scan] {portName}: Адрес {address} — неизвестный ID 0x{id:X4}. Пропускаем.");
                            continue;
                        }

                        foundDevices.Add(newDev);
                        DeviceFound?.Invoke(newDev);
                        LogQueue.Enqueue($"[Scan] {portName}: Устройство {address} ДОБАВЛЕНО (всего: {foundDevices.Count})");
                    }
                    catch (Exception ex)
                    {
                        if (sw.ElapsedMilliseconds > 200)
                        {
                            LogQueue.Enqueue($"[Scan] {portName}: Адрес {address} не отвечает (за {sw.ElapsedMilliseconds} мс)");
                        }
                    }
                }

                LogQueue.Enqueue($"[Scan] {portName}: Сканирование завершено. Найдено устройств: {foundDevices.Count}");
            }
            catch (Exception ex)
            {
                LogQueue.Enqueue($"[Scan] {portName}: Ошибка порта: {ex.Message}");
            }
            finally
            {
                if (port != null && port.IsOpen)
                {
                    try { port.DiscardInBuffer(); port.DiscardOutBuffer(); } catch { }
                    port.Close();
                }
                port?.Dispose();
            }

            return foundDevices;
        }
        private async Task<List<DeviceNode>> ScanI2CAsync(InterfaceNode iface, CancellationToken token)
        {
            var foundDevices = new List<DeviceNode>();
            FT260_I2CDotNet.FT260 i2cMaster = null;

            try
            {
                LogQueue.Enqueue("[Scan] I2C: Поиск устройств...");

                i2cMaster = new FT260_I2CDotNet.FT260();
                if (!i2cMaster.TryOpen())
                {
                    LogQueue.Enqueue("[Scan] I2C: Не удалось открыть FT260.");
                    return foundDevices;
                }

                int maxAddress = 0x77;
                int startAddress = 0x08;

                for (byte address = 0x08; address <= 0x77; address++)
                {
                    if (token.IsCancellationRequested) break;

                    ScanProgressChanged?.Invoke("I2C", address - startAddress + 1, maxAddress - startAddress + 1);

                    try
                    {
                        if (!i2cMaster.I2C_Detect(address)) continue;

                        LogQueue.Enqueue($"[Scan] I2C: ★ НАЙДЕНО ★ по адресу 0x{address:X2} (dec {address})");

                        var newDev = new DeviceNode
                        {
                            Name = $"[I2C|0x{address:X2}]",
                            Address = $"0x{address:X2}",
                            PortName = "I2C"
                        };

                        newDev.Channels.Add(new ChannelNode { Name = "Давление", Unit = "бар", RegisterAddress = 0x0000, Is32Bit = true });
                        newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0004, Is32Bit = true });
                        newDev.Channels.Add(new ChannelNode { Name = "Частота давления", Unit = "Гц", RegisterAddress = 0x0008, Is32Bit = true });
                        newDev.Channels.Add(new ChannelNode { Name = "Частота температуры", Unit = "Гц", RegisterAddress = 0x000C, Is32Bit = true });
                        newDev.Channels.Add(new ChannelNode { Name = "Температура процессора", Unit = "°C", RegisterAddress = 0x0026, Is32Bit = true });
                        newDev.Channels.Add(new ChannelNode { Name = "Напряжение питания", Unit = "В", RegisterAddress = 0x0027, Is32Bit = true });

                        foundDevices.Add(newDev);
                        DeviceFound?.Invoke(newDev);
                    }
                    catch { }
                }

                LogQueue.Enqueue($"[Scan] I2C: Сканирование завершено. Найдено устройств: {foundDevices.Count}");
            }
            catch (Exception ex)
            {
                LogQueue.Enqueue($"[Scan] I2C: Ошибка: {ex.Message}");
            }
            finally
            {
                // ★ ВСЕГДА ЗАКРЫВАЕМ FT260 ★
                i2cMaster?.Close();
            }

            return foundDevices;
        }        // --- МЕТОД ОПРОСА ---

        public void StartPolling(ObservableCollection<InterfaceNode> interfaces, bool isRs485, List<PortSetting> rs485Settings, bool isI2c, int intervalMs = 500)
        {
            Stop();
            Thread.Sleep(500);
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

            if (isI2c)
            {
                var i2cIface = interfaces.FirstOrDefault(i => i.Name.Contains("I2C"));
                if (i2cIface != null)
                {
                    var i2cDevices = i2cIface.Devices.Where(d => d.IsSelected).ToList();
                    if (i2cDevices.Any())
                    {
                        _pollingTasks.Add(Task.Run(() => PollI2CLoop(i2cDevices, intervalMs, _cts.Token)));
                    }
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
                port.ReadTimeout = 100;
                port.Open();
                var factory = new ModbusFactory();
                master = factory.CreateRtuMaster(port);
                master.Transport.ReadTimeout = 100;

                LogQueue.Enqueue($"[Poll] {portName}: Опрос запущен (таймаут 100 мс, период {intervalMs} мс)...");

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
                                    if (ch.IsDouble)
                                    {
                                        ushort[] regs = master.ReadInputRegisters(id, ch.RegisterAddress, 4);
                                        byte[] bytes = new byte[8];
                                        for (int i = 0; i < 4; i++)
                                        {
                                            bytes[i * 2] = (byte)(regs[i] & 0xFF);
                                            bytes[i * 2 + 1] = (byte)(regs[i] >> 8);
                                        }
                                        double val = BitConverter.ToDouble(bytes, 0);
                                        ch.LastValue = val;
                                        ch.IsValueSet = true; // ★ ВСЕГДА true ★
                                    }
                                    else if (ch.IsInt32)
                                    {
                                        ushort[] regs = master.ReadInputRegisters(id, ch.RegisterAddress, 2);
                                        int raw = (regs[1] << 16) | regs[0];
                                        double val = Math.Round((double)raw);
                                        ch.LastValue = val;
                                        ch.IsValueSet = true; // ★ ВСЕГДА true ★
                                    }
                                    else
                                    {
                                        ushort[] regs = master.ReadInputRegisters(id, ch.RegisterAddress, 2);
                                        byte[] bytes = new byte[4];
                                        bytes[0] = (byte)(regs[0] & 0xFF);
                                        bytes[1] = (byte)(regs[0] >> 8);
                                        bytes[2] = (byte)(regs[1] & 0xFF);
                                        bytes[3] = (byte)(regs[1] >> 8);
                                        float val = BitConverter.ToSingle(bytes, 0);
                                        ch.LastValue = val;
                                        ch.IsValueSet = true; // ★ ВСЕГДА true ★
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (!ex.Message.Contains("timed out"))
                                    {
                                        LogQueue.Enqueue($"[Debug] {portName}: Ошибка {ch.Name}: {ex.Message}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogQueue.Enqueue($"[Debug] {portName}: Ошибка устройства {dev.Address}: {ex.Message}");
                        }
                    }

                    await Task.Delay(intervalMs, token);
                }
            }
            catch (Exception ex)
            {
                LogQueue.Enqueue($"[Poll] {portName}: Ошибка: {ex.Message}");
            }
            finally
            {
                master?.Dispose();
                if (port != null && port.IsOpen)
                {
                    try { port.DiscardInBuffer(); port.DiscardOutBuffer(); } catch { }
                    port.Close();
                }
                port?.Dispose();
                LogQueue.Enqueue($"[Poll] {portName}: Опрос остановлен.");
            }
        }
        private async Task PollI2CLoop(List<DeviceNode> devices, int intervalMs, CancellationToken token)
        {
            FT260_I2CDotNet.FT260 i2cMaster = null;

            try
            {
                LogQueue.Enqueue("[Poll] I2C: Опрос запущен...");

                i2cMaster = new FT260_I2CDotNet.FT260();
                if (!i2cMaster.TryOpen())
                {
                    LogQueue.Enqueue("[Poll] I2C: Не удалось открыть FT260.");
                    return;
                }

                while (!token.IsCancellationRequested)
                {
                    foreach (var dev in devices)
                    {
                        if (token.IsCancellationRequested) break;

                        try
                        {
                            string addressStr = dev.Address.Replace("0x", "").Trim();
                            byte address = byte.Parse(addressStr, System.Globalization.NumberStyles.HexNumber);

                            foreach (var ch in dev.Channels)
                            {
                                if (!ch.IsSelected) continue;

                                try
                                {
                                    if (ch.IsDouble)
                                    {
                                        i2cMaster.Write(address, new byte[] { (byte)(ch.RegisterAddress & 0xFF) }, Start: true, Stop: false);
                                        i2cMaster.Read(address, out byte[] data, 8, RepStart: true, Stop: true);
                                        double val = BitConverter.ToDouble(data, 0);
                                        ch.LastValue = val;
                                        ch.IsValueSet = true; // ★ ВСЕГДА true ★
                                    }
                                    else
                                    {
                                        i2cMaster.Write(address, new byte[] { (byte)(ch.RegisterAddress & 0xFF) }, Start: true, Stop: false);
                                        i2cMaster.Read(address, out byte[] data, 4, RepStart: true, Stop: true);

                                        byte[] bytes = new byte[4];
                                        bytes[0] = data[0];
                                        bytes[1] = data[1];
                                        bytes[2] = data[2];
                                        bytes[3] = data[3];
                                        float val = BitConverter.ToSingle(bytes, 0);
                                        ch.LastValue = val;
                                        ch.IsValueSet = true; // ★ ВСЕГДА true ★
                                    }
                                }
                                catch
                                {
                                    // Игнорируем ошибки чтения
                                }
                            }
                        }
                        catch
                        {
                            // Игнорируем ошибки устройства
                        }
                    }
                    await Task.Delay(intervalMs, token);
                }
            }
            catch (Exception ex)
            {
                LogQueue.Enqueue($"[Poll] I2C: Ошибка: {ex.Message}");
            }
            finally
            {
                i2cMaster?.Close();
                LogQueue.Enqueue("[Poll] I2C: Опрос остановлен.");
            }
        }
        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            _pollingTasks.Clear();
            // ★ УБРАЛИ Task.WaitAll и Thread.Sleep ★
        }

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