using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using OpenOTSTray.Models;

namespace OpenOTSTray.Services;

public class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenOTSTray");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "OpenOTSTray";

    public bool HasSettingsFile() => File.Exists(SettingsPath);

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                    return Sanitize(settings);
            }
        }
        catch
        {
            // settings file missing/corrupted: fall back to defaults below
        }
        return new AppSettings();
    }

    /// <summary>
    /// settings.json is plain, unsigned local storage that any process running as this
    /// user can rewrite. Region in particular gets interpolated into request URLs
    /// (https://{region}.onetimesecret.com/...); an unvalidated value like
    /// "evil.example.com/x?" changes the actual request host and silently exfiltrates
    /// every secret the user shares. Re-validate every field against the same rules the
    /// UI enforces on save, instead of trusting whatever is on disk.
    /// </summary>
    private static AppSettings Sanitize(AppSettings settings)
    {
        if (!Array.Exists(AppSettings.SupportedRegions, r => r == settings.Region))
            settings.Region = AppSettings.DefaultRegion;

        if (settings.PasswordLength < AppSettings.MinPasswordLength || settings.PasswordLength > AppSettings.MaxPasswordLength)
            settings.PasswordLength = AppSettings.DefaultPasswordLength;

        if (string.IsNullOrWhiteSpace(settings.EmailSubject))
            settings.EmailSubject = AppSettings.DefaultEmailSubject;

        if (string.IsNullOrWhiteSpace(settings.EmailBodyTemplate) || !EmailTemplateBuilder.HasRequiredTokens(settings.EmailBodyTemplate))
            settings.EmailBodyTemplate = AppSettings.DefaultEmailBodyTemplate;

        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    public void SetStartWithWindows(bool enable)
    {
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        SetStartWithWindows(enable, exePath);
    }

    /// <summary>
    /// Same as <see cref="SetStartWithWindows(bool)"/> but for an explicit exe path,
    /// used right after installing to a new location — at that point the *running*
    /// process's own path is still the old one, so Environment.ProcessPath would write
    /// a stale entry.
    /// </summary>
    public void SetStartWithWindows(bool enable, string? exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key == null)
            return;

        if (enable)
        {
            if (!string.IsNullOrEmpty(exePath))
                key.SetValue(RunValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }
}
