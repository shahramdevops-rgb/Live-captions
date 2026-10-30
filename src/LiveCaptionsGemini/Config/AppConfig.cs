using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveCaptionsGemini.Input;

namespace LiveCaptionsGemini.Config;

internal sealed class ConfigException : Exception
{
    public ConfigException(string message, Exception? inner = null) : base(message, inner) { }
}

internal sealed class AppConfig
{
    public const string FileName = "config.json";
    public const string ApiKeyEnvVar = "GEMINI_API_KEY";

    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-3.5-flash-lite";
    public string ThinkingLevel { get; set; } = "minimal";
    public string SystemPrompt { get; set; } = DefaultSystemPrompt;
    public int MaxOutputTokens { get; set; } = 0; // 0 = not sent
    public bool KeepHistory { get; set; } = true;
    public int MaxHistoryTurns { get; set; } = 6;

    // "CtrlShift" (modifier-only) or a System.Windows.Forms.Keys name; "None" disables.
    public string SendKey { get; set; } = "CtrlShift";
    public string SkipKey { get; set; } = "F9";

    public int PollMs { get; set; } = 150;
    public double Opacity { get; set; } = 0.92;
    public float FontSize { get; set; } = 11f;
    public int Width { get; set; } = 520;
    public int Height { get; set; } = 360;

    /// <summary>Show the settings window (prompt, max tokens, history) before the overlay.</summary>
    public bool ShowSettingsOnStart { get; set; } = true;

    /// <summary>Append every sent text and answer to transcript.txt next to config.json.</summary>
    public bool SaveTranscript { get; set; } = true;

    public const int MaxOutputTokensLimit = 65_536;
    public const int MaxHistoryTurnsLimit = 50;

    [JsonIgnore] public string FilePath { get; private set; } = "";
    [JsonIgnore] public bool ApiKeyFromEnv { get; private set; }
    [JsonIgnore] public bool HasApiKey => ApiKey.Length > 0;
    [JsonIgnore] public Hotkey SendHotkey { get; private set; }
    [JsonIgnore] public Hotkey SkipHotkey { get; private set; }

    private const string DefaultSystemPrompt =
        "You are a concise assistant. The user sends a fragment of English speech transcribed by live captions. Answer or explain briefly.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Written as text (not serialized) so the generated file keeps its comments.
    // Keep in sync with config.example.json.
    private const string DefaultJson = """
        {
          // Put your key here, or leave empty and set env var GEMINI_API_KEY.
          "apiKey": "",
          "model": "gemini-3.5-flash-lite",
          "thinkingLevel": "minimal",
          "systemPrompt": "You are a concise assistant. The user sends a fragment of English speech transcribed by live captions. Answer or explain briefly.",
          "maxOutputTokens": 0,          // 0 = not sent
          "keepHistory": true,
          "maxHistoryTurns": 6,

          // "CtrlShift" (modifier-only) or any System.Windows.Forms.Keys name, e.g. "F8". "None" disables.
          "sendKey": "CtrlShift",
          "skipKey": "F9",

          "pollMs": 150,
          "opacity": 0.92,
          "fontSize": 11,
          "width": 520,
          "height": 360,

          // Settings window (system prompt, max tokens, history) before the overlay opens.
          "showSettingsOnStart": true,

          // Save every question and answer to transcript.txt (next to this file) to review later.
          "saveTranscript": true,
        }

        """;

    /// <summary>Current directory first, then next to the exe. Null if not found.</summary>
    public static string? Find()
    {
        foreach (var dir in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var path = Path.Combine(dir, FileName);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>Creates the default config in the current directory and returns its path.</summary>
    public static string CreateDefault()
    {
        var path = Path.Combine(Environment.CurrentDirectory, FileName);
        File.WriteAllText(path, DefaultJson, new UTF8Encoding(false));
        return path;
    }

    public static AppConfig Load(string path)
    {
        AppConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            // ex.Message repeats Path/LineNumber (0-based); keep only the first sentence.
            var reason = ex.Message.Split(" Path:")[0];
            var where = ex.LineNumber is long line ? $" at line {line + 1}" : "";
            var field = string.IsNullOrEmpty(ex.Path) || ex.Path == "$" ? "" : $", field {ex.Path.TrimStart('$', '.')}";
            throw new ConfigException($"{FileName} is not valid{where}{field}:\n{reason}", ex);
        }
        catch (IOException ex)
        {
            throw new ConfigException($"Cannot read {path}: {ex.Message}", ex);
        }

        if (cfg is null) throw new ConfigException($"{FileName} is empty.");

        cfg.FilePath = path;
        cfg.Normalize();
        return cfg;
    }

    private void Normalize()
    {
        ApiKey = (ApiKey ?? "").Trim();
        if (ApiKey.Length == 0)
        {
            ApiKey = (Environment.GetEnvironmentVariable(ApiKeyEnvVar) ?? "").Trim();
            ApiKeyFromEnv = ApiKey.Length > 0;
        }

        Model = string.IsNullOrWhiteSpace(Model) ? "gemini-3.5-flash-lite" : Model.Trim();
        ThinkingLevel = (ThinkingLevel ?? "").Trim();
        SystemPrompt ??= "";
        SendHotkey = ParseHotkey(nameof(SendKey), SendKey);
        SkipHotkey = ParseHotkey(nameof(SkipKey), SkipKey);
        if (SendHotkey.Kind != HotkeyKind.None && SendHotkey == SkipHotkey)
            throw new ConfigException($"sendKey and skipKey are both \"{SendHotkey}\".");
        SendKey = SendHotkey.ToString();
        SkipKey = SkipHotkey.ToString();

        MaxOutputTokens = Math.Clamp(MaxOutputTokens, 0, MaxOutputTokensLimit);
        MaxHistoryTurns = Math.Clamp(MaxHistoryTurns, 0, MaxHistoryTurnsLimit);
        PollMs = Math.Clamp(PollMs, 30, 2000);
        Opacity = Math.Clamp(Opacity, 0.2, 1.0);
        FontSize = Math.Clamp(FontSize, MinFontSize, MaxFontSize);
        Width = Math.Clamp(Width, 240, 3000);
        Height = Math.Clamp(Height, 120, 2000);
    }

    public const float MinFontSize = 7f;
    public const float MaxFontSize = 32f;

    /// <summary>Values from the settings window. History turns 0 = history off.</summary>
    public void ApplySessionSettings(string systemPrompt, int maxOutputTokens, int maxHistoryTurns)
    {
        SystemPrompt = (systemPrompt ?? "").ReplaceLineEndings("\n").Trim();
        MaxOutputTokens = Math.Clamp(maxOutputTokens, 0, MaxOutputTokensLimit);
        MaxHistoryTurns = Math.Clamp(maxHistoryTurns, 0, MaxHistoryTurnsLimit);
        KeepHistory = MaxHistoryTurns > 0;
    }

    /// <summary>
    /// Writes the settings-window values (systemPrompt, maxOutputTokens, maxHistoryTurns, keepHistory)
    /// back to config.json. Only those values are replaced in the text, so comments, the API key and every
    /// other field stay as they are.
    /// </summary>
    public void SaveSessionSettings()
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var json = File.ReadAllText(FilePath);
        json = SetJsonValue(json, "systemPrompt", JsonSerializer.Serialize(SystemPrompt, WriteOptions));
        json = SetJsonValue(json, "maxOutputTokens", MaxOutputTokens.ToString(invariant));
        json = SetJsonValue(json, "maxHistoryTurns", MaxHistoryTurns.ToString(invariant));
        json = SetJsonValue(json, "keepHistory", KeepHistory ? "true" : "false");
        File.WriteAllText(FilePath, json, new UTF8Encoding(false));
    }

    /// <summary>Saves only fontSize (from the overlay's A− / A+), leaving everything else in config.json as is.</summary>
    public void SaveFontSize(float fontSize)
    {
        FontSize = Math.Clamp(fontSize, MinFontSize, MaxFontSize);
        var json = File.ReadAllText(FilePath);
        json = SetJsonValue(json, "fontSize", FontSize.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllText(FilePath, json, new UTF8Encoding(false));
    }

    // Keeps quotes/apostrophes/non-ASCII readable in the file instead of \u escapes.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Replaces the value of a top-level "name": value pair; adds the pair if it's missing.</summary>
    private static string SetJsonValue(string json, string name, string valueJson)
    {
        // A JSON string ("..." with escapes), number, or literal.
        var pattern = "(\"" + name + "\"\\s*:\\s*)(\"(?:[^\"\\\\]|\\\\.)*\"|-?\\d+(?:\\.\\d+)?|true|false|null)";
        var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (regex.IsMatch(json)) return regex.Replace(json, m => m.Groups[1].Value + valueJson, 1);

        var brace = json.IndexOf('{');
        if (brace < 0) throw new ConfigException($"{FileName} has no JSON object.");
        return json.Insert(brace + 1, $"\n  \"{name}\": {valueJson},"); // trailing commas are allowed
    }

    private static Hotkey ParseHotkey(string field, string? value)
    {
        if (Hotkey.TryParse(value, out var hotkey)) return hotkey;
        var name = JsonNamingPolicy.CamelCase.ConvertName(field);
        throw new ConfigException(
            $"{name} \"{value}\" is not a valid key.\nUse \"CtrlShift\", \"None\", or a key name like \"F8\", \"F9\", \"Pause\", \"Scroll\".");
    }
}
