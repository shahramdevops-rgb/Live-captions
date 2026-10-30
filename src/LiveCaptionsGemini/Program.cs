using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using LiveCaptionsGemini.Captions;
using LiveCaptionsGemini.Config;
using LiveCaptionsGemini.Input;
using LiveCaptionsGemini.Logging;
using LiveCaptionsGemini.Pipeline;
using LiveCaptionsGemini.Providers;
using LiveCaptionsGemini.UI;

namespace LiveCaptionsGemini;

internal static class Program
{
    private const string Title = "LiveCaptions → Gemini";

    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Two instances would mean two keyboard hooks: every send would go out twice.
        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\LiveCaptionsGemini", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("LiveCaptions → Gemini is already running.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var config = LoadConfig();
        if (config is null) return;
        if (config.ShowSettingsOnStart && !AskSessionSettings(config)) return;

        CaptionsReader.EnsureRunning();
        var reader = new CaptionsReader();
        using var form = new OverlayForm(config);
        using var poller = new CaptionsPoller(reader, config.PollMs);
        using var hook = new KeyboardHook(config.SendHotkey, config.SkipHotkey, form);
        using var pipeline = new SendPipeline(reader, new GeminiProvider(config), form);

        // log.csv lives next to config.json.
        var logger = new CsvLogger(Path.Combine(Path.GetDirectoryName(config.FilePath) ?? ".", CsvLogger.FileName));
        pipeline.RequestCompleted += (text, result) =>
        {
            var error = logger.Append(text, result);
            if (error is not null) form.Flash(error);
        };

        poller.Changed += pipeline.OnCaptionsChanged;
        hook.SendPressed += () => pipeline.Send(); // raised on the UI thread
        hook.SkipPressed += pipeline.Skip;
        form.FormClosing += (_, _) => pipeline.Dispose();
        form.Shown += (_, _) =>
        {
            pipeline.Start();
            poller.Start();
            try
            {
                hook.Start();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                form.AppendError("Hotkeys disabled: " + ex.Message);
            }
        };
        Application.Run(form);
    }

    /// <summary>Settings window before the overlay; false = the user chose Exit.</summary>
    private static bool AskSessionSettings(AppConfig config)
    {
        using var settings = new SettingsForm(config);
        if (settings.ShowDialog() != DialogResult.OK) return false;

        config.ApplySessionSettings(settings.Prompt, settings.MaxOutputTokens, settings.MaxHistoryTurns, settings.FontSizeValue);
        try
        {
            config.SaveSessionSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ConfigException)
        {
            // Not fatal: the values are used for this session anyway.
            MessageBox.Show($"Settings are used for this session but could not be saved:\n{ex.Message}",
                Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return true;
    }

    /// <summary>Null means the app should exit (config was just created or is broken).</summary>
    private static AppConfig? LoadConfig()
    {
        var path = AppConfig.Find();
        if (path is null)
        {
            path = AppConfig.CreateDefault();
            MessageBox.Show(
                $"Created {path}\n\nPut your Gemini API key in it (or set {AppConfig.ApiKeyEnvVar}), save, and start the app again.",
                Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            OpenInNotepad(path);
            return null;
        }

        try
        {
            return AppConfig.Load(path);
        }
        catch (ConfigException ex)
        {
            MessageBox.Show($"{ex.Message}\n\n{path}", Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            OpenInNotepad(path);
            return null;
        }
    }

    private static void OpenInNotepad(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // Not critical: the message already told the user where the file is.
        }
    }
}
