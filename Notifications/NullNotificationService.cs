using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class NullNotificationService : INotificationService
    {
        public Task SendAsync(string text) => Task.CompletedTask;
    }
}