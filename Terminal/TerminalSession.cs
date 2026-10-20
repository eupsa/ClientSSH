namespace ClientSSH.Terminal;

/// <summary>One live session: a process feeding a <see cref="VtScreen"/> that a view can draw.</summary>
public sealed class TerminalSession : IDisposable
{
    private readonly ITerminalProcess _process;

    public VtScreen Screen { get; }

    /// <summary>Raised when the screen changed. May come from any thread.</summary>
    public event Action? Updated;

    /// <summary>Raised once with the process exit code. May come from any thread.</summary>
    public event Action<int>? Exited;

    public TerminalSession(ITerminalProcess process, int columns, int rows)
    {
        _process = process;
        Screen = new VtScreen(columns, rows);
        Screen.Reply += process.Write;
        process.Output += OnOutput;
        process.Exited += code => Exited?.Invoke(code);
    }

    public void Start() => _process.Start(Screen.Cols, Screen.Rows);

    public void Write(string text) => _process.Write(text);

    public void Resize(int columns, int rows)
    {
        if (columns < 1 || rows < 1) return;
        lock (Screen.Sync) Screen.Resize(columns, rows);
        _process.Resize(columns, rows);
        Updated?.Invoke();
    }

    public void Dispose() => _process.Dispose();

    private void OnOutput(string text)
    {
        lock (Screen.Sync) Screen.Feed(text);
        Updated?.Invoke();
    }
}
