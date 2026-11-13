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

        // ── Широковещательные методы (для событий, не для «живых» сообщений) ──

        public static async Task NotifyAsync(string text)
        {
            foreach (var svc in Snapshot())
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
            foreach (var svc in Snapshot())
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

        // ── Старые методы (оставлены для совместимости) ──
        // ВНИМАНИЕ: возвращают id первого ответившего сервиса.
        // Для «живых» сообщений (прогресс, таблица) НЕ ИСПОЛЬЗОВАТЬ — см. ниже.

        public static async Task<int?> SendMessageAsync(string text)
        {
            foreach (var svc in Snapshot())
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
            foreach (var svc in Snapshot())
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

        // ── ★ НОВОЕ: адресная работа с конкретным типом сервиса ──
        // BatchProgressNotifier использует эти методы, чтобы вести id
        // отдельно для каждого канала и не путать их.

        /// <summary>
        /// Отправить сообщение через первый сервис указанного типа.
        /// Возвращает id или null, если сервис не найден / отправка не удалась.
        /// </summary>
        public static async Task<int?> SendMessageViaAsync<T>(string text) where T : INotificationService
        {
            var svc = Snapshot().OfType<T>().FirstOrDefault();
            if (svc == null) return null;
            try
            {
                return await svc.SendMessageAsync(text).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SendMessageViaAsync<{typeof(T).Name}>] {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Отредактировать сообщение через первый сервис указанного типа.
        /// </summary>
        public static async Task<bool> EditMessageViaAsync<T>(int messageId, string text) where T : INotificationService
        {
            var svc = Snapshot().OfType<T>().FirstOrDefault();
            if (svc == null) return false;
            try
            {
                return await svc.EditMessageAsync(messageId, text).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditMessageViaAsync<{typeof(T).Name}>] {ex.Message}");
                return false;
            }
        }

        public static async Task UpdateStabilizationAsync(string text)
        {
            foreach (var svc in Snapshot())
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