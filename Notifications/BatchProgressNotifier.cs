using System;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class BatchProgressNotifier
    {
        private int? _progressMsgId;
        private string? _lastProgressText;
        private readonly object _lock = new object();

        // Для отправки новых сообщений (старт/конец задания, ошибки)
        public async Task SendStatusAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { await NotificationManager.SendMessageAsync(text); } catch { }
        }

        // Для редактирования одного сообщения (Шаг 1/16, Цикл 1/10)
        public async Task UpdateProgressAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            int? id;
            string? last;
            lock (_lock) { id = _progressMsgId; last = _lastProgressText; }

            if (text == last) return; // Не дёргаем API, если текст не изменился

            try
            {
                if (id.HasValue)
                {
                    bool ok = await NotificationManager.EditMessageAsync(id.Value, text);
                    if (ok)
                    {
                        lock (_lock) { _lastProgressText = text; }
                        return;
                    }
                }
                // Если ID нет или редактирование не удалось — шлём новое
                int? newId = await NotificationManager.SendMessageAsync(text);
                lock (_lock) { _progressMsgId = newId; _lastProgressText = text; }
            }
            catch { }
        }

        // Сброс состояния после завершения пакета
        public async Task ResetAsync()
        {
            await UpdateProgressAsync("✅ Все шаги завершены.");
            lock (_lock)
            {
                _progressMsgId = null;
                _lastProgressText = null;
            }
        }

        private int? _tableMsgId; // ★ Храним ID сообщения с таблицей

        public async Task UpdateTableAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                if (_tableMsgId.HasValue)
                {
                    // Пытаемся отредактировать существующее сообщение
                    bool ok = await NotificationManager.EditMessageAsync(_tableMsgId.Value, text);
                    if (ok) return; // Если успешно, выходим
                }

                // Если ID нет или редактирование не удалось — шлём новое
                int? newId = await NotificationManager.SendMessageAsync(text);
                _tableMsgId = newId;
            }
            catch { }
        }

        // Сброс ID таблицы (вызывать при завершении пакета)
        public void ResetTable()
        {
            _tableMsgId = null;
        }
    }
}