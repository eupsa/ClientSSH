using ClientSSH.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ClientSSH.Views;

public sealed partial class WelcomePage : Page
{
    public WelcomeViewModel ViewModel { get; } = App.GetService<WelcomeViewModel>();

    public WelcomePage() => InitializeComponent();
}
