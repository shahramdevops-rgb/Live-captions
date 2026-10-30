using System;
using System.IO;
using LiveCaptionsGemini.Config;

namespace LiveCaptionsGemini.Tests;

/// <summary>Settings window values written back to config.json without touching anything else.</summary>
internal static class ConfigSaveTests
{
    public static void Run(Action<string, string, string> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lcg-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, AppConfig.FileName);
        try
        {
            File.WriteAllText(path, """
                {
                  // my comment stays
                  "apiKey": "test-key-xyz",
                  "model": "gemini-3.5-flash-lite",
                  "systemPrompt": "old prompt with \"quotes\"",
                  "maxOutputTokens": 0,          // 0 = not sent
                  "keepHistory": true,
                  "maxHistoryTurns": 6,
                  "fontSize": 11,
                  "skipKey": "F9",
                }
                """);

            var prompt = "Line 1 with \"quotes\", $1, a \\ backslash and {braces}\r\nLine 2 — non-ASCII ✓\r\n";
            var config = AppConfig.Load(path);
            config.ApplySessionSettings(prompt, 300, 10, 14.5f);
            config.SaveSessionSettings();
            var text = File.ReadAllText(path);
            var reloaded = AppConfig.Load(path);

            check("comments kept", $"{text.Contains("// my comment stays")}, {text.Contains("// 0 = not sent")}", "True, True");
            check("API key and other fields untouched", $"{reloaded.ApiKey == "test-key-xyz"}, {reloaded.Model}, {reloaded.SkipKey}",
                "True, gemini-3.5-flash-lite, F9");
            check("prompt round-trips (\\r\\n → \\n, trimmed)", reloaded.SystemPrompt,
                "Line 1 with \"quotes\", $1, a \\ backslash and {braces}\nLine 2 — non-ASCII ✓");
            check("numbers saved", $"{reloaded.MaxOutputTokens}, {reloaded.MaxHistoryTurns}, {reloaded.KeepHistory}, {reloaded.FontSize}",
                "300, 10, True, 14.5");

            config.ApplySessionSettings("p", 0, 0, 99f);
            config.SaveSessionSettings();
            reloaded = AppConfig.Load(path);
            check("history 0 → keepHistory false; font clamped", $"{reloaded.MaxHistoryTurns}, {reloaded.KeepHistory}, {reloaded.FontSize}",
                "0, False, 32");

            File.WriteAllText(path, "{ \"apiKey\": \"k\" }");
            config = AppConfig.Load(path);
            config.ApplySessionSettings("added", 200, 3, 12f);
            config.SaveSessionSettings();
            reloaded = AppConfig.Load(path);
            check("missing fields are added", $"{reloaded.SystemPrompt}, {reloaded.MaxOutputTokens}, {reloaded.MaxHistoryTurns}, {reloaded.FontSize}, key={reloaded.ApiKey}",
                "added, 200, 3, 12, key=k");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
