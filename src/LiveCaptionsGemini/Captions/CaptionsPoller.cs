using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiveCaptionsGemini.Captions;

/// <summary>
/// Reads the captions every <c>pollMs</c> on a background thread and raises <see cref="Changed"/>
/// (on that background thread) when the text or availability changes. Used for the live preview;
/// a send always reads fresh instead of relying on this.
/// </summary>
internal sealed class CaptionsPoller : IDisposable
{
    /// <summary>New caption text, or null when Live Captions isn't available.</summary>
    public event Action<string?>? Changed;

    private readonly CaptionsReader _reader;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public CaptionsPoller(CaptionsReader reader, int pollMs)
    {
        _reader = reader;
        _interval = TimeSpan.FromMilliseconds(pollMs);
    }

    public void Start() => _loop ??= Task.Run(() => LoopAsync(_cts.Token));

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(1000);
        }
        catch (AggregateException)
        {
        }
        _cts.Dispose();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_interval);
        string? last = "\0"; // forces the first event, even when Read() returns null
        try
        {
            do
            {
                var text = _reader.Read();
                if (text == last) continue;
                last = text;
                Changed?.Invoke(text);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
