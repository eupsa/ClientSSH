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

    /// <summary>Starts a new session in an existing tab, replacing the old one.</summary>
    void Reconnect(SessionTab tab);

    void Close(SessionTab tab);

    /// <summary>Closes the tour's demo session, if open.</summary>
    void CloseDemo();
}
