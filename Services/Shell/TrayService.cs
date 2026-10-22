using ClientSSH.Services.Localization;
using ClientSSH.Services.Storage;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace ClientSSH.Services.Shell;

/// <summary>Tray icon plus hide-on-close. The icon exists only while one of the tray options is on.</summary>
public sealed class TrayService(ISettingsStore settings)
{
    private TaskbarIcon? _icon;
    private Window? _window;
    private bool _exiting;

    public void Attach(Window window)
    {
        _window = window;
        window.AppWindow.Closing += OnClosing;
        Apply();
    }

    /// <summary>Call after the tray options change.</summary>
    public void Apply()
    {
        var needed = settings.Current.CloseToTray || settings.Current.StartMinimized;
        if (needed && _icon is null) CreateIcon();
        else if (!needed && _icon is not null)
        {
            _icon.Dispose();
            _icon = null;
        }
    }

    public void HideToTray() => _window?.AppWindow.Hide();

    public void Show()
    {
        if (_window?.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        _window?.AppWindow.Show();
        _window?.Activate();
    }

    /// <summary>
    /// Brings the window up over other apps (restored, shown from the tray, kept on top) and flashes the taskbar button,
    /// plus a Windows notification when one can be shown. Until <see cref="EndAttention"/>.
    /// </summary>
    public void DemandAttention(string title, string body)
    {
        if (_window is null) return;
        Show();
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = true;
        var flash = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window),
            dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
        };
        FlashWindowEx(ref flash);
        MessageBeep(0x30); // the "exclamation" sound

        try
        {
            if (!_notificationsRegistered)
            {
                AppNotificationManager.Default.NotificationInvoked += (_, _) => _window.DispatcherQueue.TryEnqueue(Show);
                AppNotificationManager.Default.Register();
                _notificationsRegistered = true;
            }
            AppNotificationManager.Default.Show(new AppNotificationBuilder().AddText(title).AddText(body).BuildNotification());
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Notifications off or unavailable: the window on top and the flashing button still say it.
        }
    }

    public void EndAttention()
    {
        if (_window?.AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = false;
    }

    private bool _notificationsRegistered;

    private const uint FLASHW_ALL = 3, FLASHW_TIMERNOFG = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags, uCount, dwTimeout; }

    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO info);
    [DllImport("user32.dll")] private static extern bool MessageBeep(uint type);

    private void CreateIcon()
    {
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = Loc.Get("Tray_Open") };
        open.Click += (_, _) => Show();
        var exit = new MenuFlyoutItem { Text = Loc.Get("Tray_Exit") };
        exit.Click += (_, _) => Exit();
        menu.Items.Add(open);
        menu.Items.Add(exit);

        _icon = new TaskbarIcon
        {
            ToolTipText = "ClientSSH",
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "ClientSSH.ico")),
            ContextFlyout = menu,
            LeftClickCommand = new RelayCommand(Show),
        };
        _icon.ForceCreate(false);
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting || _icon is null || !settings.Current.CloseToTray) return;
        args.Cancel = true;
        sender.Hide();
    }

    private void Exit()
    {
        _exiting = true;
        _icon?.Dispose();
        _icon = null;
        Application.Current.Exit();
    }
}
