using System.Numerics;
using ClientSSH.Models;
using ClientSSH.Services.Localization;
using ClientSSH.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ClientSSH.Views;

public sealed partial class HostsPage : Page
{
    public HostsViewModel ViewModel { get; } = App.GetService<HostsViewModel>();

    public TourViewModel Tour { get; } = App.GetService<TourViewModel>();

    public HostsPage() => InitializeComponent();

    // Cards grow a little under the pointer; the Border's ScaleTransition animates it.
    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e) => SetCardScale(sender, 1.015f);

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e) => SetCardScale(sender, 1f);

    private static void SetCardScale(object sender, float scale)
    {
        var card = (UIElement)sender;
        card.CenterPoint = new Vector3(card.ActualSize / 2, 0);
        card.Scale = new Vector3(scale, scale, 1);
    }

    // Filled star for favorites, outline otherwise.
    public static string StarGlyph(bool favorite) => favorite ? "\uE735" : "\uE734";

    public static string LastConnectedText(DateTime? utc) =>
        utc is null ? "" : string.Format(Loc.Get("Hosts_LastConnected"), RelativeTime.Describe(utc.Value));

    private void OnFavoriteClick(object sender, RoutedEventArgs e) =>
        ViewModel.ToggleFavoriteCommand.Execute(((FrameworkElement)sender).DataContext);

    // Item buttons live in a DataTemplate, so the row's Host comes from DataContext.
    private void OnConnectClick(object sender, RoutedEventArgs e) =>
        ViewModel.ConnectCommand.Execute(((FrameworkElement)sender).DataContext);

    private void OnRemoveClick(object sender, RoutedEventArgs e) =>
        ViewModel.RemoveCommand.Execute(((FrameworkElement)sender).DataContext);
}
