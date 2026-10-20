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

    // The "+" button starts another connection: pick a host.
    private void OnAddTabButtonClick(TabView sender, object args) => ViewModel.NewSessionCommand.Execute(null);

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) =>
        ViewModel.CloseTabCommand.Execute(args.Item);
}
