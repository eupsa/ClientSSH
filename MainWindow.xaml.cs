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

        public RemoteViewModel Remote { get; } = App.GetService<RemoteViewModel>();

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
                PageKey.Remote => RemoteNavItem,
                _ => HostsNavItem,
            };
            App.GetService<ThemeService>().Attach((FrameworkElement)Content);
            App.GetService<ThemeService>().AttachBackdrop(this, BrandBackground);
            App.GetService<TrayService>().Attach(this);

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBar);
            AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            // The caption buttons follow the system theme, not the app's: color them from the palette.
            var root = (FrameworkElement)Content;
            root.ActualThemeChanged += (_, _) => ColorCaptionButtons(root);
            ColorCaptionButtons(root);
            // Right-to-left languages (Arabic) mirror the interface; the terminal view stays left to right.
            if (new System.Globalization.CultureInfo(App.GetService<Services.Storage.ISettingsStore>().Current.Language).TextInfo.IsRightToLeft)
                root.FlowDirection = FlowDirection.RightToLeft;
            App.GetService<WindowStateService>().Attach(this, 1180, 760);
        }

        private void ColorCaptionButtons(FrameworkElement root)
        {
            var key = root.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
            // The palette is a merged dictionary, so its theme dictionaries are not on Resources itself.
            // XamlControlsResources has its own "Dark"/"Light" too, so pick the one holding our token.
            var theme = Application.Current.Resources.MergedDictionaries
                .Where(d => d.ThemeDictionaries.ContainsKey(key))
                .Select(d => (ResourceDictionary)d.ThemeDictionaries[key])
                .First(d => d.ContainsKey("TextColor"));
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

        // Tunnels before the terminal's own KeyDown, so a shortcut wins over sending the keys to the shell.
        private void OnPreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            static bool Down(Windows.System.VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (!e.Handled && App.GetService<Services.Shortcuts.ShortcutService>().TryHandle(e.Key,
                    Down(Windows.System.VirtualKey.Control), Down(Windows.System.VirtualKey.Shift), Down(Windows.System.VirtualKey.Menu)))
                e.Handled = true;
        }

        private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args) =>
            ViewModel.NavigateCommand.Execute(Enum.Parse<PageKey>((string)args.InvokedItemContainer.Tag));
    }
}
