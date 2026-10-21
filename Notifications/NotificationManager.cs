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

        public static Task NotifyAsync(string text)
        {
            return _service.SendAsync(text);
        }
    }
}