using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace TERMINAL
{
    public partial class MainWindow
    {
        // ─────────────────────────────────────────────────────────────
        //  Диспетчер: вызывается таймером _platinaTimer (см. Window_Loaded).
        //  Раньше здесь была вся логика МтК @ TERMEX.
        // ─────────────────────────────────────────────────────────────
        private async Task RefreshPlatinaAsync()
        {
            if (!_readPlatinum)
            {
                // Галочка «Читать платину» снята — не обращаемся к прибору,
                // UI не трогаем (значение замирает на последнем).
                return;
            }

            switch (_platinumSource)
            {
                case "mit_803":
                    await ReadPlatinaFromMit803Async();
                    break;

                case "tercon":
                    // Заготовка: TERCON пока не реализован.
                    // AppendLog("Платина: TERCON ещё не реализован", Brushes.Gray);
                    break;

                case "mtk_termex":
                default:
                    await ReadPlatinaFromMtkTermexAsync();
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  МтК @ TERMEX — СЮДА ПЕРЕНЕСИТЕ СТАРОЕ ТЕЛО
        //  (то, что раньше было внутри RefreshPlatinaAsync:
        //   чтение через HidSharp/Excel и обновление UI).
        // ─────────────────────────────────────────────────────────────
        private async Task ReadPlatinaFromMtkTermexAsync()
        {
            // ▼▼▼ ВСТАВЬТЕ СЮДА СТАРОЕ ТЕЛО ▼▼▼
            //
            // В вашем текущем коде МтК @ TERMEX читается через HidSharp
            // (Vid=0xFFFF, Pid=0x0009, запрос "pass2 meas2? 2\n\0").
            // Найдите старый RefreshPlatinaAsync в другом partial-файле
            // и скопируйте его тело сюда.
            //
            // ▲▲▲ ВСТАВЬТЕ СЮДА СТАРОЕ ТЕЛО ▲▲▲
            await Task.CompletedTask;   // ← удалите эту заглушку, когда вставите тело
        }

        // ─────────────────────────────────────────────────────────────
        //  МИТ 8.03 rev.2 через RS-232 — заготовка.
        //  Открывает COM-порт, читает «что пришло» и пишет в лог.
        //  Реальный протокол — после // TODO.
        // ─────────────────────────────────────────────────────────────
        private async Task ReadPlatinaFromMit803Async()
        {
            try
            {
                // ─── Открыть/переоткрыть порт, если нужно ───
                lock (_platinumSerialLock)
                {
                    if (_platinumSerial == null || !_platinumSerial.IsOpen ||
                        _platinumSerial.PortName != _platinumComPort)
                    {
                        try { _platinumSerial?.Close(); } catch { }
                        try { _platinumSerial?.Dispose(); } catch { }

                        _platinumSerial = new SerialPort(_platinumComPort, 9600)
                        {
                            ReadTimeout = 1000
                        };
                        _platinumSerial.Open();
                        AppendLog($"Платина: подключен {_platinumComPort}", Brushes.Gray);
                    }
                }

                // ─── TODO: реализовать протокол МИТ 8.03 rev.2 ───
                string response = "";
                await Task.Run(() =>
                {
                    lock (_platinumSerialLock)
                    {
                        if (_platinumSerial == null) return;
                        Thread.Sleep(100);
                        if (_platinumSerial.BytesToRead > 0)
                            response = _platinumSerial.ReadExisting();
                    }
                });

                if (!string.IsNullOrWhiteSpace(response))
                    AppendLog($"Платина МИТ 8.03 (raw): '{response.Trim()}'", Brushes.Gray);
            }
            catch (Exception ex)
            {
                AppendLog($"Платина МИТ 8.03: {ex.Message}", Brushes.OrangeRed);
            }
        }
    }
}