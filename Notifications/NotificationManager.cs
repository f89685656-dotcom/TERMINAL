using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public static class NotificationManager
    {
        private static INotificationService _service = new NullNotificationService();

        public static void SetService(INotificationService service)
        {
            _service = service ?? new NullNotificationService();
        }

        public static Task NotifyAsync(string text) => _service.SendAsync(text);

        public static Task<int?> SendMessageAsync(string text) => _service.SendMessageAsync(text);

        public static Task<bool> EditMessageAsync(int messageId, string newText)
            => _service.EditMessageAsync(messageId, newText);
    }
}