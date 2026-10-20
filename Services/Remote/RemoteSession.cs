using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ClientSSH.Services.Remote;

public sealed class RemoteOptions
{
    /// <summary>Public STUN servers that only tell this PC its public address. Empty = local addresses only.</summary>
    public IReadOnlyList<string> StunServers { get; init; } = [];

    public TimeSpan StunTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>For tests on one machine.</summary>
    public bool IncludeLoopback { get; init; }

    public int FramesPerSecond { get; init; } = 8;
}

public enum RemotePhase { WaitingForPeer, Connected, Closed }

public enum RemoteCloseReason { LocalStop, RemoteStop, Timeout, Denied }

/// <summary>
/// One direct connection between two PCs, with no server in between: UDP hole punching to the addresses in the two
/// pasted codes, AES-GCM on every packet (see <see cref="RemoteChannel"/>). The host shares its screen and applies input;
/// the viewer shows frames and sends input. Nothing is shown or applied until the host person allows it.
/// </summary>
public sealed class RemoteSession : IDisposable
{
    private const int ChunkSize = 1100, MaxFrameBytes = 2_000_000, MaxBufferedInputs = 256;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMinutes(10), IdleTimeout = TimeSpan.FromSeconds(12);

    private readonly bool _isHost;
    private readonly Socket _socket;
    private readonly ECDiffieHellman _key;
    private readonly IScreenSource? _screen;
    private readonly IInputSink? _inputSink;
    private readonly RemoteOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly DateTime _started = DateTime.UtcNow;

    private RemoteCode? _offer, _reply;
    private RemoteChannel? _channel;
    private IPEndPoint? _peer;
    private DateTime _lastReceived = DateTime.UtcNow;
    private volatile RemoteAccess _access;
    private int _streaming, _closed;

    // viewer: frame assembly and input
    private uint _assemblingFrame, _deliveredFrame;
    private byte[]?[] _chunks = [];
    private int _chunksLeft;
    private uint _inputSeq;
    private readonly Dictionary<uint, (byte[] Payload, DateTime FirstSent, DateTime LastSent)> _pending = [];

    // host: input arrives in order even if a packet had to be sent again
    private uint _nextInput = 1;
    private readonly SortedDictionary<uint, byte[]> _buffered = [];
    private DateTime _gapSince;
    private uint _frameId;

    /// <summary>The text to send to the other person: the offer (host) or the reply (viewer).</summary>
    public string Code { get; private set; } = "";

    /// <summary>Six digits both people compare out loud. Known once both codes are in.</summary>
    public string? SecurityCode { get; private set; }

    /// <summary>True when two STUN servers saw different ports for this PC: a symmetric NAT, which usually blocks direct connections.</summary>
    public bool NetworkMayBlockDirect { get; private set; }

    public RemotePhase Phase { get; private set; } = RemotePhase.WaitingForPeer;
    public RemoteAccess Access => _access;

    public event Action<RemotePhase>? PhaseChanged;
    /// <summary>Host: the other PC is here, ask the person. Argument: the security code.</summary>
    public event Action<string>? PeerRequested;
    /// <summary>Viewer: a new screen image (JPEG).</summary>
    public event Action<byte[]>? FrameReceived;
    /// <summary>Viewer: what the host allowed.</summary>
    public event Action<RemoteAccess>? AccessChanged;
    public event Action<RemoteCloseReason>? Closed;

    private RemoteSession(bool isHost, RemoteOptions options, IScreenSource? screen, IInputSink? sink)
    {
        _isHost = isHost;
        _options = options;
        _screen = screen;
        _inputSink = sink;
        _key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        // Windows reports an ICMP "port unreachable" as a receive error; the other side simply is not there yet.
        if (OperatingSystem.IsWindows()) _socket.IOControl(-1744830452, [0, 0, 0, 0], null);
    }

    /// <summary>The PC that shares its screen. Gathers its public address, then makes the offer code.</summary>
    public static async Task<RemoteSession> CreateHostAsync(RemoteOptions options, IScreenSource screen, IInputSink sink)
    {
        var session = new RemoteSession(isHost: true, options, screen, sink);
        await session.PrepareAsync(RandomNumberGenerator.GetBytes(8), offer: null);
        session.StartLoops();
        return session;
    }

    /// <summary>The PC that watches. Takes the offer, answers with its own code and starts reaching for the host.</summary>
    public static async Task<RemoteSession> JoinAsync(string offerText, RemoteOptions options)
    {
        var offer = RemoteCode.Decode(offerText);
        if (offer.IsReply) throw new RemoteCodeException("This is a reply code. Paste the code the other PC created first.");

        var session = new RemoteSession(isHost: false, options, null, null);
        await session.PrepareAsync(offer.SessionId, offer);
        session.StartLoops();
        return session;
    }

    // offer == null: this PC makes the offer. Otherwise it answers that offer, and the keys are ready at once.
    private async Task PrepareAsync(byte[] sessionId, RemoteCode? offer)
    {
        var endpoints = await GatherEndpointsAsync();
        var mine = new RemoteCode(isReply: offer is not null, sessionId, RemoteKeys.ExportPublicKey(_key), endpoints);
        Code = mine.Encode();

        if (offer is null)
        {
            _offer = mine;
            return;
        }

        _offer = offer;
        _reply = mine;
        Install(offer, mine);
    }

    /// <summary>Host: the other person's reply code arrived.</summary>
    public void AcceptReply(string replyText)
    {
        if (!_isHost || _offer is null) throw new InvalidOperationException();
        var reply = RemoteCode.Decode(replyText);
        if (!reply.IsReply) throw new RemoteCodeException("This is an offer code. Paste the reply code the other PC created.");
        if (!reply.SessionId.AsSpan().SequenceEqual(_offer.SessionId)) throw new RemoteCodeException("This reply belongs to another session.");

        lock (_gate)
        {
            if (_channel is not null) return;
            _reply = reply;
            Install(_offer, reply);
        }
    }

    private void Install(RemoteCode offer, RemoteCode reply)
    {
        var (channel, security) = RemoteKeys.Derive(_key, offer, reply, _isHost);
        SecurityCode = security;
        _channel = channel;
    }

    // ---- gathering ----

    private async Task<List<IPEndPoint>> GatherEndpointsAsync()
    {
        var port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
        var found = new List<IPEndPoint>();

        // Public address first: it is the one the other network can use.
        var publicPorts = new List<int>();
        foreach (var server in _options.StunServers.Take(2))
        {
            var endpoint = await Stun.QueryAsync(_socket, server, _options.StunTimeout);
            if (endpoint is null) continue;
            publicPorts.Add(endpoint.Port);
            if (!found.Contains(endpoint)) found.Add(endpoint);
        }
        NetworkMayBlockDirect = publicPorts.Distinct().Count() > 1;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            foreach (var address in nic.GetIPProperties().UnicastAddresses.Select(a => a.Address))
            {
                if (address.AddressFamily != AddressFamily.InterNetwork || address.ToString().StartsWith("169.254", StringComparison.Ordinal)) continue;
                var endpoint = new IPEndPoint(address, port);
                if (!found.Contains(endpoint)) found.Add(endpoint);
            }
        }

        if (_options.IncludeLoopback) found.Add(new IPEndPoint(IPAddress.Loopback, port));
        if (found.Count == 0) throw new RemoteCodeException("No network address found.");
        return found.Take(8).ToList();
    }

    // ---- loops ----

    private void StartLoops()
    {
        _ = Task.Run(ReceiveLoopAsync);
        _ = Task.Run(ControlLoopAsync);
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[2048];
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var result = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), _cts.Token);
                OnDatagram(buffer.AsSpan(0, result.ReceivedBytes), (IPEndPoint)result.RemoteEndPoint);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { /* keep listening */ }
        }
    }

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
                if (now - _started > ConnectTimeout) { Close(RemoteCloseReason.Timeout); return; }
                if (tick % 3 == 0) Punch();
            }
            else if (Phase == RemotePhase.Connected)
            {
                if (tick % 20 == 0) SendKeepalive();
                ResendInputs(now);
                SkipInputGap(now);
                if (now - _lastReceived > IdleTimeout) { Close(RemoteCloseReason.Timeout); return; }
            }
        }
    }

    // Both sides send to each other's addresses at the same time: the first packet out opens the way in.
    private void Punch()
    {
        var channel = _channel;
        var other = _isHost ? _reply : _offer;
        if (channel is null || other is null) return;

        foreach (var endpoint in other.Endpoints) SendTo(endpoint, channel.Seal(PacketType.Hello, [_isHost ? (byte)0 : (byte)1]));
    }

    // ---- receiving ----

    private void OnDatagram(ReadOnlySpan<byte> datagram, IPEndPoint source)
    {
        var channel = _channel;
        if (channel is null || !channel.TryOpen(datagram, out var type, out var payload)) return;

        var becameConnected = false;
        lock (_gate)
        {
            _lastReceived = DateTime.UtcNow;
            _peer = source; // follows the other PC if its NAT picks a new port
            if (Phase == RemotePhase.WaitingForPeer)
            {
                Phase = RemotePhase.Connected;
                becameConnected = true;
            }
        }

        if (becameConnected)
        {
            Send(PacketType.Hello, [_isHost ? (byte)0 : (byte)1]);
            PhaseChanged?.Invoke(RemotePhase.Connected);
            if (_isHost) PeerRequested?.Invoke(SecurityCode ?? "");
        }

        switch (type)
        {
            case PacketType.Frame when !_isHost: OnFrameChunk(payload); break;
            case PacketType.Input when _isHost: OnInput(payload); break;
            case PacketType.Ack when !_isHost: OnAck(payload); break;
            case PacketType.Keepalive when !_isHost: OnKeepalive(payload); break;
            case PacketType.Bye: Close(RemoteCloseReason.RemoteStop); break;
        }
    }

    // ---- host: consent, frames, input ----

    /// <summary>Host: the person allowed viewing or control.</summary>
    public void Grant(RemoteAccess access)
    {
        if (!_isHost || access is not (RemoteAccess.View or RemoteAccess.Control)) return;

        _access = access;
        SendKeepalive();
        if (Interlocked.Exchange(ref _streaming, 1) == 0) _ = Task.Run(StreamLoopAsync);
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

    private async Task StreamLoopAsync()
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _options.FramesPerSecond));
        while (!_cts.IsCancellationRequested && Phase == RemotePhase.Connected && _access is RemoteAccess.View or RemoteAccess.Control)
        {
            var started = DateTime.UtcNow;
            try
            {
                var jpeg = _screen?.CaptureJpeg();
                if (jpeg is { Length: > 0 and <= MaxFrameBytes }) await SendFrameAsync(jpeg);
            }
            catch (Exception e) when (e is ExternalException or InvalidOperationException or IOException)
            {
                // a frame that cannot be captured (secure desktop, display change) is just skipped
            }

            var wait = interval - (DateTime.UtcNow - started);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, _cts.Token).ContinueWith(_ => { });
        }
        Interlocked.Exchange(ref _streaming, 0);
    }

    private async Task SendFrameAsync(byte[] jpeg)
    {
        var id = ++_frameId;
        var count = (jpeg.Length + ChunkSize - 1) / ChunkSize;
        for (var i = 0; i < count; i++)
        {
            var size = Math.Min(ChunkSize, jpeg.Length - i * ChunkSize);
            var payload = new byte[8 + size];
            BinaryPrimitives.WriteUInt32BigEndian(payload, id);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), (ushort)i);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6), (ushort)count);
            jpeg.AsSpan(i * ChunkSize, size).CopyTo(payload.AsSpan(8));
            Send(PacketType.Frame, payload);
            if (i % 16 == 15) await Task.Delay(1); // do not hand the router one big burst
        }
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
        if (payload.Length <= 8) return;
        var id = BinaryPrimitives.ReadUInt32BigEndian(payload);
        var index = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(4));
        var count = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(6));
        if (count == 0 || count > 2000 || index >= count || id <= _deliveredFrame || id < _assemblingFrame) return;

        byte[]? complete = null;
        lock (_gate)
        {
            if (id > _assemblingFrame || _chunks.Length != count)
            {
                _assemblingFrame = id; // a newer frame wins: an unfinished older one is dropped
                _chunks = new byte[count][];
                _chunksLeft = count;
            }
            if (_chunks[index] is not null) return;

            _chunks[index] = payload[8..];
            if (--_chunksLeft == 0)
            {
                complete = [.. _chunks.SelectMany(c => c!)];
                _deliveredFrame = id;
            }
        }
        if (complete is not null) FrameReceived?.Invoke(complete);
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
        try { _socket.SendTo(packet, endpoint); }
        catch (SocketException) { /* the network may be down for a moment */ }
        catch (ObjectDisposedException) { }
    }

    private void Close(RemoteCloseReason reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1) return;

        if (reason == RemoteCloseReason.LocalStop) { Send(PacketType.Bye, []); Send(PacketType.Bye, []); }
        Phase = RemotePhase.Closed;
        PhaseChanged?.Invoke(RemotePhase.Closed);
        Closed?.Invoke(reason);
        _cts.Cancel();
    }

    public void Dispose()
    {
        Close(RemoteCloseReason.LocalStop);
        _cts.Dispose();
        _socket.Dispose();
        _key.Dispose();
    }
}
