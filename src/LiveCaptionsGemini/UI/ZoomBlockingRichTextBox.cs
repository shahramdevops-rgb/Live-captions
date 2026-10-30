using System;
using System.Windows.Forms;

namespace LiveCaptionsGemini.UI;

/// <summary>
/// RichTextBox whose built-in Ctrl + mouse wheel zoom is replaced by <see cref="CtrlWheel"/>, so the
/// overlay can scale all of its text together instead of only the answers.
/// </summary>
internal sealed class ZoomBlockingRichTextBox : RichTextBox
{
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int MK_CONTROL = 0x0008;

    /// <summary>Wheel delta (positive = away from the user) while Ctrl is held.</summary>
    public event Action<int>? CtrlWheel;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEWHEEL && ((long)m.WParam & MK_CONTROL) != 0)
        {
            CtrlWheel?.Invoke((short)((long)m.WParam >> 16));
            return;
        }
        base.WndProc(ref m);
    }
}
