using System.Text;
using ClientSSH.Terminal;
using Xunit;

namespace ClientSSH.Tests;

public class SimulatedProcessTests
{
    private sealed class Run
    {
        public readonly SimulatedProcess Process = new("banner", "help text", "not found");
        public readonly StringBuilder Output = new();
        public int? ExitCode;

        public Run()
        {
            Process.Output += text => Output.Append(text);
            Process.Exited += code => ExitCode = code;
            Process.Start(80, 24);
        }

        public string Type(string input)
        {
            Output.Clear();
            Process.Write(input);
            return Output.ToString();
        }
    }

    [Fact]
    public void StartsWithBannerAndPrompt()
    {
        var run = new Run();
        Assert.Contains("banner", run.Output.ToString());
        Assert.Contains("demo@demo", run.Output.ToString());
    }

    [Fact]
    public void EchoesTypedCharactersAndRunsCommands()
    {
        var run = new Run();
        var output = run.Type("echo hi\r");
        Assert.StartsWith("echo hi\r\n", output);
        Assert.Contains("hi\r\n", output);
    }

    [Fact]
    public void BackspaceEditsTheLine()
    {
        var run = new Run();
        run.Type("ecx");
        Assert.Equal("\b \b", run.Type("\x7f"));
        Assert.Contains("hi\r\n", run.Type("ho hi\r"));
    }

    [Fact]
    public void UnknownCommandReportsNotFound()
    {
        var run = new Run();
        Assert.Contains("nope: not found", run.Type("nope\r"));
    }

    [Fact]
    public void IgnoresEscapeSequencesFromArrowKeys()
    {
        var run = new Run();
        Assert.Equal("", run.Type("\x1b[A\x1bOB"));
    }

    [Fact]
    public void ExitEndsTheSession()
    {
        var run = new Run();
        run.Type("exit\r");
        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void ClearEmitsEraseSequence()
    {
        var run = new Run();
        Assert.Contains("\x1b[2J", run.Type("clear\r"));
    }
}
