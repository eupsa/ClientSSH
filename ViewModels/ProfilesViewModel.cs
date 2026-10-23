using System.Collections.ObjectModel;
using ClientSSH.Services.Customization;
using ClientSSH.Services.Sessions;
using ClientSSH.Services.Shell;
using ClientSSH.Terminal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientSSH.ViewModels;

/// <summary>The local shells (PowerShell, CMD, WSL, Git Bash) found on this PC and the commands that open them.</summary>
public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly ProfileService _profiles;
    private readonly SessionManager _sessions;
    private readonly INavigationService _navigation;

    public ObservableCollection<ShellProfile> Profiles { get; } = [];

    [ObservableProperty] public partial bool HasProfiles { get; set; }

    /// <summary>Raised on the UI thread once detection finished.</summary>
    public event Action? Loaded;

    public ProfilesViewModel(ProfileService profiles, SessionManager sessions, INavigationService navigation)
    {
        _profiles = profiles;
        _sessions = sessions;
        _navigation = navigation;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        foreach (var profile in await _profiles.GetAsync()) Profiles.Add(profile);
        HasProfiles = Profiles.Count > 0;
        Loaded?.Invoke();
    }

    /// <summary>Opens the shell in a new tab and shows the terminal.</summary>
    [RelayCommand]
    private void OpenProfile(ShellProfile profile)
    {
        _sessions.Open(profile);
        _navigation.NavigateTo(PageKey.Sessions);
    }

    /// <summary>Opens another tab from the sessions page itself, without navigating.</summary>
    [RelayCommand]
    private void OpenTab(ShellProfile profile) => _sessions.Open(profile);

    /// <summary>For the command palette and shortcuts: "powershell", "pwsh", "cmd", "gitbash" or "wsl:&lt;distro&gt;".</summary>
    [RelayCommand]
    private void OpenProfileById(string id)
    {
        if (_profiles.Find(id) is { } profile) OpenProfile(profile);
    }
}
