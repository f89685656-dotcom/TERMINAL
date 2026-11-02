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

        public event Action<string, int, int> ScanProgressChanged; // portName, current, total
        public event Action<DeviceNode> DeviceFound; // устройство найдено

        // ★ ДОЛГОЖИВУЩИЙ ПОРТ ★
        private SerialPort _port;
        private IModbusMaster _master;
        private bool _isPortOpen = false;

        // --- МЕТОД СКАНИРОВАНИЯ ---

        public async Task<List<DeviceNode>> ScanAllAsync(ObservableCollection<InterfaceNode> interfaces, bool isRs485, List<PortSetting> rs485Settings, bool isI2c)
        {
            var foundDevices = new List<DeviceNode>();

            foreach (var iface in interfaces)
            {
                if (!iface.IsSelected) continue;

                if (iface.Name.Contains("RS-485") && isRs485)
                {
                    foreach (var setting in rs485Settings)
                    {
                        // ★ ЗАПУСКАЕМ В ФОНОВОМ ПОТОКЕ ★
                        var devices = await Task.Run(() => ScanModbusAsync(iface, setting.PortName, setting.BaudRate));
                        foundDevices.AddRange(devices);
                    }
                }
                else if (iface.Name.Contains("I2C") && isI2c)
                {
                    // I2C заглушка
                }
            }

            return foundDevices;
        }
        private async Task<List<DeviceNode>> ScanModbusAsync(InterfaceNode iface, string portName, int baudRate)
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

                LogQueue.Enqueue($"[Scan] {portName}: Порт открыт. Начинаем перебор адресов...");

                int maxAddress = 10;
                for (byte address = 1; address <= maxAddress; address++)
                {
                    ScanProgressChanged?.Invoke(portName, address, maxAddress);

                    try
                    {
                        ushort[] regs = master.ReadInputRegisters(address, 0, 2);

                        // ★ ЧИТАЕМ PRODUCT ID ★
                        uint productId = 0;
                        try
                        {
                            ushort[] idRegs = master.ReadHoldingRegisters(address, 0x0004, 2);
                            productId = ((uint)idRegs[1] << 16) | idRegs[0];
                            LogQueue.Enqueue($"[Scan] {portName}: Адрес {address}, Product ID = 0x{productId:X8}");
                        }
                        catch
                        {
                            LogQueue.Enqueue($"[Scan] {portName}: Адрес {address}, Product ID не прочитан.");
                        }

                        // ★ ФОРМИРУЕМ ИМЯ УСТРОЙСТВА С ПОРТОМ И ТИПОМ ★
                        string deviceType = "";
                        string deviceName = "";

                        if (productId == 0xC002 || productId == 0x0000C002)
                        {
                            deviceType = "C002";
                            deviceName = $"Датчик давления СКТБ ЭЛПА [COM9|C002]";
                        }
                        else if (productId == 0xDBA0 || productId == 0x0000DBA0)
                        {
                            deviceType = "DBA0";
                            deviceName = $"Датчик давления SPI/I2C [{portName}|DBA0]";
                        }
                        else if (productId == 0xDBA2 || productId == 0x0000DBA2)
                        {
                            deviceType = "DBA2";
                            deviceName = $"Датчик температуры SPI/I2C [{portName}|DBA2]";
                        }
                        else
                        {
                            deviceType = "Unknown";
                            deviceName = $"Неизвестное устройство [{portName}]";
                        }

                        var newDev = new DeviceNode
                        {
                            Name = deviceName,
                            Address = address.ToString("D3"),
                            PortName = portName
                        };

                        // ★ ВЫБИРАЕМ КАРТУ ПО PRODUCT ID ★
                        if (productId == 0xC002 || productId == 0x0000C002)
                        {
                            // --- КАРТА ДЛЯ C002 ---
                            newDev.Channels.Add(new ChannelNode { Name = "Давление", Unit = "бар", RegisterAddress = 0x0000, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0002, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Частота давления", Unit = "Гц", RegisterAddress = 0x0008, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Частота температуры", Unit = "Гц", RegisterAddress = 0x000A, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Давление (фикс)", Unit = "", RegisterAddress = 0x0010, Is32Bit = true, IsInt32 = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Температура (фикс)", Unit = "", RegisterAddress = 0x0012, Is32Bit = true, IsInt32 = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Температура процессора", Unit = "°C", RegisterAddress = 0x0020, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Напряжение питания", Unit = "В", RegisterAddress = 0x0022, Is32Bit = true });
                        }
                        else if (productId == 0xDBA0 || productId == 0x0000DBA0)
                        {
                            // --- КАРТА ДЛЯ DBA0 (ДАТЧИК ДАВЛЕНИЯ) ---
                            newDev.Channels.Add(new ChannelNode { Name = "Давление", Unit = "бар", RegisterAddress = 0x0000, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0002, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Частота давления", Unit = "Гц", RegisterAddress = 0x0008, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Частота температуры", Unit = "Гц", RegisterAddress = 0x000A, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Давление (фикс)", Unit = "", RegisterAddress = 0x0010, Is32Bit = true, IsInt32 = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Температура (фикс)", Unit = "", RegisterAddress = 0x0012, Is32Bit = true, IsInt32 = true });
                        }
                        else if (productId == 0xDBA2 || productId == 0x0000DBA2)
                        {
                            // --- КАРТА ДЛЯ DBA2 (ДАТЧИК ТЕМПЕРАТУРЫ) ---
                            newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0002, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Частота температуры", Unit = "Гц", RegisterAddress = 0x000A, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Температура (фикс)", Unit = "", RegisterAddress = 0x0012, Is32Bit = true, IsInt32 = true });
                        }
                        else
                        {
                            // --- НЕИЗВЕСТНОЕ УСТРОЙСТВО ---
                            newDev.Channels.Add(new ChannelNode { Name = "Давление", Unit = "бар", RegisterAddress = 0x0000, Is32Bit = true });
                            newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0002, Is32Bit = true });
                        }

                        foundDevices.Add(newDev);
                        DeviceFound?.Invoke(newDev);
                    }
                    catch { /* Таймаут */ }
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
        private async Task ScanI2CAsync(InterfaceNode iface)
        {
            await Task.Delay(1000);
            // I2C заглушка
        }

        // --- МЕТОД ОПРОСА ---

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
                        // ★ ГРУППИРУЕМ УСТРОЙСТВА ПО ПОРТУ ★
                        var devicesOnPort = iface.Devices
                            .Where(d => d.IsSelected && d.PortName == setting.PortName)
                            .ToList();

                        if (devicesOnPort.Any())
                        {
                            // ★ ЗАПУСКАЕМ ОТДЕЛЬНЫЙ ПОТОК ДЛЯ КАЖДОГО ПОРТА ★
                            _pollingTasks.Add(Task.Run(() =>
                                PollModbusLoop(setting.PortName, setting.BaudRate, devicesOnPort, intervalMs, _cts.Token)));
                        }
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

                // ★ ТАЙМАУТ ЧТЕНИЯ ЗАВИСИТ ОТ ПЕРИОДА ★
                // Если период 500 мс, таймаут 200 мс (чтобы успеть прочитать все каналы)
                int readTimeout = Math.Max(100, intervalMs / 2);
                port.ReadTimeout = readTimeout;
                port.Open();

                var factory = new ModbusFactory();
                master = factory.CreateRtuMaster(port);
                master.Transport.ReadTimeout = readTimeout;

                LogQueue.Enqueue($"[Poll] {portName}: Опрос запущен (таймаут {readTimeout} мс, период {intervalMs} мс)...");

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
                                    ushort[] regs = master.ReadInputRegisters(id, ch.RegisterAddress, 2);

                                    if (ch.IsInt32)
                                    {
                                        int raw = (regs[1] << 16) | regs[0];
                                        double val = Math.Round((double)raw);
                                        ch.LastValue = val;
                                        UiUpdates.Enqueue(() => ch.Value = val);
                                    }
                                    else
                                    {
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
                                    // Логируем только не-таймауты, чтобы не забивать лог
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

                    // ★ ЖДЕМ ПЕРИОД ИЗ ПОЛЗУНКА ★
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