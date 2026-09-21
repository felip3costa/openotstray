using System.Runtime.InteropServices;
using System.Windows;
using OpenOTSTray.Models;
using WinForms = System.Windows.Forms;

namespace OpenOTSTray.Services;

/// <summary>
/// Drives the "select text anywhere, press a hotkey, get a one-time link pasted back"
/// flow. There is no cross-app API for "read the current text selection", so this
/// leans on the same trick most clipboard-hack utilities use: simulate Ctrl+C to pull
/// the selection onto the clipboard, read it, call the API, then simulate Ctrl+V to
/// paste the link back over the (still active) selection. The clipboard is saved
/// before and restored after, so this doesn't clobber whatever the user had copied
/// prior to pressing the hotkey.
///
/// This only works where simulated keystrokes and clipboard access reach the focused
/// control - i.e. not into elevated (admin) windows from a non-elevated process, and
/// not into apps that ignore the clipboard for their own custom selection handling.
/// </summary>
public class SelectionLinkService
{
    private static readonly TimeSpan ClipboardPollInterval = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan ClipboardWaitTimeout = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan PasteSettleDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(400);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12; // Alt
    private const byte VK_LWIN = 0x5B;
    private const byte VK_RWIN = 0x5C;

    private readonly OneTimeSecretClient _client = new();

    /// <summary>
    /// Returns the generated history item, or null if there was nothing selected (in
    /// which case nothing was generated, so there's nothing worth logging to history).
    /// </summary>
    public async Task<PasswordResultItem?> RunAsync(AppSettings settings, Action<string, WinForms.ToolTipIcon> notify)
    {
        var previousClipboard = TryGetClipboardText();

        notify("Generating a one-time link from your selection...", WinForms.ToolTipIcon.Info);

        Clipboard.Clear();
        await ReleaseModifierKeysAsync();
        WinForms.SendKeys.SendWait("^c");

        var selected = await WaitForClipboardTextAsync();
        if (string.IsNullOrEmpty(selected))
        {
            RestoreClipboard(previousClipboard);
            notify("No text was selected - nothing to do.", WinForms.ToolTipIcon.Warning);
            return null;
        }

        var item = new PasswordResultItem
        {
            Kind = "Selection Link",
            TitleLabel = $"Selection {PreviewText(selected)}",
            Region = settings.Region,
        };

        try
        {
            var created = await _client.CreateSecretLinkAsync(selected, settings.Region);
            item.Link = created.Link;
            item.ReceiptIdentifier = created.ReceiptIdentifier;
            item.Subtitle = $"{settings.Region} · expires in {TtlFormatter.Format(604800)}";

            ClipboardHelper.SetTextSecurely(item.Link);
            await ReleaseModifierKeysAsync();
            WinForms.SendKeys.SendWait("^v");
            await Task.Delay(PasteSettleDelay);

            notify("Link generated and pasted over your selection.", WinForms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            item.ErrorMessage = ex.Message;
            item.ErrorVisibility = Visibility.Visible;
            item.Subtitle = "Failed to generate link";
            notify($"Failed to generate link: {ex.Message}", WinForms.ToolTipIcon.Error);
        }

        await Task.Delay(RestoreDelay);
        RestoreClipboard(previousClipboard);

        return item;
    }

    /// <summary>
    /// The hotkey itself may use Shift/Alt/Win as part of its chord (e.g. Ctrl+Shift+D),
    /// and WM_HOTKEY can fire while those keys are still physically held down - it's
    /// common to release the letter key a beat before the modifiers. If we then inject
    /// Ctrl+C while a real Shift is still down, the target app sees Ctrl+Shift+C instead
    /// of Ctrl+C (in Chromium-based apps like Teams or WhatsApp Desktop, that opens
    /// DevTools' element picker instead of copying), so nothing lands on the clipboard.
    /// Forcing every modifier key up first guarantees the Ctrl+C/Ctrl+V we send next is
    /// clean, regardless of what chord the user picked or how long they held it.
    /// </summary>
    private static async Task ReleaseModifierKeysAsync()
    {
        keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_RWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        await Task.Delay(30);
    }

    /// <summary>
    /// Polls instead of a single fixed delay: fast apps (Notepad) get their result in one
    /// pass, while heavier ones (Electron apps like Teams/WhatsApp) get up to
    /// ClipboardWaitTimeout to actually respond to the simulated Ctrl+C.
    /// </summary>
    private static async Task<string?> WaitForClipboardTextAsync()
    {
        var deadline = DateTime.UtcNow + ClipboardWaitTimeout;
        while (true)
        {
            var text = TryGetClipboardText();
            if (!string.IsNullOrEmpty(text) || DateTime.UtcNow >= deadline)
                return text;
            await Task.Delay(ClipboardPollInterval);
        }
    }

    private static string? TryGetClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            // clipboard can be transiently locked by another process
            return null;
        }
    }

    private static void RestoreClipboard(string? text)
    {
        try
        {
            if (text == null)
                Clipboard.Clear();
            else
                Clipboard.SetText(text);
        }
        catch
        {
            // best-effort
        }
    }

    private static string PreviewText(string value, int length = 6)
    {
        var singleLine = value.Replace("\r", "").Replace("\n", " ").Trim();
        if (string.IsNullOrEmpty(singleLine))
            return "";
        return singleLine.Length <= length ? singleLine : singleLine[..length] + "…";
    }
}
