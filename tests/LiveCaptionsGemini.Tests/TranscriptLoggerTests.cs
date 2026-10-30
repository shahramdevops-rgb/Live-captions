using System;
using System.IO;
using LiveCaptionsGemini.Logging;
using LiveCaptionsGemini.Providers;

namespace LiveCaptionsGemini.Tests;

internal static class TranscriptLoggerTests
{
    public static void Run(Action<string, string, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lcg-transcript-{Guid.NewGuid():N}.txt");
        try
        {
            var log = new TranscriptLogger(path);
            var t = new DateTime(2026, 10, 30, 14, 52, 37);
            log.Append("What is middleware in .NET Core?",
                new ProviderResult("Gemini", "gemini-3.5-flash-lite", "Yeah, so middleware is...\nIt handles requests.", 832, 1165, null), t);
            log.Append("And caching?",
                new ProviderResult("Gemini", "gemini-3.5-flash-lite", "", null, 33000, "HTTP 429: quota"), t.AddSeconds(30));

            check("transcript format", File.ReadAllText(path).Replace("\r\n", "\n"),
                "\n===== Session 2026-10-30 14:52 =====\n" +
                "\n[14:52:37] gemini-3.5-flash-lite · first token 832 ms · total 1165 ms\n" +
                "Q: What is middleware in .NET Core?\n" +
                "A: Yeah, so middleware is...\n   It handles requests.\n" +
                "\n[14:53:07] gemini-3.5-flash-lite · first token — · total 33000 ms\n" +
                "Q: And caching?\n" +
                "ERROR: HTTP 429: quota\n");

            // A new logger = a new app run → a new session header, appended to the same file.
            new TranscriptLogger(path).Append("Next", new ProviderResult("Gemini", "m", "ok", 1, 2, null), t.AddHours(1));
            var sessions = File.ReadAllText(path).Split("===== Session").Length - 1;
            check("new app run appends a new session", $"{sessions}", "2");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
