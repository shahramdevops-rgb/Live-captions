using System;
using System.Globalization;
using System.IO;
using System.Text;
using LiveCaptionsGemini.Providers;

namespace LiveCaptionsGemini.Logging;

/// <summary>
/// Appends every sent text and its answer to transcript.txt (readable, copyable after the session).
/// Thread-safe. The overlay itself can't be copied from because it never takes focus.
/// </summary>
internal sealed class TranscriptLogger
{
    public const string FileName = "transcript.txt";

    private readonly object _lock = new();
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private bool _sessionHeaderWritten;

    public TranscriptLogger(string path) => Path = path;

    public string Path { get; }

    /// <summary>Returns null on success, else why the entry couldn't be written.</summary>
    public string? Append(string sent, ProviderResult result, DateTime? time = null)
    {
        var t = time ?? DateTime.Now;
        var entry = new StringBuilder();

        lock (_lock)
        {
            if (!_sessionHeaderWritten)
                entry.Append("\r\n===== Session ").Append(t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(" =====\r\n");

            var ttft = result.FirstTokenMs is long ms ? $"{ms} ms" : "—";
            entry.Append("\r\n[").Append(t.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append("] ")
                 .Append(result.Model).Append(" · first token ").Append(ttft)
                 .Append(" · total ").Append(result.TotalMs.ToString(CultureInfo.InvariantCulture)).Append(" ms\r\n");
            entry.Append("Q: ").Append(Indent(sent)).Append("\r\n");
            if (result.Text.Trim().Length > 0) entry.Append("A: ").Append(Indent(result.Text)).Append("\r\n");
            if (result.Error is not null) entry.Append("ERROR: ").Append(Indent(result.Error)).Append("\r\n");

            try
            {
                File.AppendAllText(Path, entry.ToString(), Utf8NoBom);
                _sessionHeaderWritten = true;
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"{FileName} not written: {ex.Message}";
            }
        }
    }

    // Continuation lines line up under the text after "Q: " / "A: ".
    private static string Indent(string text) => text.Trim().ReplaceLineEndings("\r\n   ");
}
