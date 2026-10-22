using ClientSSH.Terminal;
using Xunit;

namespace ClientSSH.Tests;

public class VtScreenTests
{
    private static VtScreen Feed(string text, int cols = 20, int rows = 5)
    {
        var screen = new VtScreen(cols, rows);
        screen.Feed(text);
        return screen;
    }

    [Fact]
    public void PrintsTextAcrossLines()
    {
        var s = Feed("hello\r\nworld");
        Assert.Equal("hello", s.GetLineText(0));
        Assert.Equal("world", s.GetLineText(1));
    }

    [Fact]
    public void MovesCursorAndErases()
    {
        var s = Feed("\x1b[2;3HX");
        Assert.Equal('X', s.GetCell(1, 2).Ch);
        s.Feed("\x1b[2J");
        Assert.Equal("", s.GetLineText(1));
    }

    [Fact]
    public void AppliesColors()
    {
        var s = Feed("\x1b[31mA\x1b[0mB\x1b[38;5;200mC\x1b[38;2;1;2;3mD");
        Assert.Equal(TermColor.Indexed(1), s.GetCell(0, 0).Fg);
        Assert.Equal(TermColor.Default, s.GetCell(0, 1).Fg);
        Assert.Equal(TermColor.Indexed(200), s.GetCell(0, 2).Fg);
        Assert.Equal(TermColor.Rgb(1, 2, 3), s.GetCell(0, 3).Fg);
    }

    [Fact]
    public void WrapsAtRightEdge()
    {
        var s = Feed("abcdefg", cols: 5);
        Assert.Equal("abcde", s.GetLineText(0));
        Assert.Equal("fg", s.GetLineText(1));
    }

    [Fact]
    public void ScrollsLinesIntoScrollback()
    {
        var s = Feed("1\r\n2\r\n3\r\n4", rows: 3);
        Assert.Equal(1, s.ScrollbackCount);
        Assert.Equal("2", s.GetLineText(0));
        s.ViewOffset = 1;
        Assert.Equal("1", s.GetLineText(0));
    }

    [Fact]
    public void FullScrollbackDropsTheOldestLinesAndReusesThemBlank()
    {
        var s = new VtScreen(20, 3);
        for (var i = 0; i < 5100; i++) s.Feed($"\x1b[41m{i}\r\n");
        s.Feed("\x1b[0m");

        Assert.Equal(5000, s.ScrollbackCount);
        s.ViewOffset = 5000;
        Assert.Equal("98", s.GetLineText(0)); // 5100 lines + the empty last row: the first 98 fell off
        s.ViewOffset = 0;
        Assert.Equal("", s.GetLineText(2));
        Assert.Equal(TermColor.Indexed(1), s.GetCell(2, 5).Bg); // the reused line is blanked with the current background
    }

    [Fact]
    public void AnswersCursorPositionReport()
    {
        var replies = new List<string>();
        var s = new VtScreen(20, 5);
        s.Reply += replies.Add;
        s.Feed("ab\x1b[6n");
        Assert.Equal(["\x1b[1;3R"], replies);
    }

    [Fact]
    public void AlternateScreenRestoresMainScreen()
    {
        var s = Feed("main\x1b[?1049h\x1b[Halt\x1b[?1049l");
        Assert.Equal("main", s.GetLineText(0));
    }

    [Fact]
    public void DeletesCharacters()
    {
        var s = Feed("abcdef\x1b[1G\x1b[2P");
        Assert.Equal("cdef", s.GetLineText(0));
    }

    [Fact]
    public void ParsesSequenceSplitAcrossChunks()
    {
        var s = new VtScreen(20, 5);
        s.Feed("\x1b[3");
        s.Feed("1mX");
        Assert.Equal(TermColor.Indexed(1), s.GetCell(0, 0).Fg);
    }

    [Fact]
    public void ResizeKeepsContent()
    {
        var s = Feed("keep");
        s.Resize(10, 3);
        Assert.Equal("keep", s.GetLineText(0));
        Assert.Equal(10, s.Cols);
    }

    [Fact]
    public void ReadsWindowTitle()
    {
        var s = Feed("\x1b]0;my title\a");
        Assert.Equal("my title", s.Title);
    }
}
