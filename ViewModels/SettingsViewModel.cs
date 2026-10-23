using System.Diagnostics;
using ClientSSH.Models;
using ClientSSH.Services.Localization;
using ClientSSH.Services.Shell;
using ClientSSH.Services.Storage;
using ClientSSH.Services.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace ClientSSH.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly IThemeService _theme;
    private readonly IUpdateService _updates;
    private readonly IHistoryStore _history;
    private readonly IStartupService _startup;
    private readonly ITrayService _tray;
    private bool _syncing;
    private readonly bool _ready;
    private string? _releaseUrl;
    private UpdateResult? _available;

    // Inno Setup leaves its uninstaller next to the exe; a dev or MSIX build has none, so the row is hidden.
    private static readonly string Uninstaller = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
    public bool CanUninstall => File.Exists(Uninstaller);

    public TourViewModel Tour { get; }

    public string DataFolder => LocalData.Dir;

    public string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "";

    [ObservableProperty] public partial int ThemeIndex { get; set; }
    [ObservableProperty] public partial int LanguageIndex { get; set; }
    [ObservableProperty] public partial bool LanguageChanged { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsNotChecking))] public partial bool IsChecking { get; set; }
    [ObservableProperty] public partial string UpdateMessage { get; set; }
    [ObservableProperty] public partial bool HasUpdate { get; set; }
    [ObservableProperty] public partial bool CanInstallUpdate { get; set; }
    [ObservableProperty] public partial string UpdateRepo { get; set; }
    [ObservableProperty] public partial bool HasUpdateMessage { get; set; }
    [ObservableProperty] public partial bool SaveHistory { get; set; }
    [ObservableProperty] public partial bool AutoUpdateCheck { get; set; }
    [ObservableProperty] public partial bool HistoryCleared { get; set; }
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial bool StartupBlocked { get; set; }
    [ObservableProperty] public partial bool StartMinimized { get; set; }
    [ObservableProperty] public partial bool CloseToTray { get; set; }
    [ObservableProperty] public partial InfoBarSeverity UpdateSeverity { get; set; }

    public bool IsNotChecking => !IsChecking;

    public IReadOnlyList<LanguageOption> Languages => LanguageOption.All;

    public SettingsViewModel(ISettingsStore store, IThemeService theme, TourViewModel tour, IUpdateService updates,
        IStartupService startup, ITrayService tray, IHistoryStore history)
    {
        _store = store;
        _theme = theme;
        _updates = updates;
        _history = history;
        _startup = startup;
        _tray = tray;
        Tour = tour;
        UpdateMessage = "";
        UpdateRepo = store.Current.UpdateRepo;
        ThemeIndex = (int)store.Current.Theme;
        LanguageIndex = Math.Max(0, LanguageOption.All.ToList().FindIndex(l => l.Tag == store.Current.Language));
        SaveHistory = store.Current.SaveHistory;
        AutoUpdateCheck = store.Current.AutoUpdateCheck;
        StartMinimized = store.Current.StartMinimized;
        CloseToTray = store.Current.CloseToTray;
        _ready = true;
        _ = LoadStartupAsync();
    }

    private async Task LoadStartupAsync() => ShowStartup(await _startup.GetAsync());

    // Reflect the OS state without re-triggering the change handler.
    private void ShowStartup(StartupState state)
    {
        _syncing = true;
        StartWithWindows = state == StartupState.Enabled;
        StartupBlocked = state == StartupState.Blocked;
        _syncing = false;
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_ready || _syncing) return;
        _ = SetStartupAsync(value);
    }

    private async Task SetStartupAsync(bool enable) => ShowStartup(await _startup.SetAsync(enable));

    partial void OnUpdateRepoChanged(string value)
    {
        if (!_ready) return;
        var repo = string.IsNullOrWhiteSpace(value) ? AppSettings.DefaultUpdateRepo : value.Trim();
        _store.Save(_store.Current with { UpdateRepo = repo });
    }

    partial void OnSaveHistoryChanged(bool value)
    {
        if (!_ready) return;
        _store.Save(_store.Current with { SaveHistory = value });
    }

    partial void OnAutoUpdateCheckChanged(bool value)
    {
        if (!_ready) return;
        _store.Save(_store.Current with { AutoUpdateCheck = value });
    }

    [RelayCommand]
    private void ClearHistory()
    {
        _history.Clear();
        HistoryCleared = true;
    }

    partial void OnStartMinimizedChanged(bool value)
    {
        if (!_ready) return;
        _store.Save(_store.Current with { StartMinimized = value });
        _tray.Apply();
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (!_ready) return;
        _store.Save(_store.Current with { CloseToTray = value });
        _tray.Apply();
    }

    [RelayCommand]
    private void OpenStartupSettings() =>
        Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });

    partial void OnThemeIndexChanged(int value)
    {
        if (!_ready) return;
        _store.Save(_store.Current with { Theme = (ThemeChoice)value });
        _theme.Apply();
    }

    partial void OnLanguageIndexChanged(int value)
    {
        if (!_ready || value < 0) return;
        _store.Save(_store.Current with { Language = LanguageOption.All[value].Tag });
        LanguageChanged = true;
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(DataFolder);
        Process.Start(new ProcessStartInfo(DataFolder) { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        IsChecking = true;
        HasUpdate = false;
        CanInstallUpdate = false;
        UpdateMessage = Loc.Get("Update_Checking");
        UpdateSeverity = InfoBarSeverity.Informational;
        HasUpdateMessage = true;

        var result = await _updates.CheckAsync();
        _releaseUrl = result.Url;
        HasUpdate = result.Status == UpdateStatus.Available;
        _available = HasUpdate ? result : null;
        CanInstallUpdate = result.InstallerUrl is not null && result.SumsUrl is not null;
        UpdateMessage = result.Status switch
        {
            UpdateStatus.UpToDate => string.Format(Loc.Get("Update_UpToDate"), result.Version),
            UpdateStatus.Available => string.Format(Loc.Get("Update_Available"), result.Version),
            UpdateStatus.NoReleases => Loc.Get("Update_None"),
            _ => Loc.Get("Update_Failed"),
        };
        UpdateSeverity = result.Status switch
        {
            UpdateStatus.UpToDate => InfoBarSeverity.Success,
            UpdateStatus.Available => InfoBarSeverity.Success,
            UpdateStatus.NoReleases => InfoBarSeverity.Informational,
            _ => InfoBarSeverity.Warning,
        };
        IsChecking = false;
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_available is null) return;

        CanInstallUpdate = false;
        UpdateSeverity = InfoBarSeverity.Informational;
        UpdateMessage = Loc.Get("Update_Downloading");
        if (await _updates.InstallAsync(_available))
        {
            Microsoft.UI.Xaml.Application.Current.Exit();
            return;
        }
        UpdateSeverity = InfoBarSeverity.Warning;
        UpdateMessage = Loc.Get("Update_DownloadFailed");
        CanInstallUpdate = true;
    }

    [RelayCommand]
    private void Uninstall()
    {
        // The uninstaller asks for confirmation itself, and it can only remove the files once this app is closed.
        Process.Start(new ProcessStartInfo(Uninstaller) { UseShellExecute = true });
        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    [RelayCommand]
    private void OpenRelease()
    {
        // The URL comes from the network: only open GitHub links.
        if (Uri.TryCreate(_releaseUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && url.Host == "github.com")
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
    }
}
