namespace ClientSSH.Models;

/// <summary>A language the app is translated to. The name is written in that language on purpose.</summary>
public sealed record LanguageOption(string Tag, string Name, string Flag)
{
    public string FlagUri => $"ms-appx:///Assets/Flags/{Flag}.svg";

    /// <summary>All translations live in Strings/&lt;Tag&gt;/Resources.resw. Add a language here and add its flag SVG.</summary>
    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new("pt-BR", "Português (Brasil)", "br"),
        new("en-US", "English (United States)", "us"),
        new("es-ES", "Español", "es"),
        new("fr-FR", "Français", "fr"),
        new("de-DE", "Deutsch", "de"),
        new("it-IT", "Italiano", "it"),
        new("ru-RU", "Русский", "ru"),
        new("zh-CN", "中文（简体）", "cn"),
        new("ja-JP", "日本語", "jp"),
        new("ko-KR", "한국어", "kr"),
        new("ar-SA", "العربية", "sa"),
        new("hi-IN", "हिन्दी", "in"),
        new("tr-TR", "Türkçe", "tr"),
        new("nl-NL", "Nederlands", "nl"),
    ];
}
