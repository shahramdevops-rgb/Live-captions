using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LiveCaptionsGemini.Captions;
using LiveCaptionsGemini.Pipeline;
using LiveCaptionsGemini.Providers;
using LiveCaptionsGemini.UI;

namespace LiveCaptionsGemini.Tests;

/// <summary>SendPipeline with fake captions, a fake provider and a recording view (no network, no UI).</summary>
internal static class PipelineTests
{
    private const int AfterDoublePress = 350;

    public static void Run(Action<string, string, string> check)
    {
        DoublePressAndNothingNew(check);
        OrderedDisplayWhileStreaming(check);
        SkipAndPreview(check);
        ErrorsDoNotBreakTheChain(check);
    }

    private static void DoublePressAndNothingNew(Action<string, string, string> check)
    {
        var (captions, provider, view, pipeline) = Create();
        captions.Text = "hello there, how are you doing today";

        var first = pipeline.Send();
        var second = pipeline.Send(); // within 300 ms
        first?.Wait(5000);
        check("first send sends everything visible (no anchor yet)", provider.Sent.FirstOrDefault() ?? "", "hello there, how are you doing today");
        check("second press within 300 ms is ignored", $"{second is null}, requests={provider.Sent.Count}", "True, requests=1");

        Thread.Sleep(AfterDoublePress);
        var third = pipeline.Send();
        check("press again with no new text → nothing sent", $"{third is null}, requests={provider.Sent.Count}, flash={view.LastFlash}",
            "True, requests=1, flash=nothing new");
    }

    private static void OrderedDisplayWhileStreaming(Action<string, string, string> check)
    {
        var (captions, provider, view, pipeline) = Create();
        provider.Behavior = text => text.StartsWith("first") ? (600, new[] { "A1 ", "A2 ", "A3" }, null) : (0, new[] { "B1 ", "B2" }, null);

        captions.Text = "first question about generics";
        var a = pipeline.Send();
        Thread.Sleep(AfterDoublePress);
        captions.Text = "first question about generics. second question about async";
        var b = pipeline.Send();
        Task.WaitAll(new[] { a!, b! }, 10_000);

        var startedA = provider.StartedAt["first question about generics"];
        var startedB = provider.StartedAt["second question about async"];
        var finishedA = provider.FinishedAt["first question about generics"];
        check("second request starts before the first one finished", $"{startedB < finishedA}", "True");
        check("second send is reported as queued", view.Flashes.LastOrDefault() ?? "", "sent · queued after the current answer");
        check("answers displayed strictly in send order", string.Join(" | ", view.Log),
            "sent:first question about generics | A1  | A2  | A3 | stats | " +
            "sent:second question about async | B1  | B2 | stats");
        _ = startedA;
    }

    private static void SkipAndPreview(Action<string, string, string> check)
    {
        var (captions, provider, view, pipeline) = Create();
        captions.Text = "intro text that we already heard";
        pipeline.Skip();
        check("skip sends nothing", $"requests={provider.Sent.Count}, flash={view.LastFlash}", "requests=0, flash=skipped");

        pipeline.OnCaptionsChanged("intro text that we already heard and then a new question");
        check("preview shows only the text after the mark", view.Preview, "and then a new question");

        captions.Text = "intro text that we already heard and then a new question";
        pipeline.Send()?.Wait(5000);
        check("send after skip sends only the new text", provider.Sent.LastOrDefault() ?? "", "and then a new question");
        check("preview is cleared after send", view.Preview, "");

        pipeline.OnCaptionsChanged(null);
        check("captions unavailable → status ✗", $"{view.CaptionsOk}", "False");
    }

    private static void ErrorsDoNotBreakTheChain(Action<string, string, string> check)
    {
        var (captions, provider, view, pipeline) = Create();
        var completed = new List<string>();
        pipeline.RequestCompleted += (text, result) => { lock (completed) completed.Add(result.Error ?? "ok"); };
        provider.Behavior = text => text.Contains("boom") ? throw new InvalidOperationException("boom")
            : text.Contains("bad") ? (0, Array.Empty<string>(), "HTTP 429: quota") : (0, new[] { "fine" }, null);

        captions.Text = "a bad request";
        var t1 = pipeline.Send();
        Thread.Sleep(AfterDoublePress);
        captions.Text = "a bad request then boom";
        var t2 = pipeline.Send();
        Thread.Sleep(AfterDoublePress);
        captions.Text = "a bad request then boom and a good one";
        var t3 = pipeline.Send();
        Task.WaitAll(new[] { t1!, t2!, t3! }, 10_000);

        check("API error, provider exception, then success all displayed in order", string.Join(" | ", view.Log),
            "sent:a bad request | error:HTTP 429: quota | stats | " +
            "sent:then boom | error:Internal error: boom | stats | " +
            "sent:and a good one | fine | stats");
        lock (completed) check("RequestCompleted raised for every request", string.Join(",", completed),
            "HTTP 429: quota,Internal error: boom,ok");
    }

    private static (FakeCaptions, FakeProvider, RecordingView, SendPipeline) Create()
    {
        var captions = new FakeCaptions();
        var provider = new FakeProvider();
        var view = new RecordingView();
        return (captions, provider, view, new SendPipeline(captions, provider, view));
    }

    private sealed class FakeCaptions : ICaptionSource
    {
        public string? Text { get; set; } = "";
        public string? Read() => Text;
    }

    private sealed class FakeProvider : IChatProvider
    {
        public Func<string, (int DelayMs, string[] Tokens, string? Error)> Behavior { get; set; } =
            _ => (0, new[] { "ok" }, null);

        public List<string> Sent { get; } = new();
        public Dictionary<string, long> StartedAt { get; } = new();
        public Dictionary<string, long> FinishedAt { get; } = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public string Name => "Fake";
        public string Model => "fake-1";
        public Task<string?> WarmUpAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);

        public async Task<ProviderResult> StreamAsync(string text, ChannelWriter<string> output, CancellationToken ct = default)
        {
            lock (Sent)
            {
                Sent.Add(text);
                StartedAt[text] = _clock.ElapsedTicks;
            }
            try
            {
                var (delay, tokens, error) = Behavior(text);
                await Task.Delay(delay, ct);
                foreach (var token in tokens)
                {
                    output.TryWrite(token);
                    await Task.Delay(20, ct);
                }
                return new ProviderResult(Name, Model, string.Concat(tokens), tokens.Length > 0 ? delay : null, delay, error);
            }
            finally
            {
                lock (Sent) FinishedAt[text] = _clock.ElapsedTicks;
                output.TryComplete();
            }
        }
    }

    private sealed class RecordingView : IOverlayView
    {
        private readonly object _lock = new();
        public List<string> Log { get; } = new();
        public List<string> Flashes { get; } = new();
        public string Preview { get; private set; } = "";
        public bool CaptionsOk { get; private set; }
        public string? LastFlash { get { lock (_lock) return Flashes.LastOrDefault(); } }

        public void SetCaptionsStatus(bool ok) => CaptionsOk = ok;
        public void SetProviderStatus(bool? ok) { }
        public void Flash(string message) { lock (_lock) Flashes.Add(message); }
        public void SetPreview(string text) => Preview = text;
        public void AppendSent(string text) { lock (_lock) Log.Add("sent:" + text); }
        public void AppendToken(string token) { lock (_lock) Log.Add(token); }
        public void AppendStats(long? firstTokenMs, long totalMs) { lock (_lock) Log.Add("stats"); }
        public void AppendError(string message) { lock (_lock) Log.Add("error:" + message); }
    }
}
