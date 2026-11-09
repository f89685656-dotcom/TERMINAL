using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class VkNotificationService : INotificationService
    {
        private static readonly HttpClient _http;
        private const int VkTimeoutMs = 5000;

        /// <summary>
        /// Минимальный интервал между обновлениями одного сообщения стабилизации.
        /// Работает через UpdateStabilizationMessageAsync.
        /// </summary>
        public static TimeSpan StabilizationInterval { get; set; } = TimeSpan.FromMinutes(10);

        private readonly string _token;
        private readonly long _peerId;

        private readonly object _stabLock = new object();
        private int? _stabilizationMessageId;
        private DateTime _lastStabilizationUpdateUtc = DateTime.MinValue;

        static VkNotificationService()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            var handler = new HttpClientHandler { UseProxy = false, Proxy = null };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        }

        public VkNotificationService(string accessToken, long peerId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new ArgumentException("VK Access Token пуст", nameof(accessToken));
            _token = accessToken.Trim();
            _peerId = peerId;
        }

        // -------- INotificationService --------

        public async Task SendAsync(string text)
        {
            var id = await SendRawReturningIdAsync(text).ConfigureAwait(false);
            if (!id.HasValue)
                throw new Exception("VK: не удалось отправить сообщение");
        }

        public async Task<int?> SendMessageAsync(string text)
            => await SendRawReturningIdAsync(text).ConfigureAwait(false);

        /// <summary>
        /// Редактирование в VK. В личке messages.edit не работает,
        /// поэтому fallback — delete + send. Возвращает true, если удалось.
        /// </summary>
        public async Task<bool> EditMessageAsync(int messageId, string text)
        {
            bool edited = await TryEditMessageAsync(messageId, text).ConfigureAwait(false);
            if (edited) return true;

            await TryDeleteMessageAsync(messageId).ConfigureAwait(false);
            var newId = await SendRawReturningIdAsync(text).ConfigureAwait(false);
            return newId.HasValue;
        }

        // -------- Спец-метод для таймера стабилизации --------

        /// <summary>
        /// Обновляет ОДНО сообщение о стабилизации не чаще, чем раз в StabilizationInterval.
        /// </summary>
        public async Task<bool> UpdateStabilizationMessageAsync(string text)
        {
            int? messageId;
            lock (_stabLock)
            {
                if (DateTime.UtcNow - _lastStabilizationUpdateUtc < StabilizationInterval)
                    return false;
                _lastStabilizationUpdateUtc = DateTime.UtcNow;
                messageId = _stabilizationMessageId;
            }

            if (messageId.HasValue)
            {
                bool edited = await TryEditMessageAsync(messageId.Value, text).ConfigureAwait(false);
                if (edited) return true;
                await TryDeleteMessageAsync(messageId.Value).ConfigureAwait(false);
            }

            int? newId = await SendRawReturningIdAsync(text).ConfigureAwait(false);
            lock (_stabLock) { _stabilizationMessageId = newId; }
            return newId.HasValue;
        }

        // -------- низкоуровневые вызовы --------

        private async Task<bool> TryEditMessageAsync(int messageId, string text)
        {
            string url = "https://api.vk.com/method/messages.edit";
            var data = new Dictionary<string, string>
            {
                { "peer_id", _peerId.ToString() },
                { "message_id", messageId.ToString() },
                { "message", text ?? "" },
                { "access_token", _token },
                { "v", "5.199" }
            };
            using var cts = new CancellationTokenSource(VkTimeoutMs);
            try
            {
                using var content = new FormUrlEncodedContent(data);
                using var response = await _http.PostAsync(url, content, cts.Token).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return response.IsSuccessStatusCode && !body.Contains("\"error\"");
            }
            catch { return false; }
        }

        private async Task TryDeleteMessageAsync(int messageId)
        {
            string url = "https://api.vk.com/method/messages.delete";
            var data = new Dictionary<string, string>
            {
                { "message_ids", messageId.ToString() },
                { "delete_for_all", "1" },
                { "access_token", _token },
                { "v", "5.199" }
            };
            using var cts = new CancellationTokenSource(VkTimeoutMs);
            try
            {
                using var content = new FormUrlEncodedContent(data);
                using var response = await _http.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            }
            catch { }
        }

        private async Task<int?> SendRawReturningIdAsync(string text)
        {
            string url = "https://api.vk.com/method/messages.send";
            var data = new Dictionary<string, string>
            {
                { "peer_id", _peerId.ToString() },
                { "message", text ?? "" },
                { "random_id", new Random().Next(1, int.MaxValue).ToString() },
                { "access_token", _token },
                { "v", "5.199" }
            };
            using var cts = new CancellationTokenSource(VkTimeoutMs);
            try
            {
                using var content = new FormUrlEncodedContent(data);
                using var response = await _http.PostAsync(url, content, cts.Token).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode || body.Contains("\"error\"")) return null;

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("response", out var resp)) return null;

                if (resp.ValueKind == JsonValueKind.Number && resp.TryGetInt32(out int id1))
                    return id1;
                if (resp.ValueKind == JsonValueKind.Object &&
                    resp.TryGetProperty("message_id", out var mid) &&
                    mid.TryGetInt32(out int id2))
                    return id2;
                return null;
            }
            catch { return null; }
        }
    }
}