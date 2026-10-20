using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace ClientSSH.Services.Remote;

/// <summary>
/// Minimal STUN client (RFC 5389, Binding request). A STUN server is only a mirror: it tells a PC which public
/// address and port the internet sees for it, so the other PC knows where to send packets. It sees no content.
/// </summary>
public static class Stun
{
    private const uint MagicCookie = 0x2112A442;
    private const ushort BindingRequest = 0x0001, BindingSuccess = 0x0101;
    private const ushort MappedAddress = 0x0001, XorMappedAddress = 0x0020;

    public static byte[] NewTransactionId() => RandomNumberGenerator.GetBytes(12);

    public static byte[] BuildRequest(byte[] transactionId)
    {
        var message = new byte[20];
        BinaryPrimitives.WriteUInt16BigEndian(message, BindingRequest);
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4), MagicCookie);
        transactionId.CopyTo(message.AsSpan(8));
        return message;
    }

    /// <summary>Reads the public address out of a Binding success response for our transaction.</summary>
    public static bool TryParseResponse(ReadOnlySpan<byte> data, ReadOnlySpan<byte> transactionId, out IPEndPoint? endpoint)
    {
        endpoint = null;
        if (data.Length < 20 || BinaryPrimitives.ReadUInt16BigEndian(data) != BindingSuccess) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(data[4..]) != MagicCookie || !data.Slice(8, 12).SequenceEqual(transactionId)) return false;

        var length = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        if (20 + length > data.Length) return false;

        var attributes = data.Slice(20, length);
        while (attributes.Length >= 4)
        {
            var type = BinaryPrimitives.ReadUInt16BigEndian(attributes);
            var size = BinaryPrimitives.ReadUInt16BigEndian(attributes[2..]);
            if (4 + size > attributes.Length) return false;

            var value = attributes.Slice(4, size);
            if (type is XorMappedAddress or MappedAddress && TryReadAddress(value, xor: type == XorMappedAddress, transactionId, out endpoint))
                return true;

            var padded = 4 + ((size + 3) & ~3);
            if (padded > attributes.Length) break;
            attributes = attributes[padded..];
        }
        return false;
    }

    private static bool TryReadAddress(ReadOnlySpan<byte> value, bool xor, ReadOnlySpan<byte> transactionId, out IPEndPoint? endpoint)
    {
        endpoint = null;
        if (value.Length < 8) return false;

        var family = value[1];
        var port = BinaryPrimitives.ReadUInt16BigEndian(value[2..]);
        if (xor) port ^= (ushort)(MagicCookie >> 16);

        if (family == 1 && value.Length >= 8)
        {
            var address = value.Slice(4, 4).ToArray();
            if (xor)
            {
                Span<byte> cookie = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(cookie, MagicCookie);
                for (var i = 0; i < 4; i++) address[i] ^= cookie[i];
            }
            endpoint = new IPEndPoint(new IPAddress(address), port);
            return port != 0;
        }
        return false; // IPv6 mapped addresses are not used yet: the socket is IPv4
    }

    /// <summary>Asks one STUN server for this socket's public endpoint. Null when it does not answer in time.</summary>
    public static async Task<IPEndPoint?> QueryAsync(Socket socket, string server, TimeSpan timeout)
    {
        try
        {
            var (host, port) = SplitHostPort(server);
            var addresses = await Dns.GetHostAddressesAsync(host);
            var target = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (target is null) return null;

            var transaction = NewTransactionId();
            var remote = new IPEndPoint(target, port);
            await socket.SendToAsync(BuildRequest(transaction), SocketFlags.None, remote);

            using var cancel = new CancellationTokenSource(timeout);
            var buffer = new byte[1500];
            while (true)
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), cancel.Token);
                if (TryParseResponse(buffer.AsSpan(0, result.ReceivedBytes), transaction, out var endpoint)) return endpoint;
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or FormatException)
        {
            return null;
        }
    }

    private static (string Host, int Port) SplitHostPort(string server)
    {
        var text = server.Trim();
        if (text.StartsWith("stun:", StringComparison.OrdinalIgnoreCase)) text = text[5..];
        var colon = text.LastIndexOf(':');
        if (colon < 0) return (text, 3478);
        return int.TryParse(text[(colon + 1)..], out var port) && port is > 0 and < 65536
            ? (text[..colon], port)
            : throw new FormatException("Invalid STUN server.");
    }
}
