using ClientSSH.Services.Localization;
using ClientSSH.Services.Sessions;
using ClientSSH.Services.Shell;
using ClientSSH.Services.Shortcuts;
using ClientSSH.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientSSH.ViewModels;

/// <summary>
/// Backs the sessions page. The sessions themselves live in <see cref="SessionManager"/>. Every tab and pane action
/// is a command here, so the keyboard shortcuts, the tab menu and (later) the command palette share them.
/// Commands that take a tab or pane act on the selected tab / focused pane when given null.
/// </summary>
public sealed partial class SessionsViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private ShortcutMap? _shortcuts;
    private Dictionary<string, string>? _shortcutsSource;

    public SessionManager Sessions { get; }

    [ObservableProperty] public partial bool HasTabs { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }

    public SessionsViewModel(SessionManager sessions, INavigationService navigation, IDialogService dialogs, ISettingsStore settings)
    {
        Sessions = sessions;
        _navigation = navigation;
        _dialogs = dialogs;
        _settings = settings;
        Sessions.Tabs.CollectionChanged += OnTabsChanged;
        Refresh();
    }

    /// <summary>The current shortcuts; rebuilt when Settings saves new ones.</summary>
    public ShortcutMap Shortcuts
    {
        get
        {
            var source = _settings.Current.Shortcuts;
            if (_shortcuts is null || !ReferenceEquals(source, _shortcutsSource))
            {
                _shortcuts = new ShortcutMap(source);
                _shortcutsSource = source;
            }

            return _shortcuts;
        }
    }

    /// <summary>Runs the action bound to a pressed key chord. False when the chord is not a shortcut (the terminal gets it).</summary>
    public bool TryRunShortcut(KeyChord chord)
    {
        if (Shortcuts.Match(chord) is not { } action) return false;
        System.Windows.Input.ICommand command = action switch
        {
            ShortcutAction.NewTab => NewTabCommand,
            ShortcutAction.CloseTab => CloseTabCommand,
            ShortcutAction.NextTab => NextTabCommand,
            ShortcutAction.PreviousTab => PreviousTabCommand,
            ShortcutAction.SplitVertical => SplitVerticalCommand,
            ShortcutAction.SplitHorizontal => SplitHorizontalCommand,
            ShortcutAction.ClosePane => ClosePaneCommand,
            ShortcutAction.FocusNextPane => FocusNextPaneCommand,
            ShortcutAction.RenameTab => RenameTabCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(chord)),
        };
        command.Execute(null);
        return true;
    }

    [RelayCommand]
    private void NewSession() => _navigation.NavigateTo(PageKey.Hosts);

    /// <summary>A new tab on the focused pane's host; with no tab open, the host list.</summary>
    [RelayCommand]
    private void NewTab()
    {
        if (Sessions.Selected is { } tab) Sessions.Open(tab.Focused.Host);
        else NewSession();
    }

    [RelayCommand]
    private void CloseTab(SessionTab? tab)
    {
        if ((tab ?? Sessions.Selected) is not { } target) return;
        Sessions.Close(target);
        LeaveWhenEmpty();
    }

    [RelayCommand]
    private void NextTab() => Sessions.SelectNext(+1);

    [RelayCommand]
    private void PreviousTab() => Sessions.SelectNext(-1);

    /// <summary>Splits the focused pane with a vertical divider: the new pane opens on the right.</summary>
    [RelayCommand]
    private void SplitVertical(SessionTab? tab) => Split(tab, sideBySide: true);

    /// <summary>Splits the focused pane with a horizontal divider: the new pane opens below.</summary>
    [RelayCommand]
    private void SplitHorizontal(SessionTab? tab) => Split(tab, sideBySide: false);

    [RelayCommand]
    private void ClosePane(SessionPane? pane)
    {
        if (Sessions.Selected is not { } tab) return;
        Sessions.ClosePane(tab, pane ?? tab.Focused);
        LeaveWhenEmpty();
    }

    [RelayCommand]
    private void FocusNextPane() => Sessions.Selected?.Layout.FocusNext(+1);

    [RelayCommand]
    private void FocusPreviousPane() => Sessions.Selected?.Layout.FocusNext(-1);

    [RelayCommand]
    private async Task RenameTabAsync(SessionTab? tab)
    {
        if ((tab ?? Sessions.Selected) is not { } target) return;
        var name = (await _dialogs.AskTextAsync(Loc.Get("Sessions_RenameTitle"), target.Title))?.Trim();
        if (!string.IsNullOrEmpty(name)) target.Title = name;
    }

    public void Dispose() => Sessions.Tabs.CollectionChanged -= OnTabsChanged;

    private void Split(SessionTab? tab, bool sideBySide)
    {
        if ((tab ?? Sessions.Selected) is { } target) Sessions.Split(target, sideBySide);
    }

    private void LeaveWhenEmpty()
    {
        if (Sessions.Tabs.Count == 0) _navigation.NavigateTo(PageKey.Hosts);
    }

    private void OnTabsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        HasTabs = Sessions.Tabs.Count > 0;
        IsEmpty = !HasTabs;
    }
}
