using ClientSSH.Terminal;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace ClientSSH.Views;

/// <summary>Terminal colours read from Themes/Palette.xaml, so the palette file stays the single source.</summary>
internal sealed class TerminalPalette
{
    private readonly Color[] _ansi = new Color[16];

    public Color Background { get; private init; }
    public Color Foreground { get; private init; }
    public Color Cursor { get; private init; }

    public static TerminalPalette Load(ElementTheme theme)
    {
        var key = theme == ElementTheme.Light ? "Light" : "Dark";
        Color Get(string name)
        {
            foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
                if (dictionary.ThemeDictionaries.TryGetValue(key, out var themed) &&
                    themed is ResourceDictionary resources && resources.TryGetValue(name, out var value))
                    return (Color)value;
            throw new KeyNotFoundException(name);
        }

        var palette = new TerminalPalette
        {
            Background = Get("TerminalBackgroundColor"),
            Foreground = Get("TerminalForegroundColor"),
            Cursor = Get("TerminalCursorColor"),
        };
        for (var i = 0; i < 16; i++) palette._ansi[i] = Get($"Ansi{i}Color");
        return palette;
    }

    public Color Resolve(TermColor color, Color fallback) => color.Kind switch
    {
        ColorKind.Indexed => Indexed(color.Value),
        ColorKind.Rgb => Color.FromArgb(255, (byte)(color.Value >> 16), (byte)(color.Value >> 8), (byte)color.Value),
        _ => fallback,
    };

    private Color Indexed(int index)
    {
        if (index < 16) return _ansi[index];
        if (index >= 232)
        {
            var gray = (byte)(8 + (index - 232) * 10);
            return Color.FromArgb(255, gray, gray, gray);
        }

        index -= 16;
        static byte Level(int n) => (byte)(n == 0 ? 0 : 55 + n * 40);
        return Color.FromArgb(255, Level(index / 36), Level(index / 6 % 6), Level(index % 6));
    }

    public static Color Blend(Color from, Color to, float amount) => Color.FromArgb(255,
        (byte)(from.R * amount + to.R * (1 - amount)),
        (byte)(from.G * amount + to.G * (1 - amount)),
        (byte)(from.B * amount + to.B * (1 - amount)));
}
