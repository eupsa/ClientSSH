using System.Net;
using System.Security.Cryptography;
using ClientSSH.Services.Remote;
using Xunit;

namespace ClientSSH.Tests;

public class RemoteCodeTests
{
    private static RemoteCode Sample(bool reply = false) => new(
        reply, RandomNumberGenerator.GetBytes(8), RandomNumberGenerator.GetBytes(64),
        [new IPEndPoint(IPAddress.Parse("203.0.113.7"), 40001), new IPEndPoint(IPAddress.Parse("192.168.0.10"), 40001)]);

    [Fact]
    public void Round_trips_through_text()
    {
        var code = Sample();
        var back = RemoteCode.Decode(code.Encode());

        Assert.Equal(code.Raw, back.Raw);
        Assert.Equal(code.Endpoints, back.Endpoints);
        Assert.False(back.IsReply);
    }

    [Fact]
    public void Survives_chat_line_breaks_and_spaces()
    {
        var text = Sample(reply: true).Encode();
        var broken = text[..30] + "\r\n  " + text[30..60] + " " + text[60..];

        Assert.True(RemoteCode.Decode(broken).IsReply);
    }

    [Fact]
    public void A_changed_character_is_caught()
    {
        var text = Sample().Encode();
        var index = RemoteCode.Prefix.Length + 20;
        var tampered = text[..index] + (text[index] == 'A' ? 'B' : 'A') + text[(index + 1)..];

        Assert.Throws<RemoteCodeException>(() => RemoteCode.Decode(tampered));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("CSSH1-")]
    [InlineData("CSSH1-AAAA")]
    public void Garbage_is_rejected(string text) => Assert.Throws<RemoteCodeException>(() => RemoteCode.Decode(text));
}

public class StunTests
{
    [Fact]
    public void Reads_the_xor_mapped_address()
    {
        var transaction = Enumerable.Range(1, 12).Select(i => (byte)i).ToArray();
        // 203.0.113.7:40001 XOR-ed with the magic cookie
        ushort xport = (ushort)(40001 ^ 0x2112);
        byte[] cookie = [0x21, 0x12, 0xA4, 0x42];
        byte[] address = [203, 0, 113, 7];
        var xaddress = address.Select((b, i) => (byte)(b ^ cookie[i])).ToArray();

        byte[] response = [0x01, 0x01, 0x00, 12, 0x21, 0x12, 0xA4, 0x42, .. transaction,
            0x00, 0x20, 0x00, 0x08, 0x00, 0x01, (byte)(xport >> 8), (byte)xport, .. xaddress];

        Assert.True(Stun.TryParseResponse(response, transaction, out var endpoint));
        Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.7"), 40001), endpoint);
    }

    [Fact]
    public void Ignores_an_answer_to_another_transaction()
    {
        var mine = new byte[12];
        byte[] response = [0x01, 0x01, 0x00, 0x00, 0x21, 0x12, 0xA4, 0x42, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9];

        Assert.False(Stun.TryParseResponse(response, mine, out _));
    }
}

public class StunNetworkTests
{
    // Talks to the real public STUN servers, so it only runs when asked: set CLIENTSSH_NET_TESTS=1.
    [Fact]
    public async Task Public_stun_servers_report_a_public_address()
    {
        if (Environment.GetEnvironmentVariable("CLIENTSSH_NET_TESTS") != "1") return;

        foreach (var server in new[] { "stun.l.google.com:19302", "stun.cloudflare.com:3478" })
        {
            using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));

            var endpoint = await Stun.QueryAsync(socket, server, TimeSpan.FromSeconds(4));

            Assert.NotNull(endpoint);
            Assert.NotEqual(0, endpoint!.Port);
        }
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
        public byte[]? CaptureJpeg() => Frame;
    }

    private sealed class RecordingSink : IInputSink
    {
        public readonly List<InputEvent> Events = [];
        public void Apply(InputEvent input) { lock (Events) Events.Add(input); }
    }

    private static readonly RemoteOptions Local = new() { IncludeLoopback = true, FramesPerSecond = 20 };

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

    [Fact]
    public async Task Two_peers_connect_after_consent_stream_and_forward_input()
    {
        var screen = new FakeScreen();
        var sink = new RecordingSink();
        using var host = await RemoteSession.CreateHostAsync(Local, screen, sink);
        using var viewer = await RemoteSession.JoinAsync(host.Code, Local);

        string? hostSees = null;
        host.PeerRequested += security => hostSees = security;
        byte[]? frame = null;
        viewer.FrameReceived += f => frame = f;

        host.AcceptReply(viewer.Code);
        Assert.True(await WaitAsync(() => hostSees is not null), "host never saw the viewer");
        Assert.Equal(viewer.SecurityCode, hostSees); // both people read the same six digits

        // Not allowed yet: nothing is sent and nothing is applied.
        await Task.Delay(400);
        Assert.Null(frame);
        viewer.SendInput(new InputEvent(InputKind.KeyDown, 65));
        await Task.Delay(300);
        Assert.Empty(sink.Events);

        // View only: pictures arrive, input is still ignored.
        host.Grant(RemoteAccess.View);
        Assert.True(await WaitAsync(() => frame is not null), "no frame after View was granted");
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
    public async Task Denying_ends_the_session_for_both()
    {
        using var host = await RemoteSession.CreateHostAsync(Local, new FakeScreen(), new RecordingSink());
        using var viewer = await RemoteSession.JoinAsync(host.Code, Local);

        var hostAsked = false;
        host.PeerRequested += _ => hostAsked = true;
        RemoteCloseReason? viewerSaw = null;
        viewer.Closed += reason => viewerSaw = reason;

        host.AcceptReply(viewer.Code);
        Assert.True(await WaitAsync(() => hostAsked));
        host.Deny();

        Assert.True(await WaitAsync(() => viewerSaw is not null));
        Assert.Equal(RemoteCloseReason.Denied, viewerSaw);
        Assert.Equal(RemotePhase.Closed, host.Phase);
    }

    [Fact]
    public async Task A_reply_from_another_session_is_refused()
    {
        using var host = await RemoteSession.CreateHostAsync(Local, new FakeScreen(), new RecordingSink());
        using var other = await RemoteSession.CreateHostAsync(Local, new FakeScreen(), new RecordingSink());
        using var viewerOfOther = await RemoteSession.JoinAsync(other.Code, Local);

        Assert.Throws<RemoteCodeException>(() => host.AcceptReply(viewerOfOther.Code));
    }

    [Fact]
    public async Task Someone_elses_reply_gives_different_digits_than_the_real_viewer()
    {
        // Two PCs answer the same offer. Whoever the host accepts connects, but with different keys, so the six
        // digits differ: the people notice that the one who answered is not who they meant.
        using var host = await RemoteSession.CreateHostAsync(Local, new FakeScreen(), new RecordingSink());
        using var viewer = await RemoteSession.JoinAsync(host.Code, Local);
        using var intruder = await RemoteSession.JoinAsync(host.Code, Local);

        var asked = false;
        host.PeerRequested += _ => asked = true;
        host.AcceptReply(intruder.Code);

        Assert.True(await WaitAsync(() => asked));
        Assert.NotEqual(viewer.SecurityCode, intruder.SecurityCode);
    }
}
