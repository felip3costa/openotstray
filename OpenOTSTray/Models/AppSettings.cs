using System.Windows.Input;

namespace OpenOTSTray.Models;

public class AppSettings
{
    public static readonly string[] SupportedRegions = { "nz", "ca", "eu", "uk", "us" };
    public const string DefaultRegion = "eu";

    public const int MinPasswordLength = 4;
    public const int MaxPasswordLength = 128;
    public const int DefaultPasswordLength = 16;

    public const string DefaultEmailSubject = "Your one-time secret link";

    // Ctrl+Alt+D is rarely claimed by Windows itself or other apps, so it's a reasonable
    // default - but ApplyHotkeySettings (App.xaml.cs) only keeps it if registering it
    // actually succeeds on this machine; otherwise the feature starts disabled instead
    // of silently fighting another app for the same shortcut.
    public const ModifierKeys DefaultHotkeyModifiers = ModifierKeys.Control | ModifierKeys.Alt;
    public const Key DefaultHotkeyKey = Key.D;

    // Deliberately does NOT include {{passphrase}}: bundling the link and its unlock
    // passphrase in the same message defeats the point of having a separate passphrase.
    public const string DefaultEmailBodyTemplate =
        "Hi,\n\n" +
        "A secure one-time link has been generated for you. Use the link below to view it\n" +
        "(it can only be opened once, so save the content after viewing it):\n\n" +
        "{{link}}\n\n" +
        "Please change this password after your first login.\n\n" +
        "Best regards,";

    public int PasswordLength { get; set; } = DefaultPasswordLength;
    public string Region { get; set; } = DefaultRegion;
    public bool StartWithWindows { get; set; } = false;
    public string EmailSubject { get; set; } = DefaultEmailSubject;
    public string EmailBodyTemplate { get; set; } = DefaultEmailBodyTemplate;

    /// <summary>
    /// Global shortcut for "generate a link from the selected text in whatever app has
    /// focus, and paste it back over the selection". Stored as plain ints (rather than
    /// the enums directly) purely for stable System.Text.Json round-tripping.
    /// </summary>
    public bool HotkeyEnabled { get; set; } = true;
    public int HotkeyModifiers { get; set; } = (int)DefaultHotkeyModifiers;
    public int HotkeyKey { get; set; } = (int)DefaultHotkeyKey;
}
