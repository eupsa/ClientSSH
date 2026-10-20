using System.Runtime.InteropServices.WindowsRuntime;
using ClientSSH.Services.Remote;
using ClientSSH.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace ClientSSH.Views;

public sealed partial class RemotePage : Page
{
    public RemoteViewModel Remote { get; } = App.GetService<RemoteViewModel>();

    private bool _decoding;
    private double _imageWidth, _imageHeight;
    private long _lastMove;

    public RemotePage()
    {
        InitializeComponent();
        Remote.FrameReady += OnFrame;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Remote.FrameReady -= OnFrame;
        base.OnNavigatedFrom(e);
    }

    private void OnCopyHostCode(object sender, RoutedEventArgs e) => Copy(Remote.HostCode);

    private void OnCopyReplyCode(object sender, RoutedEventArgs e) => Copy(Remote.ViewerReplyCode);

    private static void Copy(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    // ---- showing the other screen ----

    private async void OnFrame(byte[] jpeg)
    {
        if (_decoding) return; // skip a picture rather than fall behind
        _decoding = true;
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(jpeg.AsBuffer());
            stream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            _imageWidth = image.PixelWidth;
            _imageHeight = image.PixelHeight;
            RemoteImage.Source = image;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // a damaged picture: the next one replaces it
        }
        finally
        {
            _decoding = false;
        }
    }

    // ---- sending input to the other PC ----

    // Mouse positions go as 0..65535 across the picture, so the two screens can differ in size.
    private bool TryMap(PointerRoutedEventArgs e, out int x, out int y)
    {
        x = y = 0;
        double width = RemoteImage.ActualWidth, height = RemoteImage.ActualHeight;
        if (_imageWidth <= 0 || width <= 0 || height <= 0) return false;

        var scale = Math.Min(width / _imageWidth, height / _imageHeight);
        double shownWidth = _imageWidth * scale, shownHeight = _imageHeight * scale;
        var point = e.GetCurrentPoint(RemoteImage).Position;
        var nx = (point.X - (width - shownWidth) / 2) / shownWidth;
        var ny = (point.Y - (height - shownHeight) / 2) / shownHeight;
        if (nx is < 0 or > 1 || ny is < 0 or > 1) return false;

        x = (int)(nx * 65535);
        y = (int)(ny * 65535);
        return true;
    }

    private void OnSurfacePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (Environment.TickCount64 - _lastMove < 16 || !TryMap(e, out var x, out var y)) return;
        _lastMove = Environment.TickCount64;
        Remote.SendInput(new InputEvent(InputKind.MouseMove, x, y));
    }

    private void OnSurfacePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Surface.Focus(FocusState.Pointer);
        SendButton(e, InputKind.MouseDown);
    }

    private void OnSurfacePointerReleased(object sender, PointerRoutedEventArgs e) => SendButton(e, InputKind.MouseUp);

    private void SendButton(PointerRoutedEventArgs e, InputKind kind)
    {
        if (!TryMap(e, out var x, out var y)) return;
        var button = e.GetCurrentPoint(RemoteImage).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => InputEvent.ButtonLeft,
            PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => InputEvent.ButtonRight,
            PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => InputEvent.ButtonMiddle,
            _ => -1,
        };
        if (button < 0) return;

        Remote.SendInput(new InputEvent(InputKind.MouseMove, x, y)); // be at the spot before the click
        Remote.SendInput(new InputEvent(kind, button));
    }

    private void OnSurfaceWheel(object sender, PointerRoutedEventArgs e) =>
        Remote.SendInput(new InputEvent(InputKind.Wheel, e.GetCurrentPoint(RemoteImage).Properties.MouseWheelDelta));

    private void OnSurfaceKeyDown(object sender, KeyRoutedEventArgs e) => SendKey(e, InputKind.KeyDown);

    private void OnSurfaceKeyUp(object sender, KeyRoutedEventArgs e) => SendKey(e, InputKind.KeyUp);

    private void SendKey(KeyRoutedEventArgs e, InputKind kind)
    {
        var code = (int)e.Key;
        if (code is < 1 or > 254) return;
        Remote.SendInput(new InputEvent(kind, code));
        e.Handled = true; // the keys belong to the other PC while this has focus
    }
}
