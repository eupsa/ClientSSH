using ClientSSH.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ClientSSH.Views;

public sealed partial class HostEditorDialog : ContentDialog
{
    public HostEditorViewModel ViewModel { get; }

    public HostEditorDialog(HostEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }
}
