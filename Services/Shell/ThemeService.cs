using ClientSSH.Models;
using ClientSSH.Services.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ClientSSH.Services.Shell;

public sealed class ThemeService(ISettingsStore settings)
{
    private FrameworkElement? _root;
    private Window? _window;
    private UIElement? _brandBackground;
    private WindowBackdrop? _backdrop;

    public void Attach(FrameworkElement root)
    {
        _root = root;
        Apply();
    }

    /// <summary>The window and the element painting the brand gradient, which hides while Mica or Acrylic shows.</summary>
    public void AttachBackdrop(Window window, UIElement brandBackground)
    {
        _window = window;
        _brandBackground = brandBackground;
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
        ApplyBackdrop();
    }

    private void ApplyBackdrop()
    {
        var choice = settings.Current.Backdrop;
        if (_window is null || _brandBackground is null || choice == _backdrop) return;
        _backdrop = choice;
        _window.SystemBackdrop = choice switch
        {
            WindowBackdrop.Mica => new MicaBackdrop(),
            WindowBackdrop.Acrylic => new DesktopAcrylicBackdrop(),
            _ => null,
        };
        _brandBackground.Visibility = choice == WindowBackdrop.Brand ? Visibility.Visible : Visibility.Collapsed;
    }
}
