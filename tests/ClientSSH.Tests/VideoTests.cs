using System.Diagnostics;
using ClientSSH.Services.Remote;
using Xunit;
using Xunit.Abstractions;

namespace ClientSSH.Tests;

public class VideoTests(ITestOutputHelper output)
{
    // A desktop-like test picture: flat areas, sharp edges, and a block that moves every frame.
    private static byte[] Picture(int width, int height, int frame)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                var inBlock = x >= frame * 8 % (width - 200) && x < frame * 8 % (width - 200) + 200 && y >= 300 && y < 500;
                bgra[i] = (byte)(inBlock ? 30 : x * 255 / width);
                bgra[i + 1] = (byte)(inBlock ? 200 : y * 255 / height);
                bgra[i + 2] = (byte)(inBlock ? 90 : (x / 64 + y / 64) % 2 * 200);
                bgra[i + 3] = 255;
            }
        }
        return bgra;
    }

    [Fact]
    public void Nv12_round_trip_keeps_colors_close()
    {
        const int width = 64, height = 32;
        var bgra = Picture(width, height, 0);
        var back = Yuv.Nv12ToBgra(Yuv.BgraToNv12(bgra, width, height, width * 4), width, height, width, height);

        var error = bgra.Zip(back).Select(p => Math.Abs(p.First - p.Second)).Average();
        Assert.True(error < 12, $"mean error {error}"); // chroma is shared by 2x2 pixels, so edges blur a little
    }

    [Fact]
    public void Full_hd_encodes_decodes_and_starts_with_a_key_frame()
    {
        const int width = 1920, height = 1080, frames = 60;
        using var encoder = new H264Encoder(width, height, 60, 8_000_000);
        using var decoder = new H264Decoder();

        var raw = Enumerable.Range(0, frames).Select(f => Picture(width, height, f)).ToList();
        var clock = Stopwatch.StartNew();
        var pictures = raw.Select(r => Yuv.BgraToNv12(r, width, height, width * 4)).ToList();
        var convert = clock.Elapsed;

        clock.Restart();
        var encoded = new List<byte[]>();
        for (var f = 0; f < frames; f++)
        {
            var unit = encoder.Encode(pictures[f], keyFrame: f == 0);
            if (unit is not null) encoded.Add(unit);
        }
        var encode = clock.Elapsed;

        Assert.NotEmpty(encoded);
        Assert.True(H264Encoder.IsKeyFrame(encoded[0]), "the first picture must be a key frame");

        clock.Restart();
        byte[]? last = null;
        foreach (var unit in encoded) last = decoder.Decode(unit, width, height) ?? last;
        var decode = clock.Elapsed;

        Assert.NotNull(last);
        Assert.Equal(width * height * 4, last!.Length);

        var bytes = encoded.Sum(e => e.Length);
        output.WriteLine($"{encoded.Count} units, {bytes / 1024} KB ({bytes * 8.0 / 1_000_000:0.0} Mbit for 1 s), key {encoded[0].Length / 1024} KB");
        output.WriteLine($"per frame: convert {convert.TotalMilliseconds / frames:0.0} ms, encode {encode.TotalMilliseconds / frames:0.0} ms, decode {decode.TotalMilliseconds / encoded.Count:0.0} ms");
    }

    [Fact]
    public void Decoding_into_a_reused_buffer_gives_the_same_pictures()
    {
        const int width = 640, height = 360;
        using var encoder = new H264Encoder(width, height, 60, 2_000_000);
        using var fresh = new H264Decoder();
        using var reusing = new H264Decoder();
        byte[]? shown = null;
        var compared = 0;
        for (var f = 0; f < 20; f++)
        {
            var unit = encoder.Encode(Yuv.BgraToNv12(Picture(width, height, f), width, height, width * 4), keyFrame: f == 0);
            if (unit is null) continue;
            var expected = fresh.Decode(unit, width, height);
            var previous = shown;
            var actual = reusing.Decode(unit, width, height, shown);
            if (expected is null) continue;
            Assert.Equal(expected, actual);
            if (previous is not null) Assert.Same(previous, actual);
            shown = actual;
            compared++;
        }
        Assert.True(compared > 10);
    }

    [Fact]
    public void Forcing_a_key_frame_mid_stream_works()
    {
        const int width = 640, height = 360;
        using var encoder = new H264Encoder(width, height, 60, 2_000_000);
        var units = Enumerable.Range(0, 20)
            .Select(f => encoder.Encode(Yuv.BgraToNv12(Picture(width, height, f), width, height, width * 4), keyFrame: f is 0 or 10))
            .Where(u => u is not null).ToList();

        Assert.True(units.Count(u => H264Encoder.IsKeyFrame(u!)) >= 2);
    }

    /// <summary>Captures this PC's screen in memory only (nothing is saved or sent). Opt in with CLIENTSSH_PERF_TESTS=1.</summary>
    [Fact]
    public void Screen_capture_and_encode_speed()
    {
        if (Environment.GetEnvironmentVariable("CLIENTSSH_PERF_TESTS") != "1") return;

        using var source = new ScreenSource();
        source.Next(keyFrame: true);
        var clock = Stopwatch.StartNew();
        var produced = 0;
        for (var i = 0; i < 60; i++) if (source.Next(keyFrame: false) is not null) produced++;
        output.WriteLine($"last frame: capture {source.Timings.Capture:0.0} ms, convert {source.Timings.Convert:0.0} ms, encode {source.Timings.Encode:0.0} ms");
        output.WriteLine($"capture+convert+encode: {clock.Elapsed.TotalMilliseconds / 60:0.0} ms per frame ({60 / clock.Elapsed.TotalSeconds:0} fps max), {produced} frames");
    }
}
