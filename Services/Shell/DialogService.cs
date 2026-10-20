using ClientSSH.Models;
using ClientSSH.ViewModels;
using ClientSSH.Views;
using Microsoft.UI.Xaml.Controls;

namespace ClientSSH.Services.Shell;

public interface IDialogService
{
    Task<Host?> ShowHostEditorAsync();
}

public sealed class DialogService : IDialogService
{
    public async Task<Host?> ShowHostEditorAsync()
    {
        var vm = new HostEditorViewModel();
        var dialog = new HostEditorDialog(vm) { XamlRoot = App.MainWindow.Content.XamlRoot };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? vm.ToHost() : null;
    }
}
