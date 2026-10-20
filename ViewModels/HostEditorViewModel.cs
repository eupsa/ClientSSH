using ClientSSH.Models;
using ClientSSH.Services.Remote;
using ClientSSH.Terminal;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientSSH.ViewModels;

public sealed partial class HostEditorViewModel : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))] public partial string Name { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))] public partial string Address { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))] public partial string User { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))] public partial double Port { get; set; }

    /// <summary>0 = SSH, 1 = Remote Desktop (same order as <see cref="HostKind"/>).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))]
    public partial int KindIndex { get; set; }

    private HostKind Kind => (HostKind)KindIndex;

    // Keep the port in step with the type, unless the user typed a custom one.
    partial void OnKindIndexChanged(int value)
    {
        if (Kind == HostKind.Rdp && Port == 22) Port = 3389;
        else if (Kind == HostKind.Ssh && Port == 3389) Port = 22;
    }

    public HostEditorViewModel()
    {
        Name = Address = User = "";
        Port = 22;
    }

    // Address and user end up on an ssh command line or in an .rdp file, so they must pass the same check as at connect time.
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Port is >= 1 and <= 65535 &&
        (Kind == HostKind.Rdp ? RdpLauncher.IsValid(ToHost()) : SshCommand.IsValid(ToHost()));

    public Host ToHost() => new(Guid.NewGuid(), Name.Trim(), Address.Trim(), (int)Port, User.Trim(), Kind: Kind);
}
