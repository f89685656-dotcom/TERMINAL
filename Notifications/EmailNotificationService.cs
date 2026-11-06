using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class EmailNotificationService : INotificationService
    {
        private readonly string _host, _user, _pass, _to;
        private readonly int _port;
        private readonly SmtpClient _client;

        public EmailNotificationService(string host, int port, string user, string pass, string to)
        {
            _host = host;
            _port = port;
            _user = user;
            _pass = pass;
            _to = to;

            _client = new SmtpClient(_host, _port)
            {
                Credentials = new NetworkCredential(_user, _pass),
                EnableSsl = true,
                Timeout = 15000,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
        }

        public async Task SendAsync(string text)
        {
            using var msg = new MailMessage(_user, _to, "TERMINAL", text);
            using var cts = new CancellationTokenSource(10000);
            try
            {
                await _client.SendMailAsync(msg, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Email: таймаут отправки (10 сек)");
            }
        }

        public Task<int?> SendMessageAsync(string text)
            => SendAsync(text).ContinueWith(_ => (int?)null, TaskScheduler.Default);

        public Task<bool> EditMessageAsync(int id, string text)
            => Task.FromResult(false);
    }
}