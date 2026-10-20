using System.Collections.ObjectModel;
using ClientSSH.Models;
using ClientSSH.Services.Demo;
using ClientSSH.Services.Sessions;
using ClientSSH.Services.Shell;
using ClientSSH.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientSSH.ViewModels;

public sealed partial class HostsViewModel : ObservableObject
{
    private readonly IHostStore _store;
    private readonly IHistoryStore _history;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly SessionManager _sessions;

    [ObservableProperty] public partial bool IsEmpty { get; set; }

    public ObservableCollection<Host> Hosts { get; } = [];

    public HostsViewModel(
        IHostStore store, IHistoryStore history, ISettingsStore settings, IDialogService dialogs,
        INavigationService navigation, TourViewModel tour, SessionManager sessions)
    {
        _store = store;
        _history = history;
        _settings = settings;
        _dialogs = dialogs;
        _navigation = navigation;
        _sessions = sessions;

        foreach (var host in _store.Load()) Hosts.Add(host);
        Hosts.CollectionChanged += (_, _) => IsEmpty = Hosts.Count == 0;
        _history.Cleared += ForgetLastConnections;

        // The demo host is listed only while the tour runs.
        tour.PropertyChanged += (_, _) => SyncDemoHost(tour.IsActive);
        SyncDemoHost(tour.IsActive);
        IsEmpty = Hosts.Count == 0;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var host = await _dialogs.ShowHostEditorAsync();
        if (host is null) return;
        Hosts.Add(host);
        SortFavoritesFirst();
        Persist();
    }

    [RelayCommand]
    private void Remove(Host host)
    {
        Hosts.Remove(host);
        Persist();
    }

    [RelayCommand]
    private void ToggleFavorite(Host host)
    {
        var index = Hosts.IndexOf(host);
        if (index < 0 || DemoHost.Is(host)) return;
        Hosts[index] = host with { Favorite = !host.Favorite };
        SortFavoritesFirst();
        Persist();
    }

    [RelayCommand]
    private void Connect(Host host)
    {
        OpenSession(host);
        _navigation.NavigateTo(PageKey.Sessions);
    }

    /// <summary>Opens another tab from the sessions page itself (the "+" button), without leaving it.</summary>
    [RelayCommand]
    private void OpenTab(Host host) => OpenSession(host);

    private void OpenSession(Host host)
    {
        if (_settings.Current.SaveHistory && !DemoHost.Is(host))
        {
            var when = _history.Record(host.Id);
            var index = Hosts.IndexOf(host);
            if (index >= 0) Hosts[index] = host with { LastConnectedUtc = when };
        }

        _sessions.Open(host);
    }

    private void Persist() => _store.Save(Hosts.Where(h => !DemoHost.Is(h)));

    // Stable: hosts keep their relative order inside each group. Moves (not rebuilds) keep the list animation.
    private void SortFavoritesFirst()
    {
        var sorted = Hosts.OrderByDescending(h => h.Favorite).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var current = Hosts.IndexOf(sorted[i]);
            if (current != i) Hosts.Move(current, i);
        }
    }

    private void ForgetLastConnections()
    {
        for (var i = 0; i < Hosts.Count; i++)
            if (Hosts[i].LastConnectedUtc is not null) Hosts[i] = Hosts[i] with { LastConnectedUtc = null };
    }

    private void SyncDemoHost(bool tourActive)
    {
        var listed = Hosts.Any(DemoHost.Is);
        if (tourActive && !listed) Hosts.Insert(0, DemoHost.Host);
        else if (!tourActive && listed) Hosts.Remove(DemoHost.Host);
    }
}
