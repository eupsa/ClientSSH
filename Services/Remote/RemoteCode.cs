using System.Net;
using System.Security.Cryptography;

namespace ClientSSH.Services.Remote;

public sealed class RemoteCodeException(string message) : Exception(message);

/// <summary>
/// What one PC hands to the other, as text the users paste into any chat: its one-time public key and the public
/// addresses where it can be reached. The host's code is the "offer", the viewer's the "reply". It holds no secret.
/// </summary>
public sealed class RemoteCode
{
    public const string Prefix = "CSSH1-";
    private const int SessionIdLength = 8, PublicKeyLength = 64, ChecksumLength = 4, MaxEndpoints = 8;

    public bool IsReply { get; }
    public byte[] SessionId { get; }
    public byte[] PublicKey { get; }
    public IReadOnlyList<IPEndPoint> Endpoints { get; }

    /// <summary>The exact bytes that were encoded; both sides hash these into the key, so a changed code gives different keys.</summary>
    public byte[] Raw { get; }

    public RemoteCode(bool isReply, byte[] sessionId, byte[] publicKey, IReadOnlyList<IPEndPoint> endpoints)
    {
        if (sessionId.Length != SessionIdLength || publicKey.Length != PublicKeyLength || endpoints.Count is 0 or > MaxEndpoints)
            throw new RemoteCodeException("Invalid code content.");

        IsReply = isReply;
        SessionId = sessionId;
        PublicKey = publicKey;
        Endpoints = endpoints;
        Raw = Serialize();
    }

    public string Encode()
    {
        var withChecksum = new byte[Raw.Length + ChecksumLength];
        Raw.CopyTo(withChecksum, 0);
        SHA256.HashData(Raw).AsSpan(0, ChecksumLength).CopyTo(withChecksum.AsSpan(Raw.Length));
        return Prefix + Convert.ToBase64String(withChecksum).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static RemoteCode Decode(string text)
    {
        var compact = new string((text ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!compact.StartsWith(Prefix, StringComparison.Ordinal)) throw new RemoteCodeException("Not a ClientSSH code.");

        byte[] bytes;
        try
        {
            var base64 = compact[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            bytes = Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
        }
        catch (FormatException)
        {
            throw new RemoteCodeException("The code is damaged.");
        }

        if (bytes.Length <= ChecksumLength) throw new RemoteCodeException("The code is damaged.");
        var raw = bytes.AsSpan(0, bytes.Length - ChecksumLength);
        if (!SHA256.HashData(raw).AsSpan(0, ChecksumLength).SequenceEqual(bytes.AsSpan(raw.Length)))
            throw new RemoteCodeException("The code is damaged (copy it again, whole).");

        return Parse(raw);
    }

    private byte[] Serialize()
    {
        var bytes = new List<byte> { 1, (byte)(IsReply ? 1 : 0) };
        bytes.AddRange(SessionId);
        bytes.AddRange(PublicKey);
        bytes.Add((byte)Endpoints.Count);
        foreach (var endpoint in Endpoints)
        {
            var address = endpoint.Address.GetAddressBytes();
            bytes.Add((byte)address.Length);
            bytes.AddRange(address);
            bytes.Add((byte)(endpoint.Port >> 8));
            bytes.Add((byte)endpoint.Port);
        }
        return [.. bytes];
    }

    private static RemoteCode Parse(ReadOnlySpan<byte> raw)
    {
        const int fixedLength = 2 + SessionIdLength + PublicKeyLength + 1;
        if (raw.Length < fixedLength || raw[0] != 1) throw new RemoteCodeException("This code comes from an incompatible version.");

        var isReply = raw[1] == 1;
        var sessionId = raw.Slice(2, SessionIdLength).ToArray();
        var publicKey = raw.Slice(2 + SessionIdLength, PublicKeyLength).ToArray();
        var count = raw[fixedLength - 1];
        if (count is 0 or > MaxEndpoints) throw new RemoteCodeException("The code is damaged.");

        var endpoints = new List<IPEndPoint>();
        var position = fixedLength;
        for (var i = 0; i < count; i++)
        {
            if (position >= raw.Length) throw new RemoteCodeException("The code is damaged.");
            var length = raw[position++];
            if (length is not (4 or 16) || position + length + 2 > raw.Length) throw new RemoteCodeException("The code is damaged.");

            var address = new IPAddress(raw.Slice(position, length));
            position += length;
            var port = (raw[position] << 8) | raw[position + 1];
            position += 2;
            if (port == 0) throw new RemoteCodeException("The code is damaged.");
            endpoints.Add(new IPEndPoint(address, port));
        }
        if (position != raw.Length) throw new RemoteCodeException("The code is damaged.");

        return new RemoteCode(isReply, sessionId, publicKey, endpoints);
    }
}
