using System.IO;
using System.Windows;
using Icon = System.Drawing.Icon;
using Size = System.Drawing.Size;

namespace OpenOTSTray.Services;

public static class TrayIconFactory
{
    private static readonly Uri IconUri = new("pack://application:,,,/ots-tray.ico");

    /// <summary>
    /// Loads the app icon (embedded as a WPF resource, see ots-tray.ico in the csproj)
    /// picking the frame closest to 32x32 for a crisp tray icon at common DPI scales.
    /// </summary>
    public static Icon LoadTrayIcon()
    {
        var resourceStream = Application.GetResourceStream(IconUri)
            ?? throw new FileNotFoundException("Embedded resource ots-tray.ico was not found.");

        using var stream = resourceStream.Stream;
        return new Icon(stream, new Size(32, 32));
    }
}
