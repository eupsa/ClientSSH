using ClientSSH.Models;
using ClientSSH.Services.Localization;
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

    public async Task<Host?> ShowHostEditorAsync()
    {
        var vm = new HostEditorViewModel(App.GetService<Customization.ProfileService>().Profiles, Loc.Get("Editor_AppearanceSsh"));
        var dialog = new HostEditorDialog(vm) { XamlRoot = App.MainWindow.Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var host = vm.ToHost();
        if (vm.AppearanceProfileId is { } profileId) App.GetService<Customization.AppearanceService>().SetHostProfile(host.Id, profileId);
        return host;
    }
}
