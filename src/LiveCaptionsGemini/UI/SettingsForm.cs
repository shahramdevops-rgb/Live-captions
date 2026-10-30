using System;
using System.Drawing;
using System.Windows.Forms;
using LiveCaptionsGemini.Config;

namespace LiveCaptionsGemini.UI;

/// <summary>
/// Shown once before the overlay: system prompt, max output tokens, history turns and font size,
/// pre-filled from config.json. Start (Ctrl+Enter) = OK, Exit (Esc) = Cancel.
/// </summary>
internal sealed class SettingsForm : Form
{
    private static readonly Color InputBackground = Color.FromArgb(34, 34, 40);

    private readonly TextBox _prompt;
    private readonly NumericUpDown _maxTokens;
    private readonly NumericUpDown _history;
    private readonly NumericUpDown _fontSize;
    private readonly Label _fontPreview;
    private Font? _previewFont;

    public SettingsForm(AppConfig config)
    {
        Text = "LiveCaptions → Gemini — settings";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = true;
        KeyPreview = true;
        BackColor = OverlayForm.Background;
        ForeColor = OverlayForm.TextColor;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(LogicalToDeviceUnits(760), LogicalToDeviceUnits(600));
        MinimumSize = new Size(LogicalToDeviceUnits(520), LogicalToDeviceUnits(420));

        _prompt = new TextBox
        {
            Name = "Prompt",
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = false,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = InputBackground,
            ForeColor = OverlayForm.TextColor,
            Font = new Font("Segoe UI", 10.5f),
            // TextBox wants \r\n; the config stores \n.
            Text = config.SystemPrompt.ReplaceLineEndings("\r\n"),
        };

        _maxTokens = Number("MaxTokens", 0, AppConfig.MaxOutputTokensLimit, config.MaxOutputTokens, increment: 50);
        _history = Number("History", 0, AppConfig.MaxHistoryTurnsLimit, config.KeepHistory ? config.MaxHistoryTurns : 0, increment: 1);
        _fontSize = Number("FontSize", (decimal)AppConfig.MinFontSize, (decimal)AppConfig.MaxFontSize, (decimal)config.FontSize, increment: 1);
        _fontSize.DecimalPlaces = 1;

        _fontPreview = new Label
        {
            Name = "FontPreview",
            AutoSize = true,
            Text = "Sample answer text at this size",
            Margin = new Padding(0, LogicalToDeviceUnits(6), 0, 0),
        };
        // TextChanged too: ValueChanged fires only after the typed text is validated (on leaving the box).
        _fontSize.ValueChanged += (_, _) => UpdateFontPreview();
        _fontSize.TextChanged += (_, _) => UpdateFontPreview();
        UpdateFontPreview();

        var numbers = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, LogicalToDeviceUnits(10), 0, 0),
        };
        numbers.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        numbers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(numbers, "Max output tokens", _maxTokens, "0 = no limit · ~300 ≈ 200 words");
        AddRow(numbers, "History turns", _history, "previous Q&A sent as context · 0 = off");
        AddRow(numbers, "Font size", _fontSize, "answer text in the overlay");

        var key = config.HasApiKey ? (config.ApiKeyFromEnv ? "from GEMINI_API_KEY ✓" : "from config ✓") : "MISSING ✗";
        // Fixed height + ellipsis: a long config path must not widen the layout (it pushed Start off-screen).
        var info = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Height = LogicalToDeviceUnits(40),
            ForeColor = OverlayForm.DimColor,
            Font = new Font("Segoe UI", 9f),
            Text = $"Model: {config.Model} · API key: {key} · Send: {config.SendHotkey} · Skip: {config.SkipHotkey}\n" +
                   $"Saved to {config.FilePath}",
            Margin = new Padding(0, LogicalToDeviceUnits(10), 0, 0),
        };

        var start = Button("Start", "Start  (Ctrl+Enter)", primary: true);
        var exit = Button("Exit", "Exit  (Esc)", primary: false);
        start.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        exit.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        CancelButton = exit;
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, LogicalToDeviceUnits(12), 0, 0),
        };
        buttons.Controls.Add(start);
        buttons.Controls.Add(exit);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(LogicalToDeviceUnits(14)),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // column = window width, not widest text
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "System prompt — who you are, the role, how Gemini should answer (multiple lines are fine)",
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(6)),
        });
        layout.Controls.Add(_prompt);
        layout.Controls.Add(numbers);
        layout.Controls.Add(_fontPreview);
        layout.Controls.Add(info);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                start.PerformClick();
            }
        };
        Shown += (_, _) =>
        {
            _prompt.Focus();
            _prompt.SelectionStart = _prompt.TextLength;
        };
    }

    public string Prompt => _prompt.Text;

    // NumericUpDown.Value validates typed text first, so a value typed without leaving the box still counts.
    public int MaxOutputTokens => (int)_maxTokens.Value;
    public int MaxHistoryTurns => (int)_history.Value;
    public float FontSizeValue => (float)_fontSize.Value;

    private void UpdateFontPreview()
    {
        // Dispose only fonts we created (the label starts with the shared default font).
        if (!decimal.TryParse(_fontSize.Text, out var size)) return;
        size = Math.Clamp(size, _fontSize.Minimum, _fontSize.Maximum);
        if (_previewFont is not null && (decimal)_previewFont.Size == size) return;

        var old = _previewFont;
        _previewFont = new Font("Segoe UI", (float)size);
        _fontPreview.Font = _previewFont;
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _previewFont?.Dispose();
        base.Dispose(disposing);
    }

    private NumericUpDown Number(string name, decimal min, decimal max, decimal value, decimal increment) => new()
    {
        Name = name,
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Increment = increment,
        Width = LogicalToDeviceUnits(110),
        BackColor = InputBackground,
        ForeColor = OverlayForm.TextColor,
        BorderStyle = BorderStyle.FixedSingle,
        TextAlign = HorizontalAlignment.Right,
        Margin = new Padding(LogicalToDeviceUnits(10), LogicalToDeviceUnits(3), LogicalToDeviceUnits(10), LogicalToDeviceUnits(3)),
    };

    private void AddRow(TableLayoutPanel table, string label, Control input, string hint)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.Add(input);
        row.Controls.Add(new Label
        {
            AutoSize = true,
            Text = hint,
            ForeColor = OverlayForm.DimColor,
            Margin = new Padding(0, LogicalToDeviceUnits(6), 0, 0),
        });
        table.Controls.Add(new Label
        {
            AutoSize = true,
            Text = label,
            Margin = new Padding(0, LogicalToDeviceUnits(6), 0, 0),
        });
        table.Controls.Add(row);
    }

    private Button Button(string name, string text, bool primary) => new()
    {
        Name = name,
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = primary ? OverlayForm.AccentColor : InputBackground,
        ForeColor = primary ? Color.FromArgb(15, 15, 20) : OverlayForm.TextColor,
        Padding = new Padding(LogicalToDeviceUnits(10), LogicalToDeviceUnits(3), LogicalToDeviceUnits(10), LogicalToDeviceUnits(3)),
        Margin = new Padding(LogicalToDeviceUnits(8), 0, 0, 0),
    };
}
