using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ClientSSH.Services.Remote;

public sealed class RemoteCodeException(string message) : Exception(message);

/// <summary>
/// What one PC hands to the other: only its public address (IPv4 and port), written as syllables people can say and type,
/// like "bako rimu sela fidu nomi ta". The host's code is the "offer", the viewer's the "reply". It holds no key and no secret:
/// the keys are agreed on the connection itself, and the six digits both people compare out loud expose anyone in the middle.
/// </summary>
public sealed class RemoteCode
{
    public const int CompactLength = 11 * 2;

    private const string Consonants = "bdfghjklmnprstvz", Vowels = "aeio";
    private const int SyllableCount = 11, RawLength = 8;

    public bool IsReply { get; }
    public IPEndPoint Endpoint { get; }

    public RemoteCode(bool isReply, IPEndPoint endpoint)
    {
        if (endpoint.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || endpoint.Port == 0)
            throw new RemoteCodeException("Invalid code content.");
        IsReply = isReply;
        Endpoint = endpoint;
    }

    /// <summary>Syllables in groups of two: "bako rimu sela fidu nomi ta".</summary>
    public string Encode() => string.Join(' ', Chunk(Compact(), 4));

    /// <summary>Same code without spaces, for the rendezvous line protocol.</summary>
    public string Compact()
    {
        var raw = Raw();
        var bits = new System.Numerics.BigInteger(raw, isUnsigned: true, isBigEndian: true) << (SyllableCount * 6 - RawLength * 8);
        var text = new StringBuilder();
        for (var i = SyllableCount - 1; i >= 0; i--)
        {
            var value = (int)((bits >> (i * 6)) & 63);
            text.Append(Consonants[value >> 2]).Append(Vowels[value & 3]);
        }
        return text.ToString();
    }

    public static bool IsCompact(string? text) =>
        text is { Length: CompactLength } && text.All(c => char.IsAsciiLetterLower(c) && (Consonants.Contains(c) || Vowels.Contains(c)));

    /// <summary>Accepts any spacing, dashes and capitals.</summary>
    public static RemoteCode Decode(string? text)
    {
        var letters = new string((text ?? "").Where(char.IsAsciiLetter).Select(char.ToLowerInvariant).ToArray());
        if (letters.Length != CompactLength) throw new RemoteCodeException("Not a ClientSSH code.");

        System.Numerics.BigInteger bits = 0;
        for (var i = 0; i < letters.Length; i += 2)
        {
            var c = Consonants.IndexOf(letters[i]);
            var v = Vowels.IndexOf(letters[i + 1]);
            if (c < 0 || v < 0) throw new RemoteCodeException("The code is damaged.");
            bits = (bits << 6) | (c << 2 | v);
        }

        var extra = SyllableCount * 6 - RawLength * 8;
        if ((bits & ((1 << extra) - 1)) != 0) throw new RemoteCodeException("The code is damaged.");
        var raw = (bits >> extra).ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length < RawLength) raw = [.. new byte[RawLength - raw.Length], .. raw];
        if (raw.Length != RawLength || raw[0] > 1) throw new RemoteCodeException("This code comes from an incompatible version.");
        if (Check(raw.AsSpan(0, 7)) != raw[7]) throw new RemoteCodeException("The code is damaged (type it again, whole).");

        var port = (raw[5] << 8) | raw[6];
        return new RemoteCode(raw[0] == 1, new IPEndPoint(new IPAddress(raw.AsSpan(1, 4)), port));
    }

    // flag | ipv4 (4) | port (2) | check (1)
    private byte[] Raw()
    {
        var raw = new byte[RawLength];
        raw[0] = (byte)(IsReply ? 1 : 0);
        Endpoint.Address.GetAddressBytes().CopyTo(raw, 1);
        raw[5] = (byte)(Endpoint.Port >> 8);
        raw[6] = (byte)Endpoint.Port;
        raw[7] = Check(raw.AsSpan(0, 7));
        return raw;
    }

    private static byte Check(ReadOnlySpan<byte> data) => SHA256.HashData(data)[0];

    private static IEnumerable<string> Chunk(string text, int size)
    {
        for (var i = 0; i < text.Length; i += size) yield return text.Substring(i, Math.Min(size, text.Length - i));
    }
}
