using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public static class NotificationManager
    {
        private static readonly List<INotificationService> _services = new();
        private static readonly object _lock = new object();

        // ★ Очередь: гарантирует, что уведомления уходят в порядке вызова
        private static readonly SemaphoreSlim _notifyLock = new SemaphoreSlim(1, 1);

        public static void SetService(INotificationService service)
        {
            lock (_lock)
            {
                _services.Clear();
                if (service != null) _services.Add(service);
            }
        }

        public static void ClearServices()
        {
            lock (_lock) { _services.Clear(); }
        }

        public static void AddService(INotificationService service)
        {
            lock (_lock)
            {
                if (service != null) _services.Add(service);
            }
        }

        public static List<INotificationService> GetServices()
        {
            lock (_lock)
            {
                return new List<INotificationService>(_services);
            }
        }

        public static async Task NotifyAsync(string text)
        {
            // ★ Ждём, пока предыдущее уведомление не отправится во все каналы
            await _notifyLock.WaitAsync().ConfigureAwait(false);
            try
            {
                List<INotificationService> servicesCopy;
                lock (_lock) { servicesCopy = new List<INotificationService>(_services); }

                foreach (var svc in servicesCopy)
                {
                    try { await svc.SendAsync(text).ConfigureAwait(false); }
                    catch { }
                }
            }
            finally
            {
                _notifyLock.Release();
            }
        }

        public static async Task NotifyOthersAsync(string text)
        {
            await _notifyLock.WaitAsync().ConfigureAwait(false);
            try
            {
                List<INotificationService> servicesCopy;
                lock (_lock) { servicesCopy = new List<INotificationService>(_services); }

                foreach (var svc in servicesCopy)
                {
                    if (svc is not TelegramNotificationService)
                    {
                        try { await svc.SendAsync(text).ConfigureAwait(false); }
                        catch { }
                    }
                }
            }
            finally
            {
                _notifyLock.Release();
            }
        }

        public static async Task<int?> SendMessageAsync(string text)
        {
            await _notifyLock.WaitAsync().ConfigureAwait(false);
            try
            {
                List<INotificationService> servicesCopy;
                lock (_lock) { servicesCopy = new List<INotificationService>(_services); }

                foreach (var svc in servicesCopy)
                {
                    try
                    {
                        var id = await svc.SendMessageAsync(text).ConfigureAwait(false);
                        if (id.HasValue) return id;
                    }
                    catch { }
                }
                return null;
            }
            finally
            {
                _notifyLock.Release();
            }
        }

        public static async Task<bool> EditMessageAsync(int messageId, string text)
        {
            await _notifyLock.WaitAsync().ConfigureAwait(false);
            try
            {
                List<INotificationService> servicesCopy;
                lock (_lock) { servicesCopy = new List<INotificationService>(_services); }

                foreach (var svc in servicesCopy)
                {
                    try { if (await svc.EditMessageAsync(messageId, text).ConfigureAwait(false)) return true; }
                    catch { }
                }
                return false;
            }
            finally
            {
                _notifyLock.Release();
            }
        }
    }
}