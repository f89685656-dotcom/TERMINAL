using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class TelegramNotificationService : INotificationService
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private readonly string _token;
        private readonly long _chatId;

        public TelegramNotificationService(string token, long chatId)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Telegram token пуст", nameof(token));

            _token = token;
            _chatId = chatId;
        }

        public async Task SendAsync(string text)
        {
            string url = $"https://api.telegram.org/bot{_token}/sendMessage";

            var payload = new { chat_id = _chatId, text = text ?? "" };
            string json = JsonSerializer.Serialize(payload);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(url, content).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Telegram API вернул {(int)response.StatusCode}: {body}");
            }
        }
    }
}