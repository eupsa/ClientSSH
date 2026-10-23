using System.Collections.ObjectModel;
using System.ComponentModel;
using ClientSSH.Models;
using ClientSSH.Services.Demo;
using ClientSSH.Services.Localization;
using ClientSSH.Terminal;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace ClientSSH.Services.Sessions;

/// <summary>
/// Owns every open session so they keep running while the user moves between pages. Create it on the UI
/// thread: process events come from background threads and are marshalled back through its dispatcher.
/// </summary>
public sealed partial class SessionManager : ObservableObject, ISessionManager
{
    private const int DefaultColumns = 120, DefaultRows = 30;

    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();

    public ObservableCollection<SessionTab> Tabs { get; } = [];

    [ObservableProperty] public partial SessionTab? Selected { get; set; }

    /// <summary>Opens a new tab for the host and selects it. Several tabs can share a host.</summary>
    public SessionTab Open(Host host)
    {
        var tab = new SessionTab(Connect(new SessionPane(host)), UniqueTitle(host.Name));
        Tabs.Add(tab);
        Selected = tab;
        return tab;
    }

    /// <summary>Starts a new session in an existing pane, replacing the old one.</summary>
    public void Reconnect(SessionPane pane)
    {
        pane.ConnectingAnimation = "reconnecting";
        Connect(pane);
    }

    public void Close(SessionTab tab)
    {
        foreach (var pane in tab.Panes) pane.EndSession();
        Tabs.Remove(tab);
        if (Selected == tab || Selected is null) Selected = Tabs.LastOrDefault();
    }

    /// <summary>Splits the tab's focused pane; the new pane connects to the same host and takes focus.</summary>
    public void Split(SessionTab tab, bool sideBySide) =>
        tab.Layout.Split(sideBySide, Connect(new SessionPane(tab.Focused.Host)));

    /// <summary>Closes one pane; closing the last one closes the tab.</summary>
    public void ClosePane(SessionTab tab, SessionPane pane)
    {
        if (tab.Layout.Find(pane) is not { } leaf) return;
        pane.EndSession();
        if (!tab.Layout.Close(leaf)) Close(tab);
    }

    /// <summary>Selects the next (+1) or previous (-1) tab, wrapping around.</summary>
    public void SelectNext(int step)
    {
        if (Tabs.Count == 0) return;
        var i = Selected is null ? 0 : Tabs.IndexOf(Selected);
        Selected = Tabs[((i + step) % Tabs.Count + Tabs.Count) % Tabs.Count];
    }

    /// <summary>Closes the tour's demo session, if open.</summary>
    public void CloseDemo()
    {
        foreach (var tab in Tabs.Where(t => t.Panes.Any(p => DemoHost.Is(p.Host))).ToList()) Close(tab);
    }

    private SessionPane Connect(SessionPane pane)
    {
        pane.EndSession();
        pane.ClearNotice();

        try
        {
            var session = new TerminalSession(CreateProcess(pane.Host), DefaultColumns, DefaultRows);
            session.Exited += code => _ui.TryEnqueue(() =>
            {
                pane.IsConnecting = false;
                pane.ShowNotice(string.Format(Loc.Get("Terminal_Ended"), code), isError: false);
            });
            var firstOutput = true;
            session.Updated += () =>
            {
                if (!firstOutput) return;
                firstOutput = false;
                _ui.TryEnqueue(() => ShowConnected(pane));
            };
            pane.IsConnecting = true;
            session.Start();
            pane.Session = session;
        }
        catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException)
        {
            pane.IsConnecting = false;
            pane.ShowNotice(e is InvalidOperationException ? e.Message : string.Format(Loc.Get("Terminal_StartFailed"), e.Message), isError: true);
        }

        return pane;
    }

    // Connecting animation off, short "connected" animation on.
    private async void ShowConnected(SessionPane pane)
    {
        if (!pane.IsConnecting) return;
        pane.IsConnecting = false;
        pane.JustConnected = true;
        await Task.Delay(1400);
        pane.JustConnected = false;
    }

    private static ITerminalProcess CreateProcess(Host host)
    {
        if (DemoHost.Is(host))
            return new SimulatedProcess(Loc.Get("Demo_Banner"), Loc.Get("Demo_Help"), Loc.Get("Demo_NotFound"));

        if (!SshCommand.IsInstalled) throw new InvalidOperationException(Loc.Get("Terminal_NoSsh"));
        if (!SshCommand.IsValid(host)) throw new InvalidOperationException(Loc.Get("Terminal_InvalidHost"));
        return new ConPtyProcess(SshCommand.Build(host));
    }

    private string UniqueTitle(string name)
    {
        var title = name;
        for (var n = 2; Tabs.Any(t => t.Title == title); n++) title = $"{name} ({n})";
        return title;
    }
}
