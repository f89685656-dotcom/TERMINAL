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

        // ★ ИЗМЕНЕНО: Теперь передаем объект с именем порта, текущим и общим количеством
        public event Action<ScanProgressInfo> ScanProgressChanged;
        public event Action<DeviceNode> DeviceFound;

        private readonly Dictionary<string, SemaphoreSlim> _portLocks = new Dictionary<string, SemaphoreSlim>();
        private readonly object _portLocksSync = new object();

        private readonly Dictionary<string, SemaphoreSlim> _i2cLocks = new Dictionary<string, SemaphoreSlim>();
        private readonly object _i2cLocksSync = new object();

        private SemaphoreSlim GetI2CLock(string deviceId)
        {
            lock (_i2cLocksSync)
            {
                if (!_i2cLocks.TryGetValue(deviceId, out var sem))
                {
                    sem = new SemaphoreSlim(1, 1);
                    _i2cLocks[deviceId] = sem;
                }
                return sem;
            }
        }

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
            List<string> selectedI2CDevices, // ★ Заменили bool isI2c на список
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
                else if (iface.Name.Contains("I2C") && selectedI2CDevices != null)
                {
                    // ★ Запускаем задачу для каждого выбранного переходника
                    foreach (var deviceId in selectedI2CDevices)
                    {
                        var localId = deviceId;
                        scanTasks.Add(Task.Run(async () =>
                        {
                            var devices = await ScanI2CAsync(iface, localId, token);
                            lock (foundDevices) { foundDevices.AddRange(devices); }
                        }, token));
                    }
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

            try { await sem.WaitAsync(token); }
            catch (OperationCanceledException) { return foundDevices; }

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

                // ★ Создаем объект прогресса для этого порта
                var progress = new ScanProgressInfo
                {
                    PortName = $"{portName} ({baudRate} бод)",
                    Total = maxAddress,
                    Current = 0,
                    StatusText = "Запуск..."
                };
                ScanProgressChanged?.Invoke(progress);

                for (byte address = 1; address <= maxAddress; address++)
                {
                    if (token.IsCancellationRequested) break;

                    sw.Restart();

                    // ★ Обновляем прогресс для этого порта
                    progress.Current = address;
                    progress.StatusText = $"Адрес {address}";
                    ScanProgressChanged?.Invoke(progress);

                    try
                    {
                        ushort[] idRegs = master.ReadHoldingRegisters(address, 0x00, 1);
                        ushort id = idRegs[0];
                        LogQueue.Enqueue($"[Scan] {portName}: Адрес {address}, ID = 0x{id:X4} (за {sw.ElapsedMilliseconds} мс)");

                        var newDev = new DeviceNode
                        {
                            Address = address.ToString("D3"),
                            PortName = portName,
                            Name = $"[{portName}|0x{id:X4}]"
                        };

                        if (id == 0xDB80 || id == 0xDB81)
                        {
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
                                newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0002, Is32Bit = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Частота температуры", Unit = "Гц", RegisterAddress = 0x0006, Is32Bit = true });
                            }
                            else if (address == 2)
                            {
                                newDev.Channels.Add(new ChannelNode { Name = "Давление", Unit = "бар", RegisterAddress = 0x0000, Is32Bit = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0002, Is32Bit = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Частота давления", Unit = "Гц", RegisterAddress = 0x0004, Is32Bit = true });
                                newDev.Channels.Add(new ChannelNode { Name = "Частота температуры", Unit = "Гц", RegisterAddress = 0x0006, Is32Bit = true });
                            }
                            else
                            {
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

                progress.StatusText = "Готово";
                ScanProgressChanged?.Invoke(progress);

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
        private async Task<List<DeviceNode>> ScanI2CAsync(InterfaceNode iface, string adapterId, CancellationToken token)
        {
            var foundDevices = new List<DeviceNode>();
            FT260_I2CDotNet.FT260 i2cMaster = null;

            // ★ Берем лок именно для этого адаптера
            var sem = GetI2CLock(adapterId);

            try { await sem.WaitAsync(token); }
            catch (OperationCanceledException) { return foundDevices; }

            try
            {
                LogQueue.Enqueue($"[Scan] I2C ({adapterId}): Поиск устройств...");
                i2cMaster = new FT260_I2CDotNet.FT260();

                // ★ ВАЖНО: Если библиотека поддерживает открытие по ID, используйте его:
                // if (!i2cMaster.TryOpen(adapterId)) 
                // Пока оставляем базовый TryOpen (если библиотека не умеет по ID, 
                // параллельный опрос не заработает, но ошибка компиляции уйдет).
                if (!i2cMaster.TryOpen())
                {
                    LogQueue.Enqueue($"[Scan] I2C ({adapterId}): Не удалось открыть FT260.");
                    return foundDevices;
                }

                string adapterName = adapterId;
                LogQueue.Enqueue($"[Scan] I2C: Открыт переходник: {adapterName}");

                int maxAddress = 0x77;
                int startAddress = 0x08;

                var progress = new ScanProgressInfo
                {
                    PortName = $"I2C ({adapterName})",
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
                        if (!i2cMaster.I2C_Detect(address)) continue;
                        LogQueue.Enqueue($"[Scan] I2C ({adapterName}): ★ НАЙДЕНО ★ по адресу 0x{address:X2}");

                        ushort deviceIdRead = 0xFFFF;
                        try
                        {
                            i2cMaster.Write(address, new byte[] { 0x80 }, Start: true, Stop: false);
                            i2cMaster.Read(address, out byte[] idData, 2, RepStart: true, Stop: true);
                            deviceIdRead = (ushort)((idData[1] << 8) | idData[0]);
                        }
                        catch { }

                        var newDev = new DeviceNode
                        {
                            Name = $"[{adapterName}|0x{deviceIdRead:X4}]",
                            Address = address.ToString("D3"),
                            PortName = $"I2C_{adapterName}" // ★ Привязка к конкретному переходнику
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

                progress.StatusText = "Готово";
                ScanProgressChanged?.Invoke(progress);
            }
            catch (Exception ex)
            {
                LogQueue.Enqueue($"[Scan] I2C ({adapterId}): Ошибка: {ex.Message}");
            }
            finally
            {
                i2cMaster?.Close();
                sem.Release();
            }

            return foundDevices;
        }
        public void StartPolling(
            ObservableCollection<InterfaceNode> interfaces,
            bool isRs485,
            List<PortSetting> rs485Settings,
            List<string> selectedI2CDevices, // ★ Заменили bool isI2c
            int intervalMs = 500)
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
                        var devices = iface.Devices.Where(d => d.IsSelected != false).ToList();
                        if (devices.Any())
                        {
                            _pollingTasks.Add(PollModbusLoop(localSetting.PortName, localSetting.BaudRate, devices, intervalMs, _cts.Token));
                        }
                    }
                }
            }

            // ★ Запускаем опрос для каждого I2C-переходника
            if (selectedI2CDevices != null && selectedI2CDevices.Count > 0)
            {
                var i2cIface = interfaces.FirstOrDefault(i => i.Name.Contains("I2C"));
                if (i2cIface != null && i2cIface.IsSelected != false)
                {
                    foreach (var devId in selectedI2CDevices)
                    {
                        var localId = devId;
                        // Берем устройства, привязанные к ЭТОМУ переходнику
                        var i2cDevices = i2cIface.Devices
                            .Where(d => d.IsSelected != false && d.PortName == $"I2C_{localId}")
                            .ToList();

                        if (i2cDevices.Any())
                        {
                            _pollingTasks.Add(PollI2CLoop(localId, i2cDevices, intervalMs, _cts.Token));
                        }
                    }
                }
            }

            LogQueue.Enqueue($"[Poll] Запущено задач опроса: {_pollingTasks.Count}");
        }

        private async Task PollModbusLoop(string portName, int baudRate, List<DeviceNode> devices, int intervalMs, CancellationToken token)
        {
            var sem = GetPortLock(portName);
            try { await sem.WaitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            SerialPort port = null;
            IModbusMaster master = null;

            try
            {
                port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                port.ReadTimeout = 50;

                // ★ Оборачиваем открытие в Task.Run с таймаутом, чтобы не повесить UI
                try
                {
                    await Task.Run(() => port.Open(), token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogQueue.Enqueue($"[Poll] {portName}: Не удалось открыть порт: {ex.Message}");
                    return; // Выходим, если порт занят
                }

                var factory = new ModbusFactory();
                master = factory.CreateRtuMaster(port);
                master.Transport.ReadTimeout = 50;
                master.Transport.Retries = 0;

                LogQueue.Enqueue($"[Poll] {portName}: Опрос запущен (таймаут 50 мс, период {intervalMs} мс)...");

                while (!token.IsCancellationRequested)
                {
                    foreach (var dev in devices)
                    {
                        if (token.IsCancellationRequested) break;

                        var activeChannels = dev.Channels.Where(c => c.IsSelected).ToList();
                        if (activeChannels.Count == 0) continue;

                        try
                        {
                            byte id = byte.Parse(dev.Address);

                            ushort minReg = activeChannels.Min(c => c.RegisterAddress);
                            ushort maxReg = activeChannels.Max(c => c.IsDouble ? (ushort)(c.RegisterAddress + 3) : (ushort)(c.RegisterAddress + 1));
                            ushort numRegs = (ushort)(maxReg - minReg + 1);

                            if (numRegs > 125) numRegs = 125;

                            ushort[] blockRegs = master.ReadInputRegisters(id, minReg, numRegs);

                            foreach (var ch in activeChannels)
                            {
                                int offset = ch.RegisterAddress - minReg;

                                if (ch.IsDouble)
                                {
                                    byte[] bytes = new byte[8];
                                    for (int i = 0; i < 4; i++)
                                    {
                                        bytes[i * 2] = (byte)(blockRegs[offset + i] & 0xFF);
                                        bytes[i * 2 + 1] = (byte)(blockRegs[offset + i] >> 8);
                                    }
                                    double val = BitConverter.ToDouble(bytes, 0);
                                    ch.LastValue = val;
                                    ch.Value = val;
                                }
                                else if (ch.IsInt32)
                                {
                                    int raw = (blockRegs[offset + 1] << 16) | blockRegs[offset];
                                    double val = Math.Round((double)raw);
                                    ch.LastValue = val;
                                    ch.Value = val;
                                }
                                else
                                {
                                    byte[] bytes = new byte[4];
                                    bytes[0] = (byte)(blockRegs[offset] & 0xFF);
                                    bytes[1] = (byte)(blockRegs[offset] >> 8);
                                    bytes[2] = (byte)(blockRegs[offset + 1] & 0xFF);
                                    bytes[3] = (byte)(blockRegs[offset + 1] >> 8);
                                    float val = BitConverter.ToSingle(bytes, 0);
                                    ch.LastValue = val;
                                    ch.Value = val;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (!ex.Message.Contains("timed out"))
                            {
                                LogQueue.Enqueue($"[Debug] {portName}: Ошибка устройства {dev.Address}: {ex.Message}");
                            }
                        }
                    }

                    try { await Task.Delay(intervalMs, token).ConfigureAwait(false); }
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
        private async Task PollI2CLoop(string deviceId, List<DeviceNode> devices, int intervalMs, CancellationToken token)
        {
            FT260_I2CDotNet.FT260 i2cMaster = null;
            var sem = GetI2CLock(deviceId);

            try { await sem.WaitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            try
            {
                LogQueue.Enqueue($"[Poll] I2C ({deviceId}): Опрос запущен...");
                i2cMaster = new FT260_I2CDotNet.FT260();

                // ★ Оборачиваем открытие в Task.Run, чтобы не повесить UI
                bool opened = false;
                try
                {
                    // Если в библиотеке есть TryOpen(deviceId), используйте его.
                    // Если нет — просто TryOpen().
                    opened = await Task.Run(() => i2cMaster.TryOpen(), token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogQueue.Enqueue($"[Poll] I2C ({deviceId}): Ошибка открытия: {ex.Message}");
                }

                if (!opened)
                {
                    LogQueue.Enqueue($"[Poll] I2C ({deviceId}): Не удалось открыть FT260.");
                    return;
                }

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
                LogQueue.Enqueue($"[Poll] I2C ({deviceId}): Ошибка: {ex.Message}");
            }
            finally
            {
                i2cMaster?.Close();
                sem.Release();
                LogQueue.Enqueue($"[Poll] I2C ({deviceId}): Опрос остановлен.");
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

            // 1. Отменяем все задачи
            try { cts.Cancel(); } catch { }
            _cts = null;

            // 2. Отвязываем список задач
            _pollingTasks.Clear();

            // ★ УДАЛЕНО: Блок Task.WaitAll(tasks, 5000); 
            // Из-за него интерфейс зависал на 5 секунд в ожидании закрытия старых портов.
            // Потоки сами увидят token.IsCancellationRequested и корректно закроют порты в блоке finally.

            try { cts.Dispose(); } catch { }
        }

        public void ClosePort()
        {
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