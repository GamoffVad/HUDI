using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HudiPet;

internal record ChatMessage(string Role, string Content);
internal record ModelReply(string Text, bool Truncated = false);
internal interface IModelClient
{
    Task<ModelReply> CompleteAsync(ModelSettings settings, string key, IReadOnlyList<ChatMessage> messages, IProgress<string>? progress, CancellationToken cancellation);
    Task<string[]> ListModelsAsync(ModelSettings settings, string key, CancellationToken cancellation);
}

internal sealed class HttpModelClient : IModelClient, IDisposable
{
    private readonly HttpClient client;
    internal HttpModelClient(HttpMessageHandler? handler = null)
    {
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }
    public void Dispose() => client.Dispose();
    internal static Dictionary<string, object> Payload(ModelSettings settings, IReadOnlyList<ChatMessage> messages)
    {
        var body = new Dictionary<string, object> { ["model"] = settings.Model.Trim(), ["stream"] = settings.Stream };
        if (settings.Protocol == "responses")
        {
            body["input"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray();
            body["instructions"] = settings.SystemPrompt;
            body["max_output_tokens"] = settings.MaxOutputTokens; body["store"] = false;
        }
        else
        {
            var input = new List<ChatMessage>();
            if (!string.IsNullOrWhiteSpace(settings.SystemPrompt)) input.Add(new("system", settings.SystemPrompt));
            input.AddRange(messages);
            body["messages"] = input.Select(m => new { role = m.Role, content = m.Content }).ToArray();
            body[settings.ChatTokenParameter] = settings.MaxOutputTokens;
        }
        if (settings.SendTemperature) body["temperature"] = settings.Temperature;
        return body;
    }
    private static HttpRequestMessage Request(ModelSettings settings, string key, string route, object? body = null)
    {
        var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, settings.Endpoint(route));
        if (settings.UseApiKey)
        {
            if(settings.Provider=="anthropic"&&route=="models")request.Headers.Add("x-api-key",key.Trim());
            else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        }
        if(settings.Provider=="anthropic")request.Headers.Add("anthropic-version","2023-06-01");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(body is not null && settings.Stream ? "text/event-stream" : "application/json"));
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
    }
    private static async Task EnsureSuccess(HttpResponseMessage response, string key, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = "";
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (json.RootElement.TryGetProperty("error", out var error))
                detail = error.ValueKind == JsonValueKind.String ? error.GetString()! : error.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";
        }
        catch (JsonException) { }
        if (!string.IsNullOrEmpty(key)) detail = detail.Replace(key, "[ключ скрыт]", StringComparison.Ordinal);
        detail = detail.Length > 350 ? detail[..350] + "…" : detail;
        string message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Сервис отклонил API-ключ. Проверьте его в настройках.",
            HttpStatusCode.Forbidden => "У ключа нет доступа к этому сервису или модели.",
            HttpStatusCode.NotFound => "Адрес API или модель не найдены. Проверьте формат API и название модели.",
            HttpStatusCode.TooManyRequests => "Сервис ограничил запросы: проверьте баланс и лимиты аккаунта.",
            _ => $"Сервис вернул ошибку {(int)response.StatusCode}."
        };
        throw new ModelException(message + (string.IsNullOrWhiteSpace(detail) ? "" : "\n" + detail));
    }
    public async Task<string[]> ListModelsAsync(ModelSettings settings, string key, CancellationToken cancellation)
    {
        settings.Validate(key, false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            using var request = Request(settings, key, "models");
            using var response = await client.SendAsync(request, timeout.Token);
            await EnsureSuccess(response, key, timeout.Token);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root=json.RootElement;
            var models=root.ValueKind==JsonValueKind.Array?root:root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("data",out var data)?data:default;
            if (models.ValueKind != JsonValueKind.Array)
                throw new ModelException("Сервис не вернул список моделей. Название можно ввести вручную.");
            return models.EnumerateArray().Where(m => m.TryGetProperty("id", out _)).Select(m => m.GetProperty("id").GetString() ?? "")
                .Where(id => id.Length > 0).Distinct().OrderBy(id => id).ToArray();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new ModelException("Сервис не ответил за указанное время."); }
        catch (HttpRequestException) { throw new ModelException("Не удалось подключиться. Проверьте адрес сервера и сеть."); }
        catch (JsonException) { throw new ModelException("Сервис вернул неподдерживаемый ответ вместо списка моделей."); }
    }

    private static string ResponseText(JsonElement root)
    {
        var result = new StringBuilder();
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            foreach (var item in output.EnumerateArray())
                if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) result.Append(text.GetString());
                        else if (part.TryGetProperty("refusal", out var refusal)) result.Append(refusal.GetString());
                    }
        return result.ToString();
    }
    public async Task<ModelReply> CompleteAsync(ModelSettings settings, string key, IReadOnlyList<ChatMessage> messages, IProgress<string>? progress, CancellationToken cancellation)
    {
        settings.Validate(key);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        var ct = timeout.Token;
        try
        {
            using var request = Request(settings, key, settings.Protocol == "responses" ? "responses" : "chat/completions", Payload(settings, messages));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureSuccess(response, key, ct);
            if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
                return await ReadStream(response, settings.Protocol, progress, ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            string text; bool truncated;
            if (settings.Protocol == "responses")
            {
                if (json.RootElement.TryGetProperty("status", out var status) && status.GetString() == "failed") throw new ModelException("Модель не смогла завершить ответ.");
                text = ResponseText(json.RootElement); truncated = status.ValueKind == JsonValueKind.String && status.GetString() == "incomplete";
            }
            else
            {
                var choice = json.RootElement.GetProperty("choices")[0];
                var message = choice.GetProperty("message");
                text = message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString()! : "";
                if (string.IsNullOrEmpty(text) && message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String) text = refusal.GetString()!;
                truncated = choice.TryGetProperty("finish_reason", out var reason) && reason.GetString() == "length";
            }
            if (string.IsNullOrWhiteSpace(text)) throw new ModelException("Модель вернула пустой ответ. Проверьте модель и лимит токенов.");
            progress?.Report(text); return new(text, truncated);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new ModelException("Время ожидания истекло. Можно увеличить тайм-аут в настройках."); }
        catch (HttpRequestException) { throw new ModelException("Соединение с моделью прервалось. Проверьте сеть и повторите запрос."); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        { throw new ModelException("Формат ответа не совпадает с настройками. Проверьте выбранный API."); }
    }
    private static async Task<ModelReply> ReadStream(HttpResponseMessage response, string protocol, IProgress<string>? progress, CancellationToken ct)
    {
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        var text = new StringBuilder(); var data = new StringBuilder(); bool terminal = false, truncated = false;
        void Process()
        {
            string value = data.ToString(); data.Clear(); if (string.IsNullOrWhiteSpace(value)) return;
            if (value.Trim() == "[DONE]") { terminal = true; return; }
            using var json = JsonDocument.Parse(value); var root = json.RootElement;
            if (root.TryGetProperty("error", out _)) throw new ModelException("Сервис сообщил об ошибке во время ответа. Повторите запрос.");
            if (protocol == "responses")
            {
                string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                if (type is "response.output_text.delta" or "response.refusal.delta") text.Append(root.GetProperty("delta").GetString());
                else if (type is "response.completed" or "response.incomplete")
                {
                    terminal = true; truncated = type == "response.incomplete";
                    if (text.Length == 0 && root.TryGetProperty("response", out var full)) text.Append(ResponseText(full));
                }
                else if (type is "error" or "response.failed") throw new ModelException("Модель не смогла завершить ответ.");
            }
            else if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta))
                {
                    if (delta.TryGetProperty("content", out var chunk) && chunk.ValueKind == JsonValueKind.String) text.Append(chunk.GetString());
                    else if (delta.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String) text.Append(refusal.GetString());
                }
                if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                { terminal = true; truncated = reason.GetString() == "length"; }
            }
            if (text.Length > 500_000) throw new ModelException("Ответ слишком большой. Уменьшите лимит токенов.");
            progress?.Report(text.ToString());
        }
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0) { Process(); if (terminal) break; }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            { if (data.Length > 0) data.Append('\n'); data.Append(line.AsSpan(5).TrimStart()); }
        }
        if (data.Length > 0) Process();
        if (!terminal) throw new ModelException("Ответ оборвался до завершения. Повторите запрос.");
        if (string.IsNullOrWhiteSpace(text.ToString())) throw new ModelException("Модель не вернула текст. Проверьте лимит ответа.");
        return new(text.ToString(), truncated);
    }
}
