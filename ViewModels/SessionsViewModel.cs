using ClientSSH.Services.Sessions;
using ClientSSH.Services.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientSSH.ViewModels;

/// <summary>Backs the sessions page. The sessions themselves live in <see cref="SessionManager"/>.</summary>
public sealed partial class SessionsViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigation;

    public SessionManager Sessions { get; }

    [ObservableProperty] public partial bool HasTabs { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }

    public SessionsViewModel(SessionManager sessions, INavigationService navigation)
    {
        Sessions = sessions;
        _navigation = navigation;
        Sessions.Tabs.CollectionChanged += OnTabsChanged;
        Refresh();
    }

    [RelayCommand]
    private void NewSession() => _navigation.NavigateTo(PageKey.Hosts);

    [RelayCommand]
    private void CloseTab(SessionTab tab)
    {
        Sessions.Close(tab);
        if (Sessions.Tabs.Count == 0) _navigation.NavigateTo(PageKey.Hosts);
    }

    public void Dispose() => Sessions.Tabs.CollectionChanged -= OnTabsChanged;

    private void OnTabsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        HasTabs = Sessions.Tabs.Count > 0;
        IsEmpty = !HasTabs;
    }
}
