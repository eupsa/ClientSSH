using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ClientSSH.Services.Remote;

/// <summary>
/// A tiny mailbox that lets a short numeric code stand for the long pasted codes of <see cref="RemoteCode"/>. It runs on a PC
/// the user chooses (the optional installer component), never on ours. It holds only the two public, non-secret codes for at most
/// ten minutes, in memory; it never sees keys, the screen or any input. A dishonest server can only swap the codes, which the
/// six-digit security code the two people compare out loud exposes.
/// One request per TCP connection, one text line each way: PUT/GET/REPLY/POLL/DEL &lt;9 digits&gt; [code].
/// </summary>
public sealed class RendezvousServer : IDisposable
{
    public const int DefaultPort = 47800;
    private const int MaxLine = 700, MaxEntries = 500, RequestsPerMinute = 60;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private sealed class Entry(string offer)
    {
        public string Offer = offer;
        public string? Reply;
        public DateTime Expires = DateTime.UtcNow + Lifetime;
    }

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly Dictionary<IPAddress, (DateTime Start, int Count)> _rates = [];
    private readonly object _gate = new();

    public int Port { get; }

    public RendezvousServer(int port = DefaultPort)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => ServeAsync(client));
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { /* keep listening */ }
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var stream = client.GetStream();
                var line = await ReadLineAsync(stream, MaxLine, timeout.Token);
                var from = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
                await stream.WriteAsync(Encoding.UTF8.GetBytes(Handle(line, from) + "\n"), timeout.Token);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    internal static async Task<string> ReadLineAsync(NetworkStream stream, int max, CancellationToken token)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count <= max && await stream.ReadAsync(one, token) == 1 && one[0] != (byte)'\n') bytes.Add(one[0]);
        return Encoding.UTF8.GetString([.. bytes]).Trim();
    }

    /// <summary>One request in, one answer out. Public so the rules can be tested without a socket.</summary>
    public string Handle(string line, IPAddress from)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 3 || !RendezvousClient.IsCode(parts[1])) return "BAD";
        var (command, code) = (parts[0], parts[1]);
        var payload = parts.Length == 3 && parts[2].StartsWith(RemoteCode.Prefix, StringComparison.Ordinal) ? parts[2] : null;

        lock (_gate)
        {
            var now = DateTime.UtcNow;
            foreach (var key in _entries.Where(e => e.Value.Expires < now).Select(e => e.Key).ToList()) _entries.Remove(key);

            // shortcut: one counter per address, no ban list and the table is never pruned; fine for a few friends' PCs.
            var (start, count) = _rates.GetValueOrDefault(from, (now, 0));
            if (now - start > TimeSpan.FromMinutes(1)) (start, count) = (now, 0);
            _rates[from] = (start, count + 1);
            if (count >= RequestsPerMinute) return "SLOW";

            _entries.TryGetValue(code, out var entry);
            switch (command)
            {
                case "PUT" when payload is not null:
                    if (entry is not null) return "TAKEN";
                    if (_entries.Count >= MaxEntries) return "FULL";
                    _entries[code] = new Entry(payload);
                    return "OK";
                case "GET": return entry?.Offer ?? "NONE";
                case "REPLY" when payload is not null:
                    if (entry is null || entry.Reply is not null) return "NONE";
                    entry.Reply = payload;
                    return "OK";
                case "POLL": return entry?.Reply ?? "NONE";
                case "DEL": _entries.Remove(code); return "OK";
                default: return "BAD";
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}

public static class RendezvousClient
{
    public static bool IsCode(string text) => text.Length == 9 && text.All(char.IsAsciiDigit);

    public static string NewCode() => RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000).ToString();

    public static string Format(string code) => $"{code[..3]} {code[3..6]} {code[6..]}";

    /// <summary>Accepts "123 456 789", "123-456-789" and so on. False for anything else, such as a pasted long code.</summary>
    public static bool TryParseCode(string? text, out string code)
    {
        text ??= "";
        code = new string(text.Where(char.IsAsciiDigit).ToArray());
        return IsCode(code) && text.All(c => char.IsAsciiDigit(c) || c is ' ' or '-' or '\t' or '\r' or '\n');
    }

    /// <summary>"host" or "host:port". Null when empty or malformed.</summary>
    public static (string Host, int Port)? ParseServer(string? text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return null;
        var colon = text.LastIndexOf(':');
        if (colon < 0) return (text, RendezvousServer.DefaultPort);
        return colon > 0 && int.TryParse(text[(colon + 1)..], out var port) && port is > 0 and < 65536 ? (text[..colon], port) : null;
    }

    /// <summary>Sends one request and returns the answer line. Throws on an unreachable or silent server.</summary>
    public static async Task<string> RequestAsync(string server, string line, CancellationToken token = default)
    {
        var (host, port) = ParseServer(server) ?? throw new FormatException("Bad server address.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));

        using var client = new TcpClient();
        await client.ConnectAsync(host, port, timeout.Token);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), timeout.Token);
        return await RendezvousServer.ReadLineAsync(stream, 2048, timeout.Token);
    }
}
