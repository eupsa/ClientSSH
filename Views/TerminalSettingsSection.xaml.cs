using ClientSSH.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ClientSSH.Views;

public sealed partial class TerminalSettingsSection : UserControl
{
    public AppearanceViewModel ViewModel { get; } = App.GetService<AppearanceViewModel>();

    public TerminalSettingsSection() => InitializeComponent();
}
