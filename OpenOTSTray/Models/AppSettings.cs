namespace OpenOTSTray.Models;

public class AppSettings
{
    public static readonly string[] SupportedRegions = { "nz", "ca", "eu", "uk", "us" };
    public const string DefaultRegion = "eu";

    public const int MinPasswordLength = 4;
    public const int MaxPasswordLength = 128;
    public const int DefaultPasswordLength = 16;

    public const string DefaultEmailSubject = "Your one-time secret link";

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
}
