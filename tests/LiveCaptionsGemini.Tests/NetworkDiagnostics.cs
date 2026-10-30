using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiveCaptionsGemini.Config;

namespace LiveCaptionsGemini.Tests;

/// <summary>
/// Tries the Gemini endpoints with different HTTP versions / modes to find what the network allows:
///   dotnet run --project tests/LiveCaptionsGemini.Tests -- --diag
/// The API key is never printed.
/// </summary>
internal static class NetworkDiagnostics
{
    /// <param name="model">Overrides the model from config.json (--model name).</param>
    public static async Task RunAsync(string? model)
    {
        var path = AppConfig.Find();
        if (path is null) { Console.WriteLine("no config.json"); return; }
        var config = AppConfig.Load(path);
        model ??= config.Model;
        Console.WriteLine($"model: {model}");
        var baseUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{model}";

        var proxy = HttpClient.DefaultProxy.GetProxy(new Uri(baseUrl));
        Console.WriteLine($"system proxy for Gemini: {(proxy is null ? "none" : proxy.Host + ":" + proxy.Port)}");
        Console.WriteLine($"HTTPS_PROXY env: {(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HTTPS_PROXY")) ? "not set" : "set")}");

        var body = "{\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"Say hi in three words.\"}]}]," +
                   "\"generationConfig\":{\"thinkingConfig\":{\"thinkingLevel\":\"minimal\"}}}";
        var bodyNoThinking = "{\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"Say hi in three words.\"}]}]}";

        var bodyLow = body.Replace("\"minimal\"", "\"low\"");
        var stream = baseUrl + ":streamGenerateContent?alt=sse";

        await Try("GET model, HTTP/2", HttpMethod.Get, baseUrl, null, HttpVersion.Version20);
        await Try("GET model, HTTP/1.1", HttpMethod.Get, baseUrl, null, HttpVersion.Version11);
        await Try("stream, thinking minimal, HTTP/2", HttpMethod.Post, stream, body, HttpVersion.Version20);
        await Try("stream, thinking low, HTTP/2", HttpMethod.Post, stream, bodyLow, HttpVersion.Version20);
        await Try("stream, thinking low, HTTP/1.1", HttpMethod.Post, stream, bodyLow, HttpVersion.Version11);
        await Try("no stream, no thinkingConfig, HTTP/2", HttpMethod.Post, baseUrl + ":generateContent", bodyNoThinking, HttpVersion.Version20);
        await Try("stream, no thinkingConfig, HTTP/1.1", HttpMethod.Post, stream, bodyNoThinking, HttpVersion.Version11);

        async Task Try(string name, HttpMethod method, string url, string? json, Version version)
        {
            // Fresh client per attempt so connection reuse can't hide anything.
            using var client = new HttpClient(new SocketsHttpHandler()) { Timeout = TimeSpan.FromSeconds(45) };
            using var request = new HttpRequestMessage(method, url) { Version = version, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            request.Headers.Add("x-goog-api-key", config.ApiKey);
            if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var sw = Stopwatch.StartNew();
            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                var headersMs = sw.ElapsedMilliseconds;
                var text = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"  {(response.IsSuccessStatusCode ? "✓" : "✗")} {name}: HTTP {(int)response.StatusCode} " +
                                  $"v{response.Version} headers {headersMs} ms, total {sw.ElapsedMilliseconds} ms, {text.Length} bytes");
                if (!response.IsSuccessStatusCode) Console.WriteLine("      " + Short(text));
            }
            catch (Exception ex)
            {
                var msg = new StringBuilder(ex.GetType().Name + ": " + ex.Message);
                for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException) msg.Append(" → ").Append(inner.Message);
                Console.WriteLine($"  ✗ {name}: after {sw.ElapsedMilliseconds} ms: {msg}");
            }
        }
    }

    private static string Short(string s)
    {
        s = s.Replace("\n", " ").Trim();
        return s.Length > 250 ? s[..250] + "…" : s;
    }
}
