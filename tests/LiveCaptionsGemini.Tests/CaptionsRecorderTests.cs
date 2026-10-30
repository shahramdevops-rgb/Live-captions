using System;
using System.Text;
using LiveCaptionsGemini.Logging;

namespace LiveCaptionsGemini.Tests;

/// <summary>captions.txt recording with a fake clock and an in-memory file.</summary>
internal static class CaptionsRecorderTests
{
    public static void Run(Action<string, string, string> check)
    {
        var file = new StringBuilder();
        var now = new DateTime(2026, 10, 30, 15, 0, 0);
        var recorder = new CaptionsRecorder(text => file.Append(text), () => now);
        string Out() => file.ToString().Replace("\r\n", "\n");

        // Already on screen when the app starts: not part of this session.
        recorder.OnCaptionsChanged("Old text from an earlier video.");
        recorder.Tick();
        now = now.AddSeconds(3);
        recorder.Tick();
        check("text visible at start is not recorded", Out(), "");

        // Speaking: only the stable part is written, the last 6 words are held back.
        now = now.AddSeconds(1);
        recorder.OnCaptionsChanged("Old text from an earlier video. Hello, can you tell me what dependency injection is in C sharp");
        now = now.AddMilliseconds(500);
        recorder.Tick();
        check("while speaking: last 6 words held back", Out(),
            "\n===== Session 2026-10-30 15:00 =====\n[15:00:04] Hello, can you tell me what");

        // Live Captions corrects the held-back words ("C sharp" → "C#?").
        now = now.AddMilliseconds(500);
        recorder.OnCaptionsChanged("Old text from an earlier video. Hello, can you tell me what dependency injection is in C#?");
        now = now.AddMilliseconds(500);
        recorder.Tick();
        check("corrected words still held", Out(),
            "\n===== Session 2026-10-30 15:00 =====\n[15:00:04] Hello, can you tell me what");

        // A 2 s pause: everything is written (with the correction) and the paragraph ends.
        now = now.AddSeconds(2);
        recorder.Tick();
        check("pause → rest written, corrected", Out(),
            "\n===== Session 2026-10-30 15:00 =====\n[15:00:04] Hello, can you tell me what dependency injection is in C#?");

        // Next speech after the pause starts a new timestamped paragraph; scrolling text isn't repeated.
        now = now.AddSeconds(10);
        recorder.OnCaptionsChanged("injection is in C#? And how is it different from a service locator?");
        now = now.AddSeconds(2.5);
        recorder.Tick();
        check("new paragraph after a pause, no duplicates after scrolling", Out(),
            "\n===== Session 2026-10-30 15:00 =====\n[15:00:04] Hello, can you tell me what dependency injection is in C#?" +
            "\n[15:00:20] And how is it different from a service locator?");

        // Closing the app writes what's left (nothing here) and ends the file with a newline.
        now = now.AddSeconds(1);
        recorder.OnCaptionsChanged("injection is in C#? And how is it different from a service locator? Thanks");
        recorder.Dispose();
        check("closing flushes the rest", Out(),
            "\n===== Session 2026-10-30 15:00 =====\n[15:00:04] Hello, can you tell me what dependency injection is in C#?" +
            "\n[15:00:20] And how is it different from a service locator?\n[15:00:21] Thanks\n");

        // Write failure (file locked): reported once, nothing lost — retried on the next tick.
        var fails = 0;
        var failing = true;
        var output = new StringBuilder();
        var errors = 0;
        var r2 = new CaptionsRecorder(text =>
        {
            if (failing) { fails++; throw new System.IO.IOException("locked"); }
            output.Append(text);
        }, () => now);
        r2.WriteFailed += _ => errors++;
        r2.OnCaptionsChanged("");
        r2.OnCaptionsChanged("one two three");
        now = now.AddSeconds(3);
        r2.Tick();
        r2.Tick();
        failing = false;
        r2.Tick();
        check("locked file: reported once, written on retry", $"errors={errors}, fails={fails}, written={output.ToString().EndsWith("one two three")}",
            "errors=1, fails=2, written=True");
    }
}
