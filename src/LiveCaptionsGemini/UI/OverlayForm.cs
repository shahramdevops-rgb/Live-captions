using System;
using System.Drawing;
using System.Windows.Forms;
using LiveCaptionsGemini.Config;
using static LiveCaptionsGemini.UI.NativeMethods;

namespace LiveCaptionsGemini.UI;

/// <summary>
/// Borderless, always-on-top overlay that never takes focus. Pure view: every public method
/// is safe to call from any thread (marshalled with BeginInvoke, so call order is preserved).
/// </summary>
internal sealed class OverlayForm : Form, IOverlayView
{
    private const int PreviewChars = 260;
    private const int SentChars = 140;
    private const int MaxAnswerChars = 200_000;
    private const int FlashMs = 1500;

    // Shared with SettingsForm.
    internal static readonly Color Background = Color.FromArgb(22, 22, 26);
    internal static readonly Color BorderColor = Color.FromArgb(58, 58, 68);
    internal static readonly Color TextColor = Color.FromArgb(225, 225, 230);
    internal static readonly Color DimColor = Color.FromArgb(125, 125, 135);
    internal static readonly Color AccentColor = Color.FromArgb(110, 170, 255);
    private static readonly Color FlashColor = Color.FromArgb(240, 190, 90);
    private static readonly Color ErrorColor = Color.FromArgb(240, 100, 100);

    private const float StatusFontSize = 9f;
    private const float FontStep = 1f;
    private const int SaveFontDelayMs = 800;

    private readonly AppConfig _config;
    private readonly string _model;
    private readonly int _grip;
    private readonly Panel _header;
    private readonly Label _status;
    private readonly Label _close;
    private readonly Label _preview;
    private readonly ZoomBlockingRichTextBox _answers;
    private readonly Font _answerFont;
    private readonly Font _smallFont;
    private readonly System.Windows.Forms.Timer _flashTimer = new() { Interval = FlashMs };
    private readonly System.Windows.Forms.Timer _saveFontTimer = new() { Interval = SaveFontDelayMs };

    // Answer text keeps the fonts it was written with (_answerFont/_smallFont at the start size);
    // A− / A+ scale everything: the RichTextBox via ZoomFactor (old answers too), the labels via new fonts.
    private readonly float _baseFontSize;
    private float _fontSize;
    private Font? _headerFont;
    private Font? _previewFont;

    private bool _captionsOk;
    private string _providerState = "…";

    public OverlayForm(AppConfig config)
    {
        _config = config;
        _model = config.Model;
        _baseFontSize = config.FontSize;
        _fontSize = config.FontSize;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "LiveCaptions → Gemini";
        BackColor = Background;
        ForeColor = TextColor;
        Opacity = config.Opacity;
        DoubleBuffered = true;

        _grip = LogicalToDeviceUnits(8);
        Padding = new Padding(_grip, _grip / 2, _grip, _grip);
        MinimumSize = new Size(LogicalToDeviceUnits(240), LogicalToDeviceUnits(120));
        Size = new Size(LogicalToDeviceUnits(config.Width), LogicalToDeviceUnits(config.Height));
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        var margin = LogicalToDeviceUnits(16);
        Location = new Point(area.Right - Width - margin, area.Bottom - Height - margin);

        _answerFont = new Font("Segoe UI", config.FontSize);
        _smallFont = new Font("Segoe UI", Math.Max(7f, config.FontSize - 2f));

        // 1. Header: status, A− / A+ (font size), close. Dragging the header moves the window.
        _header = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(24) };
        var header = _header;
        _status = new Label
        {
            Name = "Status",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = DimColor,
            AutoEllipsis = true,
        };
        var smaller = HeaderButton("FontSmaller", "A−", DimColor, "Smaller text (or Ctrl + mouse wheel)");
        var bigger = HeaderButton("FontBigger", "A+", DimColor, "Bigger text (or Ctrl + mouse wheel)");
        // DoubleClick too: a Label raises DoubleClick instead of Click for the 2nd of two quick clicks.
        smaller.Click += (_, _) => ChangeFontSize(-FontStep);
        smaller.DoubleClick += (_, _) => ChangeFontSize(-FontStep);
        bigger.Click += (_, _) => ChangeFontSize(+FontStep);
        bigger.DoubleClick += (_, _) => ChangeFontSize(+FontStep);
        _close = HeaderButton("Close", "✕", ErrorColor, "Close");
        _close.Click += (_, _) => Close();
        // Right-docked: the last added sits rightmost → [status][A−][A+][✕].
        header.Controls.Add(_status);
        header.Controls.Add(smaller);
        header.Controls.Add(bigger);
        header.Controls.Add(_close);
        header.MouseDown += DragWindow;
        _status.MouseDown += DragWindow;

        // 2. Pending preview: what the next send would contain.
        _preview = new Label
        {
            Name = "Preview",
            Dock = DockStyle.Top,
            ForeColor = DimColor,
            AutoEllipsis = true,
        };
        var separator = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = BorderColor };

        // 3. Answers.
        _answers = new ZoomBlockingRichTextBox
        {
            Name = "Answers",
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Background,
            ForeColor = TextColor,
            Font = _answerFont,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            TabStop = false,
        };

        // Dock order: last added docks first.
        Controls.Add(_answers);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(4) });
        Controls.Add(separator);
        Controls.Add(_preview);
        Controls.Add(header);

        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer.Stop();
            RenderStatus();
        };
        RenderStatus();

        // Ctrl + mouse wheel anywhere on the window (Windows sends the wheel to the window under
        // the mouse even without focus). The RichTextBox's own Ctrl+wheel zoom is replaced by ours.
        foreach (var control in new Control[] { this, header, _status, smaller, bigger, _close, _preview })
            control.MouseWheel += OnMouseWheelZoom;
        _answers.CtrlWheel += delta => ChangeFontSize(Math.Sign(delta) * FontStep);

        _saveFontTimer.Tick += (_, _) => SaveFontSize();
        FormClosing += (_, _) => { if (_saveFontTimer.Enabled) SaveFontSize(); };
        ApplyFontSize();
    }

    private Label HeaderButton(string name, string text, Color hoverColor, string tooltip)
    {
        var button = new Label
        {
            Name = name,
            Text = text,
            Dock = DockStyle.Right,
            Width = LogicalToDeviceUnits(26),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = DimColor,
            Cursor = Cursors.Hand,
            AccessibleName = tooltip,
        };
        button.MouseEnter += (_, _) => button.ForeColor = hoverColor == DimColor ? TextColor : hoverColor;
        button.MouseLeave += (_, _) => button.ForeColor = DimColor;
        return button;
    }

    // ---- Font size (A− / A+, Ctrl + wheel) ----

    private void OnMouseWheelZoom(object? sender, MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) != 0) ChangeFontSize(Math.Sign(e.Delta) * FontStep);
    }

    private void ChangeFontSize(float delta)
    {
        var size = Math.Clamp(_fontSize + delta, AppConfig.MinFontSize, AppConfig.MaxFontSize);
        if (size == _fontSize) return;
        _fontSize = size;
        ApplyFontSize();
        Flash($"font size {_fontSize:0.#}");
        // Save once the clicking stops.
        _saveFontTimer.Stop();
        _saveFontTimer.Start();
    }

    private void ApplyFontSize()
    {
        var scale = _fontSize / _baseFontSize;

        var oldHeader = _headerFont;
        _headerFont = new Font("Segoe UI", StatusFontSize * scale);
        foreach (Control c in _header.Controls)
        {
            c.Font = _headerFont;
            // A− / A+ / ✕ grow with the text (a fixed width clipped them at bigger sizes).
            if (c != _status) c.Width = Math.Max(LogicalToDeviceUnits(26), TextRenderer.MeasureText(c.Text, _headerFont).Width + LogicalToDeviceUnits(6));
        }
        _header.Height = Math.Max(LogicalToDeviceUnits(24), TextRenderer.MeasureText("Xg", _headerFont).Height + LogicalToDeviceUnits(6));
        oldHeader?.Dispose();

        var oldPreview = _previewFont;
        _previewFont = new Font("Segoe UI", _smallFont.Size * scale, FontStyle.Italic);
        _preview.Font = _previewFont;
        _preview.Height = TextRenderer.MeasureText("Xg", _previewFont).Height * 3 + LogicalToDeviceUnits(6);
        oldPreview?.Dispose();

        ApplyAnswerZoom();
    }

    private void ApplyAnswerZoom()
    {
        // The native control can drop its zoom (e.g. while it is empty) although ZoomFactor still reports
        // the old value, and setting an unchanged value is ignored — so go through 1 to force it.
        var scale = _fontSize / _baseFontSize;
        _answers.ZoomFactor = 1f;
        if (scale != 1f) _answers.ZoomFactor = scale;
    }

    private void SaveFontSize()
    {
        _saveFontTimer.Stop();
        try
        {
            _config.SaveFontSize(_fontSize);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ConfigException)
        {
            Flash("font size not saved: " + ex.Message);
        }
    }

    // ---- Never take focus ----

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_TOPMOST here instead of TopMost = true: the TopMost setter can activate the window.
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEACTIVATE:
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;

            case WM_NCHITTEST:
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT) m.Result = (IntPtr)HitTestEdges(m.LParam);
                return;
        }
        base.WndProc(ref m);
    }

    private int HitTestEdges(IntPtr lParam)
    {
        // Screen coordinates are signed 16-bit (negative on monitors left of / above the primary one).
        var screen = new Point(unchecked((short)(long)lParam), unchecked((short)((long)lParam >> 16)));
        var p = PointToClient(screen);
        bool left = p.X < _grip, right = p.X >= ClientSize.Width - _grip;
        bool top = p.Y < _grip, bottom = p.Y >= ClientSize.Height - _grip;

        if (top && left) return HTTOPLEFT;
        if (top && right) return HTTOPRIGHT;
        if (bottom && left) return HTBOTTOMLEFT;
        if (bottom && right) return HTBOTTOMRIGHT;
        if (left) return HTLEFT;
        if (right) return HTRIGHT;
        if (top) return HTTOP;
        if (bottom) return HTBOTTOM;
        return HTCLIENT;
    }

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate(); // redraw the border
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _flashTimer.Dispose();
            _saveFontTimer.Dispose();
            _answerFont.Dispose();
            _smallFont.Dispose();
            _headerFont?.Dispose();
            _previewFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---- Status line ----

    public void SetCaptionsStatus(bool ok) => Ui(() =>
    {
        if (_captionsOk == ok) return;
        _captionsOk = ok;
        RenderStatus();
    });

    /// <param name="ok">true = ✓, false = ✗, null = not checked yet.</param>
    public void SetProviderStatus(bool? ok) => Ui(() =>
    {
        _providerState = ok switch { true => "✓", false => "✗", null => "…" };
        RenderStatus();
    });

    /// <summary>Shows a short message in the status line for 1.5 s.</summary>
    public void Flash(string message) => Ui(() =>
    {
        _status.Text = message;
        _status.ForeColor = FlashColor;
        _flashTimer.Stop();
        _flashTimer.Start();
    });

    private void RenderStatus()
    {
        if (_flashTimer.Enabled) return;
        _status.Text = $"{(_captionsOk ? "●" : "○")} Live Captions · {_model} · Gemini {_providerState}";
        _status.ForeColor = _captionsOk ? DimColor : ErrorColor;
    }

    // ---- Preview ----

    public void SetPreview(string text) => Ui(() =>
    {
        _preview.Text = text.Length > PreviewChars ? "…" + text[^PreviewChars..] : text;
    });

    // ---- Answers ----

    /// <summary>Starts a new entry: an accent line with (the end of) the sent text.</summary>
    public void AppendSent(string text) => Ui(() =>
    {
        if (_answers.TextLength > 0) Append("\n", TextColor, _answerFont);
        var shown = text.Length > SentChars ? "…" + text[^SentChars..] : text;
        Append("▶ " + shown + "\n", AccentColor, _smallFont);
    });

    public void AppendToken(string token) => Ui(() => Append(token, TextColor, _answerFont));

    public void AppendStats(long? firstTokenMs, long totalMs) =>
        Ui(() => AppendLine($"⏱ first token: {(firstTokenMs is long ms ? $"{ms} ms" : "—")} · total: {totalMs} ms", DimColor));

    public void AppendError(string message) => Ui(() => AppendLine("⚠ " + message, ErrorColor));

    private void AppendLine(string line, Color color)
    {
        // Start on a fresh line unless we're right after the "sent" line or at the very top.
        var needsBreak = _answers.TextLength > 0 && !_answers.Text.EndsWith('\n');
        Append((needsBreak ? "\n" : "") + line + "\n", color, _smallFont);
    }

    private void Append(string text, Color color, Font font)
    {
        if (_answers.TextLength > MaxAnswerChars)
        {
            _answers.Select(0, _answers.TextLength - MaxAnswerChars / 2);
            _answers.SelectedText = "";
        }
        _answers.SelectionStart = _answers.TextLength;
        _answers.SelectionLength = 0;
        _answers.SelectionColor = color;
        _answers.SelectionFont = font;
        var wasEmpty = _answers.TextLength == 0;
        _answers.SelectedText = text;
        if (wasEmpty && _fontSize != _baseFontSize) ApplyAnswerZoom(); // zoom may not stick to an empty control
        _answers.SelectionStart = _answers.TextLength;
        _answers.ScrollToCaret();
    }

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (!InvokeRequired)
        {
            action();
            return;
        }
        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Closing.
        }
    }
}
