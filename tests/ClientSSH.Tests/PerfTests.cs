using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ClientSSH.Services.Remote;
using ClientSSH.Terminal;
using Xunit;
using Xunit.Abstractions;

namespace ClientSSH.Tests;

/// <summary>Timings and allocations of the hot paths. Opt in with CLIENTSSH_PERF_TESTS=1 (best with -c Release).</summary>
public class PerfTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("CLIENTSSH_PERF_TESTS") == "1";

    private static (TimeSpan Time, long Allocated, int Gen2) Measure(Action action)
    {
        GC.Collect();
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var gen2 = GC.CollectionCount(2);
        var clock = Stopwatch.StartNew();
        action();
        return (clock.Elapsed, GC.GetTotalAllocatedBytes(precise: true) - allocated, GC.CollectionCount(2) - gen2);
    }

    [Fact]
    public void Terminal_bulk_output()
    {
        if (!Enabled) return;

        // Like `cat` of a big coloured log: 200k lines, so the 5000-line scrollback is full for most of it.
        var text = new StringBuilder();
        for (var i = 0; i < 200_000; i++)
            text.Append("\x1b[32m").Append(i.ToString("D8")).Append("\x1b[0m some log text that fills most of a line ").Append('x', 30).Append("\r\n");
        var all = text.ToString();
        var screen = new VtScreen(120, 40);

        var (time, allocated, gen2) = Measure(() =>
        {
            for (var i = 0; i < all.Length; i += 8192) screen.Feed(all.AsSpan(i, Math.Min(8192, all.Length - i)));
        });
        output.WriteLine($"terminal: {all.Length / 1_000_000.0:0.0} M chars in {time.TotalMilliseconds:0} ms " +
                         $"({all.Length / time.TotalSeconds / 1_000_000:0.0} M chars/s), allocated {allocated / 1_000_000.0:0.0} MB, gen2 {gen2}");
        Assert.Equal(5000, screen.ScrollbackCount);
    }

    private static byte[] Picture(int width, int height, int frame)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            var inBlock = x >= frame * 8 % (width - 200) && x < frame * 8 % (width - 200) + 200 && y >= 300 && y < 500;
            bgra[i] = (byte)(inBlock ? 30 : x * 255 / width);
            bgra[i + 1] = (byte)(inBlock ? 200 : y * 255 / height);
            bgra[i + 2] = (byte)(inBlock ? 90 : (x / 64 + y / 64) % 2 * 200);
            bgra[i + 3] = 255;
        }
        return bgra;
    }

    // As ScreenSource and RemoteViewer use them: one NV12 buffer for the stream, a shown picture decoded into again.
    [Fact]
    public unsafe void Video_encode_and_decode_full_hd()
    {
        if (!Enabled) return;

        const int width = 1920, height = 1080, frames = 120;
        var raw = Enumerable.Range(0, 8).Select(f => Picture(width, height, f)).ToArray();
        using var encoder = new H264Encoder(width, height, 60, 8_000_000);
        using var decoder = new H264Decoder();
        var units = new List<byte[]>();
        var nv12 = new byte[width * height * 3 / 2];

        var encode = Measure(() =>
        {
            for (var f = 0; f < frames; f++)
            {
                fixed (byte* bgra = raw[f % raw.Length]) Yuv.BgraToNv12(bgra, width, height, width * 4, nv12);
                var unit = encoder.Encode(nv12, keyFrame: f == 0);
                if (unit is not null) units.Add(unit);
            }
        });
        var decoded = 0;
        var decode = Measure(() =>
        {
            byte[]? shown = null;
            foreach (var unit in units) if ((shown = decoder.Decode(unit, width, height, shown) ?? shown) is not null) decoded++;
        });

        output.WriteLine($"{units.Count} units, {units.Sum(u => u.Length) / 1024} KB");
        output.WriteLine($"convert+encode: {encode.Time.TotalMilliseconds / frames:0.0} ms/frame, allocated {encode.Allocated / frames / 1024} KB/frame, gen2 {encode.Gen2}");
        output.WriteLine($"decode+convert: {decode.Time.TotalMilliseconds / units.Count:0.0} ms/frame, allocated {decode.Allocated / units.Count / 1024} KB/frame, gen2 {decode.Gen2}");
        Assert.True(decoded > 0);
    }

    private sealed class BigScreen : IScreenSource
    {
        public readonly byte[] Frame = RandomNumberGenerator.GetBytes(300_000);
        public VideoFrame? Next(bool keyFrame) => new(Frame, true, 1920, 1080);
    }

    private sealed class NoSink : IInputSink
    {
        public void Apply(InputEvent input) { }
    }

    [Fact]
    public async Task Remote_stream_over_loopback()
    {
        if (!Enabled) return;

        var screen = new BigScreen();
        var options = new RemoteOptions { FramesPerSecond = 60 };
        using var hostNode = new RemoteNode(RemoteIdentity.Create(), () => screen, () => new NoSink(), options, useDht: false);
        using var viewerNode = new RemoteNode(RemoteIdentity.Create(), () => null!, () => null!, options, useDht: false);
        hostNode.SetListening(true);
        hostNode.IncomingCall += s => { s.Ring(); s.Grant(RemoteAccess.View); };

        using var viewer = viewerNode.Call(hostNode.Number);
        var received = 0;
        viewer.FrameReceived += _ => Interlocked.Increment(ref received);
        viewer.AddCandidates([new IPEndPoint(IPAddress.Loopback, hostNode.LocalPort)]);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (Volatile.Read(ref received) == 0 && DateTime.UtcNow < until) await Task.Delay(20);

        var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var start = received;
        var clock = Stopwatch.StartNew();
        await Task.Delay(3000);
        process.Refresh();
        var frames = received - start;
        var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        output.WriteLine($"remote loopback, 300 KB frames: {frames / clock.Elapsed.TotalSeconds:0} fps delivered, " +
                         $"CPU {cpuMs / Math.Max(1, frames):0.00} ms/frame, allocated {(GC.GetTotalAllocatedBytes(true) - allocated) / Math.Max(1, frames) / 1024} KB/frame");
        Assert.True(frames > 0);
    }
}
