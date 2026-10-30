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

        // ★ ДОЛГОЖИВУЩИЙ ПОРТ ★
        private SerialPort _port;
        private IModbusMaster _master;
        private bool _isPortOpen = false;

        // --- МЕТОД СКАНИРОВАНИЯ ---

        public async Task ScanAllAsync(ObservableCollection<InterfaceNode> interfaces, bool isRs485, List<PortSetting> rs485Settings, bool isI2c)
        {
            var scanTasks = new List<Task>();

            foreach (var iface in interfaces)
            {
                if (!iface.IsSelected) continue;

                if (iface.Name.Contains("RS-485") && isRs485)
                {
                    foreach (var setting in rs485Settings)
                    {
                        scanTasks.Add(Task.Run(() => ScanModbusAsync(iface, setting.PortName, setting.BaudRate)));
                    }
                }
                else if (iface.Name.Contains("I2C") && isI2c)
                {
                    scanTasks.Add(Task.Run(() => ScanI2CAsync(iface)));
                }
            }

            // ★ ЖДЕМ ЗАВЕРШЕНИЯ ВСЕХ ЗАДАЧ, НО НЕ БОЛЕЕ 10 СЕКУНД ★
            var allTasks = Task.WhenAll(scanTasks);
            var timeoutTask = Task.Delay(10000); // 10 секунд

            var completedTask = await Task.WhenAny(allTasks, timeoutTask);

            if (completedTask == timeoutTask)
            {
                LogQueue.Enqueue("[Scan] Сканирование превысило 10 секунд и было принудительно остановлено.");
            }
            else
            {
                // Если задачи завершились с ошибкой, пробрасываем её (но ловим в UI)
                try { await allTasks; } catch { }
            }
        }
        private async Task ScanModbusAsync(InterfaceNode iface, string portName, int baudRate)
        {
            try
            {
                LogQueue.Enqueue($"[Scan] Попытка открыть {portName} на {baudRate} бод...");

                // ★ ОТКРЫВАЕМ ПОРТ, ЕСЛИ ОН ЕЩЕ НЕ ОТКРЫТ ★
                if (_port == null || !_port.IsOpen)
                {
                    _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                    _port.Open();
                    var factory = new ModbusFactory();
                    _master = factory.CreateRtuMaster(_port);
                    _master.Transport.ReadTimeout = 300;
                    _isPortOpen = true;
                }

                LogQueue.Enqueue($"[Scan] Порт {portName} открыт. Начинаем перебор адресов...");

                for (byte address = 1; address < 248; address++)
                {
                    try
                    {
                        _master.ReadInputRegisters(address, 0, 2);

                        LogQueue.Enqueue($"[Scan] [{portName}] Найдено устройство по адресу {address}!");

                        var newDev = new DeviceNode { Name = "Датчик давления СКТБ ЭЛПА", Address = address.ToString("D3") };

                        // ★ ТОЧНЫЕ АДРЕСА ИЗ ТАБЛИЦЫ INPUT REGISTERS ★

                        // 1. Давление (float) - адрес 0x0000 (2 регистра: 0x00 и 0x01)
                        newDev.Channels.Add(new ChannelNode { Name = "Давление", Unit = "бар", RegisterAddress = 0x0000, Is32Bit = true });

                        // 2. Температура (float) - адрес 0x0002 (2 регистра: 0x02 и 0x03)
                        newDev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C", RegisterAddress = 0x0002, Is32Bit = true });

                        // 3. Частота давления (float) - адрес 0x0008 (2 регистра: 0x08 и 0x09)
                        newDev.Channels.Add(new ChannelNode { Name = "Частота давления", Unit = "Гц", RegisterAddress = 0x0008, Is32Bit = true });

                        // 4. Частота температуры (float) - адрес 0x000A (2 регистра: 0x0A и 0x0B)
                        newDev.Channels.Add(new ChannelNode { Name = "Частота температуры", Unit = "Гц", RegisterAddress = 0x000A, Is32Bit = true });

                        // 5. Давление (фикс. точка) (int32) - адрес 0x0010 (2 регистра: 0x10 и 0x11)
                        newDev.Channels.Add(new ChannelNode { Name = "Давление (фикс)", Unit = "", RegisterAddress = 0x0010, Is32Bit = true, IsInt32 = true });

                        // 6. Температура (фикс. точка) (int32) - адрес 0x0012 (2 регистра: 0x12 и 0x13)
                        newDev.Channels.Add(new ChannelNode { Name = "Температура (фикс)", Unit = "", RegisterAddress = 0x0012, Is32Bit = true, IsInt32 = true });

                        // 7. Температура процессора (float) - адрес 0x0020 (2 регистра: 0x20 и 0x21)
                        newDev.Channels.Add(new ChannelNode { Name = "Температура процессора", Unit = "°C", RegisterAddress = 0x0020, Is32Bit = true });

                        // 8. Напряжение питания (float) - адрес 0x0022 (2 регистра: 0x22 и 0x23)
                        newDev.Channels.Add(new ChannelNode { Name = "Напряжение питания", Unit = "В", RegisterAddress = 0x0022, Is32Bit = true });

                        UiUpdates.Enqueue(() =>
                        {
                            if (!iface.Devices.Any(d => d.Address == newDev.Address))
                            {
                                iface.Devices.Add(newDev);
                                LogQueue.Enqueue($"[Scan] Устройство {newDev.Address} добавлено в дерево.");
                            }
                        });
                    }
                    catch { /* Таймаут */ }
                }
                LogQueue.Enqueue($"[Scan] Сканирование {portName} завершено.");
            }
            catch (Exception ex)
            {
                LogQueue.Enqueue($"[Scan] Ошибка порта {portName}: {ex.Message}");
            }
            // ★ НЕ ЗАКРЫВАЕМ ПОРТ, ОН ОСТАЕТСЯ ОТКРЫТЫМ ДЛЯ ОПРОСА ★
        }

        private async Task ScanI2CAsync(InterfaceNode iface)
        {
            await Task.Delay(1000);
            LogQueue.Enqueue("[Scan] I2C: Поиск завершен (заглушка).");
            UiUpdates.Enqueue(() =>
            {
                var dev = new DeviceNode { Name = "I2C датчик", Address = "017" };
                dev.Channels.Add(new ChannelNode { Name = "Температура", Unit = "°C" });
                iface.Devices.Add(dev);
            });
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

                                    LogQueue.Enqueue($"[Debug] {ch.Name} (адрес {ch.RegisterAddress:X4}): regs=[{regs[0]}, {regs[1]}]");

                                    if (ch.IsInt32)
                                    {
                                        // ★ ФИКС. ТОЧКА (Int32) ★
                                        int raw = (regs[1] << 16) | regs[0];

                                        // Округляем до целого и приводим к double
                                        // Это уберет дробную часть, но WPF может все равно добавить .000
                                        double val = Math.Round((double)raw);

                                        UiUpdates.Enqueue(() => ch.Value = val);
                                    }
                                    else
                                    {
                                        // ★ FLOAT (Давление, Температура и т.д.) ★
                                        byte[] bytes = new byte[4];
                                        bytes[0] = (byte)(regs[0] & 0xFF);
                                        bytes[1] = (byte)(regs[0] >> 8);
                                        bytes[2] = (byte)(regs[1] & 0xFF);
                                        bytes[3] = (byte)(regs[1] >> 8);

                                        float val = BitConverter.ToSingle(bytes, 0);

                                        // Приводим к double для совместимости с типом свойства
                                        UiUpdates.Enqueue(() => ch.Value = (double)val);
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