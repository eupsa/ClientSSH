using ClientSSH.Models;
using ClientSSH.Terminal;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientSSH.Services.Sessions;

/// <summary>One pane of a tab: an SSH host or a local shell profile, its live session and any status message to show over the terminal.</summary>
public sealed partial class SessionPane(Host? host, ShellProfile? profile = null) : ObservableObject
{
    /// <summary>Null for a local shell pane.</summary>
    public Host? Host { get; } = host;

    /// <summary>The local shell this pane runs; null for SSH.</summary>
    public ShellProfile? Profile { get; } = profile;

    /// <summary>Null when the session could not start; the terminal keeps its last screen after it ends.</summary>
    [ObservableProperty] public partial TerminalSession? Session { get; set; }

    [ObservableProperty] public partial string Notice { get; set; } = "";
    [ObservableProperty] public partial bool HasNotice { get; set; }
    [ObservableProperty] public partial bool NoticeIsError { get; set; }

    /// <summary>True from the start until the first output arrives.</summary>
    [ObservableProperty] public partial bool IsConnecting { get; set; }

    /// <summary>True for a moment after the first output, for the "connected" animation.</summary>
    [ObservableProperty] public partial bool JustConnected { get; set; }

    /// <summary>"reconnecting" after a reconnect, "connecting" the first time.</summary>
    [ObservableProperty] public partial string ConnectingAnimation { get; set; } = "connecting";

    /// <summary>The pane that gets the keyboard in its tab (shown with an accent border when the tab is split).</summary>
    [ObservableProperty] public partial bool IsFocused { get; set; }

    public void ShowNotice(string message, bool isError)
    {
        Notice = message;
        NoticeIsError = isError;
        HasNotice = true;
    }

    public void ClearNotice() => HasNotice = false;

    public void EndSession()
    {
        var session = Session;
        Session = null;
        session?.Dispose();
    }
}
