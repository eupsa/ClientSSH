using ClientSSH.Models;
using ClientSSH.Terminal;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientSSH.ViewModels;

public sealed partial class HostEditorViewModel : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))] public partial string Name { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))] public partial string Address { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))] public partial string User { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))] public partial double Port { get; set; }

    private readonly IReadOnlyList<ShellProfile> _profiles;

    /// <summary>Index 0: look like the other SSH hosts; then each shell profile's appearance.</summary>
    public IReadOnlyList<string> AppearanceOptions { get; }

    [ObservableProperty] public partial int AppearanceIndex { get; set; }

    /// <summary>The profile whose appearance the new host uses, or null for the shared SSH appearance.</summary>
    public string? AppearanceProfileId => AppearanceIndex > 0 ? _profiles[AppearanceIndex - 1].Id : null;

    public HostEditorViewModel(IReadOnlyList<ShellProfile>? profiles = null, string sshAppearanceName = "")
    {
        Name = Address = User = "";
        Port = 22;
        _profiles = profiles ?? [];
        AppearanceOptions = [sshAppearanceName, .. _profiles.Select(p => p.Name)];
    }

    // Address and user end up on an ssh command line, so they must pass the same check as at connect time.
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) && SshCommand.IsValidAddress(Address.Trim()) &&
        SshCommand.IsValidUser(User.Trim()) && Port is >= 1 and <= 65535;

    public Host ToHost() => new(Guid.NewGuid(), Name.Trim(), Address.Trim(), (int)Port, User.Trim());
}
