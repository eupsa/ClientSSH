using ClientSSH.Models;
using ClientSSH.Services.Remote;
using Xunit;

namespace ClientSSH.Tests;

public class RdpLauncherTests
{
    private static Host Rdp(string address, string user = "", int port = 3389) =>
        new(Guid.NewGuid(), "pc", address, port, user, Kind: HostKind.Rdp);

    [Fact]
    public void Builds_address_and_user_lines() =>
        Assert.Equal("full address:s:192.168.0.10:3389\r\nusername:s:CORP\\ana\r\n", RdpLauncher.BuildFile(Rdp("192.168.0.10", "CORP\\ana")));

    [Fact]
    public void User_is_optional() =>
        Assert.Equal("full address:s:pc-sala.local:3390\r\n", RdpLauncher.BuildFile(Rdp("pc-sala.local", port: 3390)));

    [Theory]
    [InlineData("host\r\nshell working directory:s:C:\\", "")]
    [InlineData("-evil", "")]
    [InlineData("good.local", "ana\nalternate shell:s:cmd")]
    public void Rejects_input_that_could_add_settings(string address, string user)
    {
        Assert.False(RdpLauncher.IsValid(Rdp(address, user)));
        Assert.Throws<ArgumentException>(() => RdpLauncher.BuildFile(Rdp(address, user)));
    }

    [Fact]
    public void Rejects_a_bad_port() => Assert.False(RdpLauncher.IsValid(Rdp("good.local", port: 70000)));
}
