using ClientSSH.Services.Localization;
using ClientSSH.Services.Storage;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        _window?.AppWindow.Show();
        _window?.Activate();
    }

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
