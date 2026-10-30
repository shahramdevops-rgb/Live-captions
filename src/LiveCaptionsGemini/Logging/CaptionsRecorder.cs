using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using LiveCaptionsGemini.Captions;

namespace LiveCaptionsGemini.Logging;

/// <summary>
/// Writes everything Live Captions shows during the session to captions.txt, whether it was sent or not.
/// Live Captions keeps correcting its last few words, so while someone is speaking the last
/// <see cref="HeldBackWords"/> words are held back; after <see cref="IdleFlush"/> without changes
/// everything is written and the paragraph ends. New text is found with <see cref="AnchorText"/>,
/// so text that merely scrolls is not written twice. Thread-safe.
/// </summary>
internal sealed class CaptionsRecorder : IDisposable
{
    public const string FileName = "captions.txt";
    public const int HeldBackWords = 6;
    public static readonly TimeSpan IdleFlush = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Raised (on a background thread) when writing fails; only when the message changes.</summary>
    public event Action<string>? WriteFailed;

    private readonly object _lock = new();
    private readonly Action<string> _write;
    private readonly Func<DateTime> _now;
    private Timer? _timer;

    private bool _started;
    private string? _latest;       // caption text now
    private DateTime _lastChange;
    private string? _anchor;       // caption text up to what has been written
    private bool _headerWritten;
    private bool _paragraphOpen;
    private string? _lastError;

    public CaptionsRecorder(string path) : this(text => File.AppendAllText(path, text, Utf8NoBom), () => DateTime.Now) { }

    /// <summary>For tests: custom output and clock.</summary>
    internal CaptionsRecorder(Action<string> write, Func<DateTime> now)
    {
        _write = write;
        _now = now;
    }

    /// <summary>Checks once a second whether there is stable text to write.</summary>
    public void Start() => _timer ??= new Timer(_ => Tick(), null, 1000, 1000);

    /// <summary>From the captions poller (any thread). Null = captions not available.</summary>
    public void OnCaptionsChanged(string? text)
    {
        if (text is null) return;
        lock (_lock)
        {
            if (!_started)
            {
                // Whatever is already on screen when the app starts belongs to an earlier session.
                _anchor = text;
                _started = true;
            }
            _latest = text;
            _lastChange = _now();
        }
    }

    public void Tick()
    {
        lock (_lock) Commit(idle: _now() - _lastChange >= IdleFlush);
    }

    /// <summary>Writes everything not written yet (app closing).</summary>
    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        lock (_lock)
        {
            Commit(idle: true);
            if (_headerWritten) TryWrite("\r\n");
        }
    }

    // Caller holds _lock.
    private void Commit(bool idle)
    {
        if (_latest is null) return;

        var chunk = AnchorText.Extract(_latest, _anchor);
        string text, anchor;
        if (idle)
        {
            if (chunk.Length == 0)
            {
                _paragraphOpen = false; // a pause ends the paragraph
                return;
            }
            text = chunk;
            anchor = _latest;
        }
        else
        {
            var words = chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= HeldBackWords) return;
            var held = string.Join(' ', words[^HeldBackWords..]);
            if (!_latest.EndsWith(held, StringComparison.Ordinal)) return; // chunk is always a suffix; be safe
            text = string.Join(' ', words[..^HeldBackWords]);
            anchor = _latest[..^held.Length].TrimEnd();
        }

        var output = new StringBuilder();
        if (!_headerWritten)
            output.Append("\r\n===== Session ").Append(_now().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(" =====");
        if (_paragraphOpen)
            output.Append(' ');
        else
            output.Append("\r\n[").Append(_now().ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append("] ");
        output.Append(text);

        if (!TryWrite(output.ToString())) return; // keep the anchor: retried on the next tick
        _headerWritten = true;
        _paragraphOpen = !idle;
        _anchor = anchor;
    }

    private bool TryWrite(string text)
    {
        try
        {
            _write(text);
            _lastError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var error = $"{FileName} not written: {ex.Message}";
            if (error != _lastError) WriteFailed?.Invoke(error);
            _lastError = error;
            return false;
        }
    }
}
