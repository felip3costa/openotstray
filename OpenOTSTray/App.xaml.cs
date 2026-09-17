using System.Windows;
using OpenOTSTray.Services;
using OpenOTSTray.Windows;
using WinForms = System.Windows.Forms;

namespace OpenOTSTray;

public partial class App : System.Windows.Application
{
    private readonly SettingsService _settingsService = new();
    private WinForms.NotifyIcon? _notifyIcon;
    private Mutex? _singleInstanceMutex;
    private MainFlyoutWindow? _flyout;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "OpenOTSTray_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("Open OTS Tray is already running (check your system tray).",
                "Open OTS Tray", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _flyout = new MainFlyoutWindow(_settingsService);

        // The Run key stores an absolute path to this exe. If "Start with Windows" is on
        // and the user later moved/renamed the exe (portable app, no installer to keep it
        // pinned), the entry would otherwise silently point at a file that no longer
        // exists. Re-write it with the current path on every manual launch so it heals
        // itself the next time the user runs the app from its new location.
        var settings = _settingsService.Load();
        if (settings.StartWithWindows)
            _settingsService.SetStartWithWindows(true);

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = TrayIconFactory.LoadTrayIcon(),
            Text = "Open OTS Tray",
            Visible = true,
        };
        // Both left and right click open the flyout; Settings/About/Exit live in its gear menu.
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button is WinForms.MouseButtons.Left or WinForms.MouseButtons.Right)
                _flyout.ToggleVisibility();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
