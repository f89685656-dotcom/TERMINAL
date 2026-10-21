using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class EmailNotificationService : INotificationService
    {
        private readonly string _host, _user, _pass, _to;
        private readonly int _port;

        public EmailNotificationService(string host, int port, string user, string pass, string to)
        {
            _host = host; _port = port; _user = user; _pass = pass; _to = to;
        }

        public async Task SendAsync(string text)
        {
            using var client = new SmtpClient(_host, _port)
            {
                Credentials = new NetworkCredential(_user, _pass),
                EnableSsl = true
            };
            var msg = new MailMessage(_user, _to, "TERMINAL Уведомление", text);
            await client.SendMailAsync(msg);
        }

        // Заглушки для совместимости с интерфейсом (если вы расширяли его)
        public Task<int?> SendMessageAsync(string text) => SendAsync(text).ContinueWith(_ => (int?)null);
        public Task<bool> EditMessageAsync(int id, string text) => Task.FromResult(false);
    }
}