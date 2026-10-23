using ClientSSH.Services.Demo;
using ClientSSH.Services.Sessions;
using ClientSSH.Services.Shell;
using ClientSSH.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientSSH.ViewModels;

/// <summary>
/// First-run tour. Steps: 1 add host (hosts page), 2 demo host (hosts page),
/// 3 simulated terminal, 4 sidebar. Step 0 means no tour.
/// </summary>
public sealed partial class TourViewModel : ObservableObject
{
    private const int LastStep = 4;

    private readonly INavigationService _nav;
    private readonly ISettingsStore _settings;
    private readonly ISessionManager _sessions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(IsStep4))]
    public partial int Step { get; set; }

    public bool IsActive => Step > 0;
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;
    public bool IsStep4 => Step == 4;

    public TourViewModel(INavigationService nav, ISettingsStore settings, ISessionManager sessions)
    {
        _nav = nav;
        _settings = settings;
        _sessions = sessions;
    }

    [RelayCommand]
    private void Start()
    {
        Step = 1;
        _nav.NavigateTo(PageKey.Hosts);
    }

    [RelayCommand]
    private void Next()
    {
        if (Step >= LastStep)
        {
            Finish();
            return;
        }

        Step++;
        if (Step != 3) return;
        _sessions.Open(DemoHost.Host);
        _nav.NavigateTo(PageKey.Sessions);
    }

    [RelayCommand]
    private void Finish()
    {
        Step = 0;
        _sessions.CloseDemo();
        _settings.MarkOnboardingDone();
        _nav.NavigateTo(PageKey.Hosts);
    }
}
