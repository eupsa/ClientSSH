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

        // Copy under the lock, draw without it: the reader thread is not held up while Direct2D lays out text.
        var screen = _session.Screen;
        int rows, cols, cursorRow, cursorCol;
        bool showCursor;
        lock (screen.Sync)
        {
            (rows, cols, cursorRow, cursorCol) = (screen.Rows, screen.Cols, screen.CursorRow, screen.CursorCol);
            if (_cells.Length < rows * cols) _cells = new Cell[rows * cols];
            screen.CopyView(_cells);
            showCursor = screen.CursorVisible && screen.ViewOffset == 0 && screen.CursorRow < screen.Rows;
        }

        for (var row = 0; row < rows; row++) DrawRow(ds, _cells.AsSpan(row * cols, cols), row, palette);

        if (showCursor)
        {
            var x = cursorCol * _cellWidth;
            var y = cursorRow * _cellHeight;
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

    private Cell[] _cells = [];

    private void DrawRow(CanvasDrawingSession ds, ReadOnlySpan<Cell> cells, int row, TerminalPalette palette)
    {
        var y = row * _cellHeight;
        var col = 0;
        while (col < cells.Length)
        {
            var start = col;
            var first = cells[col];
            var (fg, bg) = CellColors(first, palette);
            var lastInk = -1;
            for (; col < cells.Length; col++)
            {
                var cell = cells[col];
                if (cell.Fg != first.Fg || cell.Bg != first.Bg || cell.Attr != first.Attr) break;
                if (cell.Ch is not (' ' or '\0')) lastInk = col;
            }

            var x = start * _cellWidth;
            var width = (col - start) * _cellWidth;
            if (bg != palette.Background) ds.FillRectangle(x, y, width, _cellHeight, bg);
            if ((first.Attr & CellAttr.Underline) != 0) ds.DrawLine(x, y + _cellHeight - 1, x + width, y + _cellHeight - 1, fg);
            if (lastInk < 0) continue;

            // Trailing blanks are left out: they draw nothing but would still be laid out.
            _runText.Clear();
            for (var c = start; c <= lastInk; c++) _runText.Append(cells[c].Ch == '\0' ? ' ' : cells[c].Ch);
            ds.DrawText(_runText.ToString(), x, y, fg, (first.Attr & CellAttr.Bold) != 0 ? _bold : _regular);
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
