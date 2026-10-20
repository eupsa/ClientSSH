using System.Text;

namespace ClientSSH.Terminal;

/// <summary>
/// A pretend shell for the guided tour: line editing, a few commands, no process and no network.
/// It speaks VT like a real host, so the tour exercises the same terminal view.
/// </summary>
public sealed class SimulatedProcess(string banner, string help, string notFound) : ITerminalProcess
{
    private const string Prompt = "\x1b[32mdemo@demo\x1b[0m:\x1b[34m~\x1b[0m$ ";

    private enum Escape { None, Start, Sequence }

    private readonly StringBuilder _line = new();
    private Escape _escape;

    public event Action<string>? Output;
    public event Action<int>? Exited;

    public void Start(int columns, int rows) => Output?.Invoke($"{banner}\r\n{Prompt}");

    public void Resize(int columns, int rows)
    {
        // Nothing to resize.
    }

    public void Write(string input)
    {
        foreach (var c in input) Handle(c);
    }

    public void Dispose()
    {
        // Nothing to release.
    }

    private void Handle(char c)
    {
        // Arrow keys and friends arrive as ESC [ ... final (or ESC O final): swallow them.
        if (_escape == Escape.Start)
        {
            _escape = c is '[' or 'O' ? Escape.Sequence : Escape.None;
            return;
        }

        if (_escape == Escape.Sequence)
        {
            if (c is >= '@' and <= '~') _escape = Escape.None;
            return;
        }

        switch (c)
        {
            case '\x1b':
                _escape = Escape.Start;
                break;
            case '\r' or '\n':
                Output?.Invoke("\r\n");
                Execute(_line.ToString().Trim());
                break;
            case '\b' or '\x7f':
                if (_line.Length > 0)
                {
                    _line.Length--;
                    Output?.Invoke("\b \b");
                }
                break;
            case '\x03':
                _line.Clear();
                Output?.Invoke($"^C\r\n{Prompt}");
                break;
            case >= ' ':
                _line.Append(c);
                Output?.Invoke(c.ToString());
                break;
        }
    }

    private void Execute(string command)
    {
        _line.Clear();
        var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var name = parts.Length == 0 ? "" : parts[0];

        switch (name)
        {
            case "":
                break;
            case "exit":
                Output?.Invoke("logout\r\n");
                Exited?.Invoke(0);
                return;
            case "clear":
                Output?.Invoke("\x1b[2J\x1b[H");
                break;
            default:
                Output?.Invoke(Run(name, parts).Replace("\n", "\r\n") + "\r\n");
                break;
        }

        Output?.Invoke(Prompt);
    }

    private string Run(string name, string[] parts) => name switch
    {
        "help" => help,
        "ls" => "Documents  Downloads  projects  notes.txt",
        "pwd" => "/home/demo",
        "whoami" or "hostname" => "demo",
        "echo" => parts.Length > 1 ? parts[1] : "",
        _ => $"{name}: {notFound}",
    };
}
