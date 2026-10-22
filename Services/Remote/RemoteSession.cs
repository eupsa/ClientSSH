using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ClientSSH.Services.Remote;

public sealed class RemoteOptions
{
    public int FramesPerSecond { get; init; } = 60;

    /// <summary>How long the caller keeps trying to reach the other PC.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(45);
}

public enum RemotePhase { WaitingForPeer, Connected, Closed }

public enum RemoteCloseReason { LocalStop, RemoteStop, Timeout, Denied }

/// <summary>
/// One direct connection between two PCs. The viewer calls a PC by its number; the host (the PC being called) proves it owns
/// that number by signing the handshake with its <see cref="RemoteIdentity"/>. The one-time keys are agreed in the handshake
/// (ECDH P-256 + HKDF) and every later packet is AES-GCM (see <see cref="RemoteChannel"/>). The host shares its screen as H.264
/// and applies input; the viewer shows frames and sends input. Nothing is shown or applied until the host person allows it.
/// Packets go through the <c>send</c> callback and come in through <see cref="OnDatagram"/>: the socket belongs to <see cref="RemoteNode"/>.
/// </summary>
public sealed class RemoteSession : IDisposable
{
    public const int ViewerHandshakeLength = 2 + 64, HostHandshakeLength = 2 + 64 + 64 + 64;
    private const int ChunkSize = 1150, FrameHeader = 13, MaxFrameBytes = 4_000_000, MaxBufferedInputs = 256;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(12), AnswerTimeout = TimeSpan.FromSeconds(30);

    private readonly bool _isHost;
    private readonly Action<IPEndPoint, byte[]> _send;
    private readonly ECDiffieHellman _key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _ownEphemeral;
    private readonly RemoteIdentity? _identity;
    private readonly string? _targetNumber;
    private readonly IScreenSource? _screen;
    private readonly IInputSink? _inputSink;
    private readonly RemoteOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly DateTime _started = DateTime.UtcNow;
    private readonly HashSet<IPEndPoint> _candidates = [];

    private byte[]? _hostReply;
    private RemoteChannel? _channel;
    private IPEndPoint? _peer;
    private DateTime _lastReceived = DateTime.UtcNow;
    private volatile RemoteAccess _access;
    private volatile bool _confirmed;
    private int _streaming, _closed, _forceKey = 1;

    // viewer: frame assembly, recovery after loss, input
    private uint _assemblingFrame, _deliveredFrame;
    private byte[]?[] _chunks = [];
    private byte _assemblingFlags;
    private int _assemblingWidth, _assemblingHeight;
    private int _chunksLeft;
    private bool _needKey = true;
    private DateTime _lastKeyRequest;
    private uint _inputSeq;
    private readonly Dictionary<uint, (byte[] Payload, DateTime FirstSent, DateTime LastSent)> _pending = [];

    // host: input arrives in order even if a packet had to be sent again
    private uint _nextInput = 1;
    private readonly SortedDictionary<uint, byte[]> _buffered = [];
    private DateTime _gapSince, _lastKeyForced;
    private uint _frameId;

    /// <summary>Six digits both people compare out loud. Known once the handshake is done.</summary>
    public string? SecurityCode { get; private set; }

    /// <summary>Host: the one-time key of the PC that called. The node uses it to spot repeats of the same call.</summary>
    public byte[]? ViewerEphemeral { get; private set; }

    /// <summary>Viewer: true once at least one address was found for the number.</summary>
    public bool HasCandidates { get { lock (_gate) return _candidates.Count > 0; } }

    public bool IsHost => _isHost;
    public RemotePhase Phase { get; private set; } = RemotePhase.WaitingForPeer;
    public RemoteAccess Access => _access;

    public event Action<RemotePhase>? PhaseChanged;
    /// <summary>Host: someone called, ask the person. Argument: the security code.</summary>
    public event Action<string>? PeerRequested;
    /// <summary>Viewer: one H.264 access unit (Annex B) with the picture size, in order, starting at a key frame.</summary>
    public event Action<VideoFrame>? FrameReceived;
    /// <summary>Viewer: what the host allowed.</summary>
    public event Action<RemoteAccess>? AccessChanged;
    public event Action<RemoteCloseReason>? Closed;

    private RemoteSession(bool isHost, Action<IPEndPoint, byte[]> send, RemoteOptions options,
        RemoteIdentity? identity = null, string? targetNumber = null, IScreenSource? screen = null, IInputSink? sink = null)
    {
        _isHost = isHost;
        _send = send;
        _options = options;
        _identity = identity;
        _targetNumber = targetNumber;
        _screen = screen;
        _inputSink = sink;
        _ownEphemeral = RemoteKeys.ExportPublicKey(_key);
    }

    /// <summary>Viewer: calls the PC with this number. Add its addresses with <see cref="AddCandidates"/> as the DHT finds them.</summary>
    public static RemoteSession Call(string number, Action<IPEndPoint, byte[]> send, RemoteOptions options)
    {
        var session = new RemoteSession(isHost: false, send, options, targetNumber: number);
        session.StartLoop();
        return session;
    }

    /// <summary>Host: answers a viewer handshake that arrived from <paramref name="from"/>.</summary>
    public static RemoteSession Answer(RemoteIdentity identity, ReadOnlySpan<byte> viewerHandshake, IPEndPoint from,
        Action<IPEndPoint, byte[]> send, RemoteOptions options, IScreenSource screen, IInputSink sink)
    {
        if (!IsViewerHandshake(viewerHandshake)) throw new RemoteException("Not a call.");

        var session = new RemoteSession(isHost: true, send, options, identity, screen: screen, sink: sink);
        var viewerEphemeral = viewerHandshake[2..].ToArray();
        var (channel, security) = RemoteKeys.Derive(session._key, session._ownEphemeral, viewerEphemeral, isHost: true);
        session.ViewerEphemeral = viewerEphemeral;
        session._channel = channel;
        session.SecurityCode = security;
        session._peer = from;
        session._hostReply = [(byte)PacketType.Handshake, 0, .. session._ownEphemeral, .. identity.PublicKey,
            .. identity.SignHandshake(session._ownEphemeral, viewerEphemeral)];
        session.Phase = RemotePhase.Connected;
        session.SendTo(from, session._hostReply);
        session.StartLoop();
        return session;
    }

    public static bool IsViewerHandshake(ReadOnlySpan<byte> datagram) =>
        datagram.Length == ViewerHandshakeLength && datagram[0] == (byte)PacketType.Handshake && datagram[1] == 1;

    /// <summary>Host: asks the person now (call after subscribing to <see cref="PeerRequested"/>).</summary>
    public void Ring() => PeerRequested?.Invoke(SecurityCode ?? "");

    /// <summary>Viewer: addresses where the called PC may be. The handshake is sent to all of them until one answers.</summary>
    public void AddCandidates(IEnumerable<IPEndPoint> endpoints)
    {
        lock (_gate) foreach (var endpoint in endpoints) if (_candidates.Count < 64) _candidates.Add(endpoint);
        Knock();
    }

    // ---- loop ----

    private void StartLoop() => _ = Task.Run(ControlLoopAsync);

    private async Task ControlLoopAsync()
    {
        var tick = 0;
        while (!_cts.IsCancellationRequested)
        {
            try { await Task.Delay(100, _cts.Token); }
            catch (OperationCanceledException) { return; }

            tick++;
            var now = DateTime.UtcNow;
            if (Phase == RemotePhase.WaitingForPeer)
            {
                if (now - _started > _options.ConnectTimeout) { Close(RemoteCloseReason.Timeout); return; }
                if (tick % 3 == 0) Knock();
            }
            else if (Phase == RemotePhase.Connected)
            {
                if (!_confirmed)
                {
                    if (_isHost && now - _started > AnswerTimeout) { Close(RemoteCloseReason.Timeout); return; }
                    if (tick % 3 == 0) Knock();
                }
                if (tick % 20 == 0) SendKeepalive();
                if (!_isHost) { ResendInputs(now); AskForKeyFrameIfNeeded(now); }
                else SkipInputGap(now);
                if (_confirmed && now - _lastReceived > IdleTimeout) { Close(RemoteCloseReason.Timeout); return; }
            }
        }
    }

    // Viewer: the handshake to every candidate (the first packet out also opens the way in). Host: its signed reply, again.
    private void Knock()
    {
        if (_isHost)
        {
            if (_hostReply is not null && _peer is not null) SendTo(_peer, _hostReply);
            if (_channel is not null) Send(PacketType.Hello, [0]);
            return;
        }

        if (_channel is not null)
        {
            Send(PacketType.Hello, [1]);
            return;
        }
        IPEndPoint[] targets;
        lock (_gate) targets = [.. _candidates];
        byte[] handshake = [(byte)PacketType.Handshake, 1, .. _ownEphemeral];
        foreach (var endpoint in targets) SendTo(endpoint, handshake);
    }

    // ---- receiving ----

    /// <summary>Every datagram for this session. Handshakes are raw; everything else must decrypt.</summary>
    public void OnDatagram(ReadOnlySpan<byte> datagram, IPEndPoint source)
    {
        if (datagram.Length > 0 && datagram[0] == (byte)PacketType.Handshake)
        {
            OnHandshake(datagram, source);
            return;
        }

        var channel = _channel;
        if (channel is null || !channel.TryOpen(datagram, out var type, out var payload)) return;

        lock (_gate)
        {
            _lastReceived = DateTime.UtcNow;
            _confirmed = true;
            _peer = source; // follows the other PC if its NAT picks a new port
        }

        switch (type)
        {
            case PacketType.Frame when !_isHost: OnFrameChunk(payload); break;
            case PacketType.KeyRequest when _isHost: OnKeyRequest(); break;
            case PacketType.Input when _isHost: OnInput(payload); break;
            case PacketType.Ack when !_isHost: OnAck(payload); break;
            case PacketType.Keepalive when !_isHost: OnKeepalive(payload); break;
            case PacketType.Bye: Close(RemoteCloseReason.RemoteStop); break;
        }
    }

    private void OnHandshake(ReadOnlySpan<byte> datagram, IPEndPoint source)
    {
        if (_isHost)
        {
            // The viewer did not get our answer yet: send it again.
            if (IsViewerHandshake(datagram) && ViewerEphemeral is not null && datagram[2..].SequenceEqual(ViewerEphemeral) && _hostReply is not null)
                SendTo(source, _hostReply);
            return;
        }

        if (datagram.Length != HostHandshakeLength || datagram[1] != 0) return;
        var hostEphemeral = datagram.Slice(2, 64).ToArray();
        var identity = datagram.Slice(66, 64).ToArray();
        var signature = datagram.Slice(130, 64).ToArray();

        // Only the PC that owns the number can sign: anything else announced under it is ignored.
        if (RemoteIdentity.NumberOf(identity) != _targetNumber) return;
        if (!RemoteIdentity.VerifyHandshake(identity, signature, hostEphemeral, _ownEphemeral)) return;

        lock (_gate)
        {
            if (_channel is not null) return;
            var (channel, security) = RemoteKeys.Derive(_key, _ownEphemeral, hostEphemeral, isHost: false);
            _channel = channel;
            SecurityCode = security;
            _peer = source;
            _lastReceived = DateTime.UtcNow;
            Phase = RemotePhase.Connected;
        }

        Send(PacketType.Hello, [1]);
        PhaseChanged?.Invoke(RemotePhase.Connected);
    }

    // ---- host: consent, frames, input ----

    /// <summary>Host: the person allowed viewing or control.</summary>
    public void Grant(RemoteAccess access)
    {
        if (!_isHost || access is not (RemoteAccess.View or RemoteAccess.Control)) return;

        _access = access;
        SendKeepalive();
        if (Interlocked.Exchange(ref _streaming, 1) == 0)
            new Thread(StreamLoop) { IsBackground = true, Name = "ClientSSH screen", Priority = ThreadPriority.AboveNormal }.Start();
    }

    /// <summary>Host: the person said no. The viewer is told, then the session ends.</summary>
    public void Deny()
    {
        if (!_isHost) return;

        _access = RemoteAccess.Denied;
        SendKeepalive();
        SendKeepalive(); // twice: UDP may lose one
        Close(RemoteCloseReason.Denied);
    }

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);

    // A thread of its own: Task.Delay is too coarse on Windows (15 ms) for 60 frames per second.
    private void StreamLoop()
    {
        if (OperatingSystem.IsWindows()) timeBeginPeriod(1);
        try
        {
            var interval = 1000.0 / Math.Max(1, _options.FramesPerSecond);
            var clock = Stopwatch.StartNew();
            var next = 0.0;
            while (!_cts.IsCancellationRequested && Phase == RemotePhase.Connected && _access is RemoteAccess.View or RemoteAccess.Control)
            {
                try
                {
                    var frame = _screen?.Next(Interlocked.Exchange(ref _forceKey, 0) == 1);
                    if (frame is { Data.Length: > 0 and <= MaxFrameBytes }) SendFrame(frame);
                }
                catch (Exception e) when (e is ExternalException or InvalidOperationException or IOException)
                {
                    // a frame that cannot be captured (secure desktop, display change) is just skipped
                }

                next += interval;
                var wait = next - clock.Elapsed.TotalMilliseconds;
                if (wait > 0) Thread.Sleep((int)wait);
                else if (wait < -interval * 3) next = clock.Elapsed.TotalMilliseconds; // fell behind: do not try to catch up
            }
        }
        finally
        {
            if (OperatingSystem.IsWindows()) timeEndPeriod(1);
            Interlocked.Exchange(ref _streaming, 0);
            if (_closed == 1) (_screen as IDisposable)?.Dispose(); // the capture buffers and the encoder live on this thread
        }
    }

    private void SendFrame(VideoFrame frame)
    {
        var id = ++_frameId;
        var data = frame.Data;
        var count = (data.Length + ChunkSize - 1) / ChunkSize;
        for (var i = 0; i < count; i++)
        {
            var size = Math.Min(ChunkSize, data.Length - i * ChunkSize);
            var payload = new byte[FrameHeader + size];
            BinaryPrimitives.WriteUInt32BigEndian(payload, id);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), (ushort)i);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6), (ushort)count);
            payload[8] = (byte)(frame.IsKey ? 1 : 0);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(9), (ushort)frame.Width);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(11), (ushort)frame.Height);
            data.AsSpan(i * ChunkSize, size).CopyTo(payload.AsSpan(FrameHeader));
            Send(PacketType.Frame, payload);
            // shortcut: no congestion control, only a breath every 32 packets so a key frame is not one burst; add pacing by measured loss later.
            if (i % 32 == 31) Thread.Sleep(0);
        }
    }

    private void OnKeyRequest()
    {
        var now = DateTime.UtcNow;
        if (now - _lastKeyForced < TimeSpan.FromMilliseconds(250)) return;
        _lastKeyForced = now;
        Interlocked.Exchange(ref _forceKey, 1);
    }

    private void OnInput(byte[] payload)
    {
        if (payload.Length != 4 + InputEvent.Length) return;
        var seq = BinaryPrimitives.ReadUInt32BigEndian(payload);
        Send(PacketType.Ack, payload[..4]);

        lock (_gate)
        {
            if (seq < _nextInput || _buffered.ContainsKey(seq) || _buffered.Count >= MaxBufferedInputs) return;
            if (_buffered.Count == 0) _gapSince = DateTime.UtcNow;
            _buffered[seq] = payload[4..];
            DrainInputs();
        }
    }

    private void DrainInputs()
    {
        while (_buffered.TryGetValue(_nextInput, out var bytes))
        {
            _buffered.Remove(_nextInput);
            _nextInput++;
            // Only what the host person allowed, and only well-formed events.
            if (_access == RemoteAccess.Control && InputEvent.TryParse(bytes, out var input)) _inputSink?.Apply(input);
        }
        if (_buffered.Count > 0) _gapSince = DateTime.UtcNow;
    }

    // A lost packet that the sender gave up on would block everything behind it.
    private void SkipInputGap(DateTime now)
    {
        lock (_gate)
        {
            if (_buffered.Count == 0 || now - _gapSince < TimeSpan.FromMilliseconds(500)) return;
            _nextInput = _buffered.Keys.First();
            DrainInputs();
        }
    }

    // ---- viewer: frames, access, input ----

    private void OnFrameChunk(byte[] payload)
    {
        if (payload.Length <= FrameHeader) return;
        var id = BinaryPrimitives.ReadUInt32BigEndian(payload);
        var index = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(4));
        var count = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(6));
        if (count == 0 || count > 4000 || index >= count || id <= _deliveredFrame || id < _assemblingFrame) return;

        VideoFrame? complete = null;
        lock (_gate)
        {
            if (id > _assemblingFrame || _chunks.Length != count)
            {
                // A newer frame while one was unfinished: that one is lost, and the frames after it need a key frame.
                if (_chunksLeft > 0 || id > _deliveredFrame + 1) _needKey = true;
                _assemblingFrame = id;
                _chunks = new byte[count][];
                _chunksLeft = count;
                _assemblingFlags = payload[8];
                _assemblingWidth = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(9));
                _assemblingHeight = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(11));
            }
            if (_chunks[index] is not null) return;

            _chunks[index] = payload[FrameHeader..];
            if (--_chunksLeft == 0)
            {
                _deliveredFrame = id;
                var isKey = (_assemblingFlags & 1) != 0;
                if (isKey) _needKey = false;
                if (!_needKey) complete = new VideoFrame([.. _chunks.SelectMany(c => c!)], isKey, _assemblingWidth, _assemblingHeight);
            }
        }
        if (complete is not null) FrameReceived?.Invoke(complete);
    }

    private void AskForKeyFrameIfNeeded(DateTime now)
    {
        if (!_needKey || _access is not (RemoteAccess.View or RemoteAccess.Control) || now - _lastKeyRequest < TimeSpan.FromMilliseconds(300)) return;
        _lastKeyRequest = now;
        Send(PacketType.KeyRequest, []);
    }

    private void OnKeepalive(byte[] payload)
    {
        if (payload.Length < 1 || !Enum.IsDefined((RemoteAccess)payload[0])) return;
        var access = (RemoteAccess)payload[0];
        if (access == _access) return;

        _access = access;
        AccessChanged?.Invoke(access);
        if (access == RemoteAccess.Denied) Close(RemoteCloseReason.Denied);
    }

    /// <summary>Viewer: forward one mouse or keyboard action. Ignored unless the host allowed control.</summary>
    public void SendInput(InputEvent input)
    {
        if (_isHost || Phase != RemotePhase.Connected || _access != RemoteAccess.Control) return;

        var payload = new byte[4 + InputEvent.Length];
        input.ToBytes().CopyTo(payload, 4);
        lock (_gate)
        {
            var seq = ++_inputSeq;
            BinaryPrimitives.WriteUInt32BigEndian(payload, seq);
            _pending[seq] = (payload, DateTime.UtcNow, DateTime.UtcNow);
        }
        Send(PacketType.Input, payload);
    }

    private void OnAck(byte[] payload)
    {
        if (payload.Length != 4) return;
        lock (_gate) _pending.Remove(BinaryPrimitives.ReadUInt32BigEndian(payload));
    }

    private void ResendInputs(DateTime now)
    {
        List<byte[]> again = [];
        lock (_gate)
        {
            foreach (var seq in _pending.Keys.ToList())
            {
                var (payload, first, last) = _pending[seq];
                if (now - first > TimeSpan.FromSeconds(3)) _pending.Remove(seq);
                else if (now - last > TimeSpan.FromMilliseconds(120))
                {
                    _pending[seq] = (payload, first, now);
                    again.Add(payload);
                }
            }
        }
        foreach (var payload in again) Send(PacketType.Input, payload);
    }

    // ---- sending and closing ----

    private void SendKeepalive() => Send(PacketType.Keepalive, [(byte)_access]);

    private void Send(PacketType type, ReadOnlySpan<byte> payload)
    {
        var channel = _channel;
        var peer = _peer;
        if (channel is null || peer is null) return;
        SendTo(peer, channel.Seal(type, payload));
    }

    private void SendTo(IPEndPoint endpoint, byte[] packet)
    {
        if (_closed == 0) _send(endpoint, packet);
    }

    private void Close(RemoteCloseReason reason)
    {
        if (reason == RemoteCloseReason.LocalStop && _closed == 0) { Send(PacketType.Bye, []); Send(PacketType.Bye, []); }
        if (Interlocked.Exchange(ref _closed, 1) == 1) return;

        Phase = RemotePhase.Closed;
        PhaseChanged?.Invoke(RemotePhase.Closed);
        Closed?.Invoke(reason);
        _cts.Cancel();
    }

    public void Dispose()
    {
        Close(RemoteCloseReason.LocalStop);
        _cts.Dispose();
        _key.Dispose();
    }
}
