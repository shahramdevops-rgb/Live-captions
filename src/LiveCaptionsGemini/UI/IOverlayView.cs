namespace LiveCaptionsGemini.UI;

/// <summary>What the send pipeline needs from the overlay. All methods are callable from any thread.</summary>
internal interface IOverlayView
{
    void SetCaptionsStatus(bool ok);

    /// <param name="ok">true = ✓, false = ✗, null = not checked yet.</param>
    void SetProviderStatus(bool? ok);

    /// <summary>Short message in the status line for 1.5 s.</summary>
    void Flash(string message);

    /// <summary>What the next send would contain.</summary>
    void SetPreview(string text);

    /// <summary>Starts a new answer entry with the sent text.</summary>
    void AppendSent(string text);

    void AppendToken(string token);

    void AppendStats(long? firstTokenMs, long totalMs);

    void AppendError(string message);
}
