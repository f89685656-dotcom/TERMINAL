using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public static class NotificationManager
    {
        private static readonly List<INotificationService> _services = new();
        private static readonly object _lock = new object();

        public static void SetService(INotificationService service)
        {
            lock (_lock)
            {
                _services.Clear();
                if (service != null) _services.Add(service);
            }
        }

        public static void ClearServices() { lock (_lock) { _services.Clear(); } }

        public static void AddService(INotificationService service)
        {
            lock (_lock) { if (service != null) _services.Add(service); }
        }

        public static List<INotificationService> GetServices()
        {
            lock (_lock) { return new List<INotificationService>(_services); }
        }

        private static List<INotificationService> Snapshot()
        {
            lock (_lock) { return new List<INotificationService>(_services); }
        }

        public static async Task NotifyAsync(string text)
        {
            var servicesCopy = Snapshot();
            foreach (var svc in servicesCopy)
            {
                try { await svc.SendAsync(text).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[NotifyAsync] {svc.GetType().Name}: {ex.Message}");
                }
            }
        }

        public static async Task NotifyOthersAsync(string text)
        {
            var servicesCopy = Snapshot();
            foreach (var svc in servicesCopy)
            {
                if (svc is not TelegramNotificationService)
                {
                    try { await svc.SendAsync(text).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[NotifyOthersAsync] {svc.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        public static async Task<int?> SendMessageAsync(string text)
        {
            var servicesCopy = Snapshot();
            foreach (var svc in servicesCopy)
            {
                try
                {
                    var id = await svc.SendMessageAsync(text).ConfigureAwait(false);
                    if (id.HasValue) return id;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SendMessageAsync] {svc.GetType().Name}: {ex.Message}");
                }
            }
            return null;
        }

        public static async Task<bool> EditMessageAsync(int messageId, string text)
        {
            var servicesCopy = Snapshot();
            foreach (var svc in servicesCopy)
            {
                try
                {
                    if (await svc.EditMessageAsync(messageId, text).ConfigureAwait(false))
                        return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EditMessageAsync] {svc.GetType().Name}: {ex.Message}");
                }
            }
            return false;
        }

        /// <summary>
        /// Обновляет одно сообщение о стабилизации у всех VK-сервисов.
        /// Внутри каждого сервиса свой троттлинг (StabilizationInterval).
        /// </summary>
        public static async Task UpdateStabilizationAsync(string text)
        {
            var servicesCopy = Snapshot();
            foreach (var svc in servicesCopy)
            {
                if (svc is VkNotificationService vk)
                {
                    try { await vk.UpdateStabilizationMessageAsync(text).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UpdateStabilization] VK: {ex.Message}");
                    }
                }
            }
        }

        public static string DumpServices()
        {
            lock (_lock)
            {
                if (_services.Count == 0) return "(пусто)";
                return string.Join(", ", _services.Select(s => s.GetType().Name));
            }
        }
    }
}