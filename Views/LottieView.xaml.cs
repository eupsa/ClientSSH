using CommunityToolkit.WinUI.Lottie;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.UI.ViewManagement;

namespace ClientSSH.Views;

/// <summary>
/// Plays one of the brand animations (Assets/Animations, from brand/anim). Picks the light or dark variant
/// for the current theme and honors the Windows "animation effects" setting.
/// </summary>
public sealed partial class LottieView : UserControl
{
    public static readonly DependencyProperty AnimationProperty = DependencyProperty.Register(
        nameof(Animation), typeof(string), typeof(LottieView), new PropertyMetadata("", (d, _) => ((LottieView)d).Reload()));

    public static readonly DependencyProperty LoopProperty = DependencyProperty.Register(
        nameof(Loop), typeof(bool), typeof(LottieView), new PropertyMetadata(true, (d, _) => ((LottieView)d).Reload()));

    private static readonly string AnimationFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Animations");

    // One per app: creating it per reload was needless work.
    private static readonly UISettings WindowsSettings = new();

    private bool _loaded;

    /// <summary>File name without extension, e.g. "connecting".</summary>
    public string Animation
    {
        get => (string)GetValue(AnimationProperty);
        set => SetValue(AnimationProperty, value);
    }

    public bool Loop
    {
        get => (bool)GetValue(LoopProperty);
        set => SetValue(LoopProperty, value);
    }

    public LottieView()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Reload();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        Reload();
    }

    // Decorative: any failure (missing or unsupported file) just leaves the space empty.
    private async void Reload()
    {
        if (!_loaded || string.IsNullOrEmpty(Animation)) return;

        var themed = Animation + (ActualTheme == ElementTheme.Dark ? ".dark.json" : ".json");
        var path = Path.Combine(AnimationFolder, File.Exists(Path.Combine(AnimationFolder, themed)) ? themed : Animation + ".json");

        try
        {
            // Load from the file path (not ms-appx): the same code works packaged and unpackaged.
            var source = new LottieVisualSource();
            await source.SetSourceAsync(await StorageFile.GetFileFromPathAsync(path));
            Player.Source = source;

            // With animations off in Windows, show a still frame: the first for loops, the last for one-shots.
            if (WindowsSettings.AnimationsEnabled) _ = Player.PlayAsync(0, 1, Loop);
            else Player.SetProgress(Loop ? 0 : 1);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Player.Source = null;
        }
    }
}
