using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class MaxNotificationService : INotificationService
    {
        private static readonly HttpClient _http = new HttpClient();
        private readonly string _token;
        private readonly long _chatId;

        public MaxNotificationService(string token, long chatId)
        {
            _token = token;
            _chatId = chatId;
        }

        public async Task SendAsync(string text)
        {
            // Базовый URL API МАКС (обязательно platform-api2.max.ru) [citation:1]
            string url = "https://platform-api2.max.ru/messages";

            var payload = new
            {
                chat_id = _chatId,
                text = text ?? ""
            };
            string json = JsonSerializer.Serialize(payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            // Токен передается в заголовке Authorization [citation:1]
            request.Headers.Add("Authorization", _token);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"MAX API вернул {(int)response.StatusCode}: {body}");
            }
        }

        public Task<int?> SendMessageAsync(string text) => SendAsync(text).ContinueWith(_ => (int?)null);
        public Task<bool> EditMessageAsync(int id, string text) => Task.FromResult(false);
    }
}