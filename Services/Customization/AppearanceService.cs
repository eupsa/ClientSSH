using ClientSSH.Models;
using ClientSSH.Services.Storage;
using ClientSSH.Terminal;

namespace ClientSSH.Services.Customization;

/// <summary>
/// Answers "how does this terminal look": the colour schemes (built in plus the user's schemes.json) and the
/// appearance resolved for a profile. <see cref="Changed"/> lets open terminals follow edits live.
/// </summary>
public sealed class AppearanceService(ISettingsStore settings)
{
    /// <summary>Optional file in the data folder with Windows Terminal "schemes" entries.</summary>
    public const string UserSchemesFile = "schemes.json";

    private IReadOnlyList<ColorScheme>? _schemes;

    public event Action? Changed;

    /// <summary>Built-in schemes first, then the user's (a user scheme with a built-in's name is skipped).</summary>
    public IReadOnlyList<ColorScheme> Schemes => _schemes ??= LoadSchemes();

    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>Null means the brand scheme from Themes/Palette.xaml.</summary>
    public ColorScheme? FindScheme(string? id) =>
        id is null or ColorScheme.BrandId ? null : Schemes.FirstOrDefault(s => s.Id == id);

    /// <summary>Appearance key for a tab: its shell profile, the profile picked for the host, or the shared SSH one.</summary>
    public string AppearanceIdFor(Host? host, ShellProfile? profile)
    {
        if (profile is not null) return profile.Id;
        if (host is not null && settings.Current.HostProfiles?.TryGetValue(host.Id.ToString(), out var id) == true) return id;
        return ShellProfile.SshId;
    }

    /// <summary>The override stored for a profile, or null when it follows the default.</summary>
    public TerminalAppearance? OverrideFor(string appearanceId) =>
        settings.Current.ProfileAppearance?.GetValueOrDefault(appearanceId);

    public TerminalAppearance Resolve(string? appearanceId)
    {
        var current = settings.Current;
        return TerminalAppearance.Resolve(appearanceId is null ? null : OverrideFor(appearanceId),
            current.TerminalDefaults, current.TerminalFontSize);
    }

    public void SetHostProfile(Guid hostId, string? profileId)
    {
        var map = new Dictionary<string, string>(settings.Current.HostProfiles ?? []);
        if (profileId is null) map.Remove(hostId.ToString());
        else map[hostId.ToString()] = profileId;
        settings.Save(settings.Current with { HostProfiles = map });
    }

    private static IReadOnlyList<ColorScheme> LoadSchemes()
    {
        var path = Path.Combine(LocalData.Dir, UserSchemesFile);
        var user = new List<ColorScheme>();
        try
        {
            if (File.Exists(path)) user.AddRange(ColorScheme.Parse(File.ReadAllText(path)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable file only means no extra schemes.
        }

        var all = ColorScheme.BuiltIn.ToList();
        all.AddRange(user.Where(u => all.All(s => s.Id != u.Id)));
        return all;
    }
}
