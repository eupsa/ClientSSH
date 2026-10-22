using ClientSSH.Models;
using ClientSSH.Services.Localization;
using ClientSSH.Services.Sessions;
using ClientSSH.Services.Shortcuts;
using Microsoft.UI.Input;
using ClientSSH.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace ClientSSH.Views;

public sealed partial class SessionsPage : Page
{
    public SessionsViewModel ViewModel { get; } = App.GetService<SessionsViewModel>();

    public TourViewModel Tour { get; } = App.GetService<TourViewModel>();

    private readonly HostsViewModel _hosts = App.GetService<HostsViewModel>();

    public AssistantViewModel Assistant { get; } = App.GetService<AssistantViewModel>();

    public SessionsPage()
    {
        InitializeComponent();
        Assistant.CommandInserted += FocusTerminalSoon;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Assistant.CommandInserted -= FocusTerminalSoon;
        ViewModel.Dispose();
        base.OnNavigatedFrom(e);
    }

    private void OnPromptKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        Assistant.AskCommand.Execute(null);
    }

    // The tab strip is only a picker: the keyboard belongs to the terminal. TabView moves focus onto its own
    // tab items when it loads or changes selection, so refuse that and give focus back to the terminal.
    private void OnTabsGettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (args.FocusState == FocusState.Programmatic && args.OldFocusedElement is not null) args.TryCancel();
    }

    private void OnTabsLoaded(object sender, RoutedEventArgs e) => FocusTerminalSoon();

    private void OnTabsSelectionChanged(object sender, SelectionChangedEventArgs e) => FocusTerminalSoon();

    private void OnTabsTapped(object sender, TappedRoutedEventArgs e) => FocusTerminalSoon();

    // After the tab strip finished its own focus handling.
    private void FocusTerminalSoon() => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, Current.FocusTerminal);

    // The "+" button lists the saved hosts next to it, so another tab opens without leaving the terminal.
    private void OnAddTabButtonClick(TabView sender, object args)
    {
        var menu = new MenuFlyout();
        foreach (var host in _hosts.Hosts)
        {
            var item = new MenuFlyoutItem { Text = host.Name, Tag = host };
            item.Click += OnHostMenuClick;
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
        var manage = new MenuFlyoutItem { Text = Loc.Get("Sessions_ManageHosts") };
        manage.Click += (_, _) => ViewModel.NewSessionCommand.Execute(null);
        menu.Items.Add(manage);

        menu.ShowAt(FindByName(sender, "AddButton") ?? sender);
    }

    private void OnHostMenuClick(object sender, RoutedEventArgs e) =>
        _hosts.OpenTabCommand.Execute(((MenuFlyoutItem)sender).Tag as Host);

    private static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Name: var n } element && n == name) return element;
            if (FindByName(child, name) is { } found) return found;
        }
        return null;
    }

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) =>
        ViewModel.CloseTabCommand.Execute(args.Item);

    // ---- tab menu and shortcuts: thin forwards to the view model's commands ----

    private static SessionTab? TabOf(object sender) => (sender as FrameworkElement)?.Tag as SessionTab;

    private void OnTabDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        ViewModel.RenameTabCommand.Execute((sender as FrameworkElement)?.DataContext as SessionTab);

    private void OnRenameClick(object sender, RoutedEventArgs e) => ViewModel.RenameTabCommand.Execute(TabOf(sender));

    private void OnSplitVerticalClick(object sender, RoutedEventArgs e) => ViewModel.SplitVerticalCommand.Execute(TabOf(sender));

    private void OnSplitHorizontalClick(object sender, RoutedEventArgs e) => ViewModel.SplitHorizontalCommand.Execute(TabOf(sender));

    private void OnClosePaneClick(object sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab) ViewModel.Sessions.Selected = tab; // the command closes the selected tab's focused pane
        ViewModel.ClosePaneCommand.Execute(null);
    }

    private void OnCloseTabClick(object sender, RoutedEventArgs e) => ViewModel.CloseTabCommand.Execute(TabOf(sender));

    private void OnPagePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Typing in the assistant box is not a terminal shortcut target.
        if (e.OriginalSource is TextBox or PasswordBox) return;
        var modifiers = KeyModifiers.None;
        if (IsDown(Windows.System.VirtualKey.Control)) modifiers |= KeyModifiers.Ctrl;
        if (IsDown(Windows.System.VirtualKey.Shift)) modifiers |= KeyModifiers.Shift;
        if (IsDown(Windows.System.VirtualKey.Menu)) modifiers |= KeyModifiers.Alt;
        // Focus follows by itself: tab changes, new panes and closed panes already move it to the right terminal.
        e.Handled = ViewModel.TryRunShortcut(new KeyChord(modifiers, (int)e.Key));
    }

    private static bool IsDown(Windows.System.VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
}
