using System.Collections.ObjectModel;
using ClientSSH.Models;
using ClientSSH.Services.Customization;
using ClientSSH.Services.Localization;
using ClientSSH.Services.Shell;
using ClientSSH.Services.Shortcuts;
using ClientSSH.Services.Storage;
using ClientSSH.Terminal;
using ClientSSH.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ClientSSH.ViewModels;

/// <param name="Id">"" for the global default, "ssh" for hosts, else a shell profile id.</param>
public sealed record AppearanceTarget(string Id, string Name);

/// <summary>
/// Terminal appearance editor (global default and per-profile overrides), window backdrop and profile shortcuts.
/// The Set* commands are also meant for the command palette.
/// </summary>
public sealed partial class AppearanceViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly AppearanceService _appearance;
    private readonly ProfileService _profiles;
    private readonly ShortcutService _shortcuts;
    private readonly ThemeService _theme;
    private bool _loading;

    public ObservableCollection<AppearanceTarget> Targets { get; } = [];

    /// <summary>Index 0 is the brand scheme, then <see cref="AppearanceService.Schemes"/>.</summary>
    public IReadOnlyList<string> SchemeNames { get; }

    /// <summary>Index 0 is the built-in font, then the installed monospace families.</summary>
    public IReadOnlyList<string> FontNames { get; }

    [ObservableProperty] public partial int TargetIndex { get; set; }
    [ObservableProperty] public partial bool IsProfileTarget { get; set; }
    [ObservableProperty] public partial bool IsShellTarget { get; set; }
    [ObservableProperty] public partial bool Customize { get; set; }
    [ObservableProperty] public partial bool CanEdit { get; set; }
    [ObservableProperty] public partial int SchemeIndex { get; set; }
    [ObservableProperty] public partial int FontIndex { get; set; }
    [ObservableProperty] public partial double FontSize { get; set; }
    [ObservableProperty] public partial int CursorIndex { get; set; }
    [ObservableProperty] public partial double CursorSize { get; set; }
    [ObservableProperty] public partial double Opacity { get; set; }
    [ObservableProperty] public partial string Shortcut { get; set; } = "";
    [ObservableProperty] public partial bool ShortcutInvalid { get; set; }
    [ObservableProperty] public partial int BackdropIndex { get; set; }
    [ObservableProperty] public partial Brush? PreviewBackground { get; set; }
    [ObservableProperty] public partial Brush? PreviewForeground { get; set; }
    [ObservableProperty] public partial IReadOnlyList<Brush> PreviewColors { get; set; } = [];

    public double MinFontSize => TerminalAppearance.MinFontSize;
    public double MaxFontSize => TerminalAppearance.MaxFontSize;
    public double MinOpacity => TerminalAppearance.MinOpacity;

    public AppearanceViewModel(ISettingsStore store, AppearanceService appearance, ProfileService profiles,
        ShortcutService shortcuts, ThemeService theme, ProfilesViewModel profilesViewModel)
    {
        _store = store;
        _appearance = appearance;
        _profiles = profiles;
        _shortcuts = shortcuts;
        _theme = theme;
        _loading = true; // until Load() below: filling the fields must not save them
        SchemeNames = [Loc.Get("Appearance_BrandScheme"), .. appearance.Schemes.Select(s => s.Name)];
        FontNames = [string.Format(Loc.Get("Appearance_DefaultFont"), TerminalView.MonoFont), .. MonospaceFonts()];
        BackdropIndex = (int)store.Current.Backdrop;

        Targets.Add(new("", Loc.Get("Appearance_TargetDefault")));
        Targets.Add(new(ShellProfile.SshId, Loc.Get("Appearance_TargetSsh")));
        AddProfiles(profilesViewModel);
        profilesViewModel.Loaded += () => AddProfiles(profilesViewModel);
        Load();
    }

    private void AddProfiles(ProfilesViewModel profiles)
    {
        foreach (var profile in profiles.Profiles)
            if (Targets.All(t => t.Id != profile.Id)) Targets.Add(new(profile.Id, profile.Name));
    }

    private static IEnumerable<string> MonospaceFonts()
    {
        using var fonts = CanvasFontSet.GetSystemFontSet();
        return fonts.Fonts.Where(f => f.IsMonospaced)
            .Select(f => f.FamilyNames.TryGetValue("en-us", out var en) ? en : f.FamilyNames.Values.FirstOrDefault())
            .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private string TargetId => TargetIndex >= 0 && TargetIndex < Targets.Count ? Targets[TargetIndex].Id : "";

    partial void OnTargetIndexChanged(int value) => Load();

    // Shows the selected target's values without saving them back.
    private void Load()
    {
        _loading = true;
        var id = TargetId;
        IsProfileTarget = id.Length > 0;
        IsShellTarget = _profiles.Find(id) is not null;
        Customize = IsProfileTarget && _appearance.OverrideFor(id) is not null;
        CanEdit = !IsProfileTarget || Customize;

        var a = _appearance.Resolve(Customize ? id : null);
        var scheme = _appearance.FindScheme(a.Scheme);
        SchemeIndex = scheme is null ? 0 : _appearance.Schemes.ToList().IndexOf(scheme) + 1;
        FontIndex = Math.Max(0, FontNames.ToList().FindIndex(f => string.Equals(f, a.FontFamily, StringComparison.OrdinalIgnoreCase)));
        FontSize = a.FontSize ?? 14;
        CursorIndex = (int)(a.Cursor ?? CursorShape.Block);
        CursorSize = a.CursorSize ?? 25;
        Opacity = a.Opacity ?? 100;
        Shortcut = IsShellTarget ? _shortcuts.ChordFor(id) : "";
        ShortcutInvalid = false;
        _loading = false;
        UpdatePreview(a, scheme);
    }

    private TerminalAppearance FromFields() => new(
        SchemeIndex <= 0 ? ColorScheme.BrandId : _appearance.Schemes[SchemeIndex - 1].Id,
        FontIndex <= 0 ? "" : FontNames[FontIndex],
        (int)FontSize,
        (CursorShape)Math.Max(0, CursorIndex),
        (int)CursorSize,
        (int)Opacity);

    private void Save()
    {
        if (_loading || !CanEdit || double.IsNaN(FontSize)) return;
        var appearance = FromFields().Clamped();
        Store(TargetId, appearance);
        UpdatePreview(appearance, _appearance.FindScheme(appearance.Scheme));
    }

    private void Store(string id, TerminalAppearance? appearance)
    {
        var current = _store.Current;
        if (id.Length == 0)
        {
            _store.Save(current with { TerminalDefaults = appearance, TerminalFontSize = appearance?.FontSize ?? current.TerminalFontSize });
        }
        else
        {
            var map = new Dictionary<string, TerminalAppearance>(current.ProfileAppearance ?? []);
            if (appearance is null) map.Remove(id);
            else map[id] = appearance;
            _store.Save(current with { ProfileAppearance = map });
        }

        _appearance.NotifyChanged();
    }

    partial void OnSchemeIndexChanged(int value) => Save();
    partial void OnFontIndexChanged(int value) => Save();
    partial void OnFontSizeChanged(double value) => Save();
    partial void OnCursorIndexChanged(int value) => Save();
    partial void OnCursorSizeChanged(double value) => Save();
    partial void OnOpacityChanged(double value) => Save();

    // On: the profile starts from a copy of the default. Off: its override is dropped and it follows the default again.
    partial void OnCustomizeChanged(bool value)
    {
        if (_loading || !IsProfileTarget) return;
        Store(TargetId, value ? _appearance.Resolve(null) : null);
        Load();
    }

    partial void OnShortcutChanged(string value)
    {
        if (_loading || !IsShellTarget) return;
        ShortcutInvalid = !_shortcuts.SetChord(TargetId, value);
    }

    partial void OnBackdropIndexChanged(int value)
    {
        if (!_loading && value >= 0) SetBackdrop((WindowBackdrop)value);
    }

    [RelayCommand]
    private void SetBackdrop(WindowBackdrop backdrop)
    {
        _store.Save(_store.Current with { Backdrop = backdrop });
        _theme.Apply();
        BackdropIndex = (int)backdrop;
    }

    [RelayCommand]
    private void SetTheme(ThemeChoice theme)
    {
        _store.Save(_store.Current with { Theme = theme });
        _theme.Apply();
        UpdatePreview(FromFields(), _appearance.FindScheme(FromFields().Scheme));
    }

    /// <summary>Sets the default colour scheme of every terminal that is not customized ("brand" or a scheme id).</summary>
    [RelayCommand]
    private void SetScheme(string schemeId)
    {
        if (schemeId != ColorScheme.BrandId && _appearance.FindScheme(schemeId) is null) return;
        var defaults = _appearance.Resolve(null) with { Scheme = schemeId };
        Store("", defaults);
        Load();
    }

    private void UpdatePreview(TerminalAppearance appearance, ColorScheme? scheme)
    {
        var theme = (App.MainWindow?.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Dark;
        var palette = TerminalPalette.Load(theme, scheme, appearance.Opacity ?? 100);
        PreviewBackground = new SolidColorBrush(palette.Background);
        PreviewForeground = new SolidColorBrush(palette.Foreground);
        PreviewColors = Enumerable.Range(0, 16).Select(i => (Brush)new SolidColorBrush(palette.Ansi(i))).ToList();
    }
}
