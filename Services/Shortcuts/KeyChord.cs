namespace ClientSSH.Services.Shortcuts;

[Flags]
public enum KeyModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

/// <summary>
/// A key plus modifiers, written like "Ctrl+Shift+T". <see cref="Key"/> is the Windows virtual-key code
/// (the same number as <c>Windows.System.VirtualKey</c>), so this file has no UI types and is unit tested.
/// </summary>
public readonly record struct KeyChord(KeyModifiers Modifiers, int Key)
{
    private static readonly Dictionary<string, int> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Space"] = 0x20, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["End"] = 0x23, ["Home"] = 0x24, ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Plus"] = 0xBB, ["Comma"] = 0xBC, ["Minus"] = 0xBD, ["Period"] = 0xBE,
    };

    /// <summary>A shortcut must not steal plain typing: it needs Ctrl or Alt, or is a function key.</summary>
    public bool IsUsable => Key != 0 && ((Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0 || IsFunctionKey(Key));

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var modifiers = KeyModifiers.None;
        int? key = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase)) modifiers |= KeyModifiers.Ctrl;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= KeyModifiers.Shift;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= KeyModifiers.Alt;
            else if (key is null && KeyCode(part) is { } code) key = code;
            else return false;
        }

        if (key is null) return false;
        chord = new KeyChord(modifiers, key.Value);
        return true;
    }

    /// <summary>The name for a virtual-key code, or null when it cannot be a shortcut key (modifiers, unknown keys).</summary>
    public static string? KeyName(int key)
    {
        if (key is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)key).ToString();
        if (IsFunctionKey(key)) return "F" + (key - 0x6F);
        foreach (var (name, code) in Named) if (code == key) return name;
        return null;
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        parts.Add(KeyName(Key) ?? "?");
        return string.Join('+', parts);
    }

    private static bool IsFunctionKey(int key) => key is >= 0x70 and <= 0x87; // F1..F24

    private static int? KeyCode(string name)
    {
        if (name.Length == 1 && char.ToUpperInvariant(name[0]) is var c && (c is >= 'A' and <= 'Z' or >= '0' and <= '9')) return c;
        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && int.TryParse(name.AsSpan(1), out var n) && n is >= 1 and <= 24) return 0x6F + n;
        return Named.TryGetValue(name, out var code) ? code : null;
    }
}
