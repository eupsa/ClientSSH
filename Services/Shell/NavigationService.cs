using ClientSSH.Views;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace ClientSSH.Services.Shell;

public enum PageKey { Welcome, Hosts, Sessions, Settings }

public interface INavigationService
{
    event Action<PageKey>? Navigated;
    void Attach(Frame frame);
    void NavigateTo(PageKey key, object? parameter = null);
}

public sealed class NavigationService : INavigationService
{
    private static readonly Dictionary<PageKey, Type> Pages = new()
    {
        [PageKey.Welcome] = typeof(WelcomePage),
        [PageKey.Hosts] = typeof(HostsPage),
        [PageKey.Sessions] = typeof(SessionsPage),
        [PageKey.Settings] = typeof(SettingsPage),
    };

    private Frame? _frame;

    public event Action<PageKey>? Navigated;

    public void Attach(Frame frame) => _frame = frame;

    public void NavigateTo(PageKey key, object? parameter = null)
    {
        _frame!.Navigate(Pages[key], parameter,
            key == PageKey.Sessions ? new DrillInNavigationTransitionInfo() : new EntranceNavigationTransitionInfo());
        _frame.BackStack.Clear();
        Navigated?.Invoke(key);
    }
}
