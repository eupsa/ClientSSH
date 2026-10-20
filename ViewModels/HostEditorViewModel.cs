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

    public HostEditorViewModel()
    {
        Name = Address = User = "";
        Port = 22;
    }

    // Address and user end up on an ssh command line, so they must pass the same check as at connect time.
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) && SshCommand.IsValidAddress(Address.Trim()) &&
        SshCommand.IsValidUser(User.Trim()) && Port is >= 1 and <= 65535;

    public Host ToHost() => new(Guid.NewGuid(), Name.Trim(), Address.Trim(), (int)Port, User.Trim());
}
