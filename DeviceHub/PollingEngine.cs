using NModbus;
using NModbus.Serial;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

        public event Action<ScanProgressInfo> ScanProgressChanged;
        public event Action<DeviceNode> DeviceFound;

        private readonly Dictionary<string, SemaphoreSlim> _portLocks = new Dictionary<string, SemaphoreSlim>();
        private readonly object _portLocksSync = new object();

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
            bool isRs485, List<PortSetting> rs485Settings,
            bool isUartEnabled, List<PortSetting> uartSettings, // ★ ДОБАВИЛИ UART
            List<string> selectedI2CDevicePaths,
            CancellationToken token)
        {
            var foundDevices = new List<DeviceNode>();
            var scanTasks = new List<Task>();

            foreach (var iface in interfaces)
            {
                if (iface.IsSelected == false) continue;

                // RS-485
                if (iface.Name.Contains("RS-485") && isRs485)
                {
                    foreach (var setting in rs485Settings)
                    {
                        var localSetting = setting;
                        scanTasks.Add(Task.Run(async () => {
                            var devices = await ScanModbusAsync(iface, localSetting.PortName, localSetting.BaudRate, false, token);
                            lock (foundDevices) { foundDevices.AddRange(devices); }
                        }, token));
                    }
                }
                // UART ★ НОВОЕ
                else if (iface.Name.Contains("UART") && isUartEnabled)
                {
                    foreach (var setting in uartSettings)
                    {
                        var localSetting = setting;
                        scanTasks.Add(Task.Run(async () => {
                            var devices = await ScanModbusAsync(iface, localSetting.PortName, localSetting.BaudRate, true, token);
                            lock (foundDevices) { foundDevices.AddRange(devices); }
                        }, token));
                    }
                }
                // I2C
                else if (iface.Name.Contains("I2C") && selectedI2CDevicePaths != null)
                {
                    foreach (var path in selectedI2CDevicePaths)
                    {
                        var localPath = path;
                        scanTasks.Add(Task.Run(async () => {
                            var devices = await ScanI2CAsync(iface, localPath, token);
                            lock (foundDevices) { foundDevices.AddRange(devices); }
                        }, token));
                    }
                }
            }

            await Task.WhenAll(scanTasks);
            return foundDevices;
        }
        private async Task<List<DeviceNode>> ScanModbusAsync(InterfaceNode iface, string portName, int baudRate, bool isUart, CancellationToken token)
        {
            var foundDevices = new List<DeviceNode>();
            SerialPort port = null;
            var sem = GetPortLock(portName);

            try { await sem.WaitAsync(token); }
            catch (OperationCanceledException) { return foundDevices; }

            try
            {
                LogQueue.Enqueue($"[Scan] {portName}: Попытка открыть на {baudRate} бод...");
                port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                port.ReadTimeout = 200;

                // Если это UART, принудительно включаем RTS (для USB-UART переходников)
                if (isUart) { port.RtsEnable = true; port.DtrEnable = true; }

                port.Open();
                var factory = new ModbusFactory();
                var master = factory.CreateRtuMaster(port);
                master.Transport.ReadTimeout = 200;
                master.Transport.Retries = 0;

                LogQueue.Enqueue($"[Scan] {portName}: Порт открыт. Начинаем перебор адресов...");

                int maxAddress = 247;
                for (byte address = 1; address <= maxAddress; address++)
                {
                    if (token.IsCancellationRequested) break;

                    try
                    {
                        ushort[] idRegs = master.ReadHoldingRegisters(address, 0x00, 1);
                        ushort id = idRegs[0];

                        var newDev = new DeviceNode { Address = address.ToString("D3"), PortName = portName, Name = $"[{portName}|0x{id:X4}]" };

                        if (id == 0xC002)
                        {
                            if (isUart)
                            {
                                // ★ КАРТА ДЛЯ UART (Ц2)
                                newDev.Channels.Add(new ChannelNode { Name = "Давление", Unit = "бар", RegisterAddress = 0x0000, IsFloat = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0002, IsFloat = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Частота давления", Unit = "Гц", RegisterAddress = 0x0008, IsFloat = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Частота температуры", Unit = "Гц", RegisterAddress = 0x000A, IsFloat = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Давление (фикс)", Unit = "", RegisterAddress = 0x0010, IsInt32 = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Температура (фикс)", Unit = "", RegisterAddress = 0x0012, IsInt32 = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Температура процессора", Unit = "°C", RegisterAddress = 0x0020, IsFloat = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Напряжение питания", Unit = "В", RegisterAddress = 0x0022, IsFloat = true });
                            }
                            else
                            {
                                // ★ КАРТА ДЛЯ RS-485 (Ц1)
                                newDev.Channels.Add(new ChannelNode { Name = "Давление", Unit = "бар", RegisterAddress = 0x0000, IsFloat = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0002, IsFloat = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Частота давления", Unit = "Гц", RegisterAddress = 0x0008, IsFloat = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Частота температуры", Unit = "Гц", RegisterAddress = 0x000A, IsFloat = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Давление (фикс)", Unit = "", RegisterAddress = 0x0010, IsInt32 = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Температура (фикс)", Unit = "", RegisterAddress = 0x0012, IsInt32 = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Температура процессора", Unit = "°C", RegisterAddress = 0x0020, IsFloat = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Напряжение питания", Unit = "В", RegisterAddress = 0x0022, IsFloat = true });
                            }

                            foundDevices.Add(newDev);
                            DeviceFound?.Invoke(newDev);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { LogQueue.Enqueue($"[Scan] {portName}: Ошибка порта: {ex.Message}"); }
            finally
            {
                if (port != null && port.IsOpen) { try { port.Close(); } catch { } }
                port?.Dispose();
                sem.Release();
            }
            return foundDevices;
        }
        private async Task<List<DeviceNode>> ScanI2CAsync(InterfaceNode iface, string devicePath, CancellationToken token)
        {
            var foundDevices = new List<DeviceNode>();
            IntPtr i2cHandle = IntPtr.Zero;

            try
            {
                // Извлекаем уникальный ID устройства (например, 7&37e6d6f6&0&0000)
                string shortName = devicePath;
                string[] pathParts = devicePath.Split('#');
                if (pathParts.Length >= 3)
                {
                    // В пути \\?\hid#vid_0403&pid_6030#7&37e6d6f6&0&0000#{...} 
                    // ID находится под индексом 2
                    shortName = pathParts[2];
                }
                else if (shortName.Length > 20)
                {
                    shortName = shortName.Substring(0, 20) + "...";
                }
                LogQueue.Enqueue($"[Scan] I2C ({shortName}): Поиск устройств...");

                int res = FT260Native.FT260_OpenByDevicePath(devicePath, out i2cHandle);
                if (res != FT260Native.FT260_OK)
                {
                    LogQueue.Enqueue($"[Scan] I2C ({shortName}): Не удалось открыть FT260 (код ошибки: {res}).");
                    return foundDevices;
                }

                FT260Native.FT260_I2CMaster_Init(i2cHandle, 100);
                LogQueue.Enqueue($"[Scan] I2C: Открыт переходник: {shortName}");

                int maxAddress = 0x77;
                int startAddress = 0x08;

                var progress = new ScanProgressInfo
                {
                    PortName = $"I2C ({shortName})",
                    Total = maxAddress - startAddress + 1,
                    Current = 0,
                    StatusText = "Запуск..."
                };
                ScanProgressChanged?.Invoke(progress);

                for (byte address = 0x08; address <= 0x77; address++)
                {
                    if (token.IsCancellationRequested) break;

                    progress.Current = address - startAddress + 1;
                    progress.StatusText = $"Адрес 0x{address:X2}";
                    ScanProgressChanged?.Invoke(progress);

                    try
                    {
                        byte[] regAddr = new byte[] { 0x80 };
                        int writeRes = FT260Native.FT260_I2CMaster_Write(i2cHandle, address, FT260Native.FT260_I2C_START, regAddr, 1, out _);
                        if (writeRes != FT260Native.FT260_OK) continue;

                        byte[] idData = new byte[2];
                        // ★ Добавлен последний параметр 5000 (таймаут 5 секунд)
                        int readRes = FT260Native.FT260_I2CMaster_Read(i2cHandle, address, FT260Native.FT260_I2C_START_AND_STOP, idData, 2, out ushort transferred, 5000);
                        if (readRes != FT260Native.FT260_OK || transferred != 2) continue;

                        ushort deviceIdRead = (ushort)((idData[1] << 8) | idData[0]);

                        if (deviceIdRead != 0xC002 && deviceIdRead != 0xDBA0 && deviceIdRead != 0xDBA2 &&
                            deviceIdRead != 0xDB80 && deviceIdRead != 0xDB81) continue;

                        LogQueue.Enqueue($"[Scan] I2C ({shortName}): ★ НАЙДЕНО ★ по адресу 0x{address:X2} (ID: 0x{deviceIdRead:X4})");

                        var newDev = new DeviceNode
                        {
                            Name = $"[{shortName}|0x{deviceIdRead:X4}]",
                            Address = address.ToString("D3"),
                            PortName = "I2C",        // ★ Явно указываем "I2C"
                            DevicePath = devicePath  // ★ Сохраняем полный путь для опроса
                        };

                        if (deviceIdRead == 0xDB80 || deviceIdRead == 0xDB81)
                        {
                            int channelCount = (deviceIdRead == 0xDB81) ? 12 : 24;
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
                        else
                        {
                            newDev.Channels.Add(new ChannelNode { Name = "Давление", Unit = "бар", RegisterAddress = 0x0000, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0004, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Частота давления", Unit = "Гц", RegisterAddress = 0x0008, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Частота температуры", Unit = "Гц", RegisterAddress = 0x000C, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Температура процессора", Unit = "°C", RegisterAddress = 0x0026, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Напряжение питания", Unit = "В", RegisterAddress = 0x0027, Is32Bit = true });
                        }

                        foundDevices.Add(newDev);
                        DeviceFound?.Invoke(newDev);
                    }
                    catch { }
                }

                progress.StatusText = "Готово";
                ScanProgressChanged?.Invoke(progress);
            }
            catch (Exception ex)
            {
                LogQueue.Enqueue($"[Scan] I2C ({devicePath}): Ошибка: {ex.Message}");
            }
            finally
            {
                if (i2cHandle != IntPtr.Zero)
                {
                    FT260Native.FT260_Close(i2cHandle);
                }
            }

            return foundDevices;
        }

        public void StartPolling(
            ObservableCollection<InterfaceNode> interfaces,
            bool isRs485,
            List<PortSetting> rs485Settings,
            bool isUartEnabled,
            List<PortSetting> uartSettings,
            List<string> selectedI2CDevicePaths,
            int intervalMs = 500)
        {
            Stop();
            if (intervalMs < 50) intervalMs = 50;

            _cts = new CancellationTokenSource();
            _pollingTasks.Clear();

            // ★ СОБИРАЕМ ВСЕ ПОРТЫ В ОДИН СПИСОК ★
            var allTasks = new List<(string PortName, int BaudRate, List<DeviceNode> Devices, bool IsUart)>();

            foreach (var iface in interfaces)
            {
                if (iface.IsSelected == false) continue;

                if (iface.Name.Contains("RS-485") && isRs485)
                {
                    foreach (var setting in rs485Settings)
                    {
                        var devices = iface.Devices.Where(d => d.IsSelected != false).ToList();
                        if (devices.Any()) allTasks.Add((setting.PortName, setting.BaudRate, devices, false));
                    }
                }
                else if (iface.Name.Contains("UART") && isUartEnabled && uartSettings != null)
                {
                    foreach (var setting in uartSettings)
                    {
                        var devices = iface.Devices.Where(d => d.IsSelected != false).ToList();
                        if (devices.Any()) allTasks.Add((setting.PortName, setting.BaudRate, devices, true));
                    }
                }
            }

            // Запускаем ОДИН поток, который будет опрашивать все порты строго по очереди
            if (allTasks.Any())
            {
                _pollingTasks.Add(Task.Run(async () =>
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        foreach (var task in allTasks)
                        {
                            if (_cts.Token.IsCancellationRequested) break;
                            await PollSinglePortOnce(task.PortName, task.BaudRate, task.Devices, task.IsUart, _cts.Token);
                            await Task.Delay(50, _cts.Token); // Пауза между разными портами
                        }
                        await Task.Delay(intervalMs, _cts.Token); // Пауза перед следующим кругом
                    }
                }, _cts.Token));
            }

            // ★ I2C оставляем как было (он работает через FT260, ему параллельность не мешает) ★
            if (selectedI2CDevicePaths != null && selectedI2CDevicePaths.Count > 0)
            {
                var i2cIface = interfaces.FirstOrDefault(i => i.Name.Contains("I2C"));
                if (i2cIface != null && i2cIface.IsSelected != false)
                {
                    foreach (var path in selectedI2CDevicePaths)
                    {
                        var localPath = path;
                        var i2cDevices = i2cIface.Devices
                            .Where(d => d.IsSelected != false && d.DevicePath == localPath)
                            .ToList();

                        if (i2cDevices.Any())
                        {
                            _pollingTasks.Add(PollI2CLoop(localPath, i2cDevices, intervalMs, _cts.Token));
                        }
                    }
                }
            }

            LogQueue.Enqueue($"[Poll] Запущен последовательный опрос портов: {allTasks.Count}");
        }

        private async Task PollSinglePortOnce(string portName, int baudRate, List<DeviceNode> devices, bool isUart, CancellationToken token)
        {
            var sem = GetPortLock(portName);
            try { await sem.WaitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            SerialPort port = null;
            IModbusMaster master = null;

            try
            {
                port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                port.ReadTimeout = 250; // Таймаут 250 мс

                if (isUart)
                {
                    port.RtsEnable = true;
                    port.DtrEnable = true;
                    port.Handshake = Handshake.None;
                }

                await Task.Run(() => port.Open(), token).ConfigureAwait(false);

                var factory = new ModbusFactory();
                master = factory.CreateRtuMaster(port);
                master.Transport.ReadTimeout = 250;
                master.Transport.Retries = 0;

                foreach (var dev in devices)
                {
                    if (token.IsCancellationRequested) break;

                    var activeChannels = dev.Channels.Where(c => c.IsSelected).ToList();
                    byte id = byte.Parse(dev.Address);

                    // ★ ЧИТАЕМ КАЖДЫЙ КАНАЛ ОТДЕЛЬНО (обходим дыры в памяти) ★
                    foreach (var ch in activeChannels)
                    {
                        if (token.IsCancellationRequested) break;

                        try
                        {
                            ushort qty = ch.IsDouble ? (ushort)4 : (ushort)2;
                            ushort[] regs = master.ReadInputRegisters(id, ch.RegisterAddress, qty);

                            if (ch.IsDouble)
                            {
                                byte[] bytes = new byte[8];
                                for (int i = 0; i < 4; i++) { bytes[i * 2] = (byte)(regs[i] & 0xFF); bytes[i * 2 + 1] = (byte)(regs[i] >> 8); }
                                ch.Value = BitConverter.ToDouble(bytes, 0);
                            }
                            else if (ch.IsInt32)
                            {
                                // ★ ВАРИАНТ 2: CDAB (Младший регистр сдвигаем в старшие биты)
                                int raw = (regs[1] << 16) | regs[0];
                                ch.Value = raw;
                            }
                            else if (ch.IsFloat)
                            {
                                ch.Value = ReadFloat32(regs, 0);
                            }
                            else
                            {
                                ch.Value = regs[0];
                            }
                        }
                        catch (NModbus.SlaveException ex) when (ex.Message.Contains("Exception Code: 2"))
                        {
                            // Если пришел Exception 2 (нет адреса) - ставим NaN и идем дальше.
                            // Галочку НЕ снимаем, чтобы при перезапуске сканирования датчик остался.
                            ch.Value = double.NaN;
                        }
                        catch (Exception)
                        {
                            // При таймауте тоже ставим NaN, не ломая график
                            ch.Value = double.NaN;
                        }
                    }

                    // Небольшая пауза между устройствами на порту
                    await Task.Delay(50, token);
                }
            }
            catch (Exception ex)
            {
                LogQueue.Enqueue($"[Poll] {portName}: Критическая ошибка: {ex.Message}");
            }
            finally
            {
                master?.Dispose();
                if (port != null && port.IsOpen) { try { port.Close(); } catch { } }
                port?.Dispose();
                sem.Release();
            }
        }        //{
        //    var sem = GetPortLock(portName);
        //    try { await sem.WaitAsync(token).ConfigureAwait(false); }
        //    catch (OperationCanceledException) { return; }

        //    SerialPort port = null;
        //    IModbusMaster master = null;
        //    var errorCounters = new Dictionary<ChannelNode, int>();

        //    try
        //    {
        //        port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
        //        port.ReadTimeout = 350; // Таймаут 350 мс

        //        // ★ НАСТРОЙКА ДЛЯ UART: принудительно поднимаем RTS и DTR ★
        //        if (isUart)
        //        {
        //            port.RtsEnable = true;
        //            port.DtrEnable = true;
        //            port.Handshake = Handshake.None;
        //        }

        //        await Task.Run(() => port.Open(), token).ConfigureAwait(false);

        //        var factory = new ModbusFactory();
        //        master = factory.CreateRtuMaster(port);
        //        master.Transport.ReadTimeout = 350;
        //        master.Transport.Retries = 0;

        //        LogQueue.Enqueue($"[Poll] {portName}: Опрос запущен...");

        //        while (!token.IsCancellationRequested)
        //        {
        //            foreach (var dev in devices)
        //            {
        //                if (token.IsCancellationRequested) break;

        //                var activeChannels = dev.Channels.Where(c => c.IsSelected).ToList();
        //                if (activeChannels.Count == 0) continue;

        //                byte id = byte.Parse(dev.Address);

        //                // Группируем каналы в блоки, чтобы читать одним запросом
        //                var sorted = activeChannels
        //                    .Select(c => new { Ch = c, Size = c.IsDouble ? 4 : (c.IsFloat || c.IsInt32 ? 2 : 1) })
        //                    .OrderBy(x => x.Ch.RegisterAddress)
        //                    .ToList();

        //                var blocks = new List<(ushort Start, ushort End, List<ChannelNode> Channels)>();
        //                foreach (var item in sorted)
        //                {
        //                    ushort start = item.Ch.RegisterAddress;
        //                    ushort end = (ushort)(start + item.Size - 1);

        //                    if (blocks.Count > 0 && start <= blocks[blocks.Count - 1].End + 1)
        //                    {
        //                        var last = blocks[blocks.Count - 1];
        //                        if (end > last.End) last.End = end;
        //                        last.Channels.Add(item.Ch);
        //                        blocks[blocks.Count - 1] = last;
        //                    }
        //                    else
        //                    {
        //                        blocks.Add((start, end, new List<ChannelNode> { item.Ch }));
        //                    }
        //                }

        //                foreach (var block in blocks)
        //                {
        //                    if (token.IsCancellationRequested) break;
        //                    ushort qty = (ushort)(block.End - block.Start + 1);

        //                    try
        //                    {
        //                        ushort[] blockRegs = master.ReadInputRegisters(id, block.Start, qty);

        //                        foreach (var ch in block.Channels)
        //                        {
        //                            int offset = ch.RegisterAddress - block.Start;

        //                            if (ch.IsDouble)
        //                            {
        //                                byte[] bytes = new byte[8];
        //                                for (int i = 0; i < 4; i++) { bytes[i * 2] = (byte)(blockRegs[offset + i] & 0xFF); bytes[i * 2 + 1] = (byte)(blockRegs[offset + i] >> 8); }
        //                                ch.Value = BitConverter.ToDouble(bytes, 0);
        //                            }
        //                            else if (ch.IsInt32)
        //                            {
        //                                // Правильная склейка Int32
        //                                byte[] rawBytes = new byte[4];
        //                                rawBytes[0] = (byte)(blockRegs[offset] >> 8); rawBytes[1] = (byte)(blockRegs[offset] & 0xFF);
        //                                rawBytes[2] = (byte)(blockRegs[offset + 1] >> 8); rawBytes[3] = (byte)(blockRegs[offset + 1] & 0xFF);
        //                                ch.Value = BitConverter.ToInt32(rawBytes, 0);
        //                            }
        //                            else if (ch.IsFloat)
        //                            {
        //                                ch.Value = ReadFloat32(blockRegs, offset);
        //                            }
        //                            else
        //                            {
        //                                ch.Value = blockRegs[offset];
        //                            }

        //                            if (errorCounters.ContainsKey(ch)) errorCounters[ch] = 0;
        //                        }
        //                    }
        //                    catch (NModbus.SlaveException ex) when (ex.Message.Contains("Exception Code: 2"))
        //                    {
        //                        // Если прибор сказал "Нет такого адреса", отключаем канал (после 3-х раз)
        //                        foreach (var ch in block.Channels)
        //                        {
        //                            ch.Value = double.NaN;
        //                            if (!errorCounters.ContainsKey(ch)) errorCounters[ch] = 0;
        //                            errorCounters[ch]++;

        //                            if (errorCounters[ch] >= 3)
        //                            {
        //                                UiUpdates.Enqueue(() => {
        //                                    ch.IsSelected = false;
        //                                    LogQueue.Enqueue($"[Fix] {portName} dev {dev.Address}: {ch.Name} отсутствует, отключен.");
        //                                });
        //                            }
        //                        }
        //                    }
        //                    catch (Exception)
        //                    {
        //                        foreach (var ch in block.Channels) ch.Value = double.NaN;
        //                    }
        //                }

        //                // ★ ВАЖНО: Пауза 200 мс между устройствами, чтобы порт "остыл" ★
        //                await Task.Delay(200, token);
        //            }

        //            try { await Task.Delay(intervalMs, token).ConfigureAwait(false); }
        //            catch (OperationCanceledException) { break; }
        //        }
        //    }
        //    catch (Exception ex) { LogQueue.Enqueue($"[Poll] {portName}: Критическая ошибка: {ex.Message}"); }
        //    finally
        //    {
        //        master?.Dispose();
        //        if (port != null && port.IsOpen) { try { port.Close(); } catch { } }
        //        port?.Dispose();
        //        sem.Release();
        //        LogQueue.Enqueue($"[Poll] {portName}: Опрос остановлен.");
        //    }
        //}
        private async Task PollI2CLoop(string devicePath, List<DeviceNode> devices, int intervalMs, CancellationToken token)
        {
            IntPtr i2cHandle = IntPtr.Zero;
            // Извлекаем уникальный ID устройства (например, 7&37e6d6f6&0&0000)
            string shortName = devicePath;
            string[] pathParts = devicePath.Split('#');
            if (pathParts.Length >= 3)
            {
                // В пути \\?\hid#vid_0403&pid_6030#7&37e6d6f6&0&0000#{...} 
                // ID находится под индексом 2
                shortName = pathParts[2];
            }
            else if (shortName.Length > 20)
            {
                shortName = shortName.Substring(0, 20) + "...";
            }

            try
            {
                LogQueue.Enqueue($"[Poll] I2C ({shortName}): Опрос запущен...");

                int res = FT260Native.FT260_OpenByDevicePath(devicePath, out i2cHandle);
                if (res != FT260Native.FT260_OK)
                {
                    LogQueue.Enqueue($"[Poll] I2C ({shortName}): Не удалось открыть FT260 (ошибка {res}).");
                    return;
                }

                FT260Native.FT260_I2CMaster_Init(i2cHandle, 100);

                while (!token.IsCancellationRequested)
                {
                    foreach (var dev in devices)
                    {
                        if (token.IsCancellationRequested) break;

                        try
                        {
                            byte address = byte.Parse(dev.Address.Trim());
                            var activeChannels = dev.Channels.Where(c => c.IsSelected).ToList();

                            foreach (var ch in activeChannels)
                            {
                                try
                                {
                                    byte[] regAddr = new byte[] { (byte)(ch.RegisterAddress & 0xFF) };
                                    FT260Native.FT260_I2CMaster_Write(i2cHandle, address, FT260Native.FT260_I2C_START, regAddr, 1, out _);

                                    if (ch.IsDouble)
                                    {
                                        byte[] data = new byte[8];
                                        FT260Native.FT260_I2CMaster_Read(i2cHandle, address, FT260Native.FT260_I2C_START_AND_STOP, data, 8, out _, 5000);
                                        double val = BitConverter.ToDouble(data, 0);
                                        ch.LastValue = val;
                                        ch.Value = val;
                                    }
                                    else
                                    {
                                        byte[] data = new byte[4];
                                        FT260Native.FT260_I2CMaster_Read(i2cHandle, address, FT260Native.FT260_I2C_START_AND_STOP, data, 4, out _, 5000);
                                        float val = BitConverter.ToSingle(data, 0);
                                        ch.LastValue = val;
                                        ch.Value = val;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }

                    try { await Task.Delay(intervalMs, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogQueue.Enqueue($"[Poll] I2C ({shortName}): Ошибка: {ex.Message}");
            }
            finally
            {
                if (i2cHandle != IntPtr.Zero)
                {
                    FT260Native.FT260_Close(i2cHandle);
                }
                LogQueue.Enqueue($"[Poll] I2C ({shortName}): Опрос остановлен.");
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

            try { cts.Dispose(); } catch { }
        }

        public void ClosePort()
        {
        }

        // ===================== FLOAT BYTE ORDER HELPERS =====================

        public enum FloatByteOrder
        {
            ABCD, // big-endian:      R0_hi R0_lo R1_hi R1_lo
            CDAB, // word-swap:       R1_hi R1_lo R0_hi R0_lo
            BADC, // byte-swap:       R0_lo R0_hi R1_lo R1_hi
            DCBA  // little-endian:   R1_lo R1_hi R0_lo R0_hi
        }

        /// <summary>
        /// Порядок байт для 32-bit float в регистрах Modbus.
        /// Меняй здесь, пока не получишь осмысленное значение на живом устройстве.
        /// </summary>
        public static FloatByteOrder FloatOrder = FloatByteOrder.BADC;

        private static float ReadFloat32(ushort[] regs, int offset)
        {
            ushort r0 = regs[offset];
            ushort r1 = regs[offset + 1];
            byte[] b = new byte[4];

            switch (FloatOrder)
            {
                case FloatByteOrder.ABCD:
                    b[0] = (byte)(r0 >> 8); b[1] = (byte)(r0 & 0xFF);
                    b[2] = (byte)(r1 >> 8); b[3] = (byte)(r1 & 0xFF);
                    break;
                case FloatByteOrder.CDAB:
                    b[0] = (byte)(r1 >> 8); b[1] = (byte)(r1 & 0xFF);
                    b[2] = (byte)(r0 >> 8); b[3] = (byte)(r0 & 0xFF);
                    break;
                case FloatByteOrder.BADC:
                    b[0] = (byte)(r0 & 0xFF); b[1] = (byte)(r0 >> 8);
                    b[2] = (byte)(r1 & 0xFF); b[3] = (byte)(r1 >> 8);
                    break;
                case FloatByteOrder.DCBA:
                default:
                    b[0] = (byte)(r1 & 0xFF); b[1] = (byte)(r1 >> 8);
                    b[2] = (byte)(r0 & 0xFF); b[3] = (byte)(r0 >> 8);
                    break;
            }
            return BitConverter.ToSingle(b, 0);
        }
    }

    public class ScanProgressInfo : INotifyPropertyChanged
    {
        private int _current;
        private int _total;
        private string _statusText;

        public string PortName { get; set; }

        public int Current
        {
            get => _current;
            set { _current = value; OnPropertyChanged(nameof(Current)); }
        }

        public int Total
        {
            get => _total;
            set { _total = value; OnPropertyChanged(nameof(Total)); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}