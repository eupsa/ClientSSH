using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ClientSSH.Services.Remote;

/// <summary>
/// Captures the main screen with GDI and returns it as a JPEG, scaled down so a frame stays small enough for a
/// home connection. shortcut: main monitor only, no mouse pointer in the picture; add Desktop Duplication later.
/// </summary>
public sealed class GdiScreenSource : IScreenSource
{
    private const int MaxWidth = 1280;
    private const long Quality = 55;

    private static readonly ImageCodecInfo Jpeg = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);

    private byte[]? _last;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    /// <summary>Null when the screen did not change since the last call (nothing to send).</summary>
    public byte[]? CaptureJpeg()
    {
        int width = GetSystemMetrics(0), height = GetSystemMetrics(1);
        if (width <= 0 || height <= 0) return null;

        using var screen = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(screen)) graphics.CopyFromScreen(0, 0, 0, 0, new Size(width, height));

        var scale = Math.Min(1.0, (double)MaxWidth / width);
        using var scaled = new Bitmap((int)(width * scale), (int)(height * scale), PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(screen, 0, 0, scaled.Width, scaled.Height);
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, Quality);
        using var stream = new MemoryStream();
        scaled.Save(stream, Jpeg, parameters);

        var bytes = stream.ToArray();
        if (_last is not null && _last.AsSpan().SequenceEqual(bytes)) return null;
        _last = bytes;
        return bytes;
    }
}
