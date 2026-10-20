using System.Text;

namespace ClientSSH.Terminal;

/// <summary>Runs a command line inside a Windows pseudo console (ConPTY).</summary>
public sealed class ConPtyProcess(string commandLine) : ITerminalProcess
{
    private PseudoConsole? _console;
    private Thread? _reader;
    private bool _disposed;

    public event Action<string>? Output;
    public event Action<int>? Exited;

    public void Start(int columns, int rows)
    {
        _console = PseudoConsole.Start(commandLine, columns, rows);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "terminal-reader" };
        _reader.Start();
        new Thread(WaitLoop) { IsBackground = true, Name = "terminal-wait" }.Start();
    }

    public void Write(string input)
    {
        var console = _console;
        if (console is null || _disposed) return;

        try
        {
            console.Input.Write(Encoding.UTF8.GetBytes(input));
            console.Input.Flush();
        }
        catch (IOException)
        {
            // The child is gone; Exited reports it.
        }
    }

    public void Resize(int columns, int rows) => _console?.Resize(columns, rows);

    private void ReadLoop()
    {
        var console = _console!;
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[8192];
        var chars = new char[8192];
        try
        {
            int read;
            while ((read = console.Output.Read(bytes, 0, bytes.Length)) > 0)
            {
                var count = decoder.GetChars(bytes, 0, read, chars, 0);
                Output?.Invoke(new string(chars, 0, count));
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // Pipe closed: the session ended.
        }
    }

    private void WaitLoop()
    {
        var console = _console!;
        int code;
        try
        {
            code = console.WaitForExit();
            // Closing the console ends the output stream, which lets the reader finish.
            console.CloseConsole();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        _reader?.Join(TimeSpan.FromSeconds(2));
        if (!_disposed) Exited?.Invoke(code);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Kill first so the wait thread unblocks, then release the pty.
        _console?.Kill();
        _console?.Dispose();
    }
}
