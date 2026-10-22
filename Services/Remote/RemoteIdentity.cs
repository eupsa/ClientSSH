using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ClientSSH.Services.Remote;

public sealed class RemoteException(string message) : Exception(message);

/// <summary>
/// This installation's long-lived key (ECDSA P-256), created once and kept DPAPI-protected on this PC. The PC number people
/// type (9 digits, like "847 291 503") is derived from its public key, so only this PC can prove it owns the number:
/// no hardware ids are read or sent.
/// </summary>
public sealed class RemoteIdentity : IDisposable
{
    private readonly ECDsa _key;

    public byte[] PublicKey { get; }
    public string Number { get; }

    private RemoteIdentity(ECDsa key)
    {
        _key = key;
        PublicKey = Export(key);
        Number = NumberOf(PublicKey);
    }

    public static RemoteIdentity Create() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public static RemoteIdentity FromPrivateKey(byte[] pkcs8)
    {
        var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(pkcs8, out _);
        return new RemoteIdentity(key);
    }

    public byte[] ExportPrivateKey() => _key.ExportPkcs8PrivateKey();

    public static string NumberOf(byte[] publicKey) =>
        (BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(publicKey)) % 1_000_000_000).ToString("000000000");

    public static string Format(string number) => $"{number[..3]} {number[3..6]} {number[6..]}";

    /// <summary>Accepts "847 291 503", "847-291-503" and so on.</summary>
    public static bool TryParseNumber(string? text, out string number)
    {
        text ??= "";
        number = new string(text.Where(char.IsAsciiDigit).ToArray());
        return number.Length == 9 && text.All(c => char.IsAsciiDigit(c) || c is ' ' or '-' or '.' or '\t');
    }

    // The host signs both one-time keys of the session, so the viewer knows it reached the PC that owns the number.
    public byte[] SignHandshake(byte[] hostEphemeral, byte[] viewerEphemeral) =>
        _key.SignData(Transcript(hostEphemeral, viewerEphemeral), HashAlgorithmName.SHA256);

    public static bool VerifyHandshake(byte[] identityPublic, byte[] signature, byte[] hostEphemeral, byte[] viewerEphemeral)
    {
        try
        {
            using var key = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = identityPublic[..32], Y = identityPublic[32..] },
            });
            return key.VerifyData(Transcript(hostEphemeral, viewerEphemeral), signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static byte[] Transcript(byte[] hostEphemeral, byte[] viewerEphemeral) =>
        [.. Encoding.ASCII.GetBytes("ClientSSH remote v3"), .. hostEphemeral, .. viewerEphemeral];

    private static byte[] Export(ECDsa key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        return [.. parameters.Q.X!, .. parameters.Q.Y!];
    }

    public void Dispose() => _key.Dispose();
}
