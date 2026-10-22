using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace ClientSSH.Services.Remote;

/// <summary>
/// This PC on the remote-access network: one UDP socket for the DHT and for sessions, the PC number, and at most one session.
/// While listening, the PC's address is stored in the public DHT under a key derived from its number, and the PC watches a
/// second key where callers leave their address, so it can open its side of the connection toward them ("knock").
/// </summary>
public sealed class RemoteNode : IDisposable
{
    private static readonly TimeSpan AnnounceEvery = TimeSpan.FromMinutes(10), CallLookupEvery = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan WatchEvery = TimeSpan.FromSeconds(4), RingCooldown = TimeSpan.FromSeconds(3);

    private readonly Socket _socket;
    private readonly Func<IScreenSource> _screen;
    private readonly Func<IInputSink> _sink;
    private readonly RemoteOptions _options;
    private readonly bool _useDht;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly HashSet<string> _answeredCalls = [];
    private readonly ConcurrentDictionary<IPEndPoint, DateTime> _knocked = new();
    private CancellationTokenSource? _listening;
    private RemoteSession? _current;
    private DateTime _lastRing;

    public RemoteIdentity Identity { get; }
    public DhtClient Dht { get; }
    public string Number => Identity.Number;
    public int LocalPort { get; }
    public bool IsListening => _listening is not null;

    /// <summary>Someone called this PC. The session is ready; call <see cref="RemoteSession.Ring"/> after subscribing.</summary>
    public event Action<RemoteSession>? IncomingCall;

    public RemoteNode(RemoteIdentity identity, Func<IScreenSource> screen, Func<IInputSink> sink, RemoteOptions options, bool useDht = true)
    {
        _useDht = useDht;
        Identity = identity;
        _screen = screen;
        _sink = sink;
        _options = options;
        Dht = new DhtClient(Send);

        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        _socket.ReceiveBufferSize = _socket.SendBufferSize = 4 * 1024 * 1024; // a 60 fps stream bursts
        // Windows reports an ICMP "port unreachable" as a receive error; the other side simply is not there yet.
        if (OperatingSystem.IsWindows()) _socket.IOControl(-1744830452, [0, 0, 0, 0], null);
        LocalPort = ((IPEndPoint)_socket.LocalEndPoint!).Port;
        _ = Task.Run(ReceiveLoopAsync);
    }

    public RemoteSession? Current { get { lock (_gate) return _current; } }

    // ---- listening (the PC can be called) ----

    public void SetListening(bool on)
    {
        lock (_gate)
        {
            if (on == IsListening) return;
            if (on)
            {
                _listening = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                if (_useDht)
                {
                    _ = Task.Run(() => ListenLoopAsync(_listening.Token));
                    _ = Task.Run(() => LanListenLoopAsync(_listening.Token));
                }
            }
            else
            {
                _listening!.Cancel();
                _listening = null;
            }
        }
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        var hostKey = DhtClient.KeyFor("host", Number);
        var callKey = DhtClient.KeyFor("call", Number);
        DhtLookup? calls = null;
        DateTime nextAnnounce = default, nextCallLookup = default;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                if (now >= nextAnnounce)
                {
                    var lookup = await Dht.LookupAsync(hostKey, token);
                    await Dht.AnnounceAsync(lookup, token);
                    nextAnnounce = now + (lookup.Closest.Count > 0 ? AnnounceEvery : TimeSpan.FromSeconds(30));
                }
                if (now >= nextCallLookup)
                {
                    calls = await Dht.LookupAsync(callKey, token);
                    nextCallLookup = now + (calls.Closest.Count > 0 ? CallLookupEvery : TimeSpan.FromSeconds(30));
                    KnockAll(calls.Peers);
                }
                else if (calls is not null)
                {
                    KnockAll(await Dht.PeersAsync(calls, token));
                }

                await Task.Delay(WatchEvery, token);
            }
            catch (OperationCanceledException) { return; }
            catch (SocketException) { await Task.Delay(WatchEvery, token).ContinueWith(_ => { }); }
        }
    }

    // Same network: the public address may not lead back inside (routers without hairpin NAT), so callers also ask the LAN.
    private static readonly IPEndPoint LanGroup = new(IPAddress.Parse("239.255.77.77"), 47801);
    private static readonly byte[] LanQuestion = "CSSH-WHO1 "u8.ToArray();

    private async Task LanListenLoopAsync(CancellationToken token)
    {
        using var lan = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            lan.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            lan.Bind(new IPEndPoint(IPAddress.Any, LanGroup.Port));
            var joined = false;
            foreach (var address in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                         .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
                         .Where(a => a.AddressFamily == AddressFamily.InterNetwork))
            {
                try { lan.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(LanGroup.Address, address)); joined = true; }
                catch (SocketException) { /* that interface has no multicast */ }
            }
            if (!joined) return;
        }
        catch (SocketException)
        {
            return; // no LAN discovery on this PC; the DHT still works
        }

        var buffer = new byte[64];
        var expected = (byte[])[.. LanQuestion, .. System.Text.Encoding.ASCII.GetBytes(Number)];
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await lan.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), token);
                // Answer from the session socket, so the caller learns the address its handshake must go to.
                if (buffer.AsSpan(0, result.ReceivedBytes).SequenceEqual(expected) && Current is null) Send((IPEndPoint)result.RemoteEndPoint, [(byte)PacketType.Knock]);
            }
            catch (OperationCanceledException) { return; }
            catch (SocketException) { }
        }
    }

    // A caller left its address: a packet toward it opens this PC's router for its handshake.
    private void KnockAll(IEnumerable<IPEndPoint> callers)
    {
        if (Current is not null) return;
        var now = DateTime.UtcNow;
        foreach (var caller in callers.Take(32))
        {
            if (_knocked.TryGetValue(caller, out var last) && now - last < TimeSpan.FromSeconds(20)) continue;
            _knocked[caller] = now;
            Send(caller, [(byte)PacketType.Knock]);
        }
        if (_knocked.Count > 512) _knocked.Clear();
    }

    // ---- calling another PC ----

    /// <summary>Starts calling the PC with this number. The session reports how it goes; it times out on its own.</summary>
    public RemoteSession Call(string number)
    {
        if (number == Number) throw new RemoteException("That is this PC's own number.");

        RemoteSession session;
        lock (_gate)
        {
            if (_current is not null) throw new RemoteException("A session is already open.");
            session = RemoteSession.Call(number, Send, _options);
            Track(session);
        }
        if (_useDht) _ = FindAsync(session, number);
        return session;
    }

    // Looks the number up and leaves this PC's address where the called PC watches, until connected or closed.
    private async Task FindAsync(RemoteSession session, string number)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token); // not disposed: the session events may still fire later
        session.Closed += _ => stop.Cancel();
        session.PhaseChanged += phase => { if (phase == RemotePhase.Connected) stop.Cancel(); };

        var token = stop.Token;
        try
        {
            var hostTask = Task.Run(async () =>
            {
                DhtLookup? found = null;
                while (!token.IsCancellationRequested)
                {
                    if (found is null || found.Closest.Count == 0)
                    {
                        found = await Dht.LookupAsync(DhtClient.KeyFor("host", number), token);
                        session.AddCandidates(found.Peers);
                    }
                    else
                    {
                        session.AddCandidates(await Dht.PeersAsync(found, token));
                    }
                    await Task.Delay(TimeSpan.FromSeconds(3), token);
                }
            }, token);

            var callTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    var lookup = await Dht.LookupAsync(DhtClient.KeyFor("call", number), token);
                    await Dht.AnnounceAsync(lookup, token);
                    await Task.Delay(TimeSpan.FromSeconds(15), token);
                }
            }, token);

            var lanTask = Task.Run(async () =>
            {
                byte[] question = [.. LanQuestion, .. System.Text.Encoding.ASCII.GetBytes(number)];
                while (!token.IsCancellationRequested)
                {
                    Send(LanGroup, question);
                    await Task.Delay(TimeSpan.FromSeconds(2), token);
                }
            }, token);

            await Task.WhenAll(hostTask, callTask, lanTask);
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    // ---- sessions ----

    private void Track(RemoteSession session)
    {
        _current = session;
        session.Closed += _ =>
        {
            lock (_gate) if (_current == session) _current = null;
        };
    }

    private void OnDatagram(ReadOnlySpan<byte> data, IPEndPoint source)
    {
        if (data.IsEmpty || Dht.OnDatagram(data)) return;

        var current = Current;
        if (data[0] == (byte)PacketType.Knock)
        {
            // The called PC opened its side toward us: try it at the address its router really uses.
            if (current is { IsHost: false, Phase: RemotePhase.WaitingForPeer }) current.AddCandidates([source]);
            return;
        }

        if (RemoteSession.IsViewerHandshake(data) && (current is null || current.IsHost))
        {
            var key = Convert.ToHexString(data[2..]);
            if (current is not null && current.ViewerEphemeral is { } known && Convert.ToHexString(known) == key)
            {
                current.OnDatagram(data, source);
                return;
            }
            Answer(data, source, key);
            return;
        }

        current?.OnDatagram(data, source);
    }

    private void Answer(ReadOnlySpan<byte> handshake, IPEndPoint source, string key)
    {
        RemoteSession session;
        lock (_gate)
        {
            // One call at a time, each call answered once, and not more than one question every few seconds.
            if (!IsListening || _current is not null || !_answeredCalls.Add(key) || DateTime.UtcNow - _lastRing < RingCooldown) return;
            if (_answeredCalls.Count > 1000) _answeredCalls.Clear();
            _lastRing = DateTime.UtcNow;
            try
            {
                session = RemoteSession.Answer(Identity, handshake, source, Send, _options, _screen(), _sink());
            }
            catch (RemoteException)
            {
                return;
            }
            Track(session);
        }
        IncomingCall?.Invoke(session);
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

    private void Send(IPEndPoint endpoint, byte[] packet)
    {
        try { _socket.SendTo(packet, endpoint); }
        catch (SocketException) { /* the network may be down for a moment */ }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        Current?.Dispose();
        _cts.Cancel();
        _socket.Dispose();
    }
}
