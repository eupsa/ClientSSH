using System.Windows.Input;
using ClientSSH.Services.Customization;
using ClientSSH.Services.Sessions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClientSSH.Views;

/// <summary>One pane: the terminal plus the session's status message.</summary>
public sealed partial class SessionView : UserControl
{
    public static readonly DependencyProperty PaneProperty = DependencyProperty.Register(
        nameof(Pane), typeof(SessionPane), typeof(SessionView),
        new PropertyMetadata(null, (d, e) => ((SessionView)d).OnPaneChanged((SessionPane?)e.OldValue, (SessionPane?)e.NewValue)));

    public static readonly DependencyProperty ShowFocusProperty = DependencyProperty.Register(
        nameof(ShowFocus), typeof(bool), typeof(SessionView), new PropertyMetadata(false));

    public static readonly DependencyProperty ReconnectCommandProperty = DependencyProperty.Register(
        nameof(ReconnectCommand), typeof(ICommand), typeof(SessionView), new PropertyMetadata(null));

    private readonly AppearanceService _appearance = App.GetService<AppearanceService>();

    public SessionPane? Pane
    {
        get => (SessionPane?)GetValue(PaneProperty);
        set => SetValue(PaneProperty, value);
    }

    /// <summary>Run with the pane when the user asks to reconnect it.</summary>
    public ICommand? ReconnectCommand
    {
        get => (ICommand?)GetValue(ReconnectCommandProperty);
        set => SetValue(ReconnectCommandProperty, value);
    }

    /// <summary>Draw the accent border on the focused pane (only useful when the tab is split).</summary>
    public bool ShowFocus
    {
        get => (bool)GetValue(ShowFocusProperty);
        set => SetValue(ShowFocusProperty, value);
    }

    /// <summary>Panes of a split tab load together: only the focused one may take the keyboard.</summary>
    public bool FocusOnLoad
    {
        get => Terminal.FocusOnLoad;
        set => Terminal.FocusOnLoad = value;
    }

    /// <summary>The user clicked into this pane's terminal.</summary>
    public event Action? PaneActivated;

    public SessionView() => InitializeComponent();

    // Settings edits reach open terminals at once.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _appearance.Changed += ApplyAppearance;
        ApplyAppearance();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _appearance.Changed -= ApplyAppearance;
        if (Pane is { } pane) pane.PropertyChanged -= OnPanePropertyChanged;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) => PaintFrame();

    private void ApplyAppearance()
    {
        var appearance = _appearance.Resolve(_appearance.AppearanceIdFor(Pane?.Host, Pane?.Profile));
        Terminal.ApplyAppearance(appearance, _appearance.FindScheme(appearance.Scheme));
        PaintFrame();
    }

    // The padding around the canvas shows the same background as the terminal itself.
    private void PaintFrame() => TerminalFrame.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Terminal.Palette.Background);

    public void FocusTerminal() => Terminal.Focus(FocusState.Programmatic);

    public static InfoBarSeverity SeverityFor(bool isError) =>
        isError ? InfoBarSeverity.Error : InfoBarSeverity.Informational;

    public static Visibility FocusVisibility(bool isFocused, bool showFocus) =>
        isFocused && showFocus ? Visibility.Visible : Visibility.Collapsed;

    private void OnPaneChanged(SessionPane? oldPane, SessionPane? newPane)
    {
        if (oldPane is not null) oldPane.PropertyChanged -= OnPanePropertyChanged;
        if (newPane is null) return;
        newPane.PropertyChanged += OnPanePropertyChanged;
        ApplyAppearance();
        Terminal.Session = newPane.Session;
    }

    // Reconnecting swaps the pane's session: point the terminal at the new one.
    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionPane.Session)) Terminal.Session = Pane?.Session;
    }

    // Programmatic focus comes from the layout itself; a click or Tab press means the user picked this pane.
    private void OnTerminalGotFocus(object sender, RoutedEventArgs e)
    {
        if (Terminal.FocusState != FocusState.Programmatic) PaneActivated?.Invoke();
    }
}
