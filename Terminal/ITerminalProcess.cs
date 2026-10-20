namespace ClientSSH.Terminal;

/// <summary>
/// What a terminal session talks to: a real child process in a pseudo console, or the tour's simulated shell.
/// Output is raw VT text for <see cref="VtScreen"/>. Events may be raised on any thread.
/// </summary>
public interface ITerminalProcess : IDisposable
{
    event Action<string>? Output;

    /// <summary>Raised once, after all output was delivered, with the exit code.</summary>
    event Action<int>? Exited;

    /// <summary>Starts the process. Subscribe to the events first so no output is lost.</summary>
    void Start(int columns, int rows);

    void Write(string input);

    void Resize(int columns, int rows);
}
