using ClientSSH.Models;
using ClientSSH.ViewModels;
using ClientSSH.Views;
using Microsoft.UI.Xaml.Controls;

namespace ClientSSH.Services.Shell;

public interface IDialogService
{
    Task<Host?> ShowHostEditorAsync();

    Task ShowMessageAsync(string message);
}

public sealed class DialogService : IDialogService
{
    public async Task<Host?> ShowHostEditorAsync()
    {
        var vm = new HostEditorViewModel();
        var dialog = new HostEditorDialog(vm) { XamlRoot = App.MainWindow.Content.XamlRoot };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? vm.ToHost() : null;
    }

    public async Task ShowMessageAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Title = "ClientSSH",
            Content = new TextBlock { Text = message, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
            CloseButtonText = Localization.Loc.Get("Dialog_Ok"),
        };
        await dialog.ShowAsync();
    }
}
