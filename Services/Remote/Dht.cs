using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace ClientSSH.Services.Remote;

/// <summary>Bencode, the encoding of BitTorrent DHT messages: byte strings, integers, lists and dictionaries.</summary>
public static class Bencode
{
    public static byte[] Encode(object value)
    {
        var output = new MemoryStream();
        Write(output, value);
        return output.ToArray();
    }

    private static void Write(MemoryStream output, object value)
    {
        switch (value)
        {
            case byte[] bytes:
                WriteAscii(output, $"{bytes.Length}:");
                output.Write(bytes);
                break;
            case string text:
                Write(output, Encoding.UTF8.GetBytes(text));
                break;
            case int or long:
                WriteAscii(output, $"i{value}e");
                break;
            case IEnumerable<KeyValuePair<string, object>> dictionary:
                output.WriteByte((byte)'d');
                foreach (var (key, item) in dictionary.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    Write(output, key);
                    Write(output, item);
                }
                output.WriteByte((byte)'e');
                break;
            case IEnumerable<object> list:
                output.WriteByte((byte)'l');
                foreach (var item in list) Write(output, item);
                output.WriteByte((byte)'e');
                break;
            default:
                throw new ArgumentException($"Cannot bencode {value.GetType()}");
        }
    }

    private static void WriteAscii(MemoryStream output, string text) => output.Write(Encoding.ASCII.GetBytes(text));

    /// <summary>Byte strings come back as byte[], integers as long, lists as List&lt;object&gt;, dictionaries as Dictionary&lt;string, object&gt;.</summary>
    public static object Decode(ReadOnlySpan<byte> data)
    {
        var position = 0;
        var value = Read(data, ref position, depth: 0);
        if (position != data.Length) throw new FormatException("Trailing data.");
        return value;
    }

    private static object Read(ReadOnlySpan<byte> data, ref int position, int depth)
    {
        if (depth > 16 || position >= data.Length) throw new FormatException("Bad bencode.");
        switch (data[position])
        {
            case (byte)'i':
            {
                var end = data[position..].IndexOf((byte)'e');
                if (end < 2 || !long.TryParse(Encoding.ASCII.GetString(data.Slice(position + 1, end - 1)), out var number)) throw new FormatException("Bad integer.");
                position += end + 1;
                return number;
            }
            case (byte)'l':
            {
                position++;
                var list = new List<object>();
                while (position < data.Length && data[position] != 'e') list.Add(Read(data, ref position, depth + 1));
                position++;
                return list;
            }
            case (byte)'d':
            {
                position++;
                var dictionary = new Dictionary<string, object>(StringComparer.Ordinal);
                while (position < data.Length && data[position] != 'e')
                {
                    if (Read(data, ref position, depth + 1) is not byte[] key) throw new FormatException("Bad key.");
                    dictionary[Encoding.Latin1.GetString(key)] = Read(data, ref position, depth + 1);
                }
                position++;
                return dictionary;
            }
            default:
            {
                var colon = data[position..].IndexOf((byte)':');
                if (colon < 1 || !int.TryParse(Encoding.ASCII.GetString(data.Slice(position, colon)), out var length) ||
                    length < 0 || position + colon + 1 + length > data.Length) throw new FormatException("Bad string.");
                var bytes = data.Slice(position + colon + 1, length).ToArray();
                position += colon + 1 + length;
                return bytes;
            }
        }
    }
}

/// <summary>What one lookup found: peers stored under the key, and the closest nodes with the tokens needed to store there.</summary>
public sealed record DhtLookup(byte[] Target, List<IPEndPoint> Peers, List<(IPEndPoint Node, byte[] Token)> Closest);

/// <summary>
/// A small client for the public BitTorrent DHT (BEP 5): millions of PCs that store "who is at this key" for 30 minutes,
/// with no owner and no central server. ClientSSH only uses get_peers and announce_peer, as a read-only node (BEP 43), on the
/// same UDP socket as the remote session: the address the DHT records is the very port the other PC must reach.
/// </summary>
public sealed class DhtClient
{
    private const int K = 8, Alpha = 8;
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);

    public static readonly string[] Bootstrap =
    [
        "router.bittorrent.com:6881", "dht.transmissionbt.com:6881", "router.utorrent.com:6881", "dht.libtorrent.org:25401",
    ];

    private readonly Action<IPEndPoint, byte[]> _send;
    private readonly byte[] _id = RandomNumberGenerator.GetBytes(20);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Dictionary<string, object>?>> _pending = new();
    private readonly ConcurrentDictionary<IPEndPoint, DateTime> _good = new();
    private readonly ConcurrentDictionary<IPEndPoint, byte> _seenPublicPorts = new();
    private int _transaction;

    public DhtClient(Action<IPEndPoint, byte[]> send) => _send = send;

    /// <summary>This PC's public address as DHT nodes saw it (BEP 42 "ip"), once any node said so.</summary>
    public IPEndPoint? PublicEndpoint { get; private set; }

    /// <summary>True when nodes saw different public ports: a symmetric NAT, which usually blocks direct connections.</summary>
    public bool PortsDiffer => _seenPublicPorts.Keys.Select(e => e.Port).Distinct().Count() > 1;

    public static byte[] KeyFor(string purpose, string number) => SHA1.HashData(Encoding.UTF8.GetBytes($"ClientSSH remote {purpose} v1 {number}"));

    /// <summary>Feed every datagram that starts with 'd'. True when it was a DHT message.</summary>
    public bool OnDatagram(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2 || data[0] != (byte)'d') return false;
        Dictionary<string, object> message;
        try
        {
            if (Bencode.Decode(data) is not Dictionary<string, object> decoded) return true;
            message = decoded;
        }
        catch (FormatException)
        {
            return true;
        }

        if (message.GetValueOrDefault("t") is not byte[] transaction || message.GetValueOrDefault("y") is not byte[] kind) return true;
        if (!_pending.TryRemove(Convert.ToHexString(transaction), out var waiter)) return true;

        if (kind is [(byte)'r'] && message.GetValueOrDefault("r") is Dictionary<string, object> response)
        {
            if (message.GetValueOrDefault("ip") is byte[] { Length: 6 } ip)
            {
                var mine = ParseEndpoint(ip);
                PublicEndpoint = mine;
                if (_seenPublicPorts.Count < 32) _seenPublicPorts.TryAdd(mine, 0);
            }
            waiter.TrySetResult(response);
        }
        else
        {
            waiter.TrySetResult(null);
        }
        return true;
    }

    private async Task<Dictionary<string, object>?> QueryAsync(IPEndPoint node, string method, Dictionary<string, object> arguments, CancellationToken token)
    {
        var transaction = BitConverter.GetBytes((ushort)Interlocked.Increment(ref _transaction));
        var waiter = new TaskCompletionSource<Dictionary<string, object>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = Convert.ToHexString(transaction);
        _pending[key] = waiter;

        arguments["id"] = _id;
        _send(node, Bencode.Encode(new Dictionary<string, object>
        {
            ["t"] = transaction, ["y"] = "q", ["q"] = method, ["a"] = arguments, ["ro"] = 1,
        }));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(QueryTimeout);
        await using (timeout.Token.Register(() => waiter.TrySetResult(null)))
        {
            var response = await waiter.Task;
            _pending.TryRemove(key, out _);
            if (response is not null && _good.Count < 400) _good[node] = DateTime.UtcNow;
            return response;
        }
    }

    /// <summary>Walks the DHT toward the key: peers stored there, and the closest nodes with their store tokens.</summary>
    public async Task<DhtLookup> LookupAsync(byte[] target, CancellationToken token = default)
    {
        var candidates = new Dictionary<IPEndPoint, BigInteger>();
        var queried = new HashSet<IPEndPoint>();
        var answered = new List<(IPEndPoint Node, byte[] Token, BigInteger Distance)>();
        var peers = new HashSet<IPEndPoint>();

        foreach (var node in _good.Keys) candidates.TryAdd(node, Distance(RandomNumberGenerator.GetBytes(20), target)); // unknown ids: queried early, ranked later
        foreach (var node in await ResolveBootstrapAsync()) candidates.TryAdd(node, BigInteger.Pow(2, 161));

        for (var round = 0; round < 12 && !token.IsCancellationRequested; round++)
        {
            var closestAnswered = answered.OrderBy(a => a.Distance).Take(K).ToList();
            var next = candidates.Where(c => !queried.Contains(c.Key)).OrderBy(c => c.Value).Take(Alpha).ToList();
            if (next.Count == 0) break;
            // Done when K nodes answered and nothing unqueried is closer than the farthest of them.
            if (closestAnswered.Count >= K && next[0].Value >= closestAnswered[^1].Distance) break;

            var replies = await Task.WhenAll(next.Select(async c =>
            {
                queried.Add(c.Key);
                return (c.Key, Response: await QueryAsync(c.Key, "get_peers", new Dictionary<string, object> { ["info_hash"] = target }, token));
            }));

            foreach (var (node, response) in replies)
            {
                if (response is null) continue;
                if (response.GetValueOrDefault("id") is byte[] { Length: 20 } id && response.GetValueOrDefault("token") is byte[] storeToken)
                    answered.Add((node, storeToken, Distance(id, target)));

                if (response.GetValueOrDefault("values") is List<object> values)
                {
                    foreach (var value in values.OfType<byte[]>().Where(v => v.Length == 6).Take(200)) peers.Add(ParseEndpoint(value));
                }
                if (response.GetValueOrDefault("nodes") is byte[] nodes)
                {
                    for (var i = 0; i + 26 <= nodes.Length; i += 26)
                    {
                        var endpoint = ParseEndpoint(nodes.AsSpan(i + 20, 6));
                        if (endpoint.Port != 0 && !IPAddress.IsLoopback(endpoint.Address)) candidates.TryAdd(endpoint, Distance(nodes.AsSpan(i, 20), target));
                    }
                }
            }
        }

        var closest = answered.OrderBy(a => a.Distance).DistinctBy(a => a.Node).Take(K).Select(a => (a.Node, a.Token)).ToList();
        return new DhtLookup(target, [.. peers], closest);
    }

    /// <summary>Stores "this socket's public address" under the key, on the closest nodes of a lookup.</summary>
    public async Task AnnounceAsync(DhtLookup lookup, CancellationToken token = default) =>
        await Task.WhenAll(lookup.Closest.Select(c => QueryAsync(c.Node, "announce_peer", new Dictionary<string, object>
        {
            ["info_hash"] = lookup.Target, ["port"] = 1, ["implied_port"] = 1, ["token"] = c.Token,
        }, token)));

    /// <summary>Asks only the closest nodes of an earlier lookup again: a cheap way to watch one key.</summary>
    public async Task<List<IPEndPoint>> PeersAsync(DhtLookup lookup, CancellationToken token = default)
    {
        var replies = await Task.WhenAll(lookup.Closest.Select(c =>
            QueryAsync(c.Node, "get_peers", new Dictionary<string, object> { ["info_hash"] = lookup.Target }, token)));
        return replies.OfType<Dictionary<string, object>>()
            .SelectMany(r => r.GetValueOrDefault("values") as List<object> ?? [])
            .OfType<byte[]>().Where(v => v.Length == 6).Select(v => ParseEndpoint(v)).Distinct().ToList();
    }

    private static async Task<List<IPEndPoint>> ResolveBootstrapAsync()
    {
        var found = new List<IPEndPoint>();
        foreach (var entry in Bootstrap)
        {
            var colon = entry.LastIndexOf(':');
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(entry[..colon]);
                found.AddRange(addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Take(2)
                    .Select(a => new IPEndPoint(a, int.Parse(entry[(colon + 1)..]))));
            }
            catch (SocketException)
            {
                // one router down is fine
            }
        }
        return found;
    }

    public static IPEndPoint ParseEndpoint(ReadOnlySpan<byte> compact) =>
        new(new IPAddress(compact[..4]), (compact[4] << 8) | compact[5]);

    private static BigInteger Distance(ReadOnlySpan<byte> id, byte[] target)
    {
        Span<byte> xor = stackalloc byte[20];
        for (var i = 0; i < 20; i++) xor[i] = (byte)(id[i] ^ target[i]);
        return new BigInteger(xor, isUnsigned: true, isBigEndian: true);
    }
}
