using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ClientSSH.Terminal;

/// <summary>A Windows pseudo console (ConPTY) hosting one child process.</summary>
internal sealed partial class PseudoConsole : IDisposable
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
}
