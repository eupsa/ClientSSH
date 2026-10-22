namespace ClientSSH.Terminal;

public enum ColorKind : byte { Default, Indexed, Rgb }

public readonly record struct TermColor(ColorKind Kind, int Value)
{
    public static readonly TermColor Default = new(ColorKind.Default, 0);

    public static TermColor Indexed(int index) => new(ColorKind.Indexed, index);

    public static TermColor Rgb(int r, int g, int b) => new(ColorKind.Rgb, (r << 16) | (g << 8) | b);
}

[Flags]
public enum CellAttr : byte { None = 0, Bold = 1, Dim = 2, Italic = 4, Underline = 8, Inverse = 16 }

public struct Cell
{
    public char Ch;
    public TermColor Fg, Bg;
    public CellAttr Attr;
}

/// <summary>
/// Minimal VT/ANSI screen: text, cursor movement, erase, scroll regions, SGR colours, alternate screen and a
/// scrollback. No UI types, so it is unit-testable. Callers must hold <see cref="Sync"/> while reading or feeding.
/// shortcut: every character is one cell wide (no CJK/emoji double width), add wcwidth when needed.
/// </summary>
public sealed class VtScreen
{
    private const int MaxScrollback = 5000;

    private enum State { Ground, Escape, Csi, Osc, OscEscape, Charset }

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

    private State _state;
    private readonly List<int> _params = [];
    private int _current = -1;
    private char _prefix;
    private readonly System.Text.StringBuilder _osc = new();

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
        var line = _scrollCount - Math.Clamp(ViewOffset, 0, _scrollCount) + viewRow;
        var cells = line < _scrollCount ? _scrollback[(_scrollStart + line) % MaxScrollback] : _screen[line - _scrollCount];
        return col < cells.Length ? cells[col] : Blank();
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

    public void Feed(ReadOnlySpan<char> text)
    {
        foreach (var c in text) Process(c);
    }

    private void Process(char c)
    {
        switch (_state)
        {
            case State.Ground: Ground(c); break;
            case State.Escape: Escape(c); break;
            case State.Csi: Csi(c); break;
            case State.Charset: _state = State.Ground; break;
            case State.Osc:
                if (c == '\a') EndOsc();
                else if (c == '\x1b') _state = State.OscEscape;
                else if (_osc.Length < 1024) _osc.Append(c);
                break;
            case State.OscEscape:
                EndOsc();
                break;
        }
    }

    private void Ground(char c)
    {
        switch (c)
        {
            case '\x1b': _state = State.Escape; break;
            case '\r': CursorCol = 0; _wrapPending = false; break;
            case '\n': case '\v': case '\f': LineFeed(); break;
            case '\b': CursorCol = Math.Max(0, CursorCol - 1); _wrapPending = false; break;
            case '\t': CursorCol = Math.Min(Cols - 1, (CursorCol / 8 + 1) * 8); _wrapPending = false; break;
            default:
                if (c >= ' ' && c != '\x7f') Print(c);
                break;
        }
    }

    private void Escape(char c)
    {
        _state = State.Ground;
        switch (c)
        {
            case '[': StartCsi(); break;
            case ']': _osc.Clear(); _state = State.Osc; break;
            case '(': case ')': case '*': case '+': _state = State.Charset; break;
            case 'D': LineFeed(); break;
            case 'E': CursorCol = 0; LineFeed(); break;
            case 'M': ReverseIndex(); break;
            case '7': SaveCursor(); break;
            case '8': RestoreCursor(); break;
            case 'c': Reset(); break;
        }
    }

    private void StartCsi()
    {
        _state = State.Csi;
        _params.Clear();
        _current = -1;
        _prefix = '\0';
    }

    private void Csi(char c)
    {
        if (c >= '0' && c <= '9')
        {
            _current = (_current < 0 ? 0 : _current) * 10 + (c - '0');
        }
        else if (c == ';' || c == ':')
        {
            _params.Add(_current);
            _current = -1;
        }
        else if (c is '?' or '>' or '=' or '<' or '!')
        {
            _prefix = c;
        }
        else if (c >= ' ' && c <= '/')
        {
            // intermediate byte: ignored
        }
        else if (c >= '@' && c <= '~')
        {
            _params.Add(_current);
            _state = State.Ground;
            Dispatch(c);
        }
        else if (c == '\x1b')
        {
            _state = State.Escape;
        }
    }

    private int P(int index, int fallback)
    {
        var value = index < _params.Count ? _params[index] : -1;
        return value <= 0 ? fallback : value;
    }

    // Parameter where an absent value means 0 (erase modes).
    private int Mode(int index) => index < _params.Count && _params[index] >= 0 ? _params[index] : 0;

    private void Dispatch(char final)
    {
        if (_prefix == '?')
        {
            if (final is 'h' or 'l') foreach (var mode in _params) PrivateMode(mode, final == 'h');
            return;
        }

        if (_prefix != '\0')
        {
            if (final == 'c' && _prefix == '>') Reply?.Invoke("\x1b[>0;0;0c");
            return;
        }

        switch (final)
        {
            case 'A': MoveTo(CursorRow - P(0, 1), CursorCol); break;
            case 'B': MoveTo(CursorRow + P(0, 1), CursorCol); break;
            case 'C': MoveTo(CursorRow, CursorCol + P(0, 1)); break;
            case 'D': MoveTo(CursorRow, CursorCol - P(0, 1)); break;
            case 'E': MoveTo(CursorRow + P(0, 1), 0); break;
            case 'F': MoveTo(CursorRow - P(0, 1), 0); break;
            case 'G': MoveTo(CursorRow, P(0, 1) - 1); break;
            case 'd': MoveTo(P(0, 1) - 1, CursorCol); break;
            case 'H': case 'f': MoveTo(P(0, 1) - 1, P(1, 1) - 1); break;
            case 'J': EraseDisplay(Mode(0)); break;
            case 'K': EraseLine(Mode(0)); break;
            case 'L': InsertLines(P(0, 1)); break;
            case 'M': DeleteLines(P(0, 1)); break;
            case 'P': DeleteChars(P(0, 1)); break;
            case '@': InsertChars(P(0, 1)); break;
            case 'X': EraseChars(P(0, 1)); break;
            case 'S': ScrollUp(P(0, 1)); break;
            case 'T': ScrollDown(P(0, 1)); break;
            case 'm': Sgr(); break;
            case 'r': SetScrollRegion(); break;
            case 's': SaveCursor(); break;
            case 'u': RestoreCursor(); break;
            case 'n':
                if (P(0, 0) == 6) Reply?.Invoke($"\x1b[{CursorRow + 1};{CursorCol + 1}R");
                else if (P(0, 0) == 5) Reply?.Invoke("\x1b[0n");
                break;
            case 'c': Reply?.Invoke("\x1b[?1;0c"); break;
        }
    }

    private void PrivateMode(int mode, bool on)
    {
        switch (mode)
        {
            case 1: AppCursorKeys = on; break;
            case 25: CursorVisible = on; break;
            case 47: case 1047: case 1049: SwitchAlternate(on); break;
        }
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

    private void Sgr()
    {
        if (_params.Count == 1 && _params[0] < 0) _params[0] = 0;
        for (var i = 0; i < _params.Count; i++)
        {
            var p = Math.Max(0, _params[i]);
            switch (p)
            {
                case 0: _fg = _bg = TermColor.Default; _attr = CellAttr.None; break;
                case 1: _attr |= CellAttr.Bold; break;
                case 2: _attr |= CellAttr.Dim; break;
                case 3: _attr |= CellAttr.Italic; break;
                case 4: _attr |= CellAttr.Underline; break;
                case 7: _attr |= CellAttr.Inverse; break;
                case 22: _attr &= ~(CellAttr.Bold | CellAttr.Dim); break;
                case 23: _attr &= ~CellAttr.Italic; break;
                case 24: _attr &= ~CellAttr.Underline; break;
                case 27: _attr &= ~CellAttr.Inverse; break;
                case >= 30 and <= 37: _fg = TermColor.Indexed(p - 30); break;
                case 39: _fg = TermColor.Default; break;
                case >= 40 and <= 47: _bg = TermColor.Indexed(p - 40); break;
                case 49: _bg = TermColor.Default; break;
                case >= 90 and <= 97: _fg = TermColor.Indexed(p - 90 + 8); break;
                case >= 100 and <= 107: _bg = TermColor.Indexed(p - 100 + 8); break;
                case 38 or 48:
                    var color = ExtendedColor(ref i);
                    if (color is { } value)
                    {
                        if (p == 38) _fg = value;
                        else _bg = value;
                    }
                    break;
            }
        }
    }

    // 38;5;n or 38;2;r;g;b. Leaves i on the last consumed parameter.
    private TermColor? ExtendedColor(ref int i)
    {
        var mode = i + 1 < _params.Count ? _params[i + 1] : -1;
        if (mode == 5 && i + 2 < _params.Count)
        {
            var index = Math.Clamp(_params[i + 2], 0, 255);
            i += 2;
            return TermColor.Indexed(index);
        }

        if (mode == 2 && i + 4 < _params.Count)
        {
            var color = TermColor.Rgb(Math.Clamp(_params[i + 2], 0, 255), Math.Clamp(_params[i + 3], 0, 255), Math.Clamp(_params[i + 4], 0, 255));
            i += 4;
            return color;
        }

        i = _params.Count;
        return null;
    }

    private void EndOsc()
    {
        _state = State.Ground;
        var text = _osc.ToString();
        var split = text.IndexOf(';');
        if (split > 0 && text[..split] is "0" or "2") Title = text[(split + 1)..];
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
