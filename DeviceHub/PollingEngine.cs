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

        private readonly Dictionary<string, SemaphoreSlim> _portLocks = new Dictionary<string, SemaphoreSlim>();
        private readonly object _portLocksSync = new object();
        private static readonly SemaphoreSlim _i2cLock = new SemaphoreSlim(1, 1);

        private SemaphoreSlim GetPortLock(string portName)
        {
            lock (_portLocksSync)
            {
                if (!_portLocks.TryGetValue(portName, out var sem))
                {
                    sem = new SemaphoreSlim(1, 1);
                    _portLocks[portName] = sem;
                }
                return sem;
            }
        }

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
                if (iface.IsSelected == false) continue;

                if (iface.Name.Contains("RS-485") && isRs485)
                {
                    foreach (var setting in rs485Settings)
                    {
                        var localSetting = setting;
                        scanTasks.Add(Task.Run(async () =>
                        {
                            var devices = await ScanModbusAsync(iface, localSetting.PortName, localSetting.BaudRate, token);
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
            var sem = GetPortLock(portName);

            try
            {
                await sem.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                return foundDevices;
            }

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
                        ushort[] idRegs = master.ReadHoldingRegisters(address, 0x00, 1);
                        ushort id = idRegs[0];
                        LogQueue.Enqueue($"[Scan] {portName}: Адрес {address}, ID = 0x{id:X4} (за {sw.ElapsedMilliseconds} мс)");

                        var newDev = new DeviceNode
                        {
                            Address = address.ToString("D3"),
                            PortName = portName
                        };

                        if (id == 0xDB80 || id == 0xDB81)
                        {
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
                sem.Release();
            }

            return foundDevices;
        }

        private async Task<List<DeviceNode>> ScanI2CAsync(InterfaceNode iface, CancellationToken token)
        {
            var foundDevices = new List<DeviceNode>();
            FT260_I2CDotNet.FT260 i2cMaster = null;

            try
            {
                await _i2cLock.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                return foundDevices;
            }

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
                i2cMaster?.Close();
                _i2cLock.Release();
            }

            return foundDevices;
        }

        public void StartPolling(ObservableCollection<InterfaceNode> interfaces, bool isRs485, List<PortSetting> rs485Settings, bool isI2c, int intervalMs = 500)
        {
            Stop();
            if (intervalMs < 50) intervalMs = 50;

            _cts = new CancellationTokenSource();
            _pollingTasks.Clear();

            foreach (var iface in interfaces)
            {
                if (iface.IsSelected == false) continue;

                if (iface.Name.Contains("RS-485") && isRs485)
                {
                    foreach (var setting in rs485Settings)
                    {
                        var localSetting = setting;
                        // Выбираем устройства, у которых галочка не снята (true или null)
                        var devices = iface.Devices.Where(d => d.IsSelected != false).ToList();
                        if (devices.Any())
                        {
                            _pollingTasks.Add(Task.Run(() => PollModbusLoop(localSetting.PortName, localSetting.BaudRate, devices, intervalMs, _cts.Token)));
                        }
                    }
                }
            }

            if (isI2c)
            {
                var i2cIface = interfaces.FirstOrDefault(i => i.Name.Contains("I2C"));
                if (i2cIface != null && i2cIface.IsSelected != false)
                {
                    var i2cDevices = i2cIface.Devices.Where(d => d.IsSelected != false).ToList();
                    if (i2cDevices.Any())
                    {
                        _pollingTasks.Add(Task.Run(() => PollI2CLoop(i2cDevices, intervalMs, _cts.Token)));
                    }
                }
            }

            LogQueue.Enqueue($"[Poll] Запущено задач опроса: {_pollingTasks.Count}");
        }

        private async Task PollModbusLoop(string portName, int baudRate, List<DeviceNode> devices, int intervalMs, CancellationToken token)
        {
            var sem = GetPortLock(portName);
            try { await sem.WaitAsync(token); }
            catch (OperationCanceledException) { return; }

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
                                        ch.Value = val;
                                    }
                                    else if (ch.IsInt32)
                                    {
                                        ushort[] regs = master.ReadInputRegisters(id, ch.RegisterAddress, 2);
                                        int raw = (regs[1] << 16) | regs[0];
                                        double val = Math.Round((double)raw);
                                        ch.LastValue = val;
                                        ch.Value = val;
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
                                        ch.Value = val;
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

                    try { await Task.Delay(intervalMs, token); }
                    catch (OperationCanceledException) { break; }
                }
            }
            catch (OperationCanceledException) { }
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
                sem.Release();
                LogQueue.Enqueue($"[Poll] {portName}: Опрос остановлен, порт освобождён.");
            }
        }

        private async Task PollI2CLoop(List<DeviceNode> devices, int intervalMs, CancellationToken token)
        {
            FT260_I2CDotNet.FT260 i2cMaster = null;
            try { await _i2cLock.WaitAsync(token); }
            catch (OperationCanceledException) { return; }

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
                                        ch.Value = val;
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
                                        ch.Value = val;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }

                    try { await Task.Delay(intervalMs, token); }
                    catch (OperationCanceledException) { break; }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogQueue.Enqueue($"[Poll] I2C: Ошибка: {ex.Message}");
            }
            finally
            {
                i2cMaster?.Close();
                _i2cLock.Release();
                LogQueue.Enqueue("[Poll] I2C: Опрос остановлен.");
            }
        }

        public void Stop()
        {
            var cts = _cts;
            if (cts == null)
            {
                _pollingTasks.Clear();
                return;
            }

            try { cts.Cancel(); } catch { }
            _cts = null;

            var tasks = _pollingTasks.ToArray();
            _pollingTasks.Clear();
            if (tasks.Length > 0)
            {
                try { Task.WaitAll(tasks, 5000); }
                catch (AggregateException) { }
            }

            try { cts.Dispose(); } catch { }
        }

        public void ClosePort()
        {
        }
    }
}