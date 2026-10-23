namespace ClientSSH.Models;

public enum CursorShape { Block, Bar, Underline }

/// <summary>
/// How a terminal looks. Every field is optional: a profile stores only what it overrides and the rest comes from
/// the global default (see <see cref="Over"/>). Null font family means the built-in monospace font.
/// </summary>
/// <param name="Scheme">Colour scheme id (<c>ColorScheme.BrandId</c> or a scheme's id).</param>
/// <param name="CursorSize">Thickness of the bar or underline cursor, in percent of the cell.</param>
/// <param name="Opacity">Terminal background opacity in percent; below 100 the window behind shows through.</param>
public sealed record TerminalAppearance(
    string? Scheme = null,
    string? FontFamily = null,
    int? FontSize = null,
    CursorShape? Cursor = null,
    int? CursorSize = null,
    int? Opacity = null)
{
    public const int MinFontSize = 8, MaxFontSize = 36, MinOpacity = 30;

    public static TerminalAppearance BuiltIn { get; } = new("brand", "", 14, CursorShape.Block, 25, 100);

    /// <summary>This appearance with every unset field taken from <paramref name="fallback"/>.</summary>
    public TerminalAppearance Over(TerminalAppearance? fallback) => fallback is null ? this : new(
        Scheme ?? fallback.Scheme,
        FontFamily ?? fallback.FontFamily,
        FontSize ?? fallback.FontSize,
        Cursor ?? fallback.Cursor,
        CursorSize ?? fallback.CursorSize,
        Opacity ?? fallback.Opacity);

    /// <summary>Values hand-edited in settings.json are kept in range.</summary>
    public TerminalAppearance Clamped() => this with
    {
        FontSize = FontSize is { } size ? Math.Clamp(size, MinFontSize, MaxFontSize) : null,
        CursorSize = CursorSize is { } cursor ? Math.Clamp(cursor, 5, 100) : null,
        Opacity = Opacity is { } opacity ? Math.Clamp(opacity, MinOpacity, 100) : null,
        Cursor = Cursor is { } shape && Enum.IsDefined(shape) ? shape : null,
    };

    /// <summary>
    /// The appearance a terminal uses: the profile's override, over the global default, over the built-in values.
    /// <paramref name="legacyFontSize"/> is the older global font size setting, used until a default font size is saved.
    /// </summary>
    public static TerminalAppearance Resolve(TerminalAppearance? profile, TerminalAppearance? defaults, int legacyFontSize) =>
        (profile?.Clamped() ?? new()).Over((defaults?.Clamped() ?? new()).Over(
            BuiltIn with { FontSize = Math.Clamp(legacyFontSize, MinFontSize, MaxFontSize) }));
}
