using ClientSSH.Services.Shortcuts;
using ClientSSH.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ClientSSH.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = App.GetService<SettingsViewModel>();

    public ShortcutsViewModel Shortcuts { get; } = App.GetService<ShortcutsViewModel>();

    public SettingsPage() => InitializeComponent();

    // ---- shortcut recorder: a click starts recording, the next key chord is handed to the view model ----

    private static ShortcutItem? ItemOf(object sender) => (sender as FrameworkElement)?.Tag as ShortcutItem;

    private void OnShortcutClick(object sender, RoutedEventArgs e)
    {
        foreach (var item in Shortcuts.Items) item.IsRecording = item == ItemOf(sender);
    }

    private void OnShortcutLostFocus(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) item.IsRecording = false;
    }

    private void OnShortcutKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ItemOf(sender) is not { IsRecording: true } item) return;
        if (e.Key == VirtualKey.Escape)
        {
            item.IsRecording = false;
            e.Handled = true;
            return;
        }

        var modifiers = KeyModifiers.None;
        if (IsDown(VirtualKey.Control)) modifiers |= KeyModifiers.Ctrl;
        if (IsDown(VirtualKey.Shift)) modifiers |= KeyModifiers.Shift;
        if (IsDown(VirtualKey.Menu)) modifiers |= KeyModifiers.Alt;
        // Plain Tab keeps moving focus; lone modifier keys wait for the real key.
        if (e.Key == VirtualKey.Tab && modifiers == KeyModifiers.None) return;
        e.Handled = true;
        if (KeyChord.KeyName((int)e.Key) is null) return;
        Shortcuts.Assign(item, new KeyChord(modifiers, (int)e.Key));
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // Binding SelectedIndex to a ComboBox filled through ItemsSource races: the box briefly reports item 0 and
    // that would overwrite the saved language. Select first, listen after.
    private void OnLanguageBoxLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        LanguageBox.SelectedIndex = ViewModel.LanguageIndex;
        LanguageBox.SelectionChanged += (_, _) =>
        {
            if (LanguageBox.SelectedIndex >= 0) ViewModel.LanguageIndex = LanguageBox.SelectedIndex;
        };
    }
}
