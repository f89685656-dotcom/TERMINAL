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
            public DateTime LastSendUtc = DateTime.MinValue;
            public DateTime LastEditUtc = DateTime.MinValue;
        }

        private readonly Dictionary<string, ChannelState> _progressChannels = new();
        private readonly Dictionary<string, ChannelState> _tableChannels = new();
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _tableLock = new SemaphoreSlim(1, 1);

        // ★ Ключи сервисов, которым сейчас НЕ шлём «шаги» (SendStatusAsync).
        //   Заполняется, когда для сервиса уже идёт таблица/прогресс.
        private readonly HashSet<string> _suppressSteps = new();
        private readonly object _suppressLock = new object();

        /// <summary>Включить/выключить подавление «шагов» для типа сервиса.</summary>
        public void SetStepsSuppressed<T>(bool suppressed) where T : INotificationService
        {
            lock (_suppressLock)
            {
                if (suppressed) _suppressSteps.Add(typeof(T).Name);
                else _suppressSteps.Remove(typeof(T).Name);
            }
        }

        private bool IsStepsSuppressed(INotificationService svc)
        {
            lock (_suppressLock) { return _suppressSteps.Contains(svc.GetType().Name); }
        }

        public async Task SendStatusAsync(string text)
        {
            foreach (var svc in NotificationManager.GetServices())
            {
                if (IsStepsSuppressed(svc)) continue;    // ★ не мешаем таблице
                try { await svc.SendAsync(text).ConfigureAwait(false); } catch { }
            }
        }

        public async Task UpdateProgressAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            await UpdateChannelAsync(_progressChannels, "progress", text).ConfigureAwait(false);
        }

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

                if (!ShouldPush(svc, state, text)) continue;

                try
                {
                    // 1) Редактируем своё сообщение, если было
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

                        // id протух — сбрасываем, попробуем отправить новое
                        lock (_lock) { state.MessageId = null; }
                    }

                    // 2) Отправляем новое
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
                        state.LastSendUtc = DateTime.UtcNow;
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

        /// <summary>
        /// Решает, надо ли сейчас что-то отправлять в этот канал.
        /// Для VK — жёсткий интервал (30 сек), чтобы не улететь в бан.
        /// </summary>
        private static bool ShouldPush(INotificationService svc, ChannelState state, string text)
        {
            // Текст не изменился — не надо.
            if (state.LastText == text) return false;

            // Для VK — не чаще раза в 30 сек.
            if (svc is VkNotificationService)
            {
                var since = DateTime.UtcNow - state.LastEditUtc;
                if (since < TimeSpan.FromSeconds(30)) return false;
            }

            return true;
        }

        public async Task ResetAsync()
        {
            foreach (var svc in NotificationManager.GetServices())
            {
                if (IsStepsSuppressed(svc)) continue;
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
                { st.MessageId = null; st.LastText = null; }
            }
        }
    }
}