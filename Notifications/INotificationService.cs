using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public interface INotificationService
    {
        Task SendAsync(string text);
    }
}