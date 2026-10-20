using ClientSSH.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ClientSSH.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = App.GetService<SettingsViewModel>();

    public SettingsPage() => InitializeComponent();

    // Binding SelectedIndex to a ComboBox filled through ItemsSource races: the box briefly reports item 0 and
    // that would overwrite the saved language. Select first, listen after.
    private void OnLanguageBoxLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        LanguageBox.SelectedIndex = ViewModel.LanguageIndex;
        LanguageBox.SelectionChanged += (_, _) =>
        {
            if (LanguageBox.SelectedIndex >= 0) ViewModel.LanguageIndex = LanguageBox.SelectedIndex;
        };
    }
}
