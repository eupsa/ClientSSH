using System.Text.Json;
using ClientSSH.Models;
using ClientSSH.Services.Shortcuts;
using ClientSSH.Terminal;
using Xunit;

namespace ClientSSH.Tests;

public class ShellProfileTests
{
    private static ShellEnvironment Fake(IEnumerable<string> files, params string[] distros)
    {
        var set = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        return new(@"C:\Windows\System32", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Users\u\AppData\Local",
            set.Contains, () => distros);
    }

    [Fact]
    public void Finds_only_installed_shells()
    {
        var env = Fake([@"C:\Windows\System32\cmd.exe", @"C:\Program Files\Git\bin\bash.exe"]);
        var profiles = ShellProfiles.Detect(env);
        Assert.Equal(["cmd", "gitbash"], profiles.Select(p => p.Id));
        Assert.Equal("\"C:\\Program Files\\Git\\bin\\bash.exe\" --login -i", profiles[1].CommandLine);
    }

    [Fact]
    public void Finds_every_kind_when_present()
    {
        var env = Fake(
        [
            @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            @"C:\Users\u\AppData\Local\Microsoft\WindowsApps\pwsh.exe",
            @"C:\Windows\System32\cmd.exe",
            @"C:\Windows\System32\wsl.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe",
        ], "Ubuntu-22.04", "docker-desktop", "bad name", "-oops");
        var profiles = ShellProfiles.Detect(env);
        Assert.Equal(["powershell", "pwsh", "cmd", "wsl:Ubuntu-22.04", "gitbash"], profiles.Select(p => p.Id));
        Assert.Equal("\"C:\\Windows\\System32\\wsl.exe\" -d Ubuntu-22.04 --cd ~", profiles[3].CommandLine);
    }

    [Fact]
    public void No_wsl_profiles_without_wsl_exe() =>
        Assert.Empty(ShellProfiles.Detect(Fake([], "Ubuntu")));

    [Theory]
    [InlineData(@"C:\a\b.exe", true)]
    [InlineData(@"b.exe", false)]
    [InlineData(@"C:\a\b.bat", false)]
    [InlineData("C:\\a\"\\b.exe", false)]
    public void Executable_paths_are_checked(string path, bool ok) => Assert.Equal(ok, ShellProfiles.IsSafeExecutable(path));

    [Fact]
    public void Parses_wsl_list_output_with_utf16_leftovers() =>
        Assert.Equal(["Ubuntu", "Debian"], ShellProfiles.ParseWslList("\uFEFFUbuntu\0\r\n\r\nDebian\r\ndocker-desktop-data\r\n"));
}

public class ColorSchemeTests
{
    [Fact]
    public void Built_in_schemes_all_parse()
    {
        Assert.Equal(8, ColorScheme.BuiltIn.Count);
        Assert.All(ColorScheme.BuiltIn, s => Assert.Equal(16, s.Ansi.Length));
        Assert.Equal(0x0C0C0Cu, ColorScheme.BuiltIn.Single(s => s.Id == "campbell").Background);
    }

    [Fact]
    public void Reads_windows_terminal_file_and_skips_bad_entries()
    {
        const string json = """
        { "schemes": [
          { "name": "Mine", "background": "#123", "foreground": "#ABCDEF",
            "black": "#000000", "red": "#000001", "green": "#000002", "yellow": "#000003", "blue": "#000004", "purple": "#000005", "cyan": "#000006", "white": "#000007",
            "brightBlack": "#000008", "brightRed": "#000009", "brightGreen": "#00000A", "brightYellow": "#00000B", "brightBlue": "#00000C", "brightPurple": "#00000D", "brightCyan": "#00000E", "brightWhite": "#00000F" },
          { "name": "Broken", "background": "red" },
          { "name": "Brand", "background": "#000000" },
        ] }
        """;
        var scheme = Assert.Single(ColorScheme.Parse(json));
        Assert.Equal("mine", scheme.Id);
        Assert.Equal(0x112233u, scheme.Background);
        Assert.Equal(0xABCDEFu, scheme.Cursor); // cursor falls back to the foreground
        Assert.Equal(0x00000Fu, scheme.Ansi[15]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("42")]
    public void Bad_files_give_no_schemes(string json) => Assert.Empty(ColorScheme.Parse(json));

    [Theory]
    [InlineData("#FFF", true, 0xFFFFFFu)]
    [InlineData("#1e3a8a", true, 0x1E3A8Au)]
    [InlineData("1E3A8A", false, 0u)]
    [InlineData("#GG0000", false, 0u)]
    public void Hex_colours(string text, bool ok, uint value)
    {
        Assert.Equal(ok, ColorScheme.TryParseHex(text, out var rgb));
        Assert.Equal(value, rgb);
    }
}

public class AppearanceTests
{
    [Fact]
    public void Profile_overrides_default_which_overrides_built_in()
    {
        var defaults = new TerminalAppearance(Scheme: "nord", FontSize: 16);
        var profile = new TerminalAppearance(FontSize: 20, Cursor: CursorShape.Bar);
        var result = TerminalAppearance.Resolve(profile, defaults, legacyFontSize: 12);
        Assert.Equal(new TerminalAppearance("nord", "", 20, CursorShape.Bar, 25, 100), result);
    }

    [Fact]
    public void Old_font_size_setting_is_used_until_a_default_is_saved() =>
        Assert.Equal(18, TerminalAppearance.Resolve(null, null, legacyFontSize: 18).FontSize);

    [Fact]
    public void Hand_edited_values_are_clamped()
    {
        var result = TerminalAppearance.Resolve(new(FontSize: 400, Opacity: 0, Cursor: (CursorShape)9), null, 14);
        Assert.Equal(TerminalAppearance.MaxFontSize, result.FontSize);
        Assert.Equal(TerminalAppearance.MinOpacity, result.Opacity);
        Assert.Equal(CursorShape.Block, result.Cursor);
    }
}

public class SettingsCompatibilityTests
{
    [Fact]
    public void Old_settings_file_loads_with_defaults_for_new_fields()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{ "OnboardingDone": true, "TerminalFontSize": 16 }""")!;
        Assert.True(settings.OnboardingDone);
        Assert.Null(settings.TerminalDefaults);
        Assert.Null(settings.ProfileAppearance);
        Assert.Equal(WindowBackdrop.Brand, settings.Backdrop);
    }

    [Fact]
    public void Customization_round_trips()
    {
        var settings = new AppSettings(
            TerminalDefaults: new("dracula", "Consolas", 15, CursorShape.Underline, 30, 85),
            ProfileAppearance: new() { ["cmd"] = new(Scheme: "campbell") },
            HostProfiles: new() { [Guid.Empty.ToString()] = "pwsh" },
            Shortcuts: new() { ["profile:cmd"] = "Ctrl+Shift+3" },
            Backdrop: WindowBackdrop.Mica);
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(settings.TerminalDefaults, back.TerminalDefaults);
        Assert.Equal("campbell", back.ProfileAppearance!["cmd"].Scheme);
        Assert.Equal("pwsh", back.HostProfiles![Guid.Empty.ToString()]);
        Assert.Equal("Ctrl+Shift+3", back.Shortcuts!["profile:cmd"]);
        Assert.Equal(WindowBackdrop.Mica, back.Backdrop);
    }
}

public class KeyChordTests
{
    [Theory]
    [InlineData("Ctrl+Shift+1", "Ctrl+Shift+1")]
    [InlineData(" shift + ctrl + f5 ", "Ctrl+Shift+F5")]
    [InlineData("Alt+W", "Alt+W")]
    public void Parses_and_normalizes(string text, string expected)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(expected, chord.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Shift+A")] // would eat typing
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+F25")]
    public void Rejects_invalid(string text) => Assert.False(KeyChord.TryParse(text, out var chord) && chord.IsUsable);
}
