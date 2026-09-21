using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using OpenOTSTray.Models;
using WinForms = System.Windows.Forms;

namespace OpenOTSTray.Services;

/// <summary>
/// Drives the "select text anywhere, press a hotkey, get a one-time link pasted back"
/// flow. There is no cross-app API for "read the current text selection", so this
/// leans on the same trick most clipboard-hack utilities use - but with Cut (Ctrl+X)
/// instead of Copy: cutting removes the original secret from the source app the
/// instant the hotkey fires, rather than leaving it sitting there - still selected and
/// visible - for however long the network call to generate the link takes. A
/// copy-then-paste-over-selection approach also failed silently in apps with flaky
/// selection tracking (Teams' rich-text compose box, for one): if the app "forgot" its
/// own selection during that wait, the paste just inserted the link at the cursor
/// instead of replacing anything, leaving the original secret sitting right next to
/// the new link. Cutting removes that failure mode entirely - there's nothing left to
/// un-select.
///
/// Because cutting is destructive up front, every exit path below accounts for the
/// text it removed: paste the link back on success, paste the original text back if
/// generation fails, and - if the user switches away from the source window while the
/// network call is in flight - leave whichever one matters on the clipboard for a
/// manual paste instead of blindly injecting it into whatever now has focus.
///
/// This only works where simulated keystrokes and clipboard access reach the focused
/// control - i.e. not into elevated (admin) windows from a non-elevated process, and
/// not into apps that ignore the clipboard for their own custom selection handling.
/// One more edge case worth knowing: a few editors (VS Code among them) treat Ctrl+X
/// with an empty selection as "cut the current line" rather than a no-op. Pressing the
/// hotkey with nothing actually selected in one of those can remove a line of text -
/// recoverable with Ctrl+Z, but worth knowing about.
/// </summary>
public class SelectionLinkService
{
    private static readonly TimeSpan ClipboardPollInterval = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan ClipboardWaitTimeout = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan PasteSettleDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ManualPasteGraceDelay = TimeSpan.FromSeconds(45);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

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
    /// <paramref name="onGenerating"/> is awaited immediately, before anything else
    /// happens - it must finish restoring focus to the source app before returning, or
    /// the Ctrl+X sent right after would be sent to our own window instead.
    /// <paramref name="onFinished"/> fires exactly once, on every exit path, with a
    /// user-facing message and whether it was a clean, no-action-needed outcome (used to
    /// pick the flyout's checkmark vs. warning icon).
    /// </summary>
    public async Task<PasswordResultItem?> RunAsync(
        AppSettings settings,
        Func<Task> onGenerating,
        Action<string, bool> onFinished)
    {
        var previousClipboard = TryGetClipboardText();
        var sourceWindow = GetForegroundWindow();
        var hotkeyModifiers = (ModifierKeys)settings.HotkeyModifiers;

        await onGenerating();

        Clipboard.Clear();
        await ReleaseModifierKeysAsync(hotkeyModifiers);
        WinForms.SendKeys.SendWait("^x");

        var selected = await WaitForClipboardTextAsync();
        if (string.IsNullOrEmpty(selected))
        {
            RestoreClipboard(previousClipboard);
            onFinished("No text was selected - nothing to do.", false);
            return null;
        }

        // The original text is gone from the source app from this point on - it only
        // exists in `selected` - so every path below has to put it, or its replacement
        // link, somewhere the user can still get to it.
        var item = new PasswordResultItem
        {
            Kind = "Selection Link",
            TitleLabel = $"Selection {PreviewText(selected)}",
            Region = settings.Region,
        };

        string? link = null;
        try
        {
            var created = await _client.CreateSecretLinkAsync(selected, settings.Region);
            link = created.Link;
            item.Link = created.Link;
            item.ReceiptIdentifier = created.ReceiptIdentifier;
            item.Subtitle = $"{settings.Region} · expires in {TtlFormatter.Format(604800)}";
        }
        catch (Exception ex)
        {
            item.ErrorMessage = ex.Message;
            item.ErrorVisibility = Visibility.Visible;
            item.Subtitle = "Failed to generate link";
        }

        var textToPlace = link ?? selected;

        if (GetForegroundWindow() != sourceWindow)
        {
            // The user switched windows while the link was generating - pasting now
            // would land in the wrong place. Leave the result on the clipboard long
            // enough for a manual paste instead of guessing, then restore whatever was
            // there before the hotkey fired.
            ClipboardHelper.SetTextSecurely(textToPlace);
            onFinished(link != null
                ? "The window changed while the link was generating, so it's on your clipboard - paste it manually."
                : $"Failed to generate link ({item.ErrorMessage}), and the window changed - your original text is on the clipboard, paste it back manually.",
                false);

            ScheduleClipboardRestore(textToPlace, previousClipboard, ManualPasteGraceDelay);
            return item;
        }

        ClipboardHelper.SetTextSecurely(textToPlace);
        await ReleaseModifierKeysAsync(hotkeyModifiers);
        WinForms.SendKeys.SendWait("^v");
        await Task.Delay(PasteSettleDelay);

        onFinished(link != null
            ? "Link generated and pasted over your selection."
            : $"Failed to generate link ({item.ErrorMessage}) - your original text was pasted back.",
            link != null);

        await Task.Delay(RestoreDelay);
        RestoreClipboard(previousClipboard);

        return item;
    }

    /// <summary>
    /// The hotkey itself may use Shift/Alt/Win as part of its chord (e.g. Ctrl+Shift+D),
    /// and WM_HOTKEY can fire while those keys are still physically held down - it's
    /// common to release the letter key a beat before the modifiers. If we then inject
    /// Ctrl+X/Ctrl+V while a real Shift is still down, the target app sees a different
    /// shortcut entirely (in Chromium-based apps like Teams or WhatsApp Desktop,
    /// Ctrl+Shift+C opens DevTools' element picker instead of copying), so nothing lands
    /// on the clipboard.
    ///
    /// Only the modifiers that are actually part of the configured hotkey get released -
    /// releasing one that was never down has its own side effects: a standalone Alt-up
    /// is exactly the signal Windows uses to toggle menu mnemonic underlines/key tips (it
    /// showed up as an unwanted key-tip overlay in the new Notepad during testing), and a
    /// standalone Win-up can trigger the Start menu. Both are only safe to send when that
    /// key was genuinely part of the chord the user just pressed.
    /// </summary>
    private static async Task ReleaseModifierKeysAsync(ModifierKeys hotkeyModifiers)
    {
        if (hotkeyModifiers == ModifierKeys.None)
            return;

        if (hotkeyModifiers.HasFlag(ModifierKeys.Shift))
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (hotkeyModifiers.HasFlag(ModifierKeys.Control))
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (hotkeyModifiers.HasFlag(ModifierKeys.Alt))
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (hotkeyModifiers.HasFlag(ModifierKeys.Windows))
        {
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_RWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        await Task.Delay(30);
    }

    /// <summary>
    /// Polls instead of a single fixed delay: fast apps (Notepad) get their result in one
    /// pass, while heavier ones (Electron apps like Teams/WhatsApp) get up to
    /// ClipboardWaitTimeout to actually respond to the simulated Ctrl+X.
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

    /// <summary>
    /// Used for the "user switched windows mid-request" case: the result is left on the
    /// clipboard for a manual paste instead of being auto-pasted somewhere unintended,
    /// then restored to whatever was there before once there's been a realistic amount
    /// of time to actually use it - but only if the clipboard still holds exactly what
    /// was put there, in case the user copied something else in the meantime.
    /// </summary>
    private static void ScheduleClipboardRestore(string expectedCurrentText, string? previousClipboard, TimeSpan delay)
    {
        _ = RestoreClipboardAfterDelayAsync(expectedCurrentText, previousClipboard, delay);
    }

    private static async Task RestoreClipboardAfterDelayAsync(string expectedCurrentText, string? previousClipboard, TimeSpan delay)
    {
        await Task.Delay(delay);
        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == expectedCurrentText)
                RestoreClipboard(previousClipboard);
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
