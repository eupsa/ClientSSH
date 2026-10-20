using ClientSSH.Services.Assistant;
using Xunit;

namespace ClientSSH.Tests;

public class CommandTextTests
{
    [Fact]
    public void Keeps_a_plain_command() =>
        Assert.Equal("ls -la /var/log", CommandText.Clean("ls -la /var/log"));

    [Fact]
    public void Strips_code_fences_and_backticks() =>
        Assert.Equal("df -h", CommandText.Clean("```bash\ndf -h\n```"));

    [Fact]
    public void Takes_only_the_first_line_so_nothing_extra_runs() =>
        Assert.Equal("ls", CommandText.Clean("ls\nrm -rf /"));

    [Fact]
    public void Removes_control_characters_and_escape_sequences() =>
        Assert.Equal("ls[2Jid", CommandText.Clean("ls\u001b[2Jid\r"));

    [Fact]
    public void Returns_empty_for_blank_or_null()
    {
        Assert.Equal("", CommandText.Clean("  \n "));
        Assert.Equal("", CommandText.Clean(null));
    }
}
