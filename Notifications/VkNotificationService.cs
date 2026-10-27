using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class VkNotificationService : INotificationService
    {
        private static readonly HttpClient _http;
        private readonly string _token;
        private readonly long _peerId;

        static VkNotificationService()
        {
            // Принудительно включаем TLS 1.2 и 1.3 для обхода ошибки "SSL connection could not be established"
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

            var handler = new HttpClientHandler
            {
                // Если программа использует прокси (Happ), разрешаем ему работать
                UseProxy = false,
                Proxy = null
            };

            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        public VkNotificationService(string accessToken, long peerId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new ArgumentException("VK Access Token пуст", nameof(accessToken));

            _token = accessToken.Trim();
            _peerId = peerId;
        }

        public async Task SendAsync(string text)
        {
            // Используем официальный API метод messages.send
            string url = "https://api.vk.com/method/messages.send";

            // Формируем данные для POST-запроса (безопаснее, чем GET с токеном в URL)
            var requestData = new System.Collections.Generic.Dictionary<string, string>
            {
                { "user_id", _peerId.ToString() }, // или peer_id, если пишем в беседу
                { "message", text ?? "" },
                { "random_id", new Random().Next(1, int.MaxValue).ToString() }, // защита от дублей
                { "access_token", _token },
                { "v", "5.199" } // Актуальная версия API
            };

            using var content = new FormUrlEncodedContent(requestData);

            // Отправляем POST запрос
            using var response = await _http.PostAsync(url, content).ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"VK API HTTP Error {(int)response.StatusCode}: {body}");
            }

            // Проверяем ответ от VK (там может быть ошибка даже при статусе 200)
            if (body.Contains("\"error\""))
            {
                throw new Exception($"VK API Error: {body}");
            }
        }

        // Заглушки для совместимости с интерфейсом
        public Task<int?> SendMessageAsync(string text) => SendAsync(text).ContinueWith(_ => (int?)null);
        public Task<bool> EditMessageAsync(int id, string text) => Task.FromResult(false);
    }
}