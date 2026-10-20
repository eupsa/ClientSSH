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
    private readonly ThemeService _theme;
    private readonly IUpdateService _updates;
    private readonly IHistoryStore _history;
    private readonly IStartupService _startup;
    private readonly TrayService _tray;
    private bool _syncing;
    private readonly bool _ready;
    private string? _releaseUrl;

    public TourViewModel Tour { get; }

    public string DataFolder => LocalData.Dir;

    public string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "";

    [ObservableProperty] public partial int ThemeIndex { get; set; }
    [ObservableProperty] public partial double FontSize { get; set; }
    [ObservableProperty] public partial int LanguageIndex { get; set; }
    [ObservableProperty] public partial bool LanguageChanged { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsNotChecking))] public partial bool IsChecking { get; set; }
    [ObservableProperty] public partial string UpdateMessage { get; set; }
    [ObservableProperty] public partial bool HasUpdate { get; set; }
    [ObservableProperty] public partial string UpdateRepo { get; set; }
    [ObservableProperty] public partial bool HasUpdateMessage { get; set; }
    [ObservableProperty] public partial bool SaveHistory { get; set; }
    [ObservableProperty] public partial bool HistoryCleared { get; set; }
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial bool StartupBlocked { get; set; }
    [ObservableProperty] public partial bool StartMinimized { get; set; }
    [ObservableProperty] public partial bool CloseToTray { get; set; }
    [ObservableProperty] public partial InfoBarSeverity UpdateSeverity { get; set; }

    public bool IsNotChecking => !IsChecking;

    // Same order as the ComboBox items in SettingsPage.
    private static readonly string[] Languages = ["pt-BR", "en-US"];

    public SettingsViewModel(ISettingsStore store, ThemeService theme, TourViewModel tour, IUpdateService updates,
        IStartupService startup, TrayService tray, IHistoryStore history)
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
        FontSize = store.Current.TerminalFontSize;
        LanguageIndex = Math.Max(0, Array.IndexOf(Languages, store.Current.Language));
        SaveHistory = store.Current.SaveHistory;
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
        _store.Save(_store.Current with { Language = Languages[value] });
        LanguageChanged = true;
    }

    partial void OnFontSizeChanged(double value)
    {
        if (!_ready || double.IsNaN(value)) return;
        _store.Save(_store.Current with { TerminalFontSize = (int)value });
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
        UpdateMessage = Loc.Get("Update_Checking");
        UpdateSeverity = InfoBarSeverity.Informational;
        HasUpdateMessage = true;

        var result = await _updates.CheckAsync();
        _releaseUrl = result.Url;
        HasUpdate = result.Status == UpdateStatus.Available;
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
    private void OpenRelease()
    {
        // The URL comes from the network: only open GitHub links.
        if (Uri.TryCreate(_releaseUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && url.Host == "github.com")
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
    }
}
