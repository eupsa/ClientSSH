using Windows.System;

namespace ClientSSH.Views;

/// <summary>The bytes an xterm-compatible terminal sends for keys that produce no character.</summary>
internal static class TerminalKeys
{
    /// <param name="appCursorKeys">The application asked for SS3 arrows (DECCKM), as vim and less do.</param>
    public static string? Sequence(VirtualKey key, bool ctrl, bool shift, bool appCursorKeys) => key switch
    {
        VirtualKey.Enter => "\r",
        VirtualKey.Back => "\x7f",
        VirtualKey.Tab => shift ? "\x1b[Z" : "\t",
        VirtualKey.Escape => "\x1b",
        VirtualKey.Up => appCursorKeys ? "\x1bOA" : "\x1b[A",
        VirtualKey.Down => appCursorKeys ? "\x1bOB" : "\x1b[B",
        VirtualKey.Right => appCursorKeys ? "\x1bOC" : "\x1b[C",
        VirtualKey.Left => appCursorKeys ? "\x1bOD" : "\x1b[D",
        VirtualKey.Home => "\x1b[H",
        VirtualKey.End => "\x1b[F",
        VirtualKey.Insert => "\x1b[2~",
        VirtualKey.Delete => "\x1b[3~",
        VirtualKey.PageUp => "\x1b[5~",
        VirtualKey.PageDown => "\x1b[6~",
        VirtualKey.F1 => "\x1bOP",
        VirtualKey.F2 => "\x1bOQ",
        VirtualKey.F3 => "\x1bOR",
        VirtualKey.F4 => "\x1bOS",
        VirtualKey.F5 => "\x1b[15~",
        VirtualKey.F6 => "\x1b[17~",
        VirtualKey.F7 => "\x1b[18~",
        VirtualKey.F8 => "\x1b[19~",
        VirtualKey.F9 => "\x1b[20~",
        VirtualKey.F10 => "\x1b[21~",
        VirtualKey.F11 => "\x1b[23~",
        VirtualKey.F12 => "\x1b[24~",
        >= VirtualKey.A and <= VirtualKey.Z when ctrl => ((char)(key - VirtualKey.A + 1)).ToString(),
        _ => null,
    };
}
