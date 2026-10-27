using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class NullNotificationService : INotificationService
    {
        public Task SendAsync(string text) => Task.CompletedTask;
        public Task<int?> SendMessageAsync(string text) => Task.FromResult<int?>(null);
        public Task<bool> EditMessageAsync(int messageId, string newText) => Task.FromResult(false);
    }
}