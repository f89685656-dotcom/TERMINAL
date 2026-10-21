using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class TelegramNotificationService : INotificationService
    {
        private static readonly HttpClient _http;

        static TelegramNotificationService()
        {
            var proxy = new WebProxy("http://127.0.0.1:2080"); // ваш NekoBox
            var handler = new HttpClientHandler
            {
                Proxy = proxy,
                UseProxy = true
            };

            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        }

        private readonly string _token;
        private readonly long _chatId;

        public TelegramNotificationService(string token, long chatId)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Telegram token пуст", nameof(token));

            _token = token.Trim();
            _chatId = chatId;
        }

        public Task SendAsync(string text) => SendMessageAsync(text);

        public async Task<int?> SendMessageAsync(string text)
        {
            string url = $"https://api.telegram.org/bot{_token}/sendMessage";
            var payload = new { chat_id = _chatId, text = text ?? "" };
            string json = JsonSerializer.Serialize(payload);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(url, content).ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Telegram API вернул {(int)response.StatusCode}: {body}");

            // Разбираем ответ, чтобы вытащить message_id
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("message_id", out var msgId))
                {
                    return msgId.GetInt32();
                }
            }
            catch { /* ignore parse errors */ }

            return null;
        }

        public async Task<bool> EditMessageAsync(int messageId, string newText)
        {
            string url = $"https://api.telegram.org/bot{_token}/editMessageText";
            var payload = new
            {
                chat_id = _chatId,
                message_id = messageId,
                text = newText ?? ""
            };
            string json = JsonSerializer.Serialize(payload);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(url, content).ConfigureAwait(false);

            if (response.IsSuccessStatusCode) return true;

            // Telegram вернёт 400, если текст не изменился — это не ошибка.
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (body.Contains("message is not modified")) return true;

            return false;
        }
    }
}