using System.Text;
using ClientSSH.Terminal;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace ClientSSH.Views;

// Win2D drawing of the screen: one text run per stretch of cells with the same colours and attributes.
public sealed partial class TerminalView
{
    private TerminalPalette? _palette;
    private ElementTheme _paletteTheme;
    private readonly StringBuilder _runText = new();

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

    private void DrawRow(CanvasDrawingSession ds, VtScreen screen, int row, TerminalPalette palette)
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

    private static (Color Fg, Color Bg) CellColors(Cell cell, TerminalPalette palette)
    {
        var fg = palette.Resolve(cell.Fg, palette.Foreground);
        var bg = palette.Resolve(cell.Bg, palette.Background);
        if ((cell.Attr & CellAttr.Inverse) != 0) (fg, bg) = (bg, fg);
        if ((cell.Attr & CellAttr.Dim) != 0) fg = TerminalPalette.Blend(fg, bg, 0.6f);
        return (fg, bg);
    }

    private TerminalPalette GetPalette()
    {
        if (_palette is null || _paletteTheme != ActualTheme)
        {
            _paletteTheme = ActualTheme;
            _palette = TerminalPalette.Load(ActualTheme);
        }

        return _palette;
    }
}
