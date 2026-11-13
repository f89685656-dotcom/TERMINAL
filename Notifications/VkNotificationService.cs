using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace TERMINAL.Notifications
{
    public class VkNotificationService : INotificationService
    {
        private static readonly HttpClient _http;
        private const int VkTimeoutMs = 10000;   // ★ было 5000 — мало при бане

        /// <summary>Минимальный интервал между любыми запросами к VK (глобально).</summary>
        public static TimeSpan MinRequestInterval { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>Минимальный интервал между обновлениями одного сообщения стабилизации.</summary>
        public static TimeSpan StabilizationInterval { get; set; } = TimeSpan.FromSeconds(30);

        private static readonly SemaphoreSlim _vkGate = new SemaphoreSlim(1, 1);
        private static DateTime _lastVkRequestUtc = DateTime.MinValue;

        private readonly string _token;
        private readonly long _peerId;

        private readonly object _stabLock = new object();
        private int? _stabilizationMessageId;
        private DateTime _lastStabilizationUpdateUtc = DateTime.MinValue;

        static VkNotificationService()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            var handler = new HttpClientHandler { UseProxy = false, Proxy = null };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
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

        public Task<int?> SendMessageAsync(string text)
            => SendRawReturningIdAsync(text);

        public async Task<bool> EditMessageAsync(int messageId, string text)
        {
            // ★ Без delete+send. Если edit не удался — возвращаем false,
            //   пусть вызывающий решает (BatchProgressNotifier сам отправит новое
            //   при следующем обновлении, с учётом троттлинга).
            return await TryEditMessageAsync(messageId, text).ConfigureAwait(false);
        }

        /// <summary>Удалить сообщение (нужно для очистки «шагов», если они мешают таблице).</summary>
        public async Task<bool> DeleteMessageAsync(int messageId)
            => await TryDeleteMessageAsync(messageId).ConfigureAwait(false);

        // -------- Спец-метод для таймера стабилизации --------

        public async Task<bool> UpdateStabilizationMessageAsync(string text)
        {
            int? messageId;
            lock (_stabLock)
            {
                // ★ Троттлинг: слишком часто — пропускаем.
                if ((DateTime.UtcNow - _lastStabilizationUpdateUtc) < StabilizationInterval)
                    return false;

                _lastStabilizationUpdateUtc = DateTime.UtcNow;
                messageId = _stabilizationMessageId;
            }

            if (messageId.HasValue)
            {
                bool edited = await TryEditMessageAsync(messageId.Value, text).ConfigureAwait(false);
                if (edited) return true;
                // id протух — отправим новое.
            }

            int? newId = await SendRawReturningIdAsync(text).ConfigureAwait(false);
            lock (_stabLock) { _stabilizationMessageId = newId; }
            return newId.HasValue;
        }

        /// <summary>Сбросить id «сообщения стабилизации» (например, после Reset).</summary>
        public void ResetStabilizationMessage()
        {
            lock (_stabLock)
            {
                _stabilizationMessageId = null;
                _lastStabilizationUpdateUtc = DateTime.MinValue;
            }
        }

        // -------- низкоуровневые вызовы --------

        /// <summary>
        /// Единая точка входа для запросов к VK:
        /// глобальный семафор + минимальный интервал между запросами + таймаут.
        /// </summary>
        private static async Task<(bool ok, int status, string body)> VkPostAsync(
            string url,
            Dictionary<string, string> data,
            CancellationToken ct)
        {
            await _vkGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Держим интервал между запросами
                var sinceLast = DateTime.UtcNow - _lastVkRequestUtc;
                if (sinceLast < MinRequestInterval)
                {
                    var wait = MinRequestInterval - sinceLast;
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                }

                _lastVkRequestUtc = DateTime.UtcNow;

                using var content = new FormUrlEncodedContent(data);
                using var response = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return (response.IsSuccessStatusCode, (int)response.StatusCode, body);
            }
            finally
            {
                _vkGate.Release();
            }
        }

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
                var (ok, status, body) = await VkPostAsync(url, data, cts.Token).ConfigureAwait(false);
                bool edited = ok && !body.Contains("\"error\"");
                if (!edited)
                    LogVk($"[VK] TryEditMessage: HTTP {status}. Ответ: {Trim(body)}");
                return edited;
            }
            catch (OperationCanceledException)
            {
                LogVk($"[VK] TryEditMessage: таймаут ({VkTimeoutMs} мс)");
                return false;
            }
            catch (Exception ex)
            {
                LogVk($"[VK] TryEditMessage: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TryDeleteMessageAsync(int messageId)
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
                var (ok, status, body) = await VkPostAsync(url, data, cts.Token).ConfigureAwait(false);
                bool deleted = ok && !body.Contains("\"error\"");
                if (!deleted)
                    LogVk($"[VK] TryDeleteMessage: HTTP {status}. Ответ: {Trim(body)}");
                return deleted;
            }
            catch (OperationCanceledException)
            {
                LogVk($"[VK] TryDeleteMessage: таймаут ({VkTimeoutMs} мс)");
                return false;
            }
            catch (Exception ex)
            {
                LogVk($"[VK] TryDeleteMessage: {ex.Message}");
                return false;
            }
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
                var (ok, status, body) = await VkPostAsync(url, data, cts.Token).ConfigureAwait(false);
                if (!ok || body.Contains("\"error\""))
                {
                    LogVk($"[VK] SendMessage: HTTP {status}. Ответ: {Trim(body)}");
                    return null;
                }

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
            catch (OperationCanceledException)
            {
                LogVk($"[VK] SendMessage: таймаут ({VkTimeoutMs} мс)");
                return null;
            }
            catch (Exception ex)
            {
                LogVk($"[VK] SendMessage: {ex.Message}");
                return null;
            }
        }

        private static string Trim(string s, int max = 200)
            => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

        private static void LogVk(string message)
        {
            try { MainWindow.Current?.LogExternal(message, Brushes.Gray); } catch { }
        }
    }
}