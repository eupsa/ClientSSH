using System.Security.Cryptography;
using ClientSSH.Services.Localization;
using ClientSSH.Services.Remote;
using ClientSSH.Services.Shell;
using ClientSSH.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace ClientSSH.ViewModels;

/// <summary>
/// Remote access like AnyDesk, with no server: this PC has a fixed number (<see cref="RemoteIdentity"/>), others call it by that
/// number through the public DHT (<see cref="RemoteNode"/>), and the person here allows or denies each call, even when ClientSSH
/// is minimized or in the tray. One session at a time. Create it on the UI thread: node and session events come from other threads.
/// </summary>
public sealed partial class RemoteViewModel : ObservableObject
{
    private const string IdentityFile = "remote-identity.bin";

    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly TrayService _tray;
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private RemoteNode? _node;
    private RemoteSession? _session;
    private RemoteViewer? _viewer;

    [ObservableProperty] public partial string MyNumber { get; set; } = "";
    [ObservableProperty] public partial string NumberInput { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial bool NetworkWarning { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool HasSession { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSecurityCode), nameof(SecurityText))]
    public partial string SecurityCode { get; set; } = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(AccessText))]
    public partial RemoteAccess Access { get; set; }

    /// <summary>True while this PC's screen is being shown to the other one.</summary>
    [ObservableProperty] public partial bool IsSharing { get; set; }

    /// <summary>True while this PC shows the other one's screen.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsNotViewing))]
    public partial bool IsViewing { get; set; }

    public bool IsNotViewing => !IsViewing;
    public bool IsIdle => !HasSession;
    public bool HasSecurityCode => SecurityCode.Length > 0;
    public string SecurityText => HasSecurityCode ? string.Format(Loc.Get("Remote_Security"), SecurityCode) : "";

    public string AccessText => Access switch
    {
        RemoteAccess.Control => Loc.Get("Remote_Access_Control"),
        RemoteAccess.View => Loc.Get("Remote_Access_View"),
        _ => "",
    };

    /// <summary>Whether others can call this PC. Saved, and applied at once.</summary>
    public bool Listening
    {
        get => _settings.Current.RemoteListen;
        set
        {
            if (value == _settings.Current.RemoteListen) return;
            _settings.Save(_settings.Current with { RemoteListen = value });
            _node?.SetListening(value);
            OnPropertyChanged();
        }
    }

    /// <summary>The other PC's newest picture (BGRA, width, height), on the UI thread.</summary>
    public event Action<byte[], int, int>? FrameReady;

    public RemoteViewModel(ISettingsStore settings, IDialogService dialogs, TrayService tray)
    {
        _settings = settings;
        _dialogs = dialogs;
        _tray = tray;
    }

    /// <summary>Opens the socket and, if allowed, starts listening for calls. Called once at app start.</summary>
    public void Start()
    {
        if (_node is not null) return;
        try
        {
            _node = new RemoteNode(LoadIdentity(), () => new ScreenSource(), () => new SendInputSink(), new RemoteOptions());
            _node.IncomingCall += session => _ui.TryEnqueue(() => OnIncomingCall(session));
            _node.SetListening(Listening);
            MyNumber = RemoteIdentity.Format(_node.Number);
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or CryptographicException or IOException)
        {
            Status = Loc.Get("Remote_Status_Failed");
        }
    }

    // The key behind the number stays on this PC, encrypted for this Windows user (DPAPI). Same number across updates.
    private static RemoteIdentity LoadIdentity()
    {
        var path = Path.Combine(LocalData.Dir, IdentityFile);
        if (File.Exists(path))
        {
            try
            {
                return RemoteIdentity.FromPrivateKey(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException)
            {
                File.Move(path, path + ".bad", overwrite: true); // another user's or a damaged key: start over with a new number
            }
        }

        var identity = RemoteIdentity.Create();
        Directory.CreateDirectory(LocalData.Dir);
        File.WriteAllBytes(path, ProtectedData.Protect(identity.ExportPrivateKey(), null, DataProtectionScope.CurrentUser));
        return identity;
    }

    [RelayCommand]
    private void Connect()
    {
        if (_node is null || HasSession) return;
        if (!RemoteIdentity.TryParseNumber(NumberInput, out var number))
        {
            Status = Loc.Get("Remote_BadNumber");
            return;
        }

        RemoteSession session;
        try
        {
            session = _node.Call(number);
        }
        catch (RemoteException) when (number == _node.Number)
        {
            Status = Loc.Get("Remote_OwnNumber");
            return;
        }

        Attach(session);
        Status = Loc.Get("Remote_Status_Searching");
        _ = ReportFoundAsync(session);
    }

    private async Task ReportFoundAsync(RemoteSession session)
    {
        while (session == _session && session.Phase == RemotePhase.WaitingForPeer)
        {
            if (session.HasCandidates)
            {
                Status = Loc.Get("Remote_Status_Found");
                return;
            }
            await Task.Delay(500);
        }
    }

    [RelayCommand]
    private void Stop() => Reset(Loc.Get("Remote_Status_Stopped"));

    /// <summary>Viewer: forward one mouse or keyboard action to the shared PC.</summary>
    public void SendInput(InputEvent input) => _session?.SendInput(input);

    // ---- someone calls this PC ----

    private void OnIncomingCall(RemoteSession session)
    {
        if (_session is not null)
        {
            session.Dispose();
            return;
        }
        Attach(session);
        session.PeerRequested += security => _ui.TryEnqueue(() => _ = AskHostAsync(session, security));
        session.Ring();
    }

    private async Task AskHostAsync(RemoteSession session, string security)
    {
        SecurityCode = security;
        Status = Loc.Get("Remote_Status_AskHost");

        // Minimized, in the tray or behind other windows: come to the front until the person answers.
        _tray.DemandAttention(Loc.Get("Remote_Request_Title"), Loc.Get("Remote_Notify_Body"));
        RemoteAccess answer;
        try { answer = await _dialogs.ShowRemoteRequestAsync(security); }
        finally { _tray.EndAttention(); }

        if (session != _session) return; // the caller gave up while the question was open
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

    // ---- session plumbing ----

    private void Attach(RemoteSession session)
    {
        _session = session;
        HasSession = true;
        SecurityCode = "";
        NetworkWarning = false;
        session.PhaseChanged += phase => _ui.TryEnqueue(() => OnPhase(session, phase));
        session.AccessChanged += access => _ui.TryEnqueue(() => OnAccess(session, access));
        session.Closed += reason => _ui.TryEnqueue(() => OnClosed(session, reason));

        if (!session.IsHost)
        {
            RemoteViewer? viewer = null;
            viewer = _viewer = new RemoteViewer(() => _ui.TryEnqueue(() =>
            {
                if (viewer?.TakeLatest() is { } picture) FrameReady?.Invoke(picture.Bgra, picture.Width, picture.Height);
            }));
            session.FrameReceived += viewer.Push;
        }
    }

    private void OnPhase(RemoteSession session, RemotePhase phase)
    {
        if (session != _session || phase != RemotePhase.Connected || session.IsHost) return;
        SecurityCode = session.SecurityCode ?? "";
        Status = Loc.Get("Remote_Status_AwaitAllow");
    }

    private void OnAccess(RemoteSession session, RemoteAccess access)
    {
        if (session != _session || session.IsHost) return;
        Access = access;
        IsViewing = access is RemoteAccess.View or RemoteAccess.Control;
        Status = IsViewing ? Loc.Get("Remote_Status_Viewing") : Loc.Get("Remote_Status_AwaitAllow");
    }

    private void OnClosed(RemoteSession session, RemoteCloseReason reason)
    {
        if (session != _session) return;
        var neverConnected = !session.IsHost && SecurityCode.Length == 0;
        NetworkWarning = neverConnected && _node?.Dht.PortsDiffer == true;
        Reset(reason switch
        {
            RemoteCloseReason.Denied => Loc.Get("Remote_Status_Denied"),
            RemoteCloseReason.RemoteStop => Loc.Get("Remote_Status_RemoteStop"),
            RemoteCloseReason.Timeout when neverConnected => Loc.Get(session.HasCandidates ? "Remote_Status_NoPath" : "Remote_Status_NotFound"),
            RemoteCloseReason.Timeout => Loc.Get("Remote_Status_Timeout"),
            _ => Loc.Get("Remote_Status_Stopped"),
        });
    }

    private void Reset(string status)
    {
        var session = _session;
        _session = null;
        session?.Dispose();
        _viewer?.Dispose();
        _viewer = null;

        HasSession = false;
        IsSharing = false;
        IsViewing = false;
        Access = RemoteAccess.None;
        SecurityCode = "";
        Status = status;
    }
}
