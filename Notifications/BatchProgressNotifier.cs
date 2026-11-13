using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class BatchProgressNotifier
    {
        private sealed class ChannelState
        {
            public int? MessageId;
            public string? LastText;
            public DateTime LastEditUtc = DateTime.MinValue;
        }

        private readonly Dictionary<string, ChannelState> _progressChannels = new();
        private readonly Dictionary<string, ChannelState> _tableChannels = new();
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _tableLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Сервис умеет редактировать сообщение (Telegram, VK).
        /// Email/Max/SMS — не умеют, для них используется SendStatusAsync.
        /// </summary>
        private static bool SupportsEdit(INotificationService svc)
            => svc is TelegramNotificationService || svc is VkNotificationService;

        public async Task SendStatusAsync(string text, bool broadcastToEditChannels = false)
        {
            foreach (var svc in NotificationManager.GetServices())
            {
                if (SupportsEdit(svc) && !broadcastToEditChannels)
                    continue;

                try { await svc.SendAsync(text).ConfigureAwait(false); } catch { }
            }
        }

        /// <summary>
        /// Обновить «живое» сообщение о прогрессе (таймер стабилизации).
        /// Только для каналов с поддержкой edit (TG, VK).
        /// </summary>
        public async Task UpdateProgressAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            await UpdateChannelAsync(_progressChannels, "progress", text).ConfigureAwait(false);
        }

        /// <summary>
        /// Обновить «живое» сообщение-таблицу.
        /// Только для каналов с поддержкой edit (TG, VK).
        /// </summary>
        public async Task UpdateTableAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            await _tableLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await UpdateChannelAsync(_tableChannels, "table", text).ConfigureAwait(false);
            }
            finally
            {
                _tableLock.Release();
            }
        }

        private async Task UpdateChannelAsync(
            Dictionary<string, ChannelState> channels,
            string channelKind,
            string text)
        {
            var services = NotificationManager.GetServices();
            if (services.Count == 0) return;

            foreach (var svc in services)
            {
                // ★ Email/Max/SMS не умеют edit — не спамим им таблицей/прогрессом
                if (!SupportsEdit(svc)) continue;

                string key = svc.GetType().Name;

                ChannelState state;
                lock (_lock)
                {
                    if (!channels.TryGetValue(key, out state!))
                    {
                        state = new ChannelState();
                        channels[key] = state;
                    }
                }

                // Текст не изменился — не дёргаем сеть
                if (state.LastText == text) continue;

                // VK — не чаще раза в 30 секунд
                if (svc is VkNotificationService)
                {
                    if ((DateTime.UtcNow - state.LastEditUtc) < TimeSpan.FromSeconds(30))
                        continue;
                }

                try
                {
                    int? existingId;
                    lock (_lock) { existingId = state.MessageId; }

                    if (existingId.HasValue)
                    {
                        bool ok = false;
                        try
                        {
                            ok = await svc.EditMessageAsync(existingId.Value, text).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[BatchProgress:{channelKind}] {key} edit: {ex.Message}");
                        }

                        if (ok)
                        {
                            lock (_lock)
                            {
                                state.LastText = text;
                                state.LastEditUtc = DateTime.UtcNow;
                            }
                            continue;
                        }

                        lock (_lock) { state.MessageId = null; }
                    }

                    int? newId = null;
                    try
                    {
                        newId = await svc.SendMessageAsync(text).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[BatchProgress:{channelKind}] {key} send: {ex.Message}");
                    }

                    lock (_lock)
                    {
                        state.MessageId = newId;
                        state.LastText = text;
                        state.LastEditUtc = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[BatchProgress:{channelKind}] {key}: {ex.Message}");
                }
            }
        }

        public async Task ResetAsync()
        {
            foreach (var svc in NotificationManager.GetServices())
            {
                try { await svc.SendAsync("✅ Все шаги завершены.").ConfigureAwait(false); }
                catch { }
            }

            lock (_lock)
            {
                foreach (var st in _progressChannels.Values)
                {
                    st.MessageId = null;
                    st.LastText = null;
                }
            }
        }

        public void ResetTable()
        {
            lock (_lock)
            {
                foreach (var st in _tableChannels.Values)
                {
                    st.MessageId = null;
                    st.LastText = null;
                }
            }
        }

        public void ResetProgress()
        {
            lock (_lock)
            {
                foreach (var st in _progressChannels.Values)
                {
                    st.MessageId = null;
                    st.LastText = null;
                }
            }
        }
    }
}