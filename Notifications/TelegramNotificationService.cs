using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TERMINAL.Notifications
{
    public class TelegramNotificationService : INotificationService
    {
        private static readonly HttpClient _http;
        private const int SendTimeoutMs = 5000;

        static TelegramNotificationService()
        {
            var proxy = new WebProxy("http://127.0.0.1:10809");
            var handler = new HttpClientHandler { Proxy = proxy, UseProxy = true };
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

            using var cts = new CancellationTokenSource(SendTimeoutMs);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new TimeoutException($"Telegram: таймаут отправки ({SendTimeoutMs} мс)");
            }
            catch (HttpRequestException ex)
            {
                throw new TimeoutException($"Telegram: сеть/прокси недоступны ({ex.Message})");
            }

            using (response)
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Telegram API вернул {(int)response.StatusCode}: {body}");

                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("result", out var result) &&
                        result.TryGetProperty("message_id", out var msgId))
                    {
                        // ★ Telegram отдаёт message_id как число; в новых чатах он может
                        //   превысить int.MaxValue — читаем как long и приводим.
                        if (msgId.TryGetInt64(out long idLong))
                            return unchecked((int)idLong);

                        // На всякий случай — если вдруг строка
                        if (msgId.ValueKind == JsonValueKind.String &&
                            long.TryParse(msgId.GetString(), out long idFromStr))
                            return unchecked((int)idFromStr);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TG Send] parse error: {ex.Message}; body={body}");
                }

                System.Diagnostics.Debug.WriteLine($"[TG Send] message_id not found; body={body}");
                return null;
            }
        }
        public async Task<bool> EditMessageAsync(int messageId, string newText)
        {
            string url = $"https://api.telegram.org/bot{_token}/editMessageText";
            var payload = new
            {
                chat_id = _chatId,
                message_id = messageId,
                text = newText ?? "",
                parse_mode = "HTML"
            };
            string json = JsonSerializer.Serialize(payload);

            using var cts = new CancellationTokenSource(SendTimeoutMs);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new TimeoutException($"Telegram edit: таймаут ({SendTimeoutMs} мс)");
            }
            catch (HttpRequestException ex)
            {
                throw new TimeoutException($"Telegram edit: сеть/прокси недоступны ({ex.Message})");
            }

            using (response)
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                    return true;

                // Текст не изменился — с точки зрения вызывающего это успех.
                if (body.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                    return true;

                // ★ "message to edit not found" — id устарел/удалён. Возвращаем false,
                //   чтобы вызывающий код отправил новое сообщение и сохранил новый id.
                System.Diagnostics.Debug.WriteLine(
                    $"[TG Edit] id={messageId} HTTP {(int)response.StatusCode} body={body}");
                return false;
            }
        }
    }