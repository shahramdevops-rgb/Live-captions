using System;
using LiveCaptionsGemini.Captions;

namespace LiveCaptionsGemini.Tests;

internal static class Program
{
    private static int _failed;
    private static int _passed;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var m = Array.IndexOf(args, "--model");
        var model = m >= 0 && m + 1 < args.Length ? args[m + 1] : null;

        if (Array.IndexOf(args, "--diag") >= 0)
        {
            NetworkDiagnostics.RunAsync(model).GetAwaiter().GetResult();
            return 0;
        }

        if (Array.IndexOf(args, "--live") >= 0)
        {
            return LiveGeminiTests.RunAsync(model).GetAwaiter().GetResult() ? 0 : 1;
        }

        Console.WriteLine("AnchorText.Extract");
        AnchorTextTests();

        Console.WriteLine();
        Console.WriteLine("CaptionsReader.Normalize");
        NormalizeTests();

        Console.WriteLine();
        Console.WriteLine("SendPipeline");
        PipelineTests.Run(Check);

        Console.WriteLine();
        Console.WriteLine("CsvLogger");
        CsvLoggerTests.Run(Check);

        Console.WriteLine();
        Console.WriteLine("Config save (settings window)");
        ConfigSaveTests.Run(Check);

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void AnchorTextTests()
    {
        const string longStart =
            "So today we are going to talk about the history of the Roman Empire and how it shaped modern Europe. ";

        Check("no anchor (startup) → everything",
            AnchorText.Extract("Hello world.", null), "Hello world.");

        Check("empty current → empty",
            AnchorText.Extract("", "anything at all here"), "");

        Check("same text (case differs) → nothing new",
            AnchorText.Extract("Hello World this is it", "hello world THIS is it"), "");

        Check("simple append",
            AnchorText.Extract("Hello world, this is a test of the system and more words",
                               "Hello world, this is a test of the system"),
            "and more words");

        Check("rolling window: start scrolled away, new text appended",
            AnchorText.Extract("how it shaped modern Europe. The first emperor was Augustus.",
                               longStart + "how it shaped modern Europe."),
            "The first emperor was Augustus.");

        Check("last word corrected (bar → bank)",
            AnchorText.Extract("we walked all the way down to the bank and then went home",
                               "we walked all the way down to the bar"),
            "bank and then went home");

        Check("two last words corrected",
            AnchorText.Extract("the meeting is scheduled for next Thursday afternoon at three",
                               "the meeting is scheduled for next Tuesday after"),
            "Thursday afternoon at three");

        Check("punctuation added after anchor → stripped",
            AnchorText.Extract("the quick brown fox jumps over the lazy dog. Then it ran away.",
                               "the quick brown fox jumps over the lazy dog"),
            "Then it ran away.");

        Check("anchor scrolled out entirely → whole current",
            AnchorText.Extract("Completely different sentence about the weather.",
                               "Nothing in common with what is visible now on the screen"),
            "Completely different sentence about the weather.");

        Check("text only scrolled (nothing new) → empty",
            AnchorText.Extract("modern Europe. The first emperor was Augustus.",
                               longStart + "modern Europe. The first emperor was Augustus."),
            "");

        Check("short anchor used as a whole",
            AnchorText.Extract("Hi there, how are you?", "Hi"),
            "there, how are you?");

        Check("repeated phrase, anchor has no context → after the FIRST occurrence",
            AnchorText.Extract("you know what I mean, and you know what I mean again",
                               "you know what I mean"),
            "and you know what I mean again");

        const string questions =
            "Okay, so let me start with the basics. What is C sharp? How does garbage collection work? What is a delegate? What is C sharp?";

        Check("same question asked again later → questions in between are kept",
            AnchorText.Extract(questions, "Okay, so let me start with the basics. What is C sharp?"),
            "How does garbage collection work? What is a delegate? What is C sharp?");

        Check("…and Live Captions added punctuation/capitals after the mark",
            AnchorText.Extract(questions, "okay so let me start with the basics what is C sharp"),
            "How does garbage collection work? What is a delegate? What is C sharp?");

        Check("only punctuation/capitals changed → nothing new",
            AnchorText.Extract("So, what is C sharp? Tell me.", "so what is c sharp tell me"), "");

        Check("last word was still being recognized (ba → bank)",
            AnchorText.Extract("we drove all the way down to the bank and parked", "we drove all the way down to the ba"),
            "bank and parked");
    }

    private static void NormalizeTests()
    {
        Check("collapse whitespace and newlines",
            CaptionsReader.Normalize("  Hello \r\n  world\t\tagain  "), "Hello world again");

        Check("null → empty", CaptionsReader.Normalize(null), "");

        Check("placeholder → empty",
            CaptionsReader.Normalize("Ready to show live captions in English (United States)"), "");
    }

    private static void Check(string name, string actual, string expected)
    {
        if (actual == expected)
        {
            _passed++;
            Console.WriteLine($"  ✓ {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  ✗ {name}\n      expected: \"{expected}\"\n      actual:   \"{actual}\"");
        }
    }
}
