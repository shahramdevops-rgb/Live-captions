using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LiveCaptionsGemini.Config;

namespace LiveCaptionsGemini.Providers;

/// <summary>Gemini REST API, streamGenerateContent over SSE.</summary>
internal sealed class GeminiProvider : IChatProvider
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";
    private static readonly TimeSpan WarmUpTimeout = TimeSpan.FromSeconds(15);

    private readonly string _apiKey;
    private readonly string _systemPrompt;
    private readonly int _maxOutputTokens;
    private readonly ChatHistory _history;
    // Thinking level actually sent ("" = no thinkingConfig). When the model rejects it we step down for the
    // rest of the session: "minimal" → "low" (some models don't support minimal) → none. Dropping it
    // straight away would make the model think at its default (high) level, the worst case for latency.
    private volatile string _thinkingLevel;

    public GeminiProvider(AppConfig config)
    {
        _apiKey = config.ApiKey;
        Model = config.Model;
        _systemPrompt = config.SystemPrompt;
        _thinkingLevel = config.ThinkingLevel;
        _maxOutputTokens = config.MaxOutputTokens;
        _history = new ChatHistory(config.KeepHistory ? config.MaxHistoryTurns : 0);
    }

    public string Name => "Gemini";
    public string Model { get; }

    /// <summary>The thinking level currently sent ("" = none), after any fallback.</summary>
    public string ThinkingLevel => _thinkingLevel;

    public async Task<string?> WarmUpAsync(CancellationToken ct = default)
    {
        if (_apiKey.Length == 0) return MissingKey;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(WarmUpTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + Uri.EscapeDataString(Model));
            request.Headers.Add("x-goog-api-key", _apiKey);
            using var response = await SharedHttp.Client.SendAsync(request, timeout.Token);
            return response.IsSuccessStatusCode ? null : await DescribeErrorAsync(response, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return $"No response within {WarmUpTimeout.TotalSeconds:0} s";
        }
        catch (HttpRequestException ex)
        {
            return "Network error: " + Describe(ex);
        }
    }

    // HttpRequestException's own message is generic ("An error occurred while sending the request");
    // the useful part is in the inner exceptions.
    private static string Describe(Exception ex)
    {
        var message = new StringBuilder(ex.Message);
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            message.Append(" → ").Append(inner.Message);
        return message.ToString();
    }

    public async Task<ProviderResult> StreamAsync(string text, ChannelWriter<string> output, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        long? firstTokenMs = null;
        var answer = new StringBuilder();
        string? error = null;

        try
        {
            if (_apiKey.Length == 0)
            {
                error = MissingKey;
            }
            else
            {
                var history = _history.Snapshot();
                while (true)
                {
                    var thinking = _thinkingLevel;
                    using var request = BuildRequest(text, history, thinking);
                    using var response = await SharedHttp.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        error = await DescribeErrorAsync(response, ct);
                        if (thinking.Length > 0 && response.StatusCode == HttpStatusCode.BadRequest &&
                            error.Contains("thinking", StringComparison.OrdinalIgnoreCase))
                        {
                            _thinkingLevel = thinking.Equals("minimal", StringComparison.OrdinalIgnoreCase) ? "low" : "";
                            error = null;
                            continue;
                        }
                        break;
                    }

                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    error = await ReadEventsAsync(stream, chunk =>
                    {
                        firstTokenMs ??= stopwatch.ElapsedMilliseconds;
                        answer.Append(chunk);
                        output.TryWrite(chunk);
                    }, ct);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            error = "Cancelled";
        }
        catch (HttpRequestException ex)
        {
            error = "Network error: " + Describe(ex);
        }
        catch (IOException ex)
        {
            error = "Connection lost: " + Describe(ex);
        }
        finally
        {
            output.TryComplete();
        }

        stopwatch.Stop();
        if (error is null) _history.Add(text, answer.ToString());
        return new ProviderResult(Name, Model, answer.ToString(), firstTokenMs, stopwatch.ElapsedMilliseconds, error);
    }

    private const string MissingKey = "API key missing: set apiKey in config.json or the GEMINI_API_KEY environment variable.";

    private HttpRequestMessage BuildRequest(string text, ChatHistory.Turn[] history, string thinkingLevel)
    {
        var contents = new JsonArray();
        foreach (var turn in history)
        {
            contents.Add(Content("user", turn.User));
            contents.Add(Content("model", turn.Model));
        }
        contents.Add(Content("user", text));

        var body = new JsonObject();
        if (_systemPrompt.Length > 0)
            body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = _systemPrompt }) };
        body["contents"] = contents;

        var generation = new JsonObject();
        if (thinkingLevel.Length > 0) generation["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = thinkingLevel };
        if (_maxOutputTokens > 0) generation["maxOutputTokens"] = _maxOutputTokens;
        if (generation.Count > 0) body["generationConfig"] = generation;

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}{Uri.EscapeDataString(Model)}:streamGenerateContent?alt=sse")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-goog-api-key", _apiKey);
        return request;

        static JsonObject Content(string role, string text) => new()
        {
            ["role"] = role,
            ["parts"] = new JsonArray(new JsonObject { ["text"] = text }),
        };
    }

    /// <summary>Reads SSE events until the stream ends. Returns an error message, or null if text arrived.</summary>
    private static async Task<string?> ReadEventsAsync(Stream stream, Action<string> onText, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var state = new StreamState();
        var data = new StringBuilder();

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Append(line.AsSpan(5).TrimStart());
            }
            else if (line.Length == 0 && data.Length > 0)
            {
                HandleEvent(data.ToString(), onText, state);
                data.Clear();
            }
            if (state.Error is not null) return state.Error;
        }
        if (data.Length > 0) HandleEvent(data.ToString(), onText, state);

        if (state.Error is not null) return state.Error;
        if (state.GotText) return null;
        if (state.BlockReason is not null) return $"Blocked by Gemini ({state.BlockReason})";
        if (state.FinishReason is not null and not "STOP") return $"No answer (finishReason: {state.FinishReason})";
        return "Empty answer";
    }

    private sealed class StreamState
    {
        public bool GotText;
        public string? Error;
        public string? FinishReason;
        public string? BlockReason;
    }

    private static void HandleEvent(string json, Action<string> onText, StreamState state)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return; // keep streaming; a malformed event shouldn't kill the answer
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                state.Error = err.TryGetProperty("message", out var m) ? m.GetString() : err.ToString();
                return;
            }
            if (root.TryGetProperty("promptFeedback", out var feedback) &&
                feedback.TryGetProperty("blockReason", out var block))
            {
                state.BlockReason = block.GetString();
            }
            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            {
                return;
            }

            var candidate = candidates[0];
            if (candidate.TryGetProperty("finishReason", out var finish)) state.FinishReason = finish.GetString();
            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
                if (!part.TryGetProperty("text", out var textElement)) continue;
                var text = textElement.GetString();
                if (string.IsNullOrEmpty(text)) continue;
                state.GotText = true;
                onText(text);
            }
        }
    }

    /// <summary>"HTTP 400 INVALID_ARGUMENT: message" from Google's error JSON, or the raw body.</summary>
    private static async Task<string> DescribeErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var code = (int)response.StatusCode;
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return $"HTTP {code}";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                ? doc.RootElement[0]
                : doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                var status = err.TryGetProperty("status", out var s) ? " " + s.GetString() : "";
                var message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                return $"HTTP {code}{status}: {message ?? body}";
            }
        }
        catch (JsonException)
        {
        }

        body = body.Trim();
        return body.Length == 0 ? $"HTTP {code} {response.ReasonPhrase}" : $"HTTP {code}: {Truncate(body, 300)}";
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
