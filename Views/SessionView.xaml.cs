using System.Windows.Input;
using ClientSSH.Services.Sessions;
using ClientSSH.Services.Storage;
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

    public static readonly DependencyProperty ReconnectCommandProperty = DependencyProperty.Register(
        nameof(ReconnectCommand), typeof(ICommand), typeof(SessionView), new PropertyMetadata(null));

    /// <summary>Run with the tab when the user asks to reconnect it.</summary>
    public ICommand? ReconnectCommand
    {
        get => (ICommand?)GetValue(ReconnectCommandProperty);
        set => SetValue(ReconnectCommandProperty, value);
    }

    public SessionView()
    {
        InitializeComponent();
        Terminal.TerminalFontSize = App.GetService<ISettingsStore>().Current.TerminalFontSize;
    }

    public void FocusTerminal() => Terminal.Focus(FocusState.Programmatic);

    public static InfoBarSeverity SeverityFor(bool isError) =>
        isError ? InfoBarSeverity.Error : InfoBarSeverity.Informational;

    private void OnTabChanged(SessionTab? oldTab, SessionTab? newTab)
    {
        if (oldTab is not null) oldTab.PropertyChanged -= OnTabPropertyChanged;
        if (newTab is null) return;
        newTab.PropertyChanged += OnTabPropertyChanged;
        Terminal.Session = newTab.Session;
        FocusTerminal();
    }

    // Reconnecting swaps the tab's session: point the terminal at the new one.
    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionTab.Session)) Terminal.Session = Tab?.Session;
    }

}
