namespace ClientSSH.Services.Commands;

/// <summary>One entry of the command palette (Ctrl+Shift+P).</summary>
/// <param name="Id">Stable id, e.g. "theme.dark". Not shown.</param>
/// <param name="Title">Localized text shown and searched.</param>
/// <param name="Execute">Runs after the palette closes.</param>
/// <param name="Keywords">Extra search words, not shown (English aliases, user@address...).</param>
/// <param name="Shortcut">Key hint shown on the right, e.g. "Ctrl+T". Empty for none.</param>
/// <param name="Glyph">Segoe Fluent Icons glyph. Empty for none.</param>
public sealed record PaletteCommand(
    string Id, string Title, Action Execute, string Keywords = "", string Shortcut = "", string Glyph = "")
{
    // Screen readers name list items by ToString.
    public override string ToString() => Title;
}
