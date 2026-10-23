namespace ClientSSH.Terminal;

/// <summary>
/// Minimal VT/ANSI screen: text, cursor movement, erase, scroll regions, SGR colours, alternate screen and a
/// scrollback. No UI types, so it is unit-testable. Callers must hold <see cref="Sync"/> while reading or feeding.
/// shortcut: every character is one cell wide (no CJK/emoji double width), add wcwidth when needed.
/// </summary>
public sealed partial class VtScreen
{
    private const int MaxScrollback = 5000;

    public readonly object Sync = new();

    private Cell[][] _screen;
    private Cell[][]? _mainScreen;
    // A ring: once full, the oldest line is overwritten (and its array reused) instead of shifting 5000 entries per new line.
    private readonly Cell[][] _scrollback = new Cell[MaxScrollback][];
    private int _scrollStart, _scrollCount;
    private int _top, _bottom;
    private bool _wrapPending;
    private TermColor _fg = TermColor.Default, _bg = TermColor.Default;
    private CellAttr _attr;
    private (int Row, int Col, TermColor Fg, TermColor Bg, CellAttr Attr) _saved;
    private (int Row, int Col) _mainCursor;

    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public int CursorRow { get; private set; }
    public int CursorCol { get; private set; }
    public bool CursorVisible { get; private set; } = true;
    public bool AppCursorKeys { get; private set; }
    public string Title { get; private set; } = "";

    /// <summary>Lines scrolled back from the live screen; 0 shows the live screen.</summary>
    public int ViewOffset { get; set; }

    public int ScrollbackCount => _scrollCount;

    /// <summary>Bytes the terminal must answer with (cursor position reports, device attributes).</summary>
    public event Action<string>? Reply;

    public VtScreen(int cols, int rows)
    {
        Cols = cols;
        Rows = rows;
        _screen = NewScreen(cols, rows);
        _bottom = rows - 1;
    }

    public Cell GetCell(int viewRow, int col)
    {
        var cells = ViewLine(viewRow);
        return col < cells.Length ? cells[col] : Blank();
    }

    /// <summary>The visible rows (scrolled back by <see cref="ViewOffset"/>), Cols cells per row, into <paramref name="cells"/>.</summary>
    public void CopyView(Span<Cell> cells)
    {
        for (var row = 0; row < Rows; row++)
        {
            var line = ViewLine(row);
            var target = cells.Slice(row * Cols, Cols);
            var count = Math.Min(line.Length, Cols);
            line.AsSpan(0, count).CopyTo(target);
            if (count < Cols) target[count..].Fill(Blank()); // a scrollback line from before a resize
        }
    }

    private Cell[] ViewLine(int viewRow)
    {
        var line = _scrollCount - Math.Clamp(ViewOffset, 0, _scrollCount) + viewRow;
        return line < _scrollCount ? _scrollback[(_scrollStart + line) % MaxScrollback] : _screen[line - _scrollCount];
    }

    public string GetLineText(int viewRow)
    {
        var chars = new char[Cols];
        for (var c = 0; c < Cols; c++) chars[c] = GetCell(viewRow, c).Ch;
        return new string(chars).TrimEnd();
    }

    public void Resize(int cols, int rows)
    {
        if (cols < 1 || rows < 1 || (cols == Cols && rows == Rows)) return;
        _screen = Resized(_screen, cols, rows);
        if (_mainScreen is not null) _mainScreen = Resized(_mainScreen, cols, rows);
        Cols = cols;
        Rows = rows;
        _top = 0;
        _bottom = rows - 1;
        CursorRow = Math.Min(CursorRow, rows - 1);
        CursorCol = Math.Min(CursorCol, cols - 1);
        _wrapPending = false;
        ViewOffset = 0;
    }

    private void SwitchAlternate(bool on)
    {
        if (on && _mainScreen is null)
        {
            _mainScreen = _screen;
            _mainCursor = (CursorRow, CursorCol);
            _screen = NewScreen(Cols, Rows);
        }
        else if (!on && _mainScreen is not null)
        {
            _screen = _mainScreen;
            _mainScreen = null;
            (CursorRow, CursorCol) = _mainCursor;
        }

        _top = 0;
        _bottom = Rows - 1;
        _wrapPending = false;
    }

    private void Print(char c)
    {
        if (_wrapPending)
        {
            CursorCol = 0;
            LineFeed();
            _wrapPending = false;
        }

        _screen[CursorRow][CursorCol] = new Cell { Ch = c, Fg = _fg, Bg = _bg, Attr = _attr };
        if (CursorCol == Cols - 1) _wrapPending = true;
        else CursorCol++;
    }

    private void MoveTo(int row, int col)
    {
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        CursorCol = Math.Clamp(col, 0, Cols - 1);
        _wrapPending = false;
    }

    private void LineFeed()
    {
        if (CursorRow == _bottom) ScrollUp(1);
        else if (CursorRow < Rows - 1) CursorRow++;
    }

    private void ReverseIndex()
    {
        if (CursorRow == _top) ScrollDown(1);
        else if (CursorRow > 0) CursorRow--;
    }

    private void ScrollUp(int count, bool keep = true)
    {
        for (var n = 0; n < Math.Min(count, _bottom - _top + 1); n++)
        {
            var spare = keep && _top == 0 && _mainScreen is null ? PushScrollback(_screen[0]) : _screen[_top];
            for (var r = _top; r < _bottom; r++) _screen[r] = _screen[r + 1];
            _screen[_bottom] = NewLine(spare);
        }
    }

    private void ScrollDown(int count)
    {
        for (var n = 0; n < Math.Min(count, _bottom - _top + 1); n++)
        {
            var spare = _screen[_bottom];
            for (var r = _bottom; r > _top; r--) _screen[r] = _screen[r - 1];
            _screen[_top] = NewLine(spare);
        }
    }

    private void InsertLines(int count)
    {
        if (CursorRow < _top || CursorRow > _bottom) return;
        var saved = _top;
        _top = CursorRow;
        ScrollDown(count);
        _top = saved;
    }

    private void DeleteLines(int count)
    {
        if (CursorRow < _top || CursorRow > _bottom) return;
        var saved = _top;
        _top = CursorRow;
        ScrollUp(count, keep: false);
        _top = saved;
    }

    private void SetScrollRegion()
    {
        var top = P(0, 1) - 1;
        var bottom = P(1, Rows) - 1;
        if (top < bottom && bottom < Rows)
        {
            _top = top;
            _bottom = bottom;
            MoveTo(0, 0);
        }
    }

    private void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0:
                EraseCells(CursorRow, CursorCol, Cols);
                for (var r = CursorRow + 1; r < Rows; r++) EraseCells(r, 0, Cols);
                break;
            case 1:
                for (var r = 0; r < CursorRow; r++) EraseCells(r, 0, Cols);
                EraseCells(CursorRow, 0, CursorCol + 1);
                break;
            case 2:
                for (var r = 0; r < Rows; r++) EraseCells(r, 0, Cols);
                break;
            case 3:
                Array.Clear(_scrollback);
                _scrollStart = _scrollCount = 0;
                ViewOffset = 0;
                break;
        }
    }

    private void EraseLine(int mode)
    {
        switch (mode)
        {
            case 0: EraseCells(CursorRow, CursorCol, Cols); break;
            case 1: EraseCells(CursorRow, 0, CursorCol + 1); break;
            case 2: EraseCells(CursorRow, 0, Cols); break;
        }
    }

    private void EraseChars(int count) => EraseCells(CursorRow, CursorCol, Math.Min(Cols, CursorCol + count));

    private void EraseCells(int row, int from, int to)
    {
        for (var c = from; c < Math.Min(to, Cols); c++) _screen[row][c] = Blank();
    }

    private void DeleteChars(int count)
    {
        var line = _screen[CursorRow];
        count = Math.Min(count, Cols - CursorCol);
        Array.Copy(line, CursorCol + count, line, CursorCol, Cols - CursorCol - count);
        EraseCells(CursorRow, Cols - count, Cols);
    }

    private void InsertChars(int count)
    {
        var line = _screen[CursorRow];
        count = Math.Min(count, Cols - CursorCol);
        Array.Copy(line, CursorCol, line, CursorCol + count, Cols - CursorCol - count);
        EraseCells(CursorRow, CursorCol, CursorCol + count);
    }

    private void SaveCursor() => _saved = (CursorRow, CursorCol, _fg, _bg, _attr);

    private void RestoreCursor()
    {
        MoveTo(_saved.Row, _saved.Col);
        (_fg, _bg, _attr) = (_saved.Fg, _saved.Bg, _saved.Attr);
    }

    private void Reset()
    {
        _screen = NewScreen(Cols, Rows);
        _mainScreen = null;
        _top = 0;
        _bottom = Rows - 1;
        _fg = _bg = TermColor.Default;
        _attr = CellAttr.None;
        CursorVisible = true;
        AppCursorKeys = false;
        MoveTo(0, 0);
    }

    private Cell Blank() => new() { Ch = ' ', Bg = _bg };

    // Returns the oldest line when the scrollback was full (free to reuse), else null.
    private Cell[]? PushScrollback(Cell[] line)
    {
        if (_scrollCount < MaxScrollback)
        {
            _scrollback[(_scrollStart + _scrollCount++) % MaxScrollback] = line;
            return null;
        }

        var oldest = _scrollback[_scrollStart];
        _scrollback[_scrollStart] = line;
        _scrollStart = (_scrollStart + 1) % MaxScrollback;
        return oldest;
    }

    // A blank line, in the array of a line that just fell off the screen when it still has the right width.
    private Cell[] NewLine(Cell[]? spare)
    {
        var line = spare?.Length == Cols ? spare : new Cell[Cols];
        Array.Fill(line, Blank());
        return line;
    }

    private static Cell[][] NewScreen(int cols, int rows)
    {
        var screen = new Cell[rows][];
        for (var r = 0; r < rows; r++)
        {
            screen[r] = new Cell[cols];
            Array.Fill(screen[r], new Cell { Ch = ' ' });
        }

        return screen;
    }

    private static Cell[][] Resized(Cell[][] old, int cols, int rows)
    {
        var screen = NewScreen(cols, rows);
        for (var r = 0; r < Math.Min(rows, old.Length); r++)
            Array.Copy(old[r], screen[r], Math.Min(cols, old[r].Length));
        return screen;
    }
}
