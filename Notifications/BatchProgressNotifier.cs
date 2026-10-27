using System;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class BatchProgressNotifier
    {
        private int? _statusMsgId;
        private int? _progressMsgId;
        private string? _lastProgressText;
        private int? _tableMsgId;       // ★ ID сообщения с таблицей
        private string? _lastTableText;
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _tableLock = new SemaphoreSlim(1, 1);

        public async Task SendStatusAsync(string text)
        {
            try { await NotificationManager.NotifyAsync(text); } catch { }
        }

        public async Task UpdateProgressAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            int? id;
            string? last;
            lock (_lock) { id = _progressMsgId; last = _lastProgressText; }
            if (text == last) return;

            try
            {
                if (id.HasValue)
                {
                    bool ok = await NotificationManager.EditMessageAsync(id.Value, text);
                    if (ok) { lock (_lock) { _lastProgressText = text; } return; }
                }
                int? newId = await NotificationManager.SendMessageAsync(text);
                lock (_lock) { _progressMsgId = newId; _lastProgressText = text; }
            }
            catch { }
        }

        // ★ ОБНОВЛЕНИЕ ТАБЛИЦЫ (только редактирование!)
        public async Task UpdateTableAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            await _tableLock.WaitAsync().ConfigureAwait(false);
            try
            {
                int? id;
                string? last;
                lock (_lock) { id = _tableMsgId; last = _lastTableText; }
                if (text == last) return;

                var tgService = NotificationManager.GetServices()
                    .OfType<TelegramNotificationService>()
                    .FirstOrDefault();
                if (tgService == null) return;

                if (id.HasValue)
                {
                    bool ok = await tgService.EditMessageAsync(id.Value, text);
                    if (ok) { lock (_lock) { _lastTableText = text; } }
                    return;
                }

                int? newId = await tgService.SendMessageAsync(text);
                lock (_lock) { _tableMsgId = newId; _lastTableText = text; }
            }
            catch
            {
                // Ошибку сети игнорируем, попробуем в следующую секунду
            }
            finally
            {
                _tableLock.Release();
            }
        }

        public async Task ResetAsync()
        {
            await UpdateProgressAsync("✅ Все шаги завершены.");
            lock (_lock) { _progressMsgId = null; _lastProgressText = null; }
        }

        // ★ ВАЖНО: вызов ResetTable должен происходить ТОЛЬКО в конце пакета,
        //    чтобы при старте следующего пакета создать новую таблицу.
        public void ResetTable()
        {
            lock (_lock) { _tableMsgId = null; _lastTableText = null; }
        }
    }
}