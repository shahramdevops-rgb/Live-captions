using System;
using System.Globalization;
using System.IO;
using System.Text;
using LiveCaptionsGemini.Providers;

namespace LiveCaptionsGemini.Logging;

/// <summary>Appends one row per request to log.csv. Thread-safe.</summary>
internal sealed class CsvLogger
{
    public const string FileName = "log.csv";
    private const string Header = "time,provider,model,ttft_ms,total_ms,input_chars,output_chars,error";

    private readonly object _lock = new();
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public CsvLogger(string path) => Path = path;

    public string Path { get; }

    /// <summary>Returns null on success, else why the row couldn't be written (e.g. file open in Excel).</summary>
    public string? Append(string input, ProviderResult result, DateTime? time = null)
    {
        var row = string.Join(",",
            (time ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            Escape(result.Provider),
            Escape(result.Model),
            result.FirstTokenMs?.ToString(CultureInfo.InvariantCulture) ?? "",
            result.TotalMs.ToString(CultureInfo.InvariantCulture),
            input.Length.ToString(CultureInfo.InvariantCulture),
            result.Text.Length.ToString(CultureInfo.InvariantCulture),
            Escape((result.Error ?? "").ReplaceLineEndings(" "))); // one physical line per row

        lock (_lock)
        {
            try
            {
                var isNew = !File.Exists(Path) || new FileInfo(Path).Length == 0;
                File.AppendAllText(Path, (isNew ? Header + "\r\n" : "") + row + "\r\n", Utf8NoBom);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"{FileName} not written: {ex.Message}";
            }
        }
    }

    // RFC 4180: quote fields containing comma, quote or line break; double inner quotes.
    private static string Escape(string field) =>
        field.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 ? field : "\"" + field.Replace("\"", "\"\"") + "\"";
}
