using ClientSSH.Terminal;

namespace ClientSSH.Services.Customization;

/// <summary>Finds the local shells once, off the UI thread (asking wsl.exe for its distros can take a moment).</summary>
public sealed class ProfileService
{
    private Task<IReadOnlyList<ShellProfile>>? _detection;

    /// <summary>Empty until <see cref="GetAsync"/> finishes.</summary>
    public IReadOnlyList<ShellProfile> Profiles { get; private set; } = [];

    public async Task<IReadOnlyList<ShellProfile>> GetAsync()
    {
        _detection ??= Task.Run(() => ShellProfiles.Detect(ShellEnvironment.Real));
        return Profiles = await _detection;
    }

    public ShellProfile? Find(string id) => Profiles.FirstOrDefault(p => p.Id == id);
}
