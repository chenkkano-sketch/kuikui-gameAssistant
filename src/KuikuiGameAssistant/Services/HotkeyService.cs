using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace KuikuiGameAssistant.Services;

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly Dictionary<int, Action> _handlers = new();
    private HwndSource? _source;
    private IntPtr _handle;

    public void Attach(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
    }

    public bool Register(int id, HotkeyModifiers modifiers, uint virtualKey, Action handler)
    {
        if (_handle == IntPtr.Zero)
        {
            return false;
        }

        Unregister(id);
        if (!RegisterHotKey(_handle, id, (uint)modifiers, virtualKey))
        {
            return false;
        }

        _handlers[id] = handler;
        return true;
    }

    public void Unregister(int id)
    {
        if (_handlers.Remove(id) && _handle != IntPtr.Zero)
        {
            UnregisterHotKey(_handle, id);
        }
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys.ToArray())
        {
            UnregisterHotKey(_handle, id);
        }

        _handlers.Clear();
        _source?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handler();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public static bool TryParseHotkey(string? text, out HotkeyModifiers modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var rawToken in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = rawToken.Trim();
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Control;
                continue;
            }

            if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Shift;
                continue;
            }

            if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Alt;
                continue;
            }

            if (token.Equals("Win", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Windows;
                continue;
            }

            if (virtualKey != 0 || !TryParseKey(token, out var key))
            {
                return false;
            }

            virtualKey = (uint)key;
        }

        return virtualKey != 0;
    }

    private static bool TryParseKey(string token, out Forms.Keys key)
    {
        key = Forms.Keys.None;
        var normalized = token.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

        if (normalized.Length == 1)
        {
            var c = char.ToUpperInvariant(normalized[0]);
            if (c is >= 'A' and <= 'Z')
            {
                key = (Forms.Keys)c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                key = Forms.Keys.D0 + (c - '0');
                return true;
            }
        }

        if (normalized.StartsWith('F')
            && int.TryParse(normalized[1..], out var functionKey)
            && functionKey is >= 1 and <= 24)
        {
            key = Forms.Keys.F1 + (functionKey - 1);
            return true;
        }

        key = normalized.ToLowerInvariant() switch
        {
            "esc" or "escape" => Forms.Keys.Escape,
            "space" => Forms.Keys.Space,
            "tab" => Forms.Keys.Tab,
            "enter" or "return" => Forms.Keys.Return,
            "backspace" => Forms.Keys.Back,
            "delete" or "del" => Forms.Keys.Delete,
            "insert" or "ins" => Forms.Keys.Insert,
            "home" => Forms.Keys.Home,
            "end" => Forms.Keys.End,
            "pageup" or "pgup" => Forms.Keys.PageUp,
            "pagedown" or "pgdn" => Forms.Keys.PageDown,
            "up" => Forms.Keys.Up,
            "down" => Forms.Keys.Down,
            "left" => Forms.Keys.Left,
            "right" => Forms.Keys.Right,
            "printscreen" or "prtsc" or "snapshot" => Forms.Keys.Snapshot,
            "pause" => Forms.Keys.Pause,
            _ => Forms.Keys.None
        };

        return key != Forms.Keys.None;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

[Flags]
public enum HotkeyModifiers : uint
{
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
    NoRepeat = 0x4000
}
