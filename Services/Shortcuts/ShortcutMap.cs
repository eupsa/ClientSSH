namespace ClientSSH.Services.Shortcuts;

/// <summary>Actions on the sessions page that have a keyboard shortcut. The names are the keys in settings.json.</summary>
public enum ShortcutAction
{
    NewTab,
    CloseTab,
    NextTab,
    PreviousTab,
    SplitVertical,
    SplitHorizontal,
    ClosePane,
    FocusNextPane,
    RenameTab,
}

/// <summary>
/// Default shortcuts plus the user's overrides (<c>AppSettings.Shortcuts</c>: action name to chord text).
/// Unknown actions and unreadable chords in the overrides are ignored, so an old or hand-edited file still loads.
/// </summary>
public sealed class ShortcutMap
{
    // Ctrl+Shift so plain Ctrl+letter keeps reaching the shell (Ctrl+C, Ctrl+R...). Avoids Ctrl+Shift+V (paste) and P (palette).
    public static readonly IReadOnlyDictionary<ShortcutAction, KeyChord> Defaults = new Dictionary<ShortcutAction, KeyChord>
    {
        [ShortcutAction.NewTab] = Parse("Ctrl+Shift+T"),
        [ShortcutAction.CloseTab] = Parse("Ctrl+Shift+W"),
        [ShortcutAction.NextTab] = Parse("Ctrl+Tab"),
        [ShortcutAction.PreviousTab] = Parse("Ctrl+Shift+Tab"),
        [ShortcutAction.SplitVertical] = Parse("Ctrl+Shift+E"),
        [ShortcutAction.SplitHorizontal] = Parse("Ctrl+Shift+O"),
        [ShortcutAction.ClosePane] = Parse("Ctrl+Shift+X"),
        [ShortcutAction.FocusNextPane] = Parse("Ctrl+Shift+N"),
        [ShortcutAction.RenameTab] = Parse("Ctrl+Shift+R"),
    };

    private readonly Dictionary<ShortcutAction, KeyChord> _chords;

    public ShortcutMap(IReadOnlyDictionary<string, string>? overrides = null)
    {
        _chords = new(Defaults);
        if (overrides is null) return;
        foreach (var (name, text) in overrides)
            if (Enum.TryParse<ShortcutAction>(name, out var action) && action.ToString() == name && // not "5"
                Enum.IsDefined(action) &&
                KeyChord.TryParse(text, out var chord) && chord.IsUsable)
                _chords[action] = chord;
    }

    public KeyChord this[ShortcutAction action] => _chords[action];

    /// <summary>The action bound to the pressed chord, if any. With duplicates, the first action in the enum wins.</summary>
    public ShortcutAction? Match(KeyChord pressed)
    {
        foreach (var action in Enum.GetValues<ShortcutAction>())
            if (_chords[action] == pressed) return action;
        return null;
    }

    /// <summary>Only the chords that differ from the defaults, for saving.</summary>
    public static Dictionary<string, string> Overrides(IReadOnlyDictionary<ShortcutAction, KeyChord> chords) =>
        chords.Where(p => !Defaults.TryGetValue(p.Key, out var d) || d != p.Value)
              .ToDictionary(p => p.Key.ToString(), p => p.Value.ToString());

    private static KeyChord Parse(string text) => KeyChord.TryParse(text, out var chord) ? chord : throw new FormatException(text);
}
