using ClientSSH.Services.Remote;
using ClientSSH.ViewModels;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.System;

namespace ClientSSH.Views;

public sealed partial class RemotePage : Page
{
    public RemoteViewModel Remote { get; } = App.GetService<RemoteViewModel>();

    private CanvasControl? _canvas;
    private CanvasBitmap? _picture;
    private long _lastMove;

    public RemotePage()
    {
        InitializeComponent();
        Remote.FrameReady += OnFrame;
        Loaded += (_, _) =>
        {
            // Win2D draws each new picture as soon as it is decoded (up to 60 per second).
            _canvas = new CanvasControl { ClearColor = Colors.Transparent, IsTabStop = false };
            _canvas.Draw += OnDraw;
            Surface.Child = _canvas;
        };
        Unloaded += (_, _) =>
        {
            _picture?.Dispose();
            _picture = null;
            _canvas?.RemoveFromVisualTree();
            _canvas = null;
            Surface.Child = null;
        };
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Remote.FrameReady -= OnFrame;
        base.OnNavigatedFrom(e);
    }

    private void OnCopyNumber(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(Remote.MyNumber.Replace(" ", ""));
        Clipboard.SetContent(package);
    }

    private void OnNumberKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        Remote.ConnectCommand.Execute(null);
        e.Handled = true;
    }

    // ---- showing the other screen ----

    private void OnFrame(byte[] bgra, int width, int height)
    {
        if (_canvas is null || !_canvas.ReadyToDraw) return;
        if (_picture is null || _picture.SizeInPixels.Width != width || _picture.SizeInPixels.Height != height)
        {
            _picture?.Dispose();
            _picture = CanvasBitmap.CreateFromBytes(_canvas, bgra, width, height, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96);
        }
        else
        {
            _picture.SetPixelBytes(bgra);
        }
        _canvas.Invalidate();
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_picture is not null) args.DrawingSession.DrawImage(_picture, Fit(), _picture.Bounds);
    }

    // Where the picture sits in the surface, kept to its proportions (in DIPs).
    private Rect Fit()
    {
        if (_picture is null || _canvas is null) return default;
        double width = _canvas.ActualWidth, height = _canvas.ActualHeight;
        var size = _picture.SizeInPixels;
        var scale = Math.Min(width / size.Width, height / size.Height);
        double shownWidth = size.Width * scale, shownHeight = size.Height * scale;
        return new Rect((width - shownWidth) / 2, (height - shownHeight) / 2, shownWidth, shownHeight);
    }

    // ---- sending input to the other PC ----

    // Mouse positions go as 0..65535 across the picture, so the two screens can differ in size.
    private bool TryMap(PointerRoutedEventArgs e, out int x, out int y)
    {
        x = y = 0;
        if (_canvas is null) return false;
        var area = Fit();
        if (area.Width <= 0 || area.Height <= 0) return false;

        var point = e.GetCurrentPoint(_canvas).Position;
        var nx = (point.X - area.X) / area.Width;
        var ny = (point.Y - area.Y) / area.Height;
        if (nx is < 0 or > 1 || ny is < 0 or > 1) return false;

        x = (int)(nx * 65535);
        y = (int)(ny * 65535);
        return true;
    }

    private void OnSurfacePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (Environment.TickCount64 - _lastMove < 8 || !TryMap(e, out var x, out var y)) return;
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
        if (_canvas is null || !TryMap(e, out var x, out var y)) return;
        var button = e.GetCurrentPoint(_canvas).Properties.PointerUpdateKind switch
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

    private void OnSurfaceWheel(object sender, PointerRoutedEventArgs e)
    {
        if (_canvas is not null) Remote.SendInput(new InputEvent(InputKind.Wheel, e.GetCurrentPoint(_canvas).Properties.MouseWheelDelta));
    }

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
