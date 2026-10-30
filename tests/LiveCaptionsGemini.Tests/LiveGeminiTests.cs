using System;
using System.Diagnostics;
using System.Threading.Channels;
using System.Threading.Tasks;
using LiveCaptionsGemini.Config;
using LiveCaptionsGemini.Providers;

namespace LiveCaptionsGemini.Tests;

/// <summary>
/// Real calls to the Gemini API with the key from config.json (run from the repo root):
///   dotnet run --project tests/LiveCaptionsGemini.Tests -- --live
/// The API key is never printed.
/// </summary>
internal static class LiveGeminiTests
{
    /// <param name="model">Overrides the model from config.json (--model name).</param>
    public static async Task<bool> RunAsync(string? model)
    {
        Console.WriteLine("Live Gemini");

        var path = AppConfig.Find();
        if (path is null)
        {
            Console.WriteLine("  - skipped: no config.json in the current directory");
            return true;
        }

        AppConfig config;
        try
        {
            config = AppConfig.Load(path);
        }
        catch (ConfigException ex)
        {
            Console.WriteLine($"  ✗ config.json could not be loaded:\n      {ex.Message.Replace("\n", "\n      ")}");
            return false;
        }
        if (!config.HasApiKey)
        {
            Console.WriteLine("  - skipped: no API key");
            return true;
        }

        if (model is not null) config.Model = model;
        Console.WriteLine($"  model={config.Model} thinking={config.ThinkingLevel} key={(config.ApiKeyFromEnv ? "env" : "config")}");
        var ok = true;
        var gemini = new GeminiProvider(config);

        // Warm-up: first opens TLS, second should reuse the connection.
        for (var i = 1; i <= 2; i++)
        {
            var sw = Stopwatch.StartNew();
            var error = await gemini.WarmUpAsync();
            ok &= Report($"warm-up #{i} ({sw.ElapsedMilliseconds} ms)", error);
            if (error is not null) return false;
        }

        var first = await Stream(gemini, "In one sentence: what is dependency injection in C#?");
        Console.WriteLine($"    (thinking level sent: \"{gemini.ThinkingLevel}\")");
        ok &= first;
        if (first)
        {
            // History: the model should know what was asked before.
            ok &= await Stream(gemini, "In at most six words: what topic did I just ask about?");
        }

        // Error paths: wrong model and wrong key must produce readable messages.
        var badModel = await new GeminiProvider(Copy(config, model: "gemini-does-not-exist")).WarmUpAsync();
        ok &= Expect("wrong model → readable error", badModel);
        var badKey = await new GeminiProvider(Copy(config, apiKey: "invalid-key-for-test")).WarmUpAsync();
        ok &= Expect("wrong key → readable error", badKey);

        return ok;
    }

    private static async Task<bool> Stream(IChatProvider provider, string question)
    {
        Console.WriteLine($"  > {question}");
        Console.Write("    ");
        var channel = Channel.CreateUnbounded<string>();
        var request = provider.StreamAsync(question, channel.Writer);
        var chunks = 0;
        await foreach (var chunk in channel.Reader.ReadAllAsync())
        {
            chunks++;
            Console.Write(chunk.Replace("\n", "\n    "));
        }
        Console.WriteLine();

        var result = await request;
        return Report($"stream: first token {result.FirstTokenMs} ms, total {result.TotalMs} ms, {chunks} chunks, {result.Text.Length} chars",
            result.Error ?? (result.Text.Length == 0 ? "empty answer" : null));
    }

    private static AppConfig Copy(AppConfig c, string? model = null, string? apiKey = null) => new()
    {
        ApiKey = apiKey ?? c.ApiKey,
        Model = model ?? c.Model,
        ThinkingLevel = c.ThinkingLevel,
        SystemPrompt = c.SystemPrompt,
    };

    private static bool Report(string name, string? error)
    {
        Console.WriteLine(error is null ? $"  ✓ {name}" : $"  ✗ {name}\n      {error}");
        return error is null;
    }

    private static bool Expect(string name, string? error)
    {
        Console.WriteLine(error is not null ? $"  ✓ {name}\n      {error}" : $"  ✗ {name}: no error returned");
        return error is not null;
    }
}
