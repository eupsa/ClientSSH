using ClientSSH.Services.Sessions;
using ClientSSH.Services.Shell;
using ClientSSH.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientSSH.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _nav;
    private readonly ISettingsStore _settings;

    [ObservableProperty] public partial bool IsSplashVisible { get; set; }
    [ObservableProperty] public partial double SplashOpacity { get; set; }
    [ObservableProperty] public partial bool IsPaneOpen { get; set; }
    [ObservableProperty] public partial bool IsPaneVisible { get; set; }
    [ObservableProperty] public partial int SessionCount { get; set; }
    [ObservableProperty] public partial bool HasSessions { get; set; }

    public MainViewModel(INavigationService nav, ISettingsStore settings, SessionManager sessions)
    {
        _nav = nav;
        _settings = settings;
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
    }

    // Sidebar: hidden on welcome, open on hosts and settings, collapsed to the icon rail on the sessions page.
    private void OnNavigated(PageKey key)
    {
        IsPaneVisible = key != PageKey.Welcome;
        IsPaneOpen = key == PageKey.Hosts || key == PageKey.Settings;
    }

    [RelayCommand]
    private void Navigate(PageKey key) => _nav.NavigateTo(key);
}
