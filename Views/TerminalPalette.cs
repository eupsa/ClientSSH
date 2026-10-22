using ClientSSH.Terminal;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace ClientSSH.Views;

/// <summary>
/// Terminal colours ready to draw: a <see cref="ColorScheme"/>, or for the brand scheme the keys in
/// Themes/Palette.xaml (so the palette file stays the single source of the app's own colours).
/// </summary>
public sealed class TerminalPalette
{
    private readonly Color[] _ansi = new Color[16];

    /// <summary>Carries the background opacity in its alpha.</summary>
    public Color Background { get; private init; }
    public Color Foreground { get; private init; }
    public Color Cursor { get; private init; }

    public Color Ansi(int index) => _ansi[index];

    public static TerminalPalette Load(ElementTheme theme, ColorScheme? scheme, int opacityPercent)
    {
        var alpha = (byte)Math.Round(Math.Clamp(opacityPercent, 0, 100) * 2.55);
        if (scheme is not null)
        {
            var fromScheme = new TerminalPalette
            {
                Background = FromRgb(scheme.Background, alpha),
                Foreground = FromRgb(scheme.Foreground),
                Cursor = FromRgb(scheme.Cursor),
            };
            for (var i = 0; i < 16; i++) fromScheme._ansi[i] = FromRgb(scheme.Ansi[i]);
            return fromScheme;
        }

        var key = theme == ElementTheme.Light ? "Light" : "Dark";
        Color Get(string name)
        {
            foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
                if (dictionary.ThemeDictionaries.TryGetValue(key, out var themed) &&
                    themed is ResourceDictionary resources && resources.TryGetValue(name, out var value))
                    return (Color)value;
            throw new KeyNotFoundException(name);
        }

        var background = Get("TerminalBackgroundColor");
        var palette = new TerminalPalette
        {
            Background = Color.FromArgb(alpha, background.R, background.G, background.B),
            Foreground = Get("TerminalForegroundColor"),
            Cursor = Get("TerminalCursorColor"),
        };
        for (var i = 0; i < 16; i++) palette._ansi[i] = Get($"Ansi{i}Color");
        return palette;
    }

    private static Color FromRgb(uint rgb, byte alpha = 255) =>
        Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public Color Resolve(TermColor color, Color fallback) => color.Kind switch
    {
        ColorKind.Indexed => Indexed(color.Value),
        ColorKind.Rgb => FromRgb((uint)color.Value),
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

    public static Color Opaque(Color color) => Color.FromArgb(255, color.R, color.G, color.B);

    public static Color Blend(Color from, Color to, float amount) => Color.FromArgb(255,
        (byte)(from.R * amount + to.R * (1 - amount)),
        (byte)(from.G * amount + to.G * (1 - amount)),
        (byte)(from.B * amount + to.B * (1 - amount)));
}
