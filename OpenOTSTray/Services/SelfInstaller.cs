using System.Diagnostics;
using System.IO;
using System.Windows;
using OpenOTSTray.Models;

namespace OpenOTSTray.Services;

/// <summary>
/// This is a portable exe with no installer, so on its own it would just run from
/// wherever the user happened to download or extract it (Downloads, a temp folder...).
/// On first run, offer to copy itself to a permanent per-user location, pin a Start
/// Menu shortcut, and enable Start with Windows — so after saying yes once, the user
/// never has to go looking for the file again.
/// </summary>
public static class SelfInstaller
{
    public static string InstallDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenOTSTray");

    private static string InstalledExePath => Path.Combine(InstallDir, "OpenOTSTray.exe");

    /// <summary>
    /// Returns true if this process just launched a copy of itself from the install
    /// location and should shut down immediately without starting the UI.
    /// </summary>
    public static bool TryOfferInstall(SettingsService settingsService)
    {
        var currentExePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExePath))
            return false;

        if (PathsMatch(currentExePath, InstalledExePath))
            return false; // already running from its permanent home

        if (settingsService.HasSettingsFile())
            return false; // not the first run - the user already made a choice before

        var result = MessageBox.Show(
            "Open OTS Tray is running from a temporary location.\n\n" +
            "Install it to your Programs folder and start it automatically with Windows? " +
            "You won't need to find this file again afterward.",
            "Open OTS Tray - First run",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            // Persist a (default) settings file either way, so we only ever ask this once.
            settingsService.Save(new AppSettings());
            return false;
        }

        try
        {
            Directory.CreateDirectory(InstallDir);
            File.Copy(currentExePath, InstalledExePath, overwrite: true);

            settingsService.Save(new AppSettings { StartWithWindows = true });
            settingsService.SetStartWithWindows(true, InstalledExePath);
            CreateStartMenuShortcut(InstalledExePath);

            Process.Start(new ProcessStartInfo(InstalledExePath) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Couldn't install to {InstallDir}:\n{ex.Message}\n\nContinuing to run from the current location.",
                "Open OTS Tray", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private static bool PathsMatch(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static void CreateStartMenuShortcut(string targetExePath)
    {
        try
        {
            var startMenuPrograms = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var shortcutPath = Path.Combine(startMenuPrograms, "Open OTS Tray.lnk");

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetExePath;
            shortcut.Save();
        }
        catch
        {
            // best-effort convenience shortcut; not worth failing the install over
        }
    }
}
