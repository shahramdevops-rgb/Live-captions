using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LiveCaptionsGemini.Captions;
using LiveCaptionsGemini.Providers;
using LiveCaptionsGemini.UI;

namespace LiveCaptionsGemini.Pipeline;

/// <summary>
/// Send / skip logic: keeps the anchor (caption text at the last mark), starts each request
/// immediately, and displays answers strictly in send order — a new answer appears only after
/// the previous one has finished streaming (one Channel per request + a chained display task).
/// </summary>
internal sealed class SendPipeline : IDisposable
{
    private const long DoublePressMs = 300;
    private static readonly TimeSpan WarmUpInterval = TimeSpan.FromMinutes(4);

    /// <summary>Raised on a background thread when a request finishes (success or error), before it is displayed.</summary>
    public event Action<string, ProviderResult>? RequestCompleted;

    private readonly ICaptionSource _captions;
    private readonly IChatProvider _provider;
    private readonly IOverlayView _view;
    private readonly CancellationTokenSource _cts = new();

    // Written on the UI thread (send/skip), read by the preview poller thread.
    private volatile string? _anchor;
    private Task _displayChain = Task.CompletedTask;
    private long _lastSendTick = long.MinValue / 2;

    public SendPipeline(ICaptionSource captions, IChatProvider provider, IOverlayView view)
    {
        _captions = captions;
        _provider = provider;
        _view = view;
    }

    /// <summary>Starts the warm-up loop (at once, then every 4 minutes).</summary>
    public void Start() => _ = KeepWarmAsync(_cts.Token);

    public void Dispose() => _cts.Cancel();

    /// <summary>From the captions poller: refreshes the status and the pending preview.</summary>
    public void OnCaptionsChanged(string? text)
    {
        _view.SetCaptionsStatus(text is not null);
        _view.SetPreview(text is null ? "" : AnchorText.Extract(text, _anchor));
    }

    /// <summary>
    /// Sends the caption text since the last mark. Call from one thread (the UI thread).
    /// Returns the display task of this entry (for tests), or null when nothing was sent.
    /// </summary>
    public Task? Send()
    {
        var now = Environment.TickCount64;
        if (now - _lastSendTick < DoublePressMs) return null; // accidental double press
        _lastSendTick = now;

        var current = _captions.Read();
        if (current is null)
        {
            _view.Flash("Live Captions not available");
            return null;
        }

        var text = AnchorText.Extract(current, _anchor);
        _anchor = current;
        _view.SetPreview("");
        if (text.Length == 0)
        {
            _view.Flash("nothing new");
            return null;
        }

        // The request starts right away, even if an earlier answer is still streaming...
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var request = Task.Run(() => RequestAsync(text, channel.Writer));

        // ...but it is shown only after the previous entry has been fully displayed.
        var previous = _displayChain;
        _view.Flash(previous.IsCompleted ? "sent" : "sent · queued after the current answer");
        _displayChain = DisplayAsync(previous, text, channel.Reader, request);
        return _displayChain;
    }

    /// <summary>Moves the anchor to "now" without sending anything.</summary>
    public void Skip()
    {
        var current = _captions.Read();
        if (current is null)
        {
            _view.Flash("Live Captions not available");
            return;
        }
        _anchor = current;
        _view.SetPreview("");
        _view.Flash("skipped");
    }

    private async Task<ProviderResult> RequestAsync(string text, ChannelWriter<string> output)
    {
        ProviderResult result;
        try
        {
            result = await _provider.StreamAsync(text, output, _cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Providers shouldn't throw, but a bug must not break the display chain.
            output.TryComplete();
            result = new ProviderResult(_provider.Name, _provider.Model, "", null, 0, "Internal error: " + ex.Message);
        }

        try
        {
            RequestCompleted?.Invoke(text, result);
        }
        catch
        {
            // Logging failures must not affect the answer.
        }
        return result;
    }

    private async Task DisplayAsync(Task previous, string text, ChannelReader<string> chunks, Task<ProviderResult> request)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch
        {
            // Never let one entry block the next.
        }

        try
        {
            _view.AppendSent(text);
            await foreach (var chunk in chunks.ReadAllAsync().ConfigureAwait(false)) _view.AppendToken(chunk);
            var result = await request.ConfigureAwait(false);
            if (result.Error is not null) _view.AppendError(result.Error);
            _view.AppendStats(result.FirstTokenMs, result.TotalMs);
        }
        catch (Exception ex)
        {
            _view.AppendError("Internal error: " + ex.Message);
        }
    }

    private async Task KeepWarmAsync(CancellationToken ct)
    {
        string? lastError = "\0";
        try
        {
            while (true)
            {
                var error = await _provider.WarmUpAsync(ct).ConfigureAwait(false);
                _view.SetProviderStatus(error is null);
                if (error is not null && error != lastError) _view.AppendError($"{_provider.Name}: {error}");
                lastError = error;
                await Task.Delay(WarmUpInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
