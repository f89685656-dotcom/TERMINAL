using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class SmsNotificationService : INotificationService
    {
        private static readonly HttpClient _http = new HttpClient();
        private readonly string _login;
        private readonly string _password;
        private readonly string _phone;

        public SmsNotificationService(string login, string password, string phone)
        {
            _login = login;
            _password = password;
            _phone = phone; // формат: +79991234567
        }

        public async Task SendAsync(string text)
        {
            // SMS ограничение: 70 символов кириллицы
            if (text.Length > 70) text = text.Substring(0, 67) + "...";

            string url = "https://smsc.ru/sys/send.php" +
                         $"?login={Uri.EscapeDataString(_login)}" +
                         $"&psw={Uri.EscapeDataString(_password)}" +
                         $"&phones={Uri.EscapeDataString(_phone)}" +
                         $"&mes={Uri.EscapeDataString(text ?? "")}" +
                         "&charset=utf-8&fmt=3";

            using var cts = new CancellationTokenSource(5000);
            try
            {
                var response = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"SMSC вернул {(int)response.StatusCode}");

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (body.Contains("\"error\""))
                    throw new HttpRequestException("SMSC: " + body);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("SMS: таймаут отправки (5 сек)");
            }
        }

        public Task<int?> SendMessageAsync(string text)
            => SendAsync(text).ContinueWith(_ => (int?)null, TaskScheduler.Default);

        public Task<bool> EditMessageAsync(int id, string text)
            => Task.FromResult(false);
    }
}