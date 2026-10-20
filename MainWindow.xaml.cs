using ClientSSH.Services.Shell;
using ClientSSH.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClientSSH
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; } = App.GetService<MainViewModel>();

        public TourViewModel Tour { get; } = App.GetService<TourViewModel>();

        public MainWindow()
        {
            InitializeComponent();
            var nav = App.GetService<INavigationService>();
            nav.Attach(RootFrame);
            nav.Navigated += key => Nav.SelectedItem = key switch
            {
                PageKey.Welcome => null,
                PageKey.Settings => SettingsNavItem,
                PageKey.Sessions => SessionsNavItem,
                _ => HostsNavItem,
            };
            App.GetService<ThemeService>().Attach((FrameworkElement)Content);
            App.GetService<TrayService>().Attach(this);

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBar);
            AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            // The caption buttons follow the system theme, not the app's: color them from the palette.
            var root = (FrameworkElement)Content;
            root.ActualThemeChanged += (_, _) => ColorCaptionButtons(root);
            ColorCaptionButtons(root);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 760));
        }

        private void ColorCaptionButtons(FrameworkElement root)
        {
            var key = root.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
            var theme = (ResourceDictionary)Application.Current.Resources.ThemeDictionaries[key];
            var text = (Windows.UI.Color)theme["TextColor"];
            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonForegroundColor = text;
            titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(0x99, text.R, text.G, text.B);
            titleBar.ButtonHoverForegroundColor = text;
            titleBar.ButtonPressedForegroundColor = text;
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x22, text.R, text.G, text.B);
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x33, text.R, text.G, text.B);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Root.Opacity = 1;
            Root.Translation = System.Numerics.Vector3.Zero;
            _ = ViewModel.StartAsync();
        }

        private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args) =>
            ViewModel.NavigateCommand.Execute(Enum.Parse<PageKey>((string)args.InvokedItemContainer.Tag));
    }
}
