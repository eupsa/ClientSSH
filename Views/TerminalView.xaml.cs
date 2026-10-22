using System.Text;
using ClientSSH.Terminal;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace ClientSSH.Views;

/// <summary>Draws a <see cref="TerminalSession"/> with Win2D (Direct2D + DirectWrite) and sends the keyboard to it.</summary>
public sealed partial class TerminalView : UserControl
{
    private static readonly string MonoFont =
        CanvasTextFormat.GetSystemFontFamilies().Contains("Cascadia Mono") ? "Cascadia Mono" : "Consolas";

    private readonly DispatcherTimerBlink _blink = new();
    private TerminalSession? _session;
    private CanvasControl? _canvas;
    private bool _loaded;
    private CanvasTextFormat _regular = new();
    private CanvasTextFormat _bold = new();
    private float _cellWidth = 8, _cellHeight = 16;
    private Palette? _palette;
    private ElementTheme _paletteTheme;
    private bool _redrawQueued;
    private char _highSurrogate;

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

    /// <summary>Take the keyboard when loaded (true unless another pane of the same tab has it).</summary>
    public bool FocusOnLoad { get; set; } = true;

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
        if (FocusOnLoad) Focus(FocusState.Programmatic);
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

    // Right click pastes (like PuTTY and Windows Terminal); left click only takes focus.
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        e.Handled = true;
        _ = PasteAsync();
    }

    private async Task PasteAsync()
    {
        try
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (!content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return;
            Send(PasteText.Clean(await content.GetTextAsync()));
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // The clipboard can be locked by another app for a moment: ignore, the user can paste again.
        }
    }

    // ---- layout ----

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

    // ---- drawing ----

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var palette = GetPalette();
        var ds = args.DrawingSession;
        ds.Clear(palette.Background);
        if (_session is null) return;

        var screen = _session.Screen;
        lock (screen.Sync)
        {
            for (var row = 0; row < screen.Rows; row++) DrawRow(ds, screen, row, palette);

            var showCursor = screen.CursorVisible && screen.ViewOffset == 0 && screen.CursorRow < screen.Rows;
            if (showCursor)
            {
                var x = screen.CursorCol * _cellWidth;
                var y = screen.CursorRow * _cellHeight;
                if (FocusState != FocusState.Unfocused)
                {
                    if (_blink.On) ds.FillRectangle(x, y, _cellWidth, _cellHeight, palette.Cursor);
                }
                else
                {
                    ds.DrawRectangle(x + 0.5f, y + 0.5f, _cellWidth - 1, _cellHeight - 1, palette.Cursor);
                }
            }
        }
    }

    private readonly StringBuilder _runText = new();

    private void DrawRow(CanvasDrawingSession ds, VtScreen screen, int row, Palette palette)
    {
        var y = row * _cellHeight;
        var text = _runText;
        var col = 0;
        while (col < screen.Cols)
        {
            var start = col;
            var first = screen.GetCell(row, col);
            var (fg, bg) = CellColors(first, palette);
            text.Clear();
            while (col < screen.Cols)
            {
                var cell = screen.GetCell(row, col);
                if (cell.Fg != first.Fg || cell.Bg != first.Bg || cell.Attr != first.Attr) break;
                text.Append(cell.Ch == '\0' ? ' ' : cell.Ch);
                col++;
            }

            var x = start * _cellWidth;
            var width = (col - start) * _cellWidth;
            if (bg != palette.Background) ds.FillRectangle(x, y, width, _cellHeight, bg);

            var run = text.ToString();
            if (run.AsSpan().Trim().Length == 0 && (first.Attr & CellAttr.Underline) == 0) continue;
            ds.DrawText(run, x, y, fg, (first.Attr & CellAttr.Bold) != 0 ? _bold : _regular);
            if ((first.Attr & CellAttr.Underline) != 0) ds.DrawLine(x, y + _cellHeight - 1, x + width, y + _cellHeight - 1, fg);
        }
    }

    private static (Color Fg, Color Bg) CellColors(Cell cell, Palette palette)
    {
        var fg = palette.Resolve(cell.Fg, palette.Foreground);
        var bg = palette.Resolve(cell.Bg, palette.Background);
        if ((cell.Attr & CellAttr.Inverse) != 0) (fg, bg) = (bg, fg);
        if ((cell.Attr & CellAttr.Dim) != 0) fg = Palette.Blend(fg, bg, 0.6f);
        return (fg, bg);
    }

    private Palette GetPalette()
    {
        if (_palette is null || _paletteTheme != ActualTheme)
        {
            _paletteTheme = ActualTheme;
            _palette = Palette.Load(ActualTheme);
        }

        return _palette;
    }

    // ---- input ----

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void Send(string text)
    {
        if (_session is null) return;
        lock (_session.Screen.Sync) _session.Screen.ViewOffset = 0;
        _session.Write(text);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = IsDown(VirtualKey.Control);
        if (ctrl && e.Key == VirtualKey.V) // also Ctrl+Shift+V
        {
            _ = PasteAsync();
            e.Handled = true;
            return;
        }

        var appKeys = _session?.Screen.AppCursorKeys ?? false;
        string? sequence = e.Key switch
        {
            VirtualKey.Enter => "\r",
            VirtualKey.Back => "\x7f",
            VirtualKey.Tab => IsDown(VirtualKey.Shift) ? "\x1b[Z" : "\t",
            VirtualKey.Escape => "\x1b",
            VirtualKey.Up => appKeys ? "\x1bOA" : "\x1b[A",
            VirtualKey.Down => appKeys ? "\x1bOB" : "\x1b[B",
            VirtualKey.Right => appKeys ? "\x1bOC" : "\x1b[C",
            VirtualKey.Left => appKeys ? "\x1bOD" : "\x1b[D",
            VirtualKey.Home => "\x1b[H",
            VirtualKey.End => "\x1b[F",
            VirtualKey.Insert => "\x1b[2~",
            VirtualKey.Delete => "\x1b[3~",
            VirtualKey.PageUp => "\x1b[5~",
            VirtualKey.PageDown => "\x1b[6~",
            VirtualKey.F1 => "\x1bOP",
            VirtualKey.F2 => "\x1bOQ",
            VirtualKey.F3 => "\x1bOR",
            VirtualKey.F4 => "\x1bOS",
            VirtualKey.F5 => "\x1b[15~",
            VirtualKey.F6 => "\x1b[17~",
            VirtualKey.F7 => "\x1b[18~",
            VirtualKey.F8 => "\x1b[19~",
            VirtualKey.F9 => "\x1b[20~",
            VirtualKey.F10 => "\x1b[21~",
            VirtualKey.F11 => "\x1b[23~",
            VirtualKey.F12 => "\x1b[24~",
            >= VirtualKey.A and <= VirtualKey.Z when ctrl => ((char)(e.Key - VirtualKey.A + 1)).ToString(),
            _ => null,
        };

        if (sequence is null) return;
        Send(sequence);
        e.Handled = true;
    }

    // shortcut: no Alt combinations; add with selection support.
    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        var c = e.Character;
        if (c < ' ' || c == '\x7f' || IsDown(VirtualKey.Control)) return;

        if (char.IsHighSurrogate(c))
        {
            _highSurrogate = c;
        }
        else if (char.IsLowSurrogate(c) && _highSurrogate != 0)
        {
            Send(new string([_highSurrogate, c]));
            _highSurrogate = '\0';
        }
        else
        {
            Send(c.ToString());
        }

        e.Handled = true;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_session is null) return;
        var lines = e.GetCurrentPoint(this).Properties.MouseWheelDelta / 120 * 3;
        lock (_session.Screen.Sync)
            _session.Screen.ViewOffset = Math.Clamp(_session.Screen.ViewOffset + lines, 0, _session.Screen.ScrollbackCount);
        _canvas?.Invalidate();
    }

    // ---- helpers ----

    private sealed class DispatcherTimerBlink
    {
        private readonly Microsoft.UI.Xaml.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(530) };

        public bool On { get; private set; } = true;
        public event Action? Tick;

        public DispatcherTimerBlink()
        {
            _timer.Tick += (_, _) =>
            {
                On = !On;
                Tick?.Invoke();
            };
        }

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();
    }

    /// <summary>Terminal colours read from Themes/Palette.xaml, so the palette file stays the single source.</summary>
    private sealed class Palette
    {
        private readonly Color[] _ansi = new Color[16];

        public Color Background { get; private init; }
        public Color Foreground { get; private init; }
        public Color Cursor { get; private init; }

        public static Palette Load(ElementTheme theme)
        {
            var key = theme == ElementTheme.Light ? "Light" : "Dark";
            Color Get(string name)
            {
                foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
                    if (dictionary.ThemeDictionaries.TryGetValue(key, out var themed) &&
                        themed is ResourceDictionary resources && resources.TryGetValue(name, out var value))
                        return (Color)value;
                throw new KeyNotFoundException(name);
            }

            var palette = new Palette
            {
                Background = Get("TerminalBackgroundColor"),
                Foreground = Get("TerminalForegroundColor"),
                Cursor = Get("TerminalCursorColor"),
            };
            for (var i = 0; i < 16; i++) palette._ansi[i] = Get($"Ansi{i}Color");
            return palette;
        }

        public Color Resolve(TermColor color, Color fallback) => color.Kind switch
        {
            ColorKind.Indexed => Indexed(color.Value),
            ColorKind.Rgb => Color.FromArgb(255, (byte)(color.Value >> 16), (byte)(color.Value >> 8), (byte)color.Value),
            _ => fallback,
        };

        private Color Indexed(int index)
        {
            if (index < 16) return _ansi[index];
            if (index >= 232)
            {
                var gray = (byte)(8 + (index - 232) * 10);
                return Color.FromArgb(255, gray, gray, gray);
            }

            index -= 16;
            static byte Level(int n) => (byte)(n == 0 ? 0 : 55 + n * 40);
            return Color.FromArgb(255, Level(index / 36), Level(index / 6 % 6), Level(index % 6));
        }

        public static Color Blend(Color from, Color to, float amount) => Color.FromArgb(255,
            (byte)(from.R * amount + to.R * (1 - amount)),
            (byte)(from.G * amount + to.G * (1 - amount)),
            (byte)(from.B * amount + to.B * (1 - amount)));
    }
}
