using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public interface INotificationService
    {
        // Старый метод — оставляем для совместимости (шлёт и забывает).
        Task SendAsync(string text);

        // Новый: отправляет сообщение и возвращает его message_id (или null, если не удалось).
        Task<int?> SendMessageAsync(string text);

        // Новый: редактирует ранее отправленное сообщение по message_id.
        Task<bool> EditMessageAsync(int messageId, string newText);
    }
}