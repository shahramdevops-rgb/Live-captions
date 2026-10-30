using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace LiveCaptionsGemini.Providers;

/// <summary>
/// One LLM backend (Gemini now; Claude / OpenAI later). Comparison mode will call
/// <see cref="StreamAsync"/> on several providers in parallel with the same text.
/// </summary>
internal interface IChatProvider
{
    /// <summary>Display name, e.g. "Gemini".</summary>
    string Name { get; }

    string Model { get; }

    /// <summary>
    /// Opens the connection (TLS) and validates key + model. Returns null on success, else an error message.
    /// Safe to call repeatedly to keep the connection warm.
    /// </summary>
    Task<string?> WarmUpAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends <paramref name="text"/> (with this provider's history) and writes answer chunks to
    /// <paramref name="output"/> as they arrive. Always completes <paramref name="output"/>, even on error.
    /// Never throws for API/network errors: they are returned in <see cref="ProviderResult.Error"/>.
    /// </summary>
    Task<ProviderResult> StreamAsync(string text, ChannelWriter<string> output, CancellationToken ct = default);
}

/// <param name="FirstTokenMs">Time from request start to the first text chunk; null if none arrived.</param>
internal sealed record ProviderResult(
    string Provider,
    string Model,
    string Text,
    long? FirstTokenMs,
    long TotalMs,
    string? Error)
{
    public bool Success => Error is null;
}
