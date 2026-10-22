using ClientSSH.Models;
using ClientSSH.Services.Remote;
using ClientSSH.ViewModels;
using ClientSSH.Views;
using Microsoft.UI.Xaml.Controls;

namespace ClientSSH.Services.Shell;

public interface IDialogService
{
    Task<Host?> ShowHostEditorAsync();

    /// <summary>Asks the person whether another PC may see (or also control) this one. Denied when they say no.</summary>
    Task<RemoteAccess> ShowRemoteRequestAsync(string securityCode);

    /// <summary>Asks for one line of text (e.g. a tab name). Null when cancelled.</summary>
    Task<string?> AskTextAsync(string title, string text);
}

public sealed class DialogService : IDialogService
{
    public async Task<RemoteAccess> ShowRemoteRequestAsync(string securityCode)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Title = Localization.Loc.Get("Remote_Request_Title"),
            Content = new TextBlock
            {
                Text = string.Format(Localization.Loc.Get("Remote_Request_Body"), securityCode),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            },
            PrimaryButtonText = Localization.Loc.Get("Remote_Request_Control"),
            SecondaryButtonText = Localization.Loc.Get("Remote_Request_View"),
            CloseButtonText = Localization.Loc.Get("Remote_Request_Deny"),
            DefaultButton = ContentDialogButton.Close, // an accidental Enter must not give access
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => RemoteAccess.Control,
            ContentDialogResult.Secondary => RemoteAccess.View,
            _ => RemoteAccess.Denied,
        };
    }

    public async Task<string?> AskTextAsync(string title, string text)
    {
        var box = new TextBox { Text = text, SelectionStart = 0, SelectionLength = text.Length };
        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Title = title,
            Content = box,
            PrimaryButtonText = Localization.Loc.Get("Dialog_Ok"),
            CloseButtonText = Localization.Loc.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        box.Loaded += (_, _) => box.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text : null;
    }

    public async Task<Host?> ShowHostEditorAsync()
    {
        var vm = new HostEditorViewModel();
        var dialog = new HostEditorDialog(vm) { XamlRoot = App.MainWindow.Content.XamlRoot };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? vm.ToHost() : null;
    }
}
