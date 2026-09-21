using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace OpenOTSTray.Services;

/// <summary>
/// Wraps the Win32 RegisterHotKey/UnregisterHotKey APIs behind a message-only window,
/// so the app can react to a key combination even while some other window has focus.
/// One instance owns exactly one system-wide binding at a time; registering a new
/// combination automatically replaces whatever was registered before.
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int HotkeyId = 0xA1F0;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private bool _isRegistered;
    private bool _disposed;

    public event Action? HotkeyPressed;

    public GlobalHotkeyService()
    {
        var parameters = new HwndSourceParameters("OpenOTSTrayHotkeySink") { ParentWindow = HWND_MESSAGE };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>
    /// Registers the given combination, replacing any previous one. Returns false if the
    /// combination is already claimed system-wide (by Windows itself or another app) -
    /// callers should treat that as "pick a different shortcut", not a crash.
    /// </summary>
    public bool TryRegister(ModifierKeys modifiers, Key key)
    {
        Unregister();

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var fsModifiers = (uint)modifiers | MOD_NOREPEAT;

        _isRegistered = RegisterHotKey(_source.Handle, HotkeyId, fsModifiers, vk);
        return _isRegistered;
    }

    public void Unregister()
    {
        if (!_isRegistered)
            return;

        UnregisterHotKey(_source.Handle, HotkeyId);
        _isRegistered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
