using ClientSSH.Services.Sessions;
using ClientSSH.Services.Shell;
using ClientSSH.Services.Localization;
using ClientSSH.Services.Storage;
using ClientSSH.Services.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientSSH.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _nav;
    private readonly ISettingsStore _settings;
    private readonly IUpdateService _updates;
    private UpdateResult? _available;

    [ObservableProperty] public partial bool IsSplashVisible { get; set; }
    [ObservableProperty] public partial double SplashOpacity { get; set; }
    [ObservableProperty] public partial bool IsPaneOpen { get; set; }
    [ObservableProperty] public partial bool IsPaneVisible { get; set; }
    [ObservableProperty] public partial int SessionCount { get; set; }
    [ObservableProperty] public partial bool HasSessions { get; set; }
    [ObservableProperty] public partial bool IsUpdateBarOpen { get; set; }
    [ObservableProperty] public partial string UpdateBarMessage { get; set; } = "";
    [ObservableProperty] public partial bool CanInstallUpdate { get; set; }

    public MainViewModel(INavigationService nav, ISettingsStore settings, SessionManager sessions, IUpdateService updates)
    {
        _nav = nav;
        _settings = settings;
        _updates = updates;
        IsSplashVisible = true;
        SplashOpacity = 1;
        IsPaneOpen = true;
        _nav.Navigated += OnNavigated;
        sessions.Tabs.CollectionChanged += (_, _) =>
        {
            SessionCount = sessions.Tabs.Count;
            HasSessions = SessionCount > 0;
        };
    }

    public async Task StartAsync()
    {
        await Task.Delay(1500);
        _nav.NavigateTo(_settings.OnboardingDone ? PageKey.Hosts : PageKey.Welcome);
        SplashOpacity = 0;
        await Task.Delay(500);
        IsSplashVisible = false;

        // On by default, off in Settings. A run that ended badly checks even then, once, because an update may be the fix.
        if (_settings.Current.AutoUpdateCheck || RunMarker.PreviousRunEndedBadly) await CheckForUpdateAsync();
    }

    /// <param name="announceAll">Also show "checking", "up to date" and failures (asked by the user, e.g. from the palette).</param>
    public async Task CheckForUpdateAsync(bool announceAll = false)
    {
        if (announceAll)
        {
            CanInstallUpdate = false;
            UpdateBarMessage = Loc.Get("Update_Checking");
            IsUpdateBarOpen = true;
        }

        var result = await _updates.CheckAsync();
        if (result.Status != UpdateStatus.Available)
        {
            if (announceAll)
                UpdateBarMessage = result.Status switch
                {
                    UpdateStatus.UpToDate => string.Format(Loc.Get("Update_UpToDate"), result.Version),
                    UpdateStatus.NoReleases => Loc.Get("Update_None"),
                    _ => Loc.Get("Update_Failed"),
                };
            return;
        }

        _available = result;
        CanInstallUpdate = result.InstallerUrl is not null && result.SumsUrl is not null;
        UpdateBarMessage = string.Format(Loc.Get("Update_Available"), result.Version);
        IsUpdateBarOpen = true;
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_available is null) return;

        CanInstallUpdate = false;
        UpdateBarMessage = Loc.Get("Update_Downloading");
        var installer = await _updates.DownloadAsync(_available);
        if (installer is null)
        {
            UpdateBarMessage = Loc.Get("Update_DownloadFailed");
            return;
        }

        // The installer closes this app, replaces the files and starts it again.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installer, "/SILENT") { UseShellExecute = true });
        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    // Sidebar: hidden on welcome, open on hosts and settings, collapsed to the icon rail on the sessions page.
    private void OnNavigated(PageKey key)
    {
        IsPaneVisible = key != PageKey.Welcome;
        IsPaneOpen = key is PageKey.Hosts or PageKey.Remote or PageKey.Settings;
    }

    [RelayCommand]
    private void Navigate(PageKey key) => _nav.NavigateTo(key);
}
