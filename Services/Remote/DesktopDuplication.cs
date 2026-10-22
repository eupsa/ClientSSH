using System.Runtime.InteropServices;

namespace ClientSSH.Services.Remote;

/// <summary>
/// The main monitor through DXGI Desktop Duplication: the GPU hands over the composed desktop in a millisecond or two, where a
/// GDI copy of a Full HD screen takes about 15. Raw COM vtable calls (no wrapper library); only the slots used are named.
/// Returns false from <see cref="TryCapture"/> when nothing changed; throws when duplication is lost or unsupported, so the
/// caller can recreate it or fall back to GDI.
/// </summary>
public sealed unsafe class DesktopDuplication : IDisposable
{
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87, D3D11_USAGE_STAGING = 3, D3D11_CPU_ACCESS_READ = 0x20000, D3D11_MAP_READ = 1;
    private const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IID_IDXGIOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
    private static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    private IntPtr _device, _context, _duplication, _staging;

    public int Width { get; }
    public int Height { get; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Texture2DDesc
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Mapped { public IntPtr Data; public uint RowPitch, DepthPitch; }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr levels, uint levelCount,
        uint sdkVersion, out IntPtr device, out int level, out IntPtr context);

    public DesktopDuplication()
    {
        Check(D3D11CreateDevice(IntPtr.Zero, 1 /* hardware */, IntPtr.Zero, 0x20 /* BGRA support */, IntPtr.Zero, 0, 7, out _device, out _, out _context));

        var dxgiDevice = QueryInterface(_device, IID_IDXGIDevice);
        try
        {
            IntPtr adapter, output;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(dxgiDevice, 7))(dxgiDevice, &adapter)); // IDXGIDevice::GetAdapter
            try { Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(adapter, 7))(adapter, 0, &output)); } // EnumOutputs(0)
            finally { Marshal.Release(adapter); }

            var output1 = QueryInterface(output, IID_IDXGIOutput1);
            Marshal.Release(output);
            try
            {
                IntPtr duplication;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(output1, 22))(output1, _device, &duplication)); // DuplicateOutput
                _duplication = duplication;
            }
            finally { Marshal.Release(output1); }
        }
        finally { Marshal.Release(dxgiDevice); }

        // DXGI_OUTDUPL_DESC starts with DXGI_MODE_DESC: Width, Height.
        var description = stackalloc byte[64];
        ((delegate* unmanaged[Stdcall]<IntPtr, byte*, void>)Slot(_duplication, 7))(_duplication, description); // GetDesc
        Width = *(int*)description;
        Height = *(int*)(description + 4);

        var desc = new Texture2DDesc
        {
            Width = (uint)Width, Height = (uint)Height, MipLevels = 1, ArraySize = 1, Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleCount = 1, Usage = D3D11_USAGE_STAGING, CpuAccessFlags = D3D11_CPU_ACCESS_READ,
        };
        IntPtr staging;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Texture2DDesc*, IntPtr, IntPtr*, int>)Slot(_device, 5))(_device, &desc, IntPtr.Zero, &staging)); // CreateTexture2D
        _staging = staging;
    }

    /// <summary>Copies the newest desktop image into <paramref name="target"/> (BGRA rows of Width*4). False when nothing changed.</summary>
    public bool TryCapture(byte* target)
    {
        var frameInfo = stackalloc byte[64];
        IntPtr resource;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, byte*, IntPtr*, int>)Slot(_duplication, 8))(_duplication, 0, frameInfo, &resource); // AcquireNextFrame
        if (hr == DXGI_ERROR_WAIT_TIMEOUT) return false;
        Check(hr);

        try
        {
            if (*(long*)frameInfo == 0) return false; // LastPresentTime 0: only the pointer moved, the picture is the same

            var texture = QueryInterface(resource, IID_ID3D11Texture2D);
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)Slot(_context, 47))(_context, _staging, texture); // CopyResource
            Marshal.Release(texture);

            Mapped mapped;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, Mapped*, int>)Slot(_context, 14))(_context, _staging, 0, D3D11_MAP_READ, 0, &mapped)); // Map
            try
            {
                var row = Width * 4;
                for (var y = 0; y < Height; y++)
                    Buffer.MemoryCopy((byte*)mapped.Data + y * mapped.RowPitch, target + y * row, row, row);
            }
            finally
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)Slot(_context, 15))(_context, _staging, 0); // Unmap
            }
            return true;
        }
        finally
        {
            Marshal.Release(resource);
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(_duplication, 14))(_duplication); // ReleaseFrame
        }
    }

    private static IntPtr Slot(IntPtr comObject, int index) => (*(IntPtr**)comObject)[index];

    private static IntPtr QueryInterface(IntPtr comObject, Guid iid)
    {
        Check(Marshal.QueryInterface(comObject, in iid, out var result));
        return result;
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    public void Dispose()
    {
        foreach (var pointer in new[] { _staging, _duplication, _context, _device }) if (pointer != IntPtr.Zero) Marshal.Release(pointer);
        _staging = _duplication = _context = _device = IntPtr.Zero;
    }
}
