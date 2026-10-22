using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClientSSH.Services.Remote;

/// <summary>
/// The main screen as H.264. The desktop comes from <see cref="DesktopDuplication"/> (GPU, fast), or from a GDI copy when
/// duplication is not available (some remote or virtual displays) or the screen is larger than Full HD (GDI scales it down).
/// The mouse pointer is drawn in, the picture becomes NV12 and goes through <see cref="H264Encoder"/>. Nothing is encoded when
/// neither the picture nor the pointer changed, unless a key frame was asked for.
/// shortcut: main monitor only.
/// </summary>
public sealed unsafe class ScreenSource(int fps = 60, int bitrate = 8_000_000) : IScreenSource, IDisposable
{
    private const int MaxWidth = 1920, MaxHeight = 1080;

    private H264Encoder? _encoder;
    private DesktopDuplication? _duplication;
    private bool _duplicationFailed;
    private byte[] _desktop = [];
    private IntPtr _memoryDc, _bitmap, _bits;
    private int _width, _height;
    private (int X, int Y, IntPtr Shape) _lastPointer;

    /// <summary>Milliseconds of the last frame per stage: capture, color conversion, encoding.</summary>
    public (double Capture, double Convert, double Encode) Timings { get; private set; }

    /// <summary>True while the GPU path is in use.</summary>
    public bool UsesDuplication => _duplication is not null;

    public VideoFrame? Next(bool keyFrame)
    {
        int screenWidth = GetSystemMetrics(0), screenHeight = GetSystemMetrics(1);
        if (screenWidth <= 0 || screenHeight <= 0) return null;

        var scale = Math.Min(1.0, Math.Min((double)MaxWidth / screenWidth, (double)MaxHeight / screenHeight));
        int width = (int)(screenWidth * scale) & ~1, height = (int)(screenHeight * scale) & ~1;
        if (width != _width || height != _height || _encoder is null)
        {
            Reset(width, height);
            keyFrame = true;
        }

        var clock = Stopwatch.StartNew();
        bool changed;
        if (scale == 1.0 && TryDuplication(width, height, out var duplicated))
        {
            changed = duplicated;
            fixed (byte* desktop = _desktop) Buffer.MemoryCopy(desktop, (void*)_bits, _desktop.Length, _desktop.Length);
        }
        else
        {
            CopyWithGdi(width, height, screenWidth, screenHeight);
            changed = true;
        }

        var pointer = DrawPointer(scale);
        changed |= pointer != _lastPointer;
        _lastPointer = pointer;
        if (!changed && !keyFrame) return null;

        var capture = clock.Elapsed.TotalMilliseconds;
        var nv12 = Yuv.BgraToNv12((byte*)_bits, width, height, width * 4);
        var convert = clock.Elapsed.TotalMilliseconds;
        var data = _encoder!.Encode(nv12, keyFrame);
        Timings = (capture, convert - capture, clock.Elapsed.TotalMilliseconds - convert);
        return data is null ? null : new VideoFrame(data, H264Encoder.IsKeyFrame(data), width, height);
    }

    // Keeps the last desktop picture in _desktop (duplication only reports changes). False: nothing new.
    private bool TryDuplication(int width, int height, out bool changed)
    {
        changed = false;
        if (_duplicationFailed) return false;
        try
        {
            if (_duplication is null)
            {
                _duplication = new DesktopDuplication();
                if (_duplication.Width != width || _duplication.Height != height) throw new InvalidOperationException("Screen size changed.");
                changed = true; // the first picture may still be empty: send the GDI copy below until duplication delivers
                CopyWithGdi(width, height, width, height);
                Marshal.Copy(_bits, _desktop, 0, _desktop.Length);
            }
            fixed (byte* desktop = _desktop) changed |= _duplication.TryCapture(desktop);
            return true;
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or InvalidCastException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Lost (desktop switch, UAC, resolution change): try again next time. Not supported at all: stay on GDI.
            var lost = e is COMException { HResult: unchecked((int)0x887A0026) /* ACCESS_LOST */ } or InvalidOperationException;
            _duplication?.Dispose();
            _duplication = null;
            _duplicationFailed = !lost;
            return false;
        }
    }

    private void CopyWithGdi(int width, int height, int screenWidth, int screenHeight)
    {
        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            if (width == screenWidth && height == screenHeight) BitBlt(_memoryDc, 0, 0, width, height, screenDc, 0, 0, SRCCOPY);
            else
            {
                SetStretchBltMode(_memoryDc, HALFTONE);
                StretchBlt(_memoryDc, 0, 0, width, height, screenDc, 0, 0, screenWidth, screenHeight, SRCCOPY);
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private (int X, int Y, IntPtr Shape) DrawPointer(double scale)
    {
        var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref info) || (info.flags & CURSOR_SHOWING) == 0) return default;
        if (!GetIconInfo(info.hCursor, out var icon)) return default;
        if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);
        if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
        DrawIconEx(_memoryDc, (int)((info.ptScreenPos.X - icon.xHotspot) * scale), (int)((info.ptScreenPos.Y - icon.yHotspot) * scale),
            info.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
        return (info.ptScreenPos.X, info.ptScreenPos.Y, info.hCursor);
    }

    private void Reset(int width, int height)
    {
        ReleaseBuffers();
        _encoder?.Dispose();
        _duplication?.Dispose();
        _duplication = null;
        (_width, _height) = (width, height);
        _desktop = new byte[width * height * 4];

        var screenDc = GetDC(IntPtr.Zero);
        _memoryDc = CreateCompatibleDC(screenDc);
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = width, biHeight = -height, // negative: top-down rows
            biPlanes = 1, biBitCount = 32,
        };
        _bitmap = CreateDIBSection(screenDc, ref header, 0, out _bits, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, screenDc);
        SelectObject(_memoryDc, _bitmap);
        _encoder = new H264Encoder(width, height, fps, bitrate);
    }

    private void ReleaseBuffers()
    {
        if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap);
        if (_memoryDc != IntPtr.Zero) DeleteDC(_memoryDc);
        _bitmap = _memoryDc = _bits = IntPtr.Zero;
    }

    public void Dispose()
    {
        ReleaseBuffers();
        _encoder?.Dispose();
        _encoder = null;
        _duplication?.Dispose();
        _duplication = null;
    }

    private const int SRCCOPY = 0x00CC0020, HALFTONE = 4, CURSOR_SHOWING = 1, DI_NORMAL = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public POINT ptScreenPos; }
    [StructLayout(LayoutKind.Sequential)] private struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO info);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int cx, int cy, int step, IntPtr brush, int flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
}
