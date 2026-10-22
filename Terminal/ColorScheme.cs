using System.Globalization;
using System.Text.Json;

namespace ClientSSH.Terminal;

/// <summary>
/// Terminal colours as data (0xRRGGBB). Schemes are functional, so unlike the app UI they may use any colour.
/// The format is Windows Terminal's "schemes" entries, so users can drop their own into schemes.json.
/// </summary>
public sealed record ColorScheme(string Id, string Name, uint Background, uint Foreground, uint Cursor, uint[] Ansi)
{
    /// <summary>The brand scheme is not data: it is read from Themes/Palette.xaml and follows light/dark.</summary>
    public const string BrandId = "brand";

    private static readonly string[] AnsiKeys =
    [
        "black", "red", "green", "yellow", "blue", "purple", "cyan", "white",
        "brightBlack", "brightRed", "brightGreen", "brightYellow", "brightBlue", "brightPurple", "brightCyan", "brightWhite",
    ];

    // Values from the schemes' own (MIT) definitions as shipped with Windows Terminal.
    private const string BuiltInJson = """
    [
      { "name": "Campbell", "background": "#0C0C0C", "foreground": "#CCCCCC", "cursorColor": "#FFFFFF",
        "black": "#0C0C0C", "red": "#C50F1F", "green": "#13A10E", "yellow": "#C19C00", "blue": "#0037DA", "purple": "#881798", "cyan": "#3A96DD", "white": "#CCCCCC",
        "brightBlack": "#767676", "brightRed": "#E74856", "brightGreen": "#16C60C", "brightYellow": "#F9F1A5", "brightBlue": "#3B78FF", "brightPurple": "#B4009E", "brightCyan": "#61D6D6", "brightWhite": "#F2F2F2" },
      { "name": "One Half Dark", "background": "#282C34", "foreground": "#DCDFE4", "cursorColor": "#FFFFFF",
        "black": "#282C34", "red": "#E06C75", "green": "#98C379", "yellow": "#E5C07B", "blue": "#61AFEF", "purple": "#C678DD", "cyan": "#56B6C2", "white": "#DCDFE4",
        "brightBlack": "#5A6374", "brightRed": "#E06C75", "brightGreen": "#98C379", "brightYellow": "#E5C07B", "brightBlue": "#61AFEF", "brightPurple": "#C678DD", "brightCyan": "#56B6C2", "brightWhite": "#DCDFE4" },
      { "name": "One Half Light", "background": "#FAFAFA", "foreground": "#383A42", "cursorColor": "#4F525D",
        "black": "#383A42", "red": "#E45649", "green": "#50A14F", "yellow": "#C18301", "blue": "#0184BC", "purple": "#A626A4", "cyan": "#0997B3", "white": "#FAFAFA",
        "brightBlack": "#4F525D", "brightRed": "#DF6C75", "brightGreen": "#98C379", "brightYellow": "#E4C07A", "brightBlue": "#61AFEF", "brightPurple": "#C577DD", "brightCyan": "#56B5C1", "brightWhite": "#FFFFFF" },
      { "name": "Solarized Dark", "background": "#002B36", "foreground": "#839496", "cursorColor": "#FFFFFF",
        "black": "#002B36", "red": "#DC322F", "green": "#859900", "yellow": "#B58900", "blue": "#268BD2", "purple": "#D33682", "cyan": "#2AA198", "white": "#EEE8D5",
        "brightBlack": "#073642", "brightRed": "#CB4B16", "brightGreen": "#586E75", "brightYellow": "#657B83", "brightBlue": "#839496", "brightPurple": "#6C71C4", "brightCyan": "#93A1A1", "brightWhite": "#FDF6E3" },
      { "name": "Solarized Light", "background": "#FDF6E3", "foreground": "#657B83", "cursorColor": "#002B36",
        "black": "#002B36", "red": "#DC322F", "green": "#859900", "yellow": "#B58900", "blue": "#268BD2", "purple": "#D33682", "cyan": "#2AA198", "white": "#EEE8D5",
        "brightBlack": "#073642", "brightRed": "#CB4B16", "brightGreen": "#586E75", "brightYellow": "#657B83", "brightBlue": "#839496", "brightPurple": "#6C71C4", "brightCyan": "#93A1A1", "brightWhite": "#FDF6E3" },
      { "name": "Dracula", "background": "#282A36", "foreground": "#F8F8F2", "cursorColor": "#F8F8F2",
        "black": "#21222C", "red": "#FF5555", "green": "#50FA7B", "yellow": "#F1FA8C", "blue": "#BD93F9", "purple": "#FF79C6", "cyan": "#8BE9FD", "white": "#F8F8F2",
        "brightBlack": "#6272A4", "brightRed": "#FF6E6E", "brightGreen": "#69FF94", "brightYellow": "#FFFFA5", "brightBlue": "#D6ACFF", "brightPurple": "#FF92DF", "brightCyan": "#A4FFFF", "brightWhite": "#FFFFFF" },
      { "name": "Nord", "background": "#2E3440", "foreground": "#D8DEE9", "cursorColor": "#D8DEE9",
        "black": "#3B4252", "red": "#BF616A", "green": "#A3BE8C", "yellow": "#EBCB8B", "blue": "#81A1C1", "purple": "#B48EAD", "cyan": "#88C0D0", "white": "#E5E9F0",
        "brightBlack": "#4C566A", "brightRed": "#BF616A", "brightGreen": "#A3BE8C", "brightYellow": "#EBCB8B", "brightBlue": "#81A1C1", "brightPurple": "#B48EAD", "brightCyan": "#8FBCBB", "brightWhite": "#ECEFF4" },
      { "name": "Tango Dark", "background": "#000000", "foreground": "#D3D7CF", "cursorColor": "#FFFFFF",
        "black": "#000000", "red": "#CC0000", "green": "#4E9A06", "yellow": "#C4A000", "blue": "#3465A4", "purple": "#75507B", "cyan": "#06989A", "white": "#D3D7CF",
        "brightBlack": "#555753", "brightRed": "#EF2929", "brightGreen": "#8AE234", "brightYellow": "#FCE94F", "brightBlue": "#729FCF", "brightPurple": "#AD7FA8", "brightCyan": "#34E2E2", "brightWhite": "#EEEEEC" }
    ]
    """;

    public static IReadOnlyList<ColorScheme> BuiltIn { get; } = Parse(BuiltInJson);

    /// <summary>Id from the name, so a scheme picked in settings survives reordering and restarts.</summary>
    public static string IdFor(string name) => name.Trim().ToLowerInvariant().Replace(' ', '-');

    /// <summary>
    /// Reads Windows Terminal schemes: an array, or an object with a "schemes" array. Entries with a missing or bad
    /// colour are skipped instead of failing the whole file. Invalid JSON yields an empty list.
    /// </summary>
    public static IReadOnlyList<ColorScheme> Parse(string json)
    {
        var schemes = new List<ColorScheme>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("schemes", out var inner)) root = inner;
            if (root.ValueKind != JsonValueKind.Array) return schemes;
            foreach (var entry in root.EnumerateArray())
                if (TryParseScheme(entry, out var scheme)) schemes.Add(scheme);
        }
        catch (JsonException)
        {
        }

        return schemes;
    }

    private static bool TryParseScheme(JsonElement entry, out ColorScheme scheme)
    {
        scheme = null!;
        if (entry.ValueKind != JsonValueKind.Object) return false;
        if (!entry.TryGetProperty("name", out var nameValue) || nameValue.GetString() is not { Length: > 0 and <= 64 } name) return false;
        if (IdFor(name) == BrandId) return false;

        bool Color(string key, out uint value)
        {
            value = 0;
            return entry.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && TryParseHex(v.GetString()!, out value);
        }

        if (!Color("background", out var background) || !Color("foreground", out var foreground)) return false;
        var ansi = new uint[16];
        for (var i = 0; i < 16; i++)
            if (!Color(AnsiKeys[i], out ansi[i])) return false;
        // Windows Terminal makes the cursor optional.
        if (!Color("cursorColor", out var cursor)) cursor = foreground;

        scheme = new ColorScheme(IdFor(name), name.Trim(), background, foreground, cursor, ansi);
        return true;
    }

    /// <summary>"#RRGGBB" or "#RGB" to 0xRRGGBB.</summary>
    public static bool TryParseHex(string text, out uint rgb)
    {
        rgb = 0;
        text = text.Trim();
        if (text.Length is not (4 or 7) || text[0] != '#') return false;
        var digits = text.Length == 4 ? string.Concat(text[1..].Select(c => $"{c}{c}")) : text[1..];
        return uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb);
    }
}
