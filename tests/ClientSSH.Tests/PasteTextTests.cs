using ClientSSH.Terminal;
using Xunit;

namespace ClientSSH.Tests;

public class PasteTextTests
{
    [Fact]
    public void Line_breaks_become_enter() =>
        Assert.Equal("a\rb\rc", PasteText.Clean("a\r\nb\nc"));

    [Fact]
    public void Escape_and_other_control_characters_are_dropped() =>
        Assert.Equal("ls[31mid", PasteText.Clean("ls\u001b[31m\u0007id"));

    [Fact]
    public void Keeps_tabs_and_unicode() =>
        Assert.Equal("a\tb ação", PasteText.Clean("a\tb ação"));

    [Fact]
    public void Empty_stays_empty()
    {
        Assert.Equal("", PasteText.Clean(null));
        Assert.Equal("", PasteText.Clean(""));
    }
}
