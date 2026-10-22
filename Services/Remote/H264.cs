using System.Runtime.InteropServices;

namespace ClientSSH.Services.Remote;

/// <summary>
/// H.264 through Windows Media Foundation (the encoder and decoder that ship with Windows): no extra library.
/// Synchronous MFTs only, fed NV12 frames from memory. shortcut: the Microsoft software encoder, not the GPU ones
/// (NVENC, Quick Sync, AMF are async MFTs); switch to them if a slow CPU cannot keep 1080p at 60 fps.
/// </summary>
public sealed class H264Encoder : IDisposable
{
    private readonly IMFTransform _mft;
    private readonly ICodecAPI? _codec;
    private readonly int _width, _height, _fps;
    private readonly MF.OutputStreamInfo _info;
    private IMFSample? _output;
    private long _frame;

    public int Width => _width;
    public int Height => _height;

    public H264Encoder(int width, int height, int fps, int bitrate)
    {
        MF.Startup();
        (_width, _height, _fps) = (width, height, fps);
        _mft = MF.Create(MF.CLSID_MSH264EncoderMFT);
        _codec = _mft as ICodecAPI;

        // Low latency and a steady rate: one picture in, one picture out, nothing held back.
        SetCodec(MF.CODECAPI_AVLowLatencyMode, true);
        SetCodec(MF.CODECAPI_AVEncCommonRateControlMode, 0u); // CBR
        SetCodec(MF.CODECAPI_AVEncCommonMeanBitRate, (uint)bitrate);
        SetCodec(MF.CODECAPI_AVEncCommonQualityVsSpeed, 0u); // fastest
        SetCodec(MF.CODECAPI_AVEncMPVGOPSize, (uint)(fps * 2)); // a key frame every 2 s even if no loss is reported

        // The encoder wants its output type before the input type.
        var output = MF.VideoType(MF.MFVideoFormat_H264, width, height, fps);
        output.SetUINT32(MF.MF_MT_AVG_BITRATE, (uint)bitrate);
        output.SetUINT32(MF.MF_MT_MPEG2_PROFILE, 66); // baseline: no B-frames, decodes in order
        MF.Check(_mft.SetOutputType(0, output, 0));
        MF.Check(_mft.SetInputType(0, MF.VideoType(MF.MFVideoFormat_NV12, width, height, fps), 0));

        MF.Check(_mft.GetOutputStreamInfo(0, out _info));
        _mft.ProcessMessage(MF.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0);
        _mft.ProcessMessage(MF.MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
    }

    private void SetCodec(Guid api, object value)
    {
        if (_codec is null) return;
        try { _codec.SetValue(ref api, ref value); }
        catch (COMException) { /* an encoder that lacks one knob still works */ }
    }

    /// <summary>One NV12 frame in, one H.264 access unit out (null while the encoder still holds it).</summary>
    public byte[]? Encode(byte[] nv12, bool keyFrame)
    {
        if (keyFrame) SetCodec(MF.CODECAPI_AVEncVideoForceKeyFrame, 1u);

        var sample = MF.SampleFrom(nv12, nv12.Length);
        var duration = 10_000_000L / _fps;
        sample.SetSampleTime(_frame * duration);
        sample.SetSampleDuration(duration);
        _frame++;

        var result = MF.Check(_mft.ProcessInput(0, sample, 0), MF.MF_E_NOTACCEPTING);
        Marshal.ReleaseComObject(sample);

        byte[]? output = null;
        while (MF.TryProcessOutput(_mft, _info, ref _output, null, out var bytes, out var length, out _))
            output = output is null ? bytes : [.. output, .. bytes.AsSpan(0, length)];
        if (result == MF.MF_E_NOTACCEPTING) MF.Check(_mft.ProcessInput(0, MF.SampleFrom(nv12, nv12.Length), 0));
        return output;
    }

    /// <summary>True when the access unit holds an IDR picture (NAL type 5): the decoder can start there.</summary>
    public static bool IsKeyFrame(ReadOnlySpan<byte> annexB)
    {
        for (var i = 0; i + 3 < annexB.Length; i++)
        {
            if (annexB[i] == 0 && annexB[i + 1] == 0 && annexB[i + 2] == 1 && (annexB[i + 3] & 0x1F) == 5) return true;
        }
        return false;
    }

    public void Dispose()
    {
        _mft.ProcessMessage(MF.MFT_MESSAGE_NOTIFY_END_STREAMING, 0);
        MF.Release(ref _output);
        Marshal.ReleaseComObject(_mft);
    }
}

/// <summary>H.264 access units in, BGRA pictures out (see <see cref="H264Encoder"/>).</summary>
public sealed class H264Decoder : IDisposable
{
    private readonly IMFTransform _mft;
    private MF.OutputStreamInfo _info;
    private IMFSample? _output;
    private byte[]? _nv12;
    private int _width, _height;

    public H264Decoder()
    {
        MF.Startup();
        _mft = MF.Create(MF.CLSID_MSH264DecoderMFT);
        if (_mft.GetAttributes(out var attributes) == 0 && attributes is not null) attributes.SetUINT32(MF.MF_LOW_LATENCY, 1);

        var input = MF.NewType();
        input.SetGUID(MF.MF_MT_MAJOR_TYPE, MF.MFMediaType_Video);
        input.SetGUID(MF.MF_MT_SUBTYPE, MF.MFVideoFormat_H264);
        MF.Check(_mft.SetInputType(0, input, 0));
        PickNv12Output();
        _mft.ProcessMessage(MF.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0);
        _mft.ProcessMessage(MF.MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
    }

    private void PickNv12Output()
    {
        for (uint i = 0; _mft.GetOutputAvailableType(0, i, out var type) == 0; i++)
        {
            type.GetGUID(MF.MF_MT_SUBTYPE, out var subtype);
            if (subtype != MF.MFVideoFormat_NV12) continue;
            MF.Check(_mft.SetOutputType(0, type, 0));
            if (type.GetUINT64(MF.MF_MT_FRAME_SIZE, out var size) == 0) (_width, _height) = ((int)(size >> 32), (int)(size & 0xFFFFFFFF));
            break;
        }
        MF.Check(_mft.GetOutputStreamInfo(0, out _info));
    }

    /// <summary>
    /// Decodes one access unit. Returns the newest picture it produced, cropped to width x height, or null.
    /// The picture goes into <paramref name="into"/> when it has the right size, so a caller can reuse its buffers.
    /// </summary>
    public byte[]? Decode(byte[] accessUnit, int width, int height, byte[]? into = null)
    {
        var sample = MF.SampleFrom(accessUnit, accessUnit.Length);
        var result = MF.Check(_mft.ProcessInput(0, sample, 0), MF.MF_E_NOTACCEPTING);
        Marshal.ReleaseComObject(sample);

        var produced = 0;
        while (true)
        {
            if (MF.TryProcessOutput(_mft, _info, ref _output, _nv12, out var nv12, out var length, out var streamChange))
            {
                (_nv12, produced) = (nv12, length);
                continue;
            }
            if (!streamChange) break;
            MF.Release(ref _output); // its size was for the old stream
            produced = 0;
            PickNv12Output(); // the first SPS tells the decoder the real size
        }

        // Only the newest picture is converted: older ones from the same call would be overwritten anyway.
        var picture = produced > 0 && _width > 0 && _height > 0 ? Yuv.Nv12ToBgra(_nv12!, produced, _width, _height, width, height, into) : null;
        if (result == MF.MF_E_NOTACCEPTING) return Decode(accessUnit, width, height, picture ?? into) ?? picture;
        return picture;
    }

    public void Dispose()
    {
        MF.Release(ref _output);
        Marshal.ReleaseComObject(_mft);
    }
}

/// <summary>BT.601 limited range, the default of the Windows H.264 encoder and decoder.</summary>
public static class Yuv
{
    public static unsafe byte[] BgraToNv12(byte[] source, int width, int height, int stride)
    {
        fixed (byte* src = source) return BgraToNv12(src, width, height, stride);
    }

    public static unsafe byte[] BgraToNv12(byte* src, int width, int height, int stride) =>
        BgraToNv12(src, width, height, stride, new byte[width * height * 3 / 2]);

    /// <summary>Into <paramref name="nv12"/> (width * height * 3 / 2 bytes), so a stream can reuse one buffer.</summary>
    public static unsafe byte[] BgraToNv12(byte* src, int width, int height, int stride, byte[] nv12)
    {
        {
            fixed (byte* dst = nv12)
            {
                var (s0, d0) = ((nint)src, (nint)dst);
                Parallel.For(0, height / 2, pair =>
                {
                    byte* top = (byte*)s0 + pair * 2 * stride, bottom = top + stride;
                    byte* y0 = (byte*)d0 + pair * 2 * width, y1 = y0 + width, uv = (byte*)d0 + width * height + pair * width;
                    for (var x = 0; x < width; x += 2, top += 8, bottom += 8)
                    {
                        y0[x] = Luma(top); y0[x + 1] = Luma(top + 4); y1[x] = Luma(bottom); y1[x + 1] = Luma(bottom + 4);
                        int b = (top[0] + top[4] + bottom[0] + bottom[4]) >> 2;
                        int g = (top[1] + top[5] + bottom[1] + bottom[5]) >> 2;
                        int r = (top[2] + top[6] + bottom[2] + bottom[6]) >> 2;
                        uv[x] = (byte)(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
                        uv[x + 1] = (byte)(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
                    }
                });
            }
        }
        return nv12;
    }

    private static unsafe byte Luma(byte* p) => (byte)(((66 * p[2] + 129 * p[1] + 25 * p[0] + 128) >> 8) + 16);

    /// <summary>The decoder may pad (1080 becomes 1088): the buffer has its own size, the picture is cropped to width x height.</summary>
    public static byte[] Nv12ToBgra(byte[] nv12, int bufferWidth, int bufferHeight, int width, int height) =>
        Nv12ToBgra(nv12, nv12.Length, bufferWidth, bufferHeight, width, height);

    /// <summary>The first <paramref name="length"/> bytes of <paramref name="nv12"/>; into <paramref name="into"/> when it has the right size.</summary>
    public static byte[] Nv12ToBgra(byte[] nv12, int length, int bufferWidth, int bufferHeight, int width, int height, byte[]? into = null)
    {
        width = Math.Min(width, bufferWidth);
        height = Math.Min(height, bufferHeight);
        var stride = length * 2 / 3 / bufferHeight; // the decoder may also pad each row
        var bgra = into?.Length == width * height * 4 ? into : new byte[width * height * 4];
        unsafe
        {
            fixed (byte* src = nv12, dst = bgra)
            {
                var (s0, d0) = ((nint)src, (nint)dst);
                var (w, h, bh) = (width, height, bufferHeight);
                Parallel.For(0, h, row =>
                {
                    byte* y = (byte*)s0 + row * stride, uv = (byte*)s0 + stride * bh + row / 2 * stride, o = (byte*)d0 + row * w * 4;
                    for (var x = 0; x < w; x++, o += 4)
                    {
                        int c = 298 * (y[x] - 16), d = uv[x & ~1] - 128, e = uv[(x & ~1) + 1] - 128;
                        o[0] = Clip((c + 516 * d + 128) >> 8);
                        o[1] = Clip((c - 100 * d - 208 * e + 128) >> 8);
                        o[2] = Clip((c + 409 * e + 128) >> 8);
                        o[3] = 255;
                    }
                });
            }
        }
        return bgra;
    }

    private static byte Clip(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}

// ---- Media Foundation interop (only what the two classes above use) ----

internal static class MF
{
    public const int MF_E_TRANSFORM_NEED_MORE_INPUT = unchecked((int)0xC00D6D72);
    public const int MF_E_TRANSFORM_STREAM_CHANGE = unchecked((int)0xC00D6D61);
    public const int MF_E_NOTACCEPTING = unchecked((int)0xC00D36B5);
    public const uint MFT_MESSAGE_NOTIFY_BEGIN_STREAMING = 0x10000000, MFT_MESSAGE_NOTIFY_END_STREAMING = 0x10000001,
        MFT_MESSAGE_NOTIFY_START_OF_STREAM = 0x10000003;
    private const uint MFT_OUTPUT_STREAM_PROVIDES_SAMPLES = 0x100, MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES = 0x200;

    public static readonly Guid CLSID_MSH264EncoderMFT = new("6ca50344-051a-4ded-9779-a43305165e35");
    public static readonly Guid CLSID_MSH264DecoderMFT = new("62ce7e72-4c71-4d20-b15d-452831a87d9d");
    public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid MF_MT_MPEG2_PROFILE = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    public static readonly Guid MF_LOW_LATENCY = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
    public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_NV12 = new("3231564e-0000-0010-8000-00aa00389b71");
    public static readonly Guid CODECAPI_AVLowLatencyMode = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
    public static readonly Guid CODECAPI_AVEncCommonRateControlMode = new("1c0608e9-370c-4710-8a58-cb6181c42423");
    public static readonly Guid CODECAPI_AVEncCommonMeanBitRate = new("f7222374-2144-4815-b550-a37f8e12ee52");
    public static readonly Guid CODECAPI_AVEncCommonQualityVsSpeed = new("98332df8-03cd-476b-89fa-3f9e442dec9f");
    public static readonly Guid CODECAPI_AVEncMPVGOPSize = new("95f31b26-95a4-41aa-9303-246a7fc6eef1");
    public static readonly Guid CODECAPI_AVEncVideoForceKeyFrame = new("398c1b98-8353-475a-9ef2-8f265d260345");

    [StructLayout(LayoutKind.Sequential)]
    public struct OutputStreamInfo { public uint Flags, Size, Alignment; }

    [StructLayout(LayoutKind.Sequential)]
    public struct OutputDataBuffer { public uint StreamId; public IntPtr Sample; public uint Status; public IntPtr Events; }

    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateMediaType(out IMFMediaType type);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateSample(out IMFSample sample);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateMemoryBuffer(uint maxLength, out IMFMediaBuffer buffer);

    private static int _started;

    public static void Startup()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0) Check(MFStartup(0x00020070, 0)); // MF_VERSION, MFSTARTUP_FULL
    }

    public static int Check(int hr, int allowed = 0)
    {
        if (hr < 0 && hr != allowed) Marshal.ThrowExceptionForHR(hr);
        return hr;
    }

    public static IMFTransform Create(Guid clsid) =>
        (IMFTransform)(Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, throwOnError: true)!)
            ?? throw new COMException("Media Foundation transform not available."));

    public static IMFMediaType NewType()
    {
        Check(MFCreateMediaType(out var type));
        return type;
    }

    public static IMFMediaType VideoType(Guid subtype, int width, int height, int fps)
    {
        var type = NewType();
        type.SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
        type.SetGUID(MF_MT_SUBTYPE, subtype);
        type.SetUINT64(MF_MT_FRAME_SIZE, ((ulong)width << 32) | (uint)height);
        type.SetUINT64(MF_MT_FRAME_RATE, ((ulong)fps << 32) | 1);
        type.SetUINT64(MF_MT_PIXEL_ASPECT_RATIO, (1UL << 32) | 1);
        type.SetUINT32(MF_MT_INTERLACE_MODE, 2); // progressive
        return type;
    }

    public static IMFSample SampleFrom(byte[] data, int capacity)
    {
        Check(MFCreateSample(out var sample));
        Check(MFCreateMemoryBuffer((uint)Math.Max(capacity, 1), out var buffer));
        buffer.Lock(out var pointer, out _, out _);
        Marshal.Copy(data, 0, pointer, data.Length);
        buffer.Unlock();
        buffer.SetCurrentLength((uint)data.Length);
        sample.AddBuffer(buffer);
        Marshal.ReleaseComObject(buffer);
        return sample;
    }

    public static void Release(ref IMFSample? sample)
    {
        if (sample is not null) Marshal.ReleaseComObject(sample);
        sample = null;
    }

    /// <summary>
    /// One output sample's first <paramref name="length"/> bytes, in <paramref name="into"/> when it is large enough, else in a new
    /// array of exactly that length. False when the transform needs more input (or, with <paramref name="streamChange"/>, a new
    /// output type). When the transform does not provide samples, ours is created once in <paramref name="reuse"/> and kept:
    /// a fresh multi-megabyte buffer per call was most of the cost.
    /// </summary>
    public static bool TryProcessOutput(IMFTransform mft, OutputStreamInfo info, ref IMFSample? reuse, byte[]? into,
        out byte[] bytes, out int length, out bool streamChange)
    {
        bytes = [];
        length = 0;
        streamChange = false;
        var providesSamples = (info.Flags & (MFT_OUTPUT_STREAM_PROVIDES_SAMPLES | MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES)) != 0;
        var ours = providesSamples ? null : reuse ??= SampleFrom([], (int)Math.Max(info.Size, 1 << 20));

        var block = Marshal.AllocHGlobal(Marshal.SizeOf<OutputDataBuffer>());
        var data = new OutputDataBuffer { Sample = ours is null ? IntPtr.Zero : Marshal.GetIUnknownForObject(ours) };
        Marshal.StructureToPtr(data, block, false);
        try
        {
            var hr = mft.ProcessOutput(0, 1, block, out _);
            data = Marshal.PtrToStructure<OutputDataBuffer>(block);
            if (hr == MF_E_TRANSFORM_NEED_MORE_INPUT) return false;
            if (hr == MF_E_TRANSFORM_STREAM_CHANGE) { streamChange = true; return false; }
            Check(hr);

            // Our own sample comes back as the same RCW: releasing it here would break it for the next call.
            var sample = ours ?? (IMFSample)Marshal.GetObjectForIUnknown(data.Sample);
            Check(sample.ConvertToContiguousBuffer(out var buffer));
            buffer.Lock(out var pointer, out _, out var current);
            length = (int)current;
            bytes = into is not null && into.Length >= length ? into : new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            buffer.Unlock();
            if (ours is not null) buffer.SetCurrentLength(0); // empty again for the next output
            Marshal.ReleaseComObject(buffer);
            if (ours is null) Marshal.ReleaseComObject(sample);
            return true;
        }
        finally
        {
            if (data.Sample != IntPtr.Zero) Marshal.Release(data.Sample);
            if (data.Events != IntPtr.Zero) Marshal.Release(data.Events);
            Marshal.FreeHGlobal(block);
        }
    }
}

// Vtable order matters: unused slots are declared as placeholders so the used methods land in the right place.

[ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFAttributes
{
    void _GetItem(); void _GetItemType(); void _CompareItem(); void _Compare();
    [PreserveSig] int GetUINT32(in Guid key, out uint value);
    [PreserveSig] int GetUINT64(in Guid key, out ulong value);
    void _GetDouble();
    [PreserveSig] int GetGUID(in Guid key, out Guid value);
    void _GetStringLength(); void _GetString(); void _GetAllocatedString(); void _GetBlobSize(); void _GetBlob();
    void _GetAllocatedBlob(); void _GetUnknown(); void _SetItem(); void _DeleteItem(); void _DeleteAllItems();
    [PreserveSig] int SetUINT32(in Guid key, uint value);
    [PreserveSig] int SetUINT64(in Guid key, ulong value);
    void _SetDouble();
    [PreserveSig] int SetGUID(in Guid key, in Guid value);
    void _SetString(); void _SetBlob(); void _SetUnknown(); void _LockStore(); void _UnlockStore(); void _GetCount();
    void _GetItemByIndex(); void _CopyAllItems();
}

[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaType
{
    void _GetItem(); void _GetItemType(); void _CompareItem(); void _Compare();
    [PreserveSig] int GetUINT32(in Guid key, out uint value);
    [PreserveSig] int GetUINT64(in Guid key, out ulong value);
    void _GetDouble();
    [PreserveSig] int GetGUID(in Guid key, out Guid value);
    void _GetStringLength(); void _GetString(); void _GetAllocatedString(); void _GetBlobSize(); void _GetBlob();
    void _GetAllocatedBlob(); void _GetUnknown(); void _SetItem(); void _DeleteItem(); void _DeleteAllItems();
    [PreserveSig] int SetUINT32(in Guid key, uint value);
    [PreserveSig] int SetUINT64(in Guid key, ulong value);
    void _SetDouble();
    [PreserveSig] int SetGUID(in Guid key, in Guid value);
    void _SetString(); void _SetBlob(); void _SetUnknown(); void _LockStore(); void _UnlockStore(); void _GetCount();
    void _GetItemByIndex(); void _CopyAllItems();
    void _GetMajorType(); void _IsCompressedFormat(); void _IsEqual(); void _GetRepresentation(); void _FreeRepresentation();
}

[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSample
{
    void _GetItem(); void _GetItemType(); void _CompareItem(); void _Compare(); void _GetUINT32(); void _GetUINT64();
    void _GetDouble(); void _GetGUID(); void _GetStringLength(); void _GetString(); void _GetAllocatedString();
    void _GetBlobSize(); void _GetBlob(); void _GetAllocatedBlob(); void _GetUnknown(); void _SetItem(); void _DeleteItem();
    void _DeleteAllItems(); void _SetUINT32(); void _SetUINT64(); void _SetDouble(); void _SetGUID(); void _SetString();
    void _SetBlob(); void _SetUnknown(); void _LockStore(); void _UnlockStore(); void _GetCount(); void _GetItemByIndex();
    void _CopyAllItems();
    void _GetSampleFlags(); void _SetSampleFlags(); void _GetSampleTime();
    [PreserveSig] int SetSampleTime(long time);
    void _GetSampleDuration();
    [PreserveSig] int SetSampleDuration(long duration);
    void _GetBufferCount(); void _GetBufferByIndex();
    [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
}

[ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaBuffer
{
    [PreserveSig] int Lock(out IntPtr buffer, out uint maxLength, out uint currentLength);
    [PreserveSig] int Unlock();
    void _GetCurrentLength();
    [PreserveSig] int SetCurrentLength(uint length);
}

[ComImport, Guid("bf94c121-5b05-4e6f-8000-ba598961414d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFTransform
{
    void _GetStreamLimits(); void _GetStreamCount(); void _GetStreamIDs(); void _GetInputStreamInfo();
    [PreserveSig] int GetOutputStreamInfo(uint stream, out MF.OutputStreamInfo info);
    [PreserveSig] int GetAttributes(out IMFAttributes attributes);
    void _GetInputStreamAttributes(); void _GetOutputStreamAttributes(); void _DeleteInputStream(); void _AddInputStreams();
    void _GetInputAvailableType();
    [PreserveSig] int GetOutputAvailableType(uint stream, uint index, out IMFMediaType type);
    [PreserveSig] int SetInputType(uint stream, IMFMediaType type, uint flags);
    [PreserveSig] int SetOutputType(uint stream, IMFMediaType type, uint flags);
    void _GetInputCurrentType(); void _GetOutputCurrentType(); void _GetInputStatus(); void _GetOutputStatus();
    void _SetOutputBounds(); void _ProcessEvent();
    [PreserveSig] int ProcessMessage(uint message, nuint parameter);
    [PreserveSig] int ProcessInput(uint stream, IMFSample sample, uint flags);
    [PreserveSig] int ProcessOutput(uint flags, uint count, IntPtr buffers, out uint status);
}

[ComImport, Guid("901db4c7-31ce-41a2-85dc-8fa0bf41b8da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICodecAPI
{
    void _IsSupported(); void _IsModifiable(); void _GetParameterRange(); void _GetParameterValues(); void _GetDefaultValue();
    void _GetValue();
    void SetValue(ref Guid api, ref object value);
}
