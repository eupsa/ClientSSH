using System.ComponentModel;
using ClientSSH.Services.Commands;
using ClientSSH.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ClientSSH.Views;

/// <summary>Ctrl+Shift+P overlay. Only keyboard and focus plumbing here; state lives in the view model.</summary>
public sealed partial class CommandPalette : UserControl
{
    public CommandPaletteViewModel ViewModel { get; } = App.GetService<CommandPaletteViewModel>();

    private UIElement? _returnFocus;

    public CommandPalette()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;
    }

    // Remember what had focus (usually the terminal) and give it back when the palette closes.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ViewModel.IsOpen) || XamlRoot is null) return;
        if (ViewModel.IsOpen)
        {
            _returnFocus = FocusManager.GetFocusedElement(XamlRoot) as UIElement;
            DispatcherQueue.TryEnqueue(() => SearchBox.Focus(FocusState.Programmatic)); // after the card becomes visible
        }
        else if (_returnFocus is { } element && element.XamlRoot is not null)
            element.Focus(FocusState.Programmatic);
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down: ViewModel.Move(1); break;
            case VirtualKey.Up: ViewModel.Move(-1); break;
            case VirtualKey.Enter: ViewModel.RunCommand.Execute(null); break;
            case VirtualKey.Escape: ViewModel.Close(); break;
            default: return;
        }

        e.Handled = true;
    }

    // Escape from anywhere in the card (e.g. after tabbing into the list).
    private void OnCardKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        ViewModel.Close();
    }

    private void OnBackdropTapped(object sender, TappedRoutedEventArgs e) => ViewModel.Close();

    private void OnItemClick(object sender, ItemClickEventArgs e) => ViewModel.RunCommand.Execute(e.ClickedItem);

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not PaletteCommand command) return;
        ResultsList.ScrollIntoView(command);
        // Focus stays in the search box while arrows move the highlight: tell screen readers what is highlighted.
        if (SearchBox.FocusState == FocusState.Unfocused) return;
        FrameworkElementAutomationPeer.CreatePeerForElement(SearchBox).RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent,
            command.Title, "PaletteSelection");
    }
}
