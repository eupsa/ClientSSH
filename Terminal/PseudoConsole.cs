using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ClientSSH.Terminal;

/// <summary>A Windows pseudo console (ConPTY) hosting one child process.</summary>
internal sealed class PseudoConsole : IDisposable
{
    private IntPtr _console;
    private IntPtr _process;
    private readonly SafeFileHandle _inputWrite;
    private readonly SafeFileHandle _outputRead;
    private bool _closed;

    public FileStream Input { get; }
    public FileStream Output { get; }

    private PseudoConsole(IntPtr console, IntPtr process, SafeFileHandle inputWrite, SafeFileHandle outputRead)
    {
        _console = console;
        _process = process;
        _inputWrite = inputWrite;
        _outputRead = outputRead;
        Input = new FileStream(inputWrite, FileAccess.Write, 0, false);
        Output = new FileStream(outputRead, FileAccess.Read, 0, false);
    }

    public static PseudoConsole Start(string commandLine, int columns, int rows)
    {
        if (!CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0) ||
            !CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var hr = CreatePseudoConsole(new Coord((short)columns, (short)rows), inputRead, outputWrite, 0, out var console);
        // The pseudo console keeps its own copies of these two ends.
        inputRead.Dispose();
        outputWrite.Dispose();
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);

        var attributes = IntPtr.Zero;
        try
        {
            var size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size) ||
                !UpdateProcThreadAttribute(attributes, 0, (IntPtr)ProcThreadAttributePseudoConsole, console,
                    (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var startup = new StartupInfoEx { Attributes = attributes };
            startup.Info.cb = Marshal.SizeOf<StartupInfoEx>();

            if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                    ExtendedStartupInfoPresent, IntPtr.Zero, null, ref startup, out var process))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            CloseHandle(process.hThread);
            return new PseudoConsole(console, process.hProcess, inputWrite, outputRead);
        }
        catch
        {
            ClosePseudoConsole(console);
            inputWrite.Dispose();
            outputRead.Dispose();
            throw;
        }
        finally
        {
            if (attributes != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }
        }
    }

    public void Resize(int columns, int rows)
    {
        if (!_closed) ResizePseudoConsole(_console, new Coord((short)columns, (short)rows));
    }

    /// <summary>Blocks until the child exits and returns its exit code.</summary>
    public int WaitForExit()
    {
        WaitForSingleObject(_process, 0xFFFFFFFF);
        GetExitCodeProcess(_process, out var code);
        return (int)code;
    }

    /// <summary>Closing the console ends the output stream, which lets the reader finish.</summary>
    public void CloseConsole()
    {
        if (_closed) return;
        _closed = true;
        ClosePseudoConsole(_console);
        _console = IntPtr.Zero;
    }

    public void Kill()
    {
        if (_process != IntPtr.Zero) TerminateProcess(_process, 1);
    }

    public void Dispose()
    {
        Kill();
        CloseConsole();
        Input.Dispose();
        Output.Dispose();
        if (_process != IntPtr.Zero)
        {
            CloseHandle(_process);
            _process = IntPtr.Zero;
        }
    }

    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const int ProcThreadAttributePseudoConsole = 0x00020016;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Coord(short x, short y)
    {
        public readonly short X = x;
        public readonly short Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo Info;
        public IntPtr Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, IntPtr attributes, uint size);

    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);

    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(IntPtr console, Coord size);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr console);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? application, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string? directory,
        ref StartupInfoEx startup, out ProcessInformation process);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
