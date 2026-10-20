using ClientSSH.Models;
using ClientSSH.Services.Storage;
using Microsoft.UI.Xaml;

namespace ClientSSH.Services.Shell;

public sealed class ThemeService(ISettingsStore settings)
{
    private FrameworkElement? _root;

    public void Attach(FrameworkElement root)
    {
        _root = root;
        Apply();
    }

    public void Apply()
    {
        if (_root is null) return;
        _root.RequestedTheme = settings.Current.Theme switch
        {
            ThemeChoice.Light => ElementTheme.Light,
            ThemeChoice.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }
}
