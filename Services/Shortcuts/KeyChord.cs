namespace ClientSSH.Services.Shortcuts;

/// <summary>
/// A key combination written as text, e.g. "Ctrl+Shift+1". Keys are A-Z, 0-9 and F1-F24. At least Ctrl or Alt is
/// required so a shortcut never swallows plain typing in the terminal.
/// </summary>
public sealed record KeyChord(bool Ctrl, bool Shift, bool Alt, string Key)
{
    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool ctrl = false, shift = false, alt = false;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": ctrl = true; break;
                case "SHIFT": shift = true; break;
                case "ALT": alt = true; break;
                case var k when key is null && IsKey(k): key = k; break;
                default: return false;
            }
        }

        if (key is null || !(ctrl || alt)) return false;
        chord = new KeyChord(ctrl, shift, alt, key);
        return true;
    }

    private static bool IsKey(string k) =>
        (k.Length == 1 && (k[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')) ||
        (k.Length is 2 or 3 && k[0] == 'F' && int.TryParse(k[1..], out var n) && n is >= 1 and <= 24);

    public override string ToString() =>
        string.Join("+", new[] { Ctrl ? "Ctrl" : null, Shift ? "Shift" : null, Alt ? "Alt" : null, Key }.Where(p => p is not null));
}
