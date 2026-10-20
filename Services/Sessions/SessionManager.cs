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
public sealed partial class SessionManager : ObservableObject
{
    private const int DefaultColumns = 120, DefaultRows = 30;

    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();

    public ObservableCollection<SessionTab> Tabs { get; } = [];

    [ObservableProperty] public partial SessionTab? Selected { get; set; }

    /// <summary>Opens a new tab for the host and selects it. Several tabs can share a host.</summary>
    public SessionTab Open(Host host)
    {
        var tab = new SessionTab(host, UniqueTitle(host.Name));
        Connect(tab);
        Tabs.Add(tab);
        Selected = tab;
        return tab;
    }

    /// <summary>Starts a new session in an existing tab, replacing the old one.</summary>
    public void Reconnect(SessionTab tab)
    {
        tab.ConnectingAnimation = "reconnecting";
        Connect(tab);
    }

    public void Close(SessionTab tab)
    {
        tab.EndSession();
        Tabs.Remove(tab);
        if (Selected == tab || Selected is null) Selected = Tabs.LastOrDefault();
    }

    /// <summary>Closes the tour's demo session, if open.</summary>
    public void CloseDemo()
    {
        foreach (var tab in Tabs.Where(t => DemoHost.Is(t.Host)).ToList()) Close(tab);
    }

    private void Connect(SessionTab tab)
    {
        tab.EndSession();
        tab.ClearNotice();

        try
        {
            var session = new TerminalSession(CreateProcess(tab.Host), DefaultColumns, DefaultRows);
            session.Exited += code => _ui.TryEnqueue(() =>
            {
                tab.IsConnecting = false;
                tab.ShowNotice(string.Format(Loc.Get("Terminal_Ended"), code), isError: false);
            });
            var firstOutput = true;
            session.Updated += () =>
            {
                if (!firstOutput) return;
                firstOutput = false;
                _ui.TryEnqueue(() => ShowConnected(tab));
            };
            tab.IsConnecting = true;
            session.Start();
            tab.Session = session;
        }
        catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException)
        {
            tab.IsConnecting = false;
            tab.ShowNotice(e is InvalidOperationException ? e.Message : string.Format(Loc.Get("Terminal_StartFailed"), e.Message), isError: true);
        }
    }

    // Connecting animation off, short "connected" animation on.
    private async void ShowConnected(SessionTab tab)
    {
        if (!tab.IsConnecting) return;
        tab.IsConnecting = false;
        tab.JustConnected = true;
        await Task.Delay(1400);
        tab.JustConnected = false;
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
