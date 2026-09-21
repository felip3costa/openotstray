using System.Windows;

namespace OpenOTSTray.Services;

public static class ClipboardHelper
{
    /// <summary>
    /// Copies text while opting out of Windows Clipboard History and Cloud Clipboard sync,
    /// so a copied password/link/passphrase doesn't linger in Win+V history or silently
    /// roam to the user's other devices via the Microsoft account clipboard sync.
    /// </summary>
    public static void SetTextSecurely(string text)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        data.SetData("CanIncludeInClipboardHistory", false);
        data.SetData("CanUploadToCloudClipboard", false);
        Clipboard.SetDataObject(data, true);
    }
}
