using System;
using System.IO;
using LiveCaptionsGemini.Logging;
using LiveCaptionsGemini.Providers;

namespace LiveCaptionsGemini.Tests;

internal static class CsvLoggerTests
{
    public static void Run(Action<string, string, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lcg-test-{Guid.NewGuid():N}.csv");
        try
        {
            var logger = new CsvLogger(path);
            var time = new DateTime(2026, 10, 30, 14, 5, 9, 123);

            var ok = logger.Append("hello world", new ProviderResult("Gemini", "gemini-3.5-flash-lite", "Hi!", 563, 1276, null), time);
            logger.Append("abc", new ProviderResult("Gemini", "m", "", null, 33000, "HTTP 400: bad \"thing\", see\nline 2"), time);

            var lines = File.ReadAllText(path).Split("\r\n");
            check("returns null on success", ok ?? "null", "null");
            check("header written once", lines[0], "time,provider,model,ttft_ms,total_ms,input_chars,output_chars,error");
            check("success row", lines[1], "2026-10-30 14:05:09.123,Gemini,gemini-3.5-flash-lite,563,1276,11,3,");
            check("error row: empty ttft, quoted error, newline → space", lines[2],
                "2026-10-30 14:05:09.123,Gemini,m,,33000,3,0,\"HTTP 400: bad \"\"thing\"\", see line 2\"");
            check("exactly 2 data rows", $"{lines.Length}", "4"); // header, 2 rows, trailing empty

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var locked = logger.Append("x", new ProviderResult("Gemini", "m", "y", 1, 2, null));
                check("locked file (e.g. open in Excel) → error message, no crash",
                    locked?.StartsWith("log.csv not written") == true ? "reported" : locked ?? "null", "reported");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
