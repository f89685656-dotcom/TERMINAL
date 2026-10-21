using System.Collections.Generic;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public static class NotificationManager
    {
        private static readonly List<INotificationService> _services = new();

        // ★ Оставляем для совместимости со старым кодом
        public static void SetService(INotificationService service)
        {
            _services.Clear();
            if (service != null) _services.Add(service);
        }

        public static void ClearServices() => _services.Clear();

        public static void AddService(INotificationService service)
        {
            if (service != null) _services.Add(service);
        }

        public static async Task NotifyAsync(string text)
        {
            foreach (var svc in _services)
            {
                try { await svc.SendAsync(text); }
                catch { /* Не блокируем остальные каналы */ }
            }
        }

        public static async Task<int?> SendMessageAsync(string text)
        {
            foreach (var svc in _services)
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
            foreach (var svc in _services)
            {
                try { if (await svc.EditMessageAsync(messageId, text)) return true; }
                catch { }
            }
            return false;
        }
    }
}