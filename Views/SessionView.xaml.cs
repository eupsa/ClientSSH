using ClientSSH.Services.Customization;
using ClientSSH.Services.Sessions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClientSSH.Views;

/// <summary>The content of one tab: the terminal plus the session's status message.</summary>
public sealed partial class SessionView : UserControl
{
    public static readonly DependencyProperty TabProperty = DependencyProperty.Register(
        nameof(Tab), typeof(SessionTab), typeof(SessionView),
        new PropertyMetadata(null, (d, e) => ((SessionView)d).OnTabChanged((SessionTab?)e.OldValue, (SessionTab?)e.NewValue)));

    public SessionTab? Tab
    {
        get => (SessionTab?)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    private readonly AppearanceService _appearance = App.GetService<AppearanceService>();

    public SessionView() => InitializeComponent();

    // Settings edits reach open terminals at once.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _appearance.Changed += ApplyAppearance;
        ApplyAppearance();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _appearance.Changed -= ApplyAppearance;

    private void OnActualThemeChanged(FrameworkElement sender, object args) => PaintFrame();

    private void ApplyAppearance()
    {
        var appearance = _appearance.Resolve(_appearance.AppearanceIdFor(Tab?.Host, Tab?.Profile));
        Terminal.ApplyAppearance(appearance, _appearance.FindScheme(appearance.Scheme));
        PaintFrame();
    }

    // The padding around the canvas shows the same background as the terminal itself.
    private void PaintFrame() => TerminalFrame.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Terminal.Palette.Background);

    public void FocusTerminal() => Terminal.Focus(FocusState.Programmatic);

    public static InfoBarSeverity SeverityFor(bool isError) =>
        isError ? InfoBarSeverity.Error : InfoBarSeverity.Informational;

    private void OnTabChanged(SessionTab? oldTab, SessionTab? newTab)
    {
        if (oldTab is not null) oldTab.PropertyChanged -= OnTabPropertyChanged;
        if (newTab is null) return;
        newTab.PropertyChanged += OnTabPropertyChanged;
        ApplyAppearance();
        Terminal.Session = newTab.Session;
        FocusTerminal();
    }

    // Reconnecting swaps the tab's session: point the terminal at the new one.
    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionTab.Session)) Terminal.Session = Tab?.Session;
    }

    private void OnReconnectClick(object sender, RoutedEventArgs e)
    {
        if (Tab is { } tab) App.GetService<SessionManager>().Reconnect(tab);
    }
}
