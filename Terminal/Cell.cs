namespace ClientSSH.Terminal;

public enum ColorKind : byte { Default, Indexed, Rgb }

public readonly record struct TermColor(ColorKind Kind, int Value)
{
    public static readonly TermColor Default = new(ColorKind.Default, 0);

    public static TermColor Indexed(int index) => new(ColorKind.Indexed, index);

    public static TermColor Rgb(int r, int g, int b) => new(ColorKind.Rgb, (r << 16) | (g << 8) | b);
}

[Flags]
public enum CellAttr : byte { None = 0, Bold = 1, Dim = 2, Italic = 4, Underline = 8, Inverse = 16 }

public struct Cell
{
    public char Ch;
    public TermColor Fg, Bg;
    public CellAttr Attr;
}
