using System.Collections.Specialized;
using ClientSSH.Models;
using ClientSSH.Services.Demo;
using ClientSSH.Services.Localization;
using ClientSSH.Services.Sessions;
using ClientSSH.Services.Shell;
using ClientSSH.Services.Storage;
using ClientSSH.ViewModels;

namespace ClientSSH.Services.Commands;

/// <summary>The commands ClientSSH ships with. Other features register theirs into <see cref="ICommandRegistry"/> the same way.</summary>
public sealed class BuiltInCommands(
    ICommandRegistry registry, INavigationService nav, ISettingsStore settings, ThemeService theme,
    SessionManager sessions, HostsViewModel hosts, MainViewModel main)
{
    private const int MaxClosed = 10;

    // Hosts of tabs closed in this run, oldest first. Memory only: nothing is stored (history stays opt-in).
    private readonly List<Host> _closed = [];

    public void Register()
    {
        sessions.Tabs.CollectionChanged += OnTabsChanged;

        registry.Register(new PaletteCommand("session.new", Loc.Get("Cmd_NewConnection"),
            () => nav.NavigateTo(PageKey.Hosts), "new tab ssh connect", Glyph: ""));
        registry.Register(SessionCommands);
        registry.Register(OpenHostCommands);
        registry.Register(new PaletteCommand("host.add", Loc.Get("Cmd_AddHost"),
            () => hosts.AddCommand.Execute(null), "new host create", Glyph: ""));

        registry.Register(new PaletteCommand("go.hosts", Loc.Get("Cmd_GoHosts"), () => nav.NavigateTo(PageKey.Hosts), "go hosts home page", Glyph: ""));
        registry.Register(new PaletteCommand("go.sessions", Loc.Get("Cmd_GoSessions"), () => nav.NavigateTo(PageKey.Sessions), "go sessions tabs terminal page", Glyph: ""));
        registry.Register(new PaletteCommand("go.remote", Loc.Get("Cmd_GoRemote"), () => nav.NavigateTo(PageKey.Remote), "remote access desktop anydesk page", Glyph: ""));
        registry.Register(new PaletteCommand("go.settings", Loc.Get("Cmd_GoSettings"), () => nav.NavigateTo(PageKey.Settings), "settings preferences options page", Glyph: ""));

        registry.Register(new PaletteCommand("theme.light", Loc.Get("Cmd_ThemeLight"), () => SetTheme(ThemeChoice.Light), "theme light", Glyph: ""));
        registry.Register(new PaletteCommand("theme.dark", Loc.Get("Cmd_ThemeDark"), () => SetTheme(ThemeChoice.Dark), "theme dark", Glyph: ""));
        registry.Register(new PaletteCommand("theme.system", Loc.Get("Cmd_ThemeSystem"), () => SetTheme(ThemeChoice.System), "theme system windows default", Glyph: ""));

        registry.Register(new PaletteCommand("app.checkUpdates", Loc.Get("Cmd_CheckUpdates"),
            () => _ = main.CheckForUpdateAsync(announceAll: true), "check updates version upgrade", Glyph: ""));
    }

    private IEnumerable<PaletteCommand> SessionCommands()
    {
        if (sessions.Selected is { } tab)
            yield return new PaletteCommand("session.close", string.Format(Loc.Get("Cmd_CloseTab"), tab.Title), () => CloseTab(tab), "close tab session disconnect", Glyph: "");

        var reopenable = Reopenable();
        if (reopenable.Count > 0)
            yield return new PaletteCommand("session.restore", string.Format(Loc.Get("Cmd_RestoreTab"), reopenable[^1].Current.Name),
                () => Restore(1), "restore reopen closed tab session", Glyph: "");
        if (reopenable.Count > 1)
            yield return new PaletteCommand("session.restoreAll", string.Format(Loc.Get("Cmd_RestoreAll"), reopenable.Count),
                () => Restore(reopenable.Count), "restore reopen closed tabs sessions all", Glyph: "");
    }

    // Most recently used first when the opt-in history gave the hosts a last-connected time; otherwise the list order.
    private IEnumerable<PaletteCommand> OpenHostCommands() =>
        hosts.Hosts
            .OrderByDescending(h => h.LastConnectedUtc ?? DateTime.MinValue)
            .Select(h => new PaletteCommand("host.open." + h.Id, string.Format(Loc.Get("Cmd_OpenHost"), h.Name),
                () => hosts.ConnectCommand.Execute(h), $"ssh connect {h.User}@{h.Address} {h.Group}", Glyph: h.Favorite ? "" : ""))
            .ToList();

    private void CloseTab(SessionTab tab)
    {
        sessions.Close(tab);
        if (sessions.Tabs.Count == 0) nav.NavigateTo(PageKey.Hosts);
    }

    private void SetTheme(ThemeChoice choice)
    {
        settings.Save(settings.Current with { Theme = choice });
        theme.Apply();
    }

    // Hosts removed since their tab closed are skipped; edited ones reopen with their current details.
    private List<(Host Closed, Host Current)> Reopenable() =>
    [
        .. _closed
            .Select(c => (Closed: c, Current: hosts.Hosts.FirstOrDefault(h => h.Id == c.Id)))
            .Where(p => p.Current is not null)
            .Select(p => (p.Closed, p.Current!)),
    ];

    private void Restore(int count)
    {
        foreach (var (closed, current) in Reopenable().TakeLast(count))
        {
            _closed.Remove(closed);
            hosts.OpenTabCommand.Execute(current);
        }

        nav.NavigateTo(PageKey.Sessions);
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Remove || e.OldItems is null) return;
        foreach (SessionTab tab in e.OldItems)
            if (!DemoHost.Is(tab.Host)) _closed.Add(tab.Host);
        if (_closed.Count > MaxClosed) _closed.RemoveRange(0, _closed.Count - MaxClosed);
    }
}
