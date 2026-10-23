using ClientSSH.Models;
using ClientSSH.Services.Storage;
using Microsoft.UI.Xaml;

namespace ClientSSH.Services.Shell;

public interface IThemeService
{
    void Attach(FrameworkElement root);

    /// <summary>Applies the saved theme choice; call after it changes.</summary>
    void Apply();
}

public sealed class ThemeService(ISettingsStore settings) : IThemeService
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
