namespace ClientSSH.Terminal;

// The escape-sequence state machine; it only calls the buffer operations in VtScreen.cs.
public sealed partial class VtScreen
{
    private enum State { Ground, Escape, Csi, Osc, OscEscape, Charset }

    private State _state;
    private readonly List<int> _params = [];
    private int _current = -1;
    private char _prefix;
    private readonly System.Text.StringBuilder _osc = new();

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
}
