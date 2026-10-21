using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class VkNotificationService : INotificationService
    {
        private static readonly HttpClient _http = new HttpClient();
        private readonly string _token;
        private readonly long _peerId;

        public VkNotificationService(string accessToken, long peerId)
        {
            _token = accessToken;
            _peerId = peerId;
        }

        public async Task SendAsync(string text)
        {
            // VK API через прямое HTTP-обращение (не требует VkNet)
            string url = "https://api.vk.com/method/messages.send" +
                         $"?user_id={_peerId}" +
                         $"&message={Uri.EscapeDataString(text ?? "")}" +
                         $"&random_id={new Random().Next()}" +
                         $"&access_token={_token}" +
                         "&v=5.131";

            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"VK API вернул {(int)response.StatusCode}");
        }

        public Task<int?> SendMessageAsync(string text) => SendAsync(text).ContinueWith(_ => (int?)null);
        public Task<bool> EditMessageAsync(int id, string text) => Task.FromResult(false);
    }
}