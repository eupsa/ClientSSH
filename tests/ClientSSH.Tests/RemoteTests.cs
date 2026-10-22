using System.Net;
using System.Security.Cryptography;
using ClientSSH.Services.Remote;
using Xunit;

namespace ClientSSH.Tests;

public class RemoteIdentityTests
{
    [Fact]
    public void The_number_is_nine_digits_and_survives_a_reload()
    {
        using var identity = RemoteIdentity.Create();
        using var again = RemoteIdentity.FromPrivateKey(identity.ExportPrivateKey());

        Assert.Matches("^[0-9]{9}$", identity.Number);
        Assert.Equal(identity.Number, again.Number);
        Assert.Equal(identity.Number, RemoteIdentity.NumberOf(identity.PublicKey));
    }

    [Fact]
    public void Two_installations_get_different_numbers()
    {
        using var a = RemoteIdentity.Create();
        using var b = RemoteIdentity.Create();
        Assert.NotEqual(a.Number, b.Number);
    }

    [Fact]
    public void Only_the_owner_can_sign_the_handshake()
    {
        using var owner = RemoteIdentity.Create();
        using var other = RemoteIdentity.Create();
        var (host, viewer) = (RandomNumberGenerator.GetBytes(64), RandomNumberGenerator.GetBytes(64));

        Assert.True(RemoteIdentity.VerifyHandshake(owner.PublicKey, owner.SignHandshake(host, viewer), host, viewer));
        Assert.False(RemoteIdentity.VerifyHandshake(owner.PublicKey, other.SignHandshake(host, viewer), host, viewer));
        Assert.False(RemoteIdentity.VerifyHandshake(owner.PublicKey, owner.SignHandshake(host, viewer), viewer, host));
    }

    [Theory]
    [InlineData("847 291 503", true)]
    [InlineData("847-291-503", true)]
    [InlineData("847291503", true)]
    [InlineData("84729150", false)]
    [InlineData("847 291 50a", false)]
    public void Numbers_parse(string text, bool ok) => Assert.Equal(ok, RemoteIdentity.TryParseNumber(text, out _));
}

public class DhtTests
{
    [Fact]
    public void Bencode_round_trips_a_query()
    {
        var bytes = Bencode.Encode(new Dictionary<string, object>
        {
            ["t"] = new byte[] { 1, 2 }, ["y"] = "q", ["q"] = "get_peers",
            ["a"] = new Dictionary<string, object> { ["id"] = new byte[20], ["implied_port"] = 1 },
        });
        var back = (Dictionary<string, object>)Bencode.Decode(bytes);

        Assert.Equal("q"u8.ToArray(), back["y"]);
        Assert.Equal(1L, ((Dictionary<string, object>)back["a"])["implied_port"]);
        Assert.StartsWith("d1:ad2:id20:", System.Text.Encoding.Latin1.GetString(bytes)); // keys sorted, as the spec requires
    }

    [Theory]
    [InlineData("d1:a")]
    [InlineData("i12")]
    [InlineData("5:abc")]
    [InlineData("le1")]
    public void Bencode_rejects_garbage(string text) =>
        Assert.Throws<FormatException>(() => Bencode.Decode(System.Text.Encoding.ASCII.GetBytes(text)));

    [Fact]
    public void Compact_peers_parse() =>
        Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.7"), 40001), DhtClient.ParseEndpoint([203, 0, 113, 7, 0x9C, 0x41]));

    [Fact]
    public void Host_and_call_keys_differ_per_number()
    {
        Assert.NotEqual(DhtClient.KeyFor("host", "123456789"), DhtClient.KeyFor("call", "123456789"));
        Assert.NotEqual(DhtClient.KeyFor("host", "123456789"), DhtClient.KeyFor("host", "123456780"));
    }
}

/// <summary>Uses the real public DHT. Opt in with CLIENTSSH_NET_TESTS=1.</summary>
public class DhtNetworkTests
{
    [Fact]
    public async Task A_listening_pc_can_be_found_by_its_number()
    {
        if (Environment.GetEnvironmentVariable("CLIENTSSH_NET_TESTS") != "1") return;

        using var identity = RemoteIdentity.Create();
        using var host = new RemoteNode(identity, () => null!, () => null!, new RemoteOptions());
        host.SetListening(true);
        using var finder = new RemoteNode(RemoteIdentity.Create(), () => null!, () => null!, new RemoteOptions());

        var key = DhtClient.KeyFor("host", identity.Number);
        var until = DateTime.UtcNow.AddSeconds(90);
        List<IPEndPoint> peers = [];
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(3000);
            peers = (await finder.Dht.LookupAsync(key)).Peers;
            if (host.Dht.PublicEndpoint is { } mine && peers.Contains(mine)) break;
        }

        Assert.NotNull(host.Dht.PublicEndpoint);
        Assert.Contains(host.Dht.PublicEndpoint, peers);
    }
}

public class InputEventTests
{
    [Fact]
    public void Round_trips() =>
        Assert.True(InputEvent.TryParse(new InputEvent(InputKind.MouseMove, 123, 65535).ToBytes(), out var back) && back == new InputEvent(InputKind.MouseMove, 123, 65535));

    [Theory]
    [InlineData(InputKind.MouseMove, 70000, 0)]
    [InlineData(InputKind.MouseDown, 5, 0)]
    [InlineData(InputKind.KeyDown, 0, 0)]
    [InlineData(InputKind.KeyDown, 300, 0)]
    [InlineData(InputKind.Wheel, 999999, 0)]
    public void Out_of_range_is_rejected(InputKind kind, int a, int b) =>
        Assert.False(InputEvent.TryParse(new InputEvent(kind, a, b).ToBytes(), out _));

    [Fact]
    public void Wrong_length_or_kind_is_rejected()
    {
        Assert.False(InputEvent.TryParse(new byte[3], out _));
        Assert.False(InputEvent.TryParse(new byte[] { 99, 0, 0, 0, 1, 0, 0, 0, 1 }, out _));
    }
}

public class RemoteChannelTests
{
    private static (RemoteChannel A, RemoteChannel B) Pair()
    {
        var k1 = RandomNumberGenerator.GetBytes(32);
        var k2 = RandomNumberGenerator.GetBytes(32);
        return (new RemoteChannel(k1, k2), new RemoteChannel(k2, k1));
    }

    [Fact]
    public void Sealed_packets_open_on_the_other_side()
    {
        var (a, b) = Pair();
        var packet = a.Seal(PacketType.Frame, [1, 2, 3]);

        Assert.True(b.TryOpen(packet, out var type, out var payload));
        Assert.Equal(PacketType.Frame, type);
        Assert.Equal(new byte[] { 1, 2, 3 }, payload);
    }

    [Fact]
    public void A_packet_is_accepted_only_once()
    {
        var (a, b) = Pair();
        var packet = a.Seal(PacketType.Input, [7]);

        Assert.True(b.TryOpen(packet, out _, out _));
        Assert.False(b.TryOpen(packet, out _, out _));
    }

    [Fact]
    public void Reordered_packets_inside_the_window_are_accepted()
    {
        var (a, b) = Pair();
        var first = a.Seal(PacketType.Input, [1]);
        var second = a.Seal(PacketType.Input, [2]);

        Assert.True(b.TryOpen(second, out _, out _));
        Assert.True(b.TryOpen(first, out _, out _));
    }

    [Fact]
    public void A_packet_older_than_the_window_is_rejected()
    {
        var (a, b) = Pair();
        var old = a.Seal(PacketType.Input, [1]);
        for (var i = 0; i < 100; i++) Assert.True(b.TryOpen(a.Seal(PacketType.Keepalive, [0]), out _, out _));

        Assert.False(b.TryOpen(old, out _, out _));
    }

    [Fact]
    public void A_flipped_bit_is_rejected()
    {
        var (a, b) = Pair();
        var packet = a.Seal(PacketType.Frame, [1, 2, 3]);
        packet[10] ^= 1;

        Assert.False(b.TryOpen(packet, out _, out _));
    }

    [Fact]
    public void Another_pair_of_keys_cannot_open_it()
    {
        var (a, _) = Pair();
        var (_, stranger) = Pair();

        Assert.False(stranger.TryOpen(a.Seal(PacketType.Frame, [1]), out _, out _));
    }

    [Fact]
    public void A_packet_cannot_be_turned_back_on_its_sender()
    {
        var (a, b) = Pair();
        var packet = a.Seal(PacketType.Frame, [1]);

        Assert.False(a.TryOpen(packet, out _, out _)); // each direction has its own key
        Assert.True(b.TryOpen(packet, out _, out _));
    }
}

public sealed class RemoteSessionTests
{
    private sealed class FakeScreen : IScreenSource
    {
        public readonly byte[] Frame = RandomNumberGenerator.GetBytes(150_000); // spans many datagrams
        public VideoFrame? Next(bool keyFrame) => new(Frame, keyFrame, 1920, 1080);
    }

    private sealed class RecordingSink : IInputSink
    {
        public readonly List<InputEvent> Events = [];
        public void Apply(InputEvent input) { lock (Events) Events.Add(input); }
    }

    private static readonly RemoteOptions Local = new() { FramesPerSecond = 30 };

    private static async Task<bool> WaitAsync(Func<bool> condition, int milliseconds = 10_000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    // Two nodes on this PC, without the DHT: the caller is told the host's address directly.
    private static (RemoteNode Host, RemoteNode Viewer, FakeScreen Screen, RecordingSink Sink) Pair()
    {
        var screen = new FakeScreen();
        var sink = new RecordingSink();
        var host = new RemoteNode(RemoteIdentity.Create(), () => screen, () => sink, Local, useDht: false);
        var viewer = new RemoteNode(RemoteIdentity.Create(), () => null!, () => null!, Local, useDht: false);
        host.SetListening(true);
        return (host, viewer, screen, sink);
    }

    private static IPEndPoint Loopback(RemoteNode node) => new(IPAddress.Loopback, node.LocalPort);

    [Fact]
    public async Task A_call_by_number_rings_streams_after_consent_and_forwards_input()
    {
        var (hostNode, viewerNode, screen, sink) = Pair();
        using var a = hostNode;
        using var b = viewerNode;

        RemoteSession? host = null;
        string? hostSees = null;
        hostNode.IncomingCall += s => { s.PeerRequested += code => hostSees = code; host = s; s.Ring(); };

        using var viewer = viewerNode.Call(hostNode.Number);
        byte[]? frame = null;
        var keyFirst = false;
        viewer.FrameReceived += f => { if (frame is null) keyFirst = f.IsKey; frame = f.Data; };
        viewer.AddCandidates([Loopback(hostNode)]);

        Assert.True(await WaitAsync(() => hostSees is not null), "the host was never asked");
        Assert.True(await WaitAsync(() => viewer.SecurityCode is not null));
        Assert.Equal(viewer.SecurityCode, hostSees); // both people read the same six digits

        // Not allowed yet: nothing is sent and nothing is applied.
        await Task.Delay(400);
        Assert.Null(frame);
        viewer.SendInput(new InputEvent(InputKind.KeyDown, 65));
        await Task.Delay(300);
        Assert.Empty(sink.Events);

        // View only: pictures arrive, the first one a key frame; input is still ignored.
        host!.Grant(RemoteAccess.View);
        Assert.True(await WaitAsync(() => frame is not null), "no frame after View was granted");
        Assert.True(keyFirst);
        Assert.Equal(screen.Frame, frame);
        Assert.True(await WaitAsync(() => viewer.Access == RemoteAccess.View));
        viewer.SendInput(new InputEvent(InputKind.KeyDown, 66));
        await Task.Delay(300);
        Assert.Empty(sink.Events);

        // Control: input arrives once and in order.
        host.Grant(RemoteAccess.Control);
        Assert.True(await WaitAsync(() => viewer.Access == RemoteAccess.Control));
        for (var i = 1; i <= 30; i++) viewer.SendInput(new InputEvent(InputKind.MouseMove, i * 100, i * 50));
        viewer.SendInput(new InputEvent(InputKind.KeyDown, 65));
        viewer.SendInput(new InputEvent(InputKind.KeyUp, 65));

        Assert.True(await WaitAsync(() => { lock (sink.Events) return sink.Events.Count >= 32; }), "input did not arrive");
        lock (sink.Events)
        {
            Assert.Equal(32, sink.Events.Count);
            Assert.Equal(new InputEvent(InputKind.MouseMove, 100, 50), sink.Events[0]);
            Assert.Equal(new InputEvent(InputKind.KeyDown, 65), sink.Events[30]);
            Assert.Equal(new InputEvent(InputKind.KeyUp, 65), sink.Events[31]);
        }
    }

    [Fact]
    public async Task Denying_ends_the_call_for_both_and_it_does_not_ring_again()
    {
        var (hostNode, viewerNode, _, _) = Pair();
        using var a = hostNode;
        using var b = viewerNode;

        var rings = 0;
        RemoteSession? host = null;
        hostNode.IncomingCall += s => { Interlocked.Increment(ref rings); host = s; };

        using var viewer = viewerNode.Call(hostNode.Number);
        RemoteCloseReason? viewerSaw = null;
        viewer.Closed += reason => viewerSaw = reason;
        viewer.AddCandidates([Loopback(hostNode)]);

        Assert.True(await WaitAsync(() => host is not null && viewer.Phase == RemotePhase.Connected));
        host!.Deny();

        Assert.True(await WaitAsync(() => viewerSaw is not null));
        Assert.Equal(RemoteCloseReason.Denied, viewerSaw);
        await Task.Delay(500);
        Assert.Equal(1, rings);
    }

    [Fact]
    public async Task A_pc_that_does_not_own_the_number_is_ignored()
    {
        var (hostNode, viewerNode, _, _) = Pair();
        using var a = hostNode;
        using var b = viewerNode;

        // The host answers, but its signature cannot match a number that is not its own.
        using var viewer = viewerNode.Call("000000001");
        viewer.AddCandidates([Loopback(hostNode)]);

        await Task.Delay(1500);
        Assert.Equal(RemotePhase.WaitingForPeer, viewer.Phase);
        Assert.Null(viewer.SecurityCode);
    }

    [Fact]
    public async Task A_pc_that_is_not_listening_does_not_ring()
    {
        var (hostNode, viewerNode, _, _) = Pair();
        using var a = hostNode;
        using var b = viewerNode;
        hostNode.SetListening(false);

        var rang = false;
        hostNode.IncomingCall += _ => rang = true;
        using var viewer = viewerNode.Call(hostNode.Number);
        viewer.AddCandidates([Loopback(hostNode)]);

        await Task.Delay(1000);
        Assert.False(rang);
    }

    [Fact]
    public void Calling_your_own_number_is_refused()
    {
        using var node = new RemoteNode(RemoteIdentity.Create(), () => null!, () => null!, Local, useDht: false);
        Assert.Throws<RemoteException>(() => node.Call(node.Number));
    }
}
