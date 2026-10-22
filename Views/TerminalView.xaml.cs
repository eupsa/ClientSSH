using ClientSSH.Terminal;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClientSSH.Views;

/// <summary>
/// Draws a <see cref="TerminalSession"/> with Win2D (Direct2D + DirectWrite) and sends the keyboard to it.
/// Lifecycle and layout here; drawing in TerminalView.Drawing.cs, input in TerminalView.Input.cs.
/// </summary>
public sealed partial class TerminalView : UserControl
{
    private static readonly string MonoFont =
        CanvasTextFormat.GetSystemFontFamilies().Contains("Cascadia Mono") ? "Cascadia Mono" : "Consolas";

    private readonly CursorBlink _blink = new();
    private TerminalSession? _session;
    private CanvasControl? _canvas;
    private bool _loaded;
    private CanvasTextFormat _regular = new();
    private CanvasTextFormat _bold = new();
    private float _cellWidth = 8, _cellHeight = 16;
    private bool _redrawQueued;

    public static readonly DependencyProperty TerminalFontSizeProperty = DependencyProperty.Register(
        nameof(TerminalFontSize), typeof(double), typeof(TerminalView),
        new PropertyMetadata(14.0, (d, _) => ((TerminalView)d).UpdateMetrics()));

    public double TerminalFontSize
    {
        get => (double)GetValue(TerminalFontSizeProperty);
        set => SetValue(TerminalFontSizeProperty, value);
    }

    public TerminalView()
    {
        InitializeComponent();
        UpdateMetrics();
    }

    public TerminalSession? Session
    {
        get => _session;
        set
        {
            Detach();
            _session = value;
            if (_loaded) Attach();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        Opacity = 1;
        // Not a tab stop: on a left click XAML focuses the clicked control itself after our PointerPressed, which would
        // take focus from this view (a right click does not do that, so typing only worked after one).
        _canvas = new CanvasControl { ClearColor = Colors.Transparent, IsTabStop = false };
        _canvas.Draw += OnDraw;
        CanvasHost.Children.Add(_canvas);
        // Redraw for the blink only when there is a cursor to blink: an idle terminal should cost nothing.
        _blink.Tick += () =>
        {
            if (FocusState != FocusState.Unfocused && _session?.Screen.CursorVisible == true) _canvas?.Invalidate();
        };
        _blink.Start();
        Attach();
        Focus(FocusState.Programmatic);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loaded = false;
        _blink.Stop();
        Detach();
        if (_canvas is not null)
        {
            _canvas.Draw -= OnDraw;
            // Win2D controls must be released explicitly or they leak.
            _canvas.RemoveFromVisualTree();
            CanvasHost.Children.Clear();
            _canvas = null;
        }
    }

    private void Attach()
    {
        if (_session is null) return;
        _session.Updated += OnSessionUpdated;
        FitToSize();
        _canvas?.Invalidate();
    }

    private void Detach()
    {
        if (_session is not null) _session.Updated -= OnSessionUpdated;
    }

    // Output arrives on a reader thread: coalesce into one redraw per UI turn.
    private void OnSessionUpdated()
    {
        if (_redrawQueued) return;
        _redrawQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _redrawQueued = false;
            _canvas?.Invalidate();
        });
    }

    private void OnFocusChanged(object sender, RoutedEventArgs e) => _canvas?.Invalidate();

    private void UpdateMetrics()
    {
        if (_regular is null) return;
        _regular = new CanvasTextFormat { FontFamily = MonoFont, FontSize = (float)TerminalFontSize, WordWrapping = CanvasWordWrapping.NoWrap };
        _bold = new CanvasTextFormat { FontFamily = MonoFont, FontSize = (float)TerminalFontSize, FontWeight = Microsoft.UI.Text.FontWeights.Bold, WordWrapping = CanvasWordWrapping.NoWrap };
        using var layout = new CanvasTextLayout(CanvasDevice.GetSharedDevice(), "M", _regular, 1000, 1000);
        _cellWidth = (float)layout.LayoutBounds.Width;
        _cellHeight = (float)Math.Ceiling(layout.LayoutBounds.Height);
        FitToSize();
        _canvas?.Invalidate();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => FitToSize();

    private void FitToSize()
    {
        if (_session is null || ActualWidth < 1 || ActualHeight < 1) return;
        var columns = Math.Max(1, (int)(ActualWidth / _cellWidth));
        var rows = Math.Max(1, (int)(ActualHeight / _cellHeight));
        if (columns != _session.Screen.Cols || rows != _session.Screen.Rows) _session.Resize(columns, rows);
    }
}
