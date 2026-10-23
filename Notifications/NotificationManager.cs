using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace TERMINAL.Notifications
{
    public static class NotificationManager
    {
        private static readonly List<INotificationService> _services = new();
        private static readonly object _lock = new object(); // ★ Блокировка

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
                // Возвращаем копию, чтобы внешний код не мог изменить оригинал
                return new List<INotificationService>(_services);
            }
        }

        public static async Task NotifyAsync(string text)
        {
            // Делаем копию списка под блокировкой, а перебираем уже копию
            List<INotificationService> servicesCopy;
            lock (_lock) { servicesCopy = new List<INotificationService>(_services); }

            foreach (var svc in servicesCopy)
            {
                try { await svc.SendAsync(text); }
                catch { }
            }
        }

        public static async Task NotifyOthersAsync(string text)
        {
            List<INotificationService> servicesCopy;
            lock (_lock) { servicesCopy = new List<INotificationService>(_services); }

            foreach (var svc in servicesCopy)
            {
                if (svc is not TelegramNotificationService)
                {
                    try { await svc.SendAsync(text); }
                    catch { }
                }
            }
        }

        public static async Task<int?> SendMessageAsync(string text)
        {
            List<INotificationService> servicesCopy;
            lock (_lock) { servicesCopy = new List<INotificationService>(_services); }

            foreach (var svc in servicesCopy)
            {
                try
                {
                    var id = await svc.SendMessageAsync(text);
                    if (id.HasValue) return id;
                }
                catch { }
            }
            return null;
        }

        public static async Task<bool> EditMessageAsync(int messageId, string text)
        {
            List<INotificationService> servicesCopy;
            lock (_lock) { servicesCopy = new List<INotificationService>(_services); }

            foreach (var svc in servicesCopy)
            {
                try { if (await svc.EditMessageAsync(messageId, text)) return true; }
                catch { }
            }
            return false;
        }
    }
}