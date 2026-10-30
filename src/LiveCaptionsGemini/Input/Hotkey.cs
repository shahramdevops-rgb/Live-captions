using System;
using System.Windows.Forms;

namespace LiveCaptionsGemini.Input;

internal enum HotkeyKind
{
    None,
    /// <summary>Ctrl+Shift pressed and released with no other key in between.</summary>
    CtrlShift,
    /// <summary>A single non-modifier key (e.g. F9), pressed with no modifiers held.</summary>
    Key,
}

internal readonly record struct Hotkey(HotkeyKind Kind, Keys Key)
{
    public static Hotkey None => new(HotkeyKind.None, Keys.None);

    /// <summary>"None"/"" → disabled, "CtrlShift" → modifier-only combo, otherwise a Keys name like "F8".</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = None;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Equals("None", StringComparison.OrdinalIgnoreCase)) return true;

        if (text.Replace("+", "").Equals("CtrlShift", StringComparison.OrdinalIgnoreCase))
        {
            hotkey = new Hotkey(HotkeyKind.CtrlShift, Keys.None);
            return true;
        }

        // Enum.TryParse also accepts numbers and combined flags ("F8, Control"); allow plain key names only.
        if (char.IsDigit(text[0]) || text.Contains(',')) return false;
        if (!Enum.TryParse<Keys>(text, ignoreCase: true, out var key)) return false;
        if (key == Keys.None || (key & Keys.Modifiers) != 0 || IsModifierKey(key)) return false;

        hotkey = new Hotkey(HotkeyKind.Key, key);
        return true;
    }

    public override string ToString() => Kind switch
    {
        HotkeyKind.CtrlShift => "Ctrl+Shift",
        HotkeyKind.Key => Key.ToString(),
        _ => "None",
    };

    private static bool IsModifierKey(Keys key) => key is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;
}
