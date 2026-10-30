using System;
using System.Net;
using System.Net.Http;

namespace LiveCaptionsGemini.Providers;

/// <summary>
/// One HttpClient for all providers, tuned for latency: HTTP/2 when available, connections kept
/// open (and pinged) for the whole session so a send never pays for DNS/TCP/TLS again.
/// </summary>
internal static class SharedHttp
{
    public static HttpClient Client { get; } = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = System.Threading.Timeout.InfiniteTimeSpan,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(30),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            // No automatic decompression: streamed SSE chunks must reach us as soon as they arrive.
            AutomaticDecompression = DecompressionMethods.None,
        };

        return new HttpClient(handler)
        {
            // Streaming answers can take long; callers cancel explicitly where needed.
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
    }
}
