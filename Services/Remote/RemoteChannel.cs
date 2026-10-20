using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ClientSSH.Services.Remote;

public enum PacketType : byte { Hello = 1, Frame = 2, Input = 3, Ack = 4, Keepalive = 5, Bye = 6, Handshake = 16 }

/// <summary>
/// Encrypts and authenticates every datagram with AES-256-GCM. Each direction has its own key and a counter that is
/// the nonce, so a nonce is never reused; a sliding window rejects repeated or very old packets.
/// </summary>
public sealed class RemoteChannel
{
    private const int HeaderLength = 9, TagLength = 16, NonceLength = 12, Window = 64;

    private readonly AesGcm _send, _receive;
    private ulong _sendCounter;
    private ulong _highestReceived;
    private ulong _seenBits;
    private bool _anyReceived;
    private readonly object _lock = new();

    public RemoteChannel(byte[] sendKey, byte[] receiveKey)
    {
        _send = new AesGcm(sendKey, TagLength);
        _receive = new AesGcm(receiveKey, TagLength);
    }

    /// <summary>Datagram layout: type (1) | counter (8) | ciphertext | tag (16).</summary>
    public byte[] Seal(PacketType type, ReadOnlySpan<byte> payload)
    {
        var packet = new byte[HeaderLength + payload.Length + TagLength];
        packet[0] = (byte)type;
        lock (_lock) BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(1), ++_sendCounter);

        _send.Encrypt(Nonce(packet.AsSpan(1, 8)), payload, packet.AsSpan(HeaderLength, payload.Length),
            packet.AsSpan(HeaderLength + payload.Length, TagLength), packet.AsSpan(0, HeaderLength));
        return packet;
    }

    public bool TryOpen(ReadOnlySpan<byte> datagram, out PacketType type, out byte[] payload)
    {
        type = default;
        payload = [];
        if (datagram.Length < HeaderLength + TagLength) return false;

        var counter = BinaryPrimitives.ReadUInt64BigEndian(datagram[1..]);
        if (IsReplay(counter)) return false;

        var length = datagram.Length - HeaderLength - TagLength;
        var plain = new byte[length];
        try
        {
            _receive.Decrypt(Nonce(datagram.Slice(1, 8)), datagram.Slice(HeaderLength, length),
                datagram.Slice(HeaderLength + length, TagLength), plain, datagram[..HeaderLength]);
        }
        catch (CryptographicException)
        {
            return false; // wrong key, tampered, or not ours
        }

        // Only a packet that authenticated may move the window.
        lock (_lock)
        {
            if (!MarkSeen(counter)) return false;
        }
        type = (PacketType)datagram[0];
        payload = plain;
        return true;
    }

    private static byte[] Nonce(ReadOnlySpan<byte> counter)
    {
        var nonce = new byte[NonceLength];
        counter.CopyTo(nonce.AsSpan(4));
        return nonce;
    }

    private bool IsReplay(ulong counter)
    {
        lock (_lock)
        {
            if (!_anyReceived || counter > _highestReceived) return false;
            var behind = _highestReceived - counter;
            return behind >= Window || (_seenBits & (1UL << (int)behind)) != 0;
        }
    }

    private bool MarkSeen(ulong counter)
    {
        if (!_anyReceived)
        {
            _anyReceived = true;
            _highestReceived = counter;
            _seenBits = 1;
            return true;
        }

        if (counter > _highestReceived)
        {
            var shift = counter - _highestReceived;
            _seenBits = shift >= Window ? 1 : (_seenBits << (int)shift) | 1;
            _highestReceived = counter;
            return true;
        }

        var behind = _highestReceived - counter;
        if (behind >= Window || (_seenBits & (1UL << (int)behind)) != 0) return false;
        _seenBits |= 1UL << (int)behind;
        return true;
    }
}

public static class RemoteKeys
{
    private static readonly byte[] Info = "ClientSSH remote v2"u8.ToArray();

    /// <summary>
    /// ECDH (P-256) between this PC's one-time key and the other's public key from its handshake, then HKDF with both public
    /// keys as salt. Someone in the middle ends up with different keys than the two people, so the six digits differ.
    /// </summary>
    public static (RemoteChannel Channel, string SecurityCode) Derive(ECDiffieHellman own, byte[] ownPublic, byte[] peerPublic, bool isHost)
    {
        byte[] secret;
        try
        {
            using var peer = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = peerPublic[..32], Y = peerPublic[32..] },
            });
            secret = own.DeriveRawSecretAgreement(peer.PublicKey);
        }
        catch (CryptographicException)
        {
            throw new RemoteCodeException("The other PC sent an invalid key.");
        }

        var (hostKey, viewerKey) = isHost ? (ownPublic, peerPublic) : (peerPublic, ownPublic);
        var salt = SHA256.HashData([.. hostKey, .. viewerKey]);
        var material = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32 + 32 + 4, salt, Info);
        var hostToViewer = material[..32];
        var viewerToHost = material[32..64];

        // Six digits both people can read aloud to be sure nobody sits in the middle.
        var digits = BinaryPrimitives.ReadUInt32BigEndian(material.AsSpan(64)) % 1_000_000;
        var security = $"{digits / 1000:000} {digits % 1000:000}";

        return (isHost ? new RemoteChannel(hostToViewer, viewerToHost) : new RemoteChannel(viewerToHost, hostToViewer), security);
    }

    public static byte[] ExportPublicKey(ECDiffieHellman key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        return [.. parameters.Q.X!, .. parameters.Q.Y!];
    }
}
