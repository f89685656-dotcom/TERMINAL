using System;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class BatchProgressNotifier
    {
        private int? _statusMsgId;
        private int? _progressMsgId;
        private string? _lastProgressText;
        private int? _tableMsgId;
        private string? _lastTableText;
        private readonly object _lock = new object();

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

        // ★ ИСПРАВЛЕННЫЙ МЕТОД: Работает ТОЛЬКО с Telegram
        public async Task UpdateTableAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            int? id;
            string? last;
            lock (_lock) { id = _tableMsgId; last = _lastTableText; }
            if (text == last) return;

            try
            {
                var tgService = NotificationManager.GetServices()
                    .OfType<TelegramNotificationService>()
                    .FirstOrDefault();

                if (tgService == null) return;

                if (id.HasValue)
                {
                    // Пытаемся отредактировать. Если не вышло — просто выходим (не спамим).
                    bool ok = await tgService.EditMessageAsync(id.Value, text);
                    if (ok)
                    {
                        lock (_lock) { _lastTableText = text; }
                        return;
                    }
                    // Если не удалось отредактировать (например, таймаут), 
                    // просто выходим. При следующем тике попробуем снова.
                    return;
                }

                // Если ID нет — отправляем новое и сохраняем ID
                int? newId = await tgService.SendMessageAsync(text);
                lock (_lock) { _tableMsgId = newId; _lastTableText = text; }
            }
            catch { }
        }

        public async Task ResetAsync()
        {
            await UpdateProgressAsync("✅ Все шаги завершены.");
            lock (_lock) { _progressMsgId = null; _lastProgressText = null; }
        }

        public void ResetTable()
        {
            lock (_lock) { _tableMsgId = null; _lastTableText = null; }
        }
    }
}