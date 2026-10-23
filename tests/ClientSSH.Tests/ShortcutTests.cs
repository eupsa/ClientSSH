using ClientSSH.Services.Shortcuts;
using Xunit;

namespace ClientSSH.Tests;

public class ShortcutTests
{
    [Theory]
    [InlineData("Ctrl+Shift+T", KeyModifiers.Ctrl | KeyModifiers.Shift, 'T', "Ctrl+Shift+T")]
    [InlineData(" shift + ctrl + t ", KeyModifiers.Ctrl | KeyModifiers.Shift, 'T', "Ctrl+Shift+T")]
    [InlineData("Ctrl+Tab", KeyModifiers.Ctrl, 0x09, "Ctrl+Tab")]
    [InlineData("Alt+Shift+Minus", KeyModifiers.Shift | KeyModifiers.Alt, 0xBD, "Shift+Alt+Minus")]
    [InlineData("Control+5", KeyModifiers.Ctrl, '5', "Ctrl+5")]
    [InlineData("F2", KeyModifiers.None, 0x71, "F2")]
    [InlineData("ctrl+f12", KeyModifiers.Ctrl, 0x7B, "Ctrl+F12")]
    [InlineData("Ctrl+PageDown", KeyModifiers.Ctrl, 0x22, "Ctrl+PageDown")]
    public void Parses_and_formats(string text, KeyModifiers modifiers, int key, string canonical)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(new KeyChord(modifiers, key), chord);
        Assert.Equal(canonical, chord.ToString());
        Assert.True(KeyChord.TryParse(chord.ToString(), out var again));
        Assert.Equal(chord, again);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+T+U")]
    [InlineData("Ctrl+Banana")]
    [InlineData("F25")]
    [InlineData("Ctrl++")]
    public void Rejects_bad_text(string? text) => Assert.False(KeyChord.TryParse(text, out _));

    [Theory]
    [InlineData("T", false)]
    [InlineData("Shift+T", false)]
    [InlineData("Ctrl+T", true)]
    [InlineData("Alt+T", true)]
    [InlineData("F5", true)]
    public void Usable_chords_never_steal_plain_typing(string text, bool usable)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(usable, chord.IsUsable);
    }

    [Fact]
    public void KeyName_skips_modifier_keys() => Assert.Null(KeyChord.KeyName(0x10)); // Shift itself

    [Fact]
    public void Defaults_match_and_are_unique()
    {
        var map = new ShortcutMap();
        Assert.Equal(ShortcutAction.NewTab, map.Match(new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, 'T')));
        Assert.Equal(ShortcutAction.NextTab, map.Match(new KeyChord(KeyModifiers.Ctrl, 0x09)));
        Assert.Equal(ShortcutAction.PreviousTab, map.Match(new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x09)));
        Assert.Null(map.Match(new KeyChord(KeyModifiers.Ctrl, 'C'))); // reaches the shell
        Assert.Null(map.Match(new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, 'V'))); // paste
        Assert.Equal(ShortcutMap.Defaults.Count, ShortcutMap.Defaults.Values.Distinct().Count());
        Assert.All(ShortcutMap.Defaults.Values, c => Assert.True(c.IsUsable));
        Assert.Equal(Enum.GetValues<ShortcutAction>().Length, ShortcutMap.Defaults.Count);
    }

    [Fact]
    public void Overrides_replace_defaults_and_bad_entries_are_ignored()
    {
        var map = new ShortcutMap(new Dictionary<string, string>
        {
            ["NewTab"] = "Alt+N",
            ["CloseTab"] = "nonsense",
            ["RenameTab"] = "R", // would steal typing
            ["NoSuchAction"] = "Ctrl+Q",
            ["5"] = "Ctrl+Q", // numeric enum names are not actions
        });

        Assert.Equal("Alt+N", map[ShortcutAction.NewTab].ToString());
        Assert.Equal(ShortcutMap.Defaults[ShortcutAction.CloseTab], map[ShortcutAction.CloseTab]);
        Assert.Equal(ShortcutMap.Defaults[ShortcutAction.RenameTab], map[ShortcutAction.RenameTab]);
        Assert.Null(map.Match(new KeyChord(KeyModifiers.Ctrl, 'Q')));
        Assert.Null(map.Match(ShortcutMap.Defaults[ShortcutAction.NewTab]));
    }

    [Fact]
    public void Overrides_saves_only_changes()
    {
        var chords = new Dictionary<ShortcutAction, KeyChord>(ShortcutMap.Defaults)
        {
            [ShortcutAction.ClosePane] = new(KeyModifiers.Alt, 'X'),
        };
        var saved = ShortcutMap.Overrides(chords);
        Assert.Equal(new Dictionary<string, string> { ["ClosePane"] = "Alt+X" }, saved);
        Assert.Equal("Alt+X", new ShortcutMap(saved)[ShortcutAction.ClosePane].ToString());
    }
}
