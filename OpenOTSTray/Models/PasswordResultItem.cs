using System.Windows;

namespace OpenOTSTray.Models;

public class PasswordResultItem
{
    public string Kind { get; set; } = "Password";
    public string Icon { get; set; } = "\U0001F512";
    public string TitleLabel { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Password { get; set; } = "";
    public string Passphrase { get; set; } = "";
    public string Link { get; set; } = "";
    public string EmailBody { get; set; } = "";
    public string Region { get; set; } = "";
    public string ReceiptIdentifier { get; set; } = "";
    public bool IsOpened { get; set; }
    public string? ErrorMessage { get; set; }
    public Visibility ErrorVisibility { get; set; } = Visibility.Collapsed;
    public Visibility TitleDisplayVisibility { get; set; } = Visibility.Visible;
    public Visibility TitleEditVisibility { get; set; } = Visibility.Collapsed;
    public bool Succeeded => ErrorMessage == null;
    public Visibility PasswordButtonVisibility => string.IsNullOrEmpty(Password) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PassphraseButtonVisibility => string.IsNullOrEmpty(Passphrase) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// False for a restored item whose link couldn't be decrypted (moved to another
    /// machine/user, or the history file predates this field) — Password and Passphrase
    /// are never persisted at all, so those already hide via the two properties above.
    /// </summary>
    public bool CanCopyLink => Succeeded && !string.IsNullOrEmpty(Link);
    public bool CanSendEmail => Succeeded && !string.IsNullOrEmpty(EmailBody);
}
