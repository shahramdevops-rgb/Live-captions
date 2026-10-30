using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LiveCaptionsGemini.Input;

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL). Never swallows keys.
/// Runs on its own thread with its own message loop, so a busy UI thread can't delay
/// system-wide keyboard input (Windows silently removes hooks that keep timing out).
/// Events are raised on the UI thread via BeginInvoke on <c>invoker</c>.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    public event Action? SendPressed;
    public event Action? SkipPressed;

    private readonly Hotkey _send;
    private readonly Hotkey _skip;
    private readonly Control _invoker;
    // Kept in a field: the native hook only holds a function pointer, the GC must not collect the delegate.
    private readonly HookProc _proc;

    private Thread? _thread;
    private uint _threadId;

    // Ctrl+Shift state machine; touched only on the hook thread.
    private bool _armed;   // both were down together with nothing else
    private bool _spoiled; // another key was pressed while Ctrl or Shift was held
    private bool _fired;   // already fired for this press

    public KeyboardHook(Hotkey send, Hotkey skip, Control invoker)
    {
        _send = send;
        _skip = skip;
        _invoker = invoker;
        _proc = OnHook;
    }

    public void Start()
    {
        if (_thread is not null) return;

        var ready = new ManualResetEventSlim();
        var error = 0;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            var hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) error = Marshal.GetLastWin32Error();
            ready.Set();
            if (hook == IntPtr.Zero) return;

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            UnhookWindowsHookEx(hook);
        })
        {
            IsBackground = true,
            Name = "KeyboardHook",
        };
        _thread.Start();
        ready.Wait();
        ready.Dispose();

        if (error != 0)
        {
            _thread = null;
            throw new Win32Exception(error, "Could not install the keyboard hook.");
        }
    }

    public void Dispose()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
    }

    private IntPtr OnHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var msg = (int)wParam;
                var vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN) OnKey(vk, isDown: true);
                else if (msg is WM_KEYUP or WM_SYSKEYUP) OnKey(vk, isDown: false);
            }
            catch
            {
                // Never let an exception escape into the native hook chain.
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void OnKey(int vk, bool isDown)
    {
        // In a low-level hook, GetAsyncKeyState still reports the state *before* this event,
        // so modifier state is derived from it plus the current event (no state can get stuck).
        var isCtrl = vk is VK_CONTROL or VK_LCONTROL or VK_RCONTROL;
        var isShift = vk is VK_SHIFT or VK_LSHIFT or VK_RSHIFT;
        var ctrl = isCtrl ? isDown || IsPressed(Opposite(vk)) : IsPressed(VK_CONTROL);
        var shift = isShift ? isDown || IsPressed(Opposite(vk)) : IsPressed(VK_SHIFT);

        if (isCtrl || isShift)
        {
            if (isDown)
            {
                if (ctrl && shift && !_spoiled && !IsAltOrWinPressed()) _armed = true;
            }
            else
            {
                if (_armed && !_spoiled && !_fired)
                {
                    _fired = true;
                    if (_send.Kind == HotkeyKind.CtrlShift) Raise(SendPressed);
                    else if (_skip.Kind == HotkeyKind.CtrlShift) Raise(SkipPressed);
                }
                if (!ctrl && !shift) _armed = _spoiled = _fired = false;
            }
            return;
        }

        if (!isDown) return;

        // Any other key (Alt and Win included) while Ctrl or Shift is held: e.g. Ctrl+Shift+T.
        if (ctrl || shift) _spoiled = true;

        if (IsPressed(vk)) return; // auto-repeat
        if (ctrl || shift || IsAltOrWinPressed()) return;

        var key = (Keys)vk;
        if (_send.Kind == HotkeyKind.Key && key == _send.Key) Raise(SendPressed);
        else if (_skip.Kind == HotkeyKind.Key && key == _skip.Key) Raise(SkipPressed);
    }

    private void Raise(Action? handler)
    {
        if (handler is null || _invoker.IsDisposed || !_invoker.IsHandleCreated) return;
        try
        {
            _invoker.BeginInvoke(handler);
        }
        catch (InvalidOperationException)
        {
            // Form is closing.
        }
    }

    private static bool IsPressed(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool IsAltOrWinPressed() => IsPressed(VK_MENU) || IsPressed(VK_LWIN) || IsPressed(VK_RWIN);

    private static int Opposite(int vk) => vk switch
    {
        VK_LCONTROL => VK_RCONTROL,
        VK_RCONTROL => VK_LCONTROL,
        VK_LSHIFT => VK_RSHIFT,
        VK_RSHIFT => VK_LSHIFT,
        _ => vk,
    };

    #region Native

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    #endregion
}
