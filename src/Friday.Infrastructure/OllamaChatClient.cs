using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Friday.Core;

namespace Friday.Infrastructure;

public sealed record ChatDelta(string Content, IReadOnlyList<ToolCall> ToolCalls, bool Done)
{
    /// <summary>Son satırda Ollama'nın ölçümleri: komutun işlenmesi, düşünme ve cevap üretimi.</summary>
    public ChatTimings? Timings { get; init; }

    /// <summary>Modelin düşünme metninin uzunluğu (karakter); seslendirilmez.</summary>
    public int ThinkingLength { get; init; }
}

public sealed record ChatTimings(int PromptTokens, double PromptMs, int GeneratedTokens, double GenerateMs, double LoadMs);

/// <summary>Ollama'nın /api/chat ucuyla akışlı (satır satır JSON) konuşur.</summary>
public sealed class OllamaChatClient(HttpClient http, OllamaServer server)
{
    /// <summary>Model son istekten bu kadar sonra GPU'dan boşaltılır (ayardaki GpuIdleMinutes).</summary>
    public string KeepAlive { get; set; } = "5m";

    /// <summary>
    /// Isınma ve sohbet istekleri aynı bağlam boyutunu kullanmalı; num_ctx değişirse Ollama modeli GPU'ya
    /// baştan yükler (~4 sn). 8192; araç tanımları (~2000 belirteç), hafıza ve sohbet geçmişi için yer bırakır.
    /// </summary>
    private static JsonObject Options() => new() { ["temperature"] = 0.6, ["num_ctx"] = 8192 };

    /// <summary>Modeli hemen GPU'dan boşaltır (ör. görme modeli işini bitirince).</summary>
    public async Task UnloadAsync(string model, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["model"] = model, ["keep_alive"] = 0 };
        using var response = await http.PostAsJsonAsync(new Uri(server.BaseAddress, "api/generate"), body, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Modeli GPU'ya yükler; ilk soruda yükleme beklenmesin diye açılışta çağrılır.</summary>
    public async Task WarmUpAsync(string model, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["model"] = model, ["keep_alive"] = KeepAlive, ["options"] = Options() };
        using var response = await http.PostAsJsonAsync(new Uri(server.BaseAddress, "api/generate"), body, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"'{model}' modeli yüklü değil. setup.ps1'i çalıştırın.");
        }

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Akışsız tek bir istek: pano işlemleri ve ekran yorumlama gibi ara işler için. Görseller JPEG/PNG baytları
    /// olarak verilir; Gemma 4 bunları yerel olarak "görür".
    /// </summary>
    public async Task<string> CompleteAsync(
        string model, string system, string user, IReadOnlyList<byte[]>? images, string? keepAlive,
        CancellationToken cancellationToken)
    {
        var message = new JsonObject { ["role"] = "user", ["content"] = user };
        if (images is { Count: > 0 })
        {
            message["images"] = new JsonArray(images.Select(image => (JsonNode)Convert.ToBase64String(image)).ToArray());
        }

        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system }, message),
            ["stream"] = false,
            ["think"] = false,
            ["keep_alive"] = keepAlive ?? KeepAlive,
            ["options"] = Options()
        };
        using var response = await http.PostAsJsonAsync(new Uri(server.BaseAddress, "api/chat"), body, cancellationToken)
            .ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Ollama hatası ({(int)response.StatusCode}): {text}");
        }

        return JsonNode.Parse(text)?["message"]?["content"]?.GetValue<string>()?.Trim() ?? "";
    }

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        string model,
        IReadOnlyList<ChatMessage> messages,
        JsonArray tools,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = BuildRequest(model, messages, tools, KeepAlive);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server.BaseAddress, "api/chat"))
        {
            Content = JsonContent.Create(body)
        };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"Ollama hatası ({(int)response.StatusCode}): {error}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (ParseLine(line) is { } delta)
            {
                yield return delta;
                if (delta.Done)
                {
                    yield break;
                }
            }
        }
    }

    internal static JsonObject BuildRequest(string model, IReadOnlyList<ChatMessage> messages, JsonArray tools, string keepAlive = "5m")
    {
        var items = new JsonArray();
        foreach (var message in messages)
        {
            var item = new JsonObject
            {
                ["role"] = message.Role.ToString().ToLowerInvariant(),
                ["content"] = message.Content
            };
            if (message.ToolName is not null)
            {
                item["tool_name"] = message.ToolName;
            }

            if (message.ToolCalls.Count > 0)
            {
                var calls = new JsonArray();
                foreach (var call in message.ToolCalls)
                {
                    var arguments = new JsonObject();
                    foreach (var (key, value) in call.Arguments)
                    {
                        arguments[key] = value;
                    }

                    calls.Add(new JsonObject
                    {
                        ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = arguments }
                    });
                }

                item["tool_calls"] = calls;
            }

            items.Add(item);
        }

        return new JsonObject
        {
            ["model"] = model,
            ["messages"] = items,
            ["tools"] = tools.DeepClone(),
            ["stream"] = true,
            // Gemma 4 düşünme kapalıyken araç çağırmak yerine "aracı kullanacağım" diye anlatıyor; açıkken
            // doğru aracı çağırıyor ve kısa sorularda yalnızca ~0,5 sn ekliyor. Düşünce metni ayrı bir alanda
            // ("thinking") gelir, seslendirilmez.
            ["think"] = true,
            ["keep_alive"] = keepAlive,
            ["options"] = Options()
        };
    }

    internal static ChatDelta? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var node = JsonNode.Parse(line);
        if (node?["error"]?.GetValue<string>() is { } error)
        {
            throw new HttpRequestException($"Ollama hatası: {error}");
        }

        var message = node?["message"];
        var content = message?["content"]?.GetValue<string>() ?? "";
        var calls = new List<ToolCall>();
        if (message?["tool_calls"] is JsonArray toolCalls)
        {
            foreach (var call in toolCalls)
            {
                var function = call?["function"];
                var name = function?["name"]?.GetValue<string>();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (function?["arguments"] is JsonObject values)
                {
                    foreach (var (key, value) in values)
                    {
                        arguments[key] = value?.GetValueKind() == JsonValueKind.String
                            ? value.GetValue<string>()
                            : value?.ToJsonString() ?? "";
                    }
                }

                calls.Add(new ToolCall(name, arguments));
            }
        }

        var done = node?["done"]?.GetValue<bool>() ?? false;
        var thinking = message?["thinking"]?.GetValue<string>()?.Length ?? 0;
        ChatTimings? timings = null;
        if (done)
        {
            static double Ms(JsonNode? value) => (value?.GetValue<long>() ?? 0) / 1_000_000.0;
            timings = new ChatTimings(
                node?["prompt_eval_count"]?.GetValue<int>() ?? 0,
                Ms(node?["prompt_eval_duration"]),
                node?["eval_count"]?.GetValue<int>() ?? 0,
                Ms(node?["eval_duration"]),
                Ms(node?["load_duration"]));
        }

        return new ChatDelta(content, calls, done) { Timings = timings, ThinkingLength = thinking };
    }
}
