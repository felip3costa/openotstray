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
    private static readonly TimeSpan CopyDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan PasteDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(400);

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
        WinForms.SendKeys.SendWait("^c");
        await Task.Delay(CopyDelay);

        var selected = TryGetClipboardText();
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
            WinForms.SendKeys.SendWait("^v");
            await Task.Delay(PasteDelay);

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
