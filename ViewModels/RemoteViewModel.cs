using ClientSSH.Services.Localization;
using ClientSSH.Services.Remote;
using ClientSSH.Services.Shell;
using ClientSSH.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace ClientSSH.ViewModels;

/// <summary>
/// Remote access between two PCs with no server: one shares its screen, the other watches and controls it. The two
/// people trade two pasted codes (<see cref="RemoteCode"/>) over any chat, and the host person must allow the viewer.
/// One session at a time. Create it on the UI thread: session events come from background threads.
/// </summary>
public sealed partial class RemoteViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private RemoteSession? _session;
    private CancellationTokenSource? _mailbox;
    private string _serverCode = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsHost), nameof(IsViewer))]
    public partial bool HasSession { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsHost), nameof(IsViewer))]
    public partial bool SessionIsHost { get; set; }

    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial string HostCode { get; set; } = "";
    [ObservableProperty] public partial string HostReplyInput { get; set; } = "";
    [ObservableProperty] public partial string OfferInput { get; set; } = "";
    [ObservableProperty] public partial string ViewerReplyCode { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSecurityCode), nameof(SecurityText))]
    public partial string SecurityCode { get; set; } = "";
    [ObservableProperty] public partial bool NetworkWarning { get; set; }

    /// <summary>The short code that stands for the host's long code, when a rendezvous server is set.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasShortCode))]
    public partial string ShortCode { get; set; } = "";

    public bool HasShortCode => ShortCode.Length > 0;

    /// <summary>Address of the user's own rendezvous server ("host" or "host:port"); empty means codes are swapped by hand.</summary>
    public string ServerAddress
    {
        get => _settings.Current.RendezvousAddress;
        set
        {
            value = value.Trim();
            if (value == _settings.Current.RendezvousAddress) return;
            _settings.Save(_settings.Current with { RendezvousAddress = value });
            OnPropertyChanged();
        }
    }

    /// <summary>The host's reply box is shown until the connection is made.</summary>
    [ObservableProperty] public partial bool AwaitingReply { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccessText))]
    public partial RemoteAccess Access { get; set; }

    /// <summary>True while this PC's screen is being shown to the other one.</summary>
    [ObservableProperty] public partial bool IsSharing { get; set; }

    /// <summary>True while this PC shows the other one's screen.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsNotViewing))]
    public partial bool IsViewing { get; set; }

    public bool IsNotViewing => !IsViewing;
    public bool HasSecurityCode => SecurityCode.Length > 0;
    public string SecurityText => HasSecurityCode ? string.Format(Loc.Get("Remote_Security"), SecurityCode) : "";

    public bool IsIdle => !HasSession;
    public bool IsHost => HasSession && SessionIsHost;
    public bool IsViewer => HasSession && !SessionIsHost;

    public string AccessText => Access switch
    {
        RemoteAccess.Control => Loc.Get("Remote_Access_Control"),
        RemoteAccess.View => Loc.Get("Remote_Access_View"),
        _ => "",
    };

    /// <summary>A new picture of the other PC's screen, on the UI thread.</summary>
    public event Action<byte[]>? FrameReady;

    public RemoteViewModel(ISettingsStore settings, IDialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
    }

    private RemoteOptions Options => new()
    {
        StunServers = _settings.Current.RemoteStunServers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(2).ToList(),
    };

    [RelayCommand]
    private async Task ShareAsync()
    {
        if (HasSession) return;
        Begin(isHost: true, Loc.Get("Remote_Status_Preparing"));
        try
        {
            var session = await RemoteSession.CreateHostAsync(Options, new GdiScreenSource(), new SendInputSink());
            Attach(session);
            HostCode = session.Code;
            AwaitingReply = true;
            NetworkWarning = session.NetworkMayBlockDirect;
            Status = Loc.Get("Remote_Status_SendCode");
            await PublishShortCodeAsync(session);
        }
        catch (Exception e) when (e is RemoteCodeException or System.Net.Sockets.SocketException)
        {
            Reset(Loc.Get("Remote_Status_Failed"));
        }
    }

    // With a server set, the host also gets a short code; the reply then arrives by itself and the paste box stays as a fallback.
    private async Task PublishShortCodeAsync(RemoteSession session)
    {
        var server = ServerAddress;
        if (RendezvousClient.ParseServer(server) is null) return;

        try
        {
            for (var attempt = 0; attempt < 5 && _serverCode.Length == 0; attempt++)
            {
                var code = RendezvousClient.NewCode();
                if (await RendezvousClient.RequestAsync(server, $"PUT {code} {RemoteCode.Decode(session.Code).Compact()}") == "OK") _serverCode = code;
            }
        }
        catch (Exception e) when (IsNetworkError(e)) { }

        if (_serverCode.Length == 0)
        {
            Status = Loc.Get("Remote_Status_ServerFail") + " " + Loc.Get("Remote_Status_SendCode");
            return;
        }

        ShortCode = RendezvousClient.Format(_serverCode);
        Status = Loc.Get("Remote_Status_ShareShort");
        _mailbox = new CancellationTokenSource();
        _ = PollReplyAsync(session, server, _serverCode, _mailbox.Token);
    }

    private async Task PollReplyAsync(RemoteSession session, string server, string code, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(1500, token);
                var answer = await RendezvousClient.RequestAsync(server, $"POLL {code}", token);
                if (!RemoteCode.IsCompact(answer)) continue;

                _ui.TryEnqueue(() =>
                {
                    if (session != _session) return;
                    HostReplyInput = answer;
                    AcceptReply();
                });
                return;
            }
        }
        catch (Exception e) when (e is OperationCanceledException || IsNetworkError(e)) { }
    }

    private sealed class ServerUnreachableException : Exception;

    private async Task<string> AskServerAsync(string line)
    {
        try { return await RendezvousClient.RequestAsync(ServerAddress, line); }
        catch (Exception e) when (IsNetworkError(e)) { throw new ServerUnreachableException(); }
    }

    private static bool IsNetworkError(Exception e) => e is System.Net.Sockets.SocketException or IOException or OperationCanceledException or FormatException;

    [RelayCommand]
    private void AcceptReply()
    {
        if (_session is null || !SessionIsHost) return;
        try
        {
            _session.AcceptReply(HostReplyInput);
            AwaitingReply = false;
            Status = Loc.Get("Remote_Status_WaitingPeer");
        }
        catch (RemoteCodeException)
        {
            Status = Loc.Get("Remote_BadCode");
        }
    }

    [RelayCommand]
    private async Task JoinAsync()
    {
        if (HasSession) return;
        Begin(isHost: false, Loc.Get("Remote_Status_Preparing"));
        try
        {
            var viaServer = RendezvousClient.TryParseCode(OfferInput, out var shortCode);
            var offer = OfferInput;
            if (viaServer)
            {
                offer = await AskServerAsync($"GET {shortCode}");
                if (!RemoteCode.IsCompact(offer)) throw new RemoteCodeException("Unknown code.");
            }

            var session = await RemoteSession.JoinAsync(offer, Options);
            Attach(session);
            ViewerReplyCode = session.Code;
            SecurityCode = session.SecurityCode ?? "";
            NetworkWarning = session.NetworkMayBlockDirect;
            Status = Loc.Get("Remote_Status_SendReply");

            if (viaServer)
            {
                await AskServerAsync($"REPLY {shortCode} {RemoteCode.Decode(session.Code).Compact()}");
                Status = Loc.Get("Remote_Status_WaitingPeer");
            }
        }
        catch (ServerUnreachableException)
        {
            Reset(Loc.Get("Remote_Status_ServerFail"));
        }
        catch (RemoteCodeException)
        {
            Reset(Loc.Get("Remote_BadCode"));
        }
        catch (System.Net.Sockets.SocketException)
        {
            Reset(Loc.Get("Remote_Status_Failed"));
        }
    }

    [RelayCommand]
    private void Stop() => Reset(Loc.Get("Remote_Status_Stopped"));

    /// <summary>Viewer: forward one mouse or keyboard action to the shared PC.</summary>
    public void SendInput(InputEvent input) => _session?.SendInput(input);

    // ---- session plumbing ----

    private void Begin(bool isHost, string status)
    {
        SessionIsHost = isHost;
        HasSession = true;
        Status = status;
        HostCode = HostReplyInput = ViewerReplyCode = SecurityCode = ShortCode = "";
        NetworkWarning = false;
    }

    private void Attach(RemoteSession session)
    {
        _session = session;
        session.PhaseChanged += phase => _ui.TryEnqueue(() => OnPhase(session, phase));
        session.PeerRequested += security => _ui.TryEnqueue(() => _ = AskHostAsync(session, security));
        session.FrameReceived += frame => _ui.TryEnqueue(() => FrameReady?.Invoke(frame));
        session.AccessChanged += access => _ui.TryEnqueue(() => OnAccess(session, access));
        session.Closed += reason => _ui.TryEnqueue(() => OnClosed(session, reason));
    }

    private void OnPhase(RemoteSession session, RemotePhase phase)
    {
        if (session != _session || phase != RemotePhase.Connected) return;
        AwaitingReply = false;
        SecurityCode = session.SecurityCode ?? "";
        Status = SessionIsHost ? Loc.Get("Remote_Status_AskHost") : Loc.Get("Remote_Status_AwaitAllow");
    }

    private async Task AskHostAsync(RemoteSession session, string security)
    {
        SecurityCode = security;
        var answer = await _dialogs.ShowRemoteRequestAsync(security);
        if (session != _session) return; // it ended while the question was open

        if (answer is RemoteAccess.View or RemoteAccess.Control)
        {
            session.Grant(answer);
            Access = answer;
            IsSharing = true;
            Status = Loc.Get("Remote_Status_Sharing");
        }
        else
        {
            session.Deny();
        }
    }

    private void OnAccess(RemoteSession session, RemoteAccess access)
    {
        if (session != _session || SessionIsHost) return;
        Access = access;
        IsViewing = access is RemoteAccess.View or RemoteAccess.Control;
        Status = IsViewing ? Loc.Get("Remote_Status_Viewing") : Loc.Get("Remote_Status_AwaitAllow");
    }

    private void OnClosed(RemoteSession session, RemoteCloseReason reason)
    {
        if (session != _session) return;
        Reset(reason switch
        {
            RemoteCloseReason.Denied => Loc.Get("Remote_Status_Denied"),
            RemoteCloseReason.RemoteStop => Loc.Get("Remote_Status_RemoteStop"),
            RemoteCloseReason.Timeout => Loc.Get("Remote_Status_Timeout"),
            _ => Loc.Get("Remote_Status_Stopped"),
        });
    }

    private void Reset(string status)
    {
        var session = _session;
        _session = null;
        session?.Dispose();

        _mailbox?.Cancel();
        _mailbox = null;
        if (_serverCode.Length > 0) _ = ForgetShortCodeAsync(ServerAddress, _serverCode);
        _serverCode = "";

        HasSession = false;
        AwaitingReply = false;
        IsSharing = false;
        IsViewing = false;
        Access = RemoteAccess.None;
        HostCode = HostReplyInput = OfferInput = ViewerReplyCode = SecurityCode = ShortCode = "";
        NetworkWarning = false;
        Status = status;
    }

    private static async Task ForgetShortCodeAsync(string server, string code)
    {
        try { await RendezvousClient.RequestAsync(server, $"DEL {code}"); }
        catch (Exception e) when (IsNetworkError(e)) { }
    }
}
