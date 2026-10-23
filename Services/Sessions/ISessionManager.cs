using System.Collections.ObjectModel;
using System.ComponentModel;
using ClientSSH.Models;

namespace ClientSSH.Services.Sessions;

/// <summary>The open tabs and their live sessions, shared by every page.</summary>
public interface ISessionManager : INotifyPropertyChanged
{
    ObservableCollection<SessionTab> Tabs { get; }

    SessionTab? Selected { get; set; }

    /// <summary>Opens a new tab for the host and selects it. Several tabs can share a host.</summary>
    SessionTab Open(Host host);

    /// <summary>Starts a new session in an existing pane, replacing the old one.</summary>
    void Reconnect(SessionPane pane);

    /// <summary>Splits the tab's focused pane: side by side, or one above the other.</summary>
    void Split(SessionTab tab, bool sideBySide);

    /// <summary>Closes one pane; closing the last one closes the tab.</summary>
    void ClosePane(SessionTab tab, SessionPane pane);

    /// <summary>Selects the next (+1) or previous (-1) tab, wrapping around.</summary>
    void SelectNext(int step);

    void Close(SessionTab tab);

    /// <summary>Closes the tour's demo session, if open.</summary>
    void CloseDemo();
}
