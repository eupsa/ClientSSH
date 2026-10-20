using ClientSSH.Services.Updates;
using Xunit;

namespace ClientSSH.Tests;

public class Sha256SumsTests
{
    private const string Sums = "ABC123  ClientSSH-Setup-0.2.0.exe\r\nfff000 *Other.exe\r\n";

    [Fact]
    public void Matches_ignoring_case_and_line_endings() =>
        Assert.True(Sha256Sums.Matches(Sums, "ClientSSH-Setup-0.2.0.exe", "abc123"));

    [Fact]
    public void Rejects_a_different_hash() =>
        Assert.False(Sha256Sums.Matches(Sums, "ClientSSH-Setup-0.2.0.exe", "abc124"));

    [Fact]
    public void Rejects_a_file_not_listed() =>
        Assert.False(Sha256Sums.Matches(Sums, "ClientSSH-Setup-0.3.0.exe", "abc123"));

    [Fact]
    public void Accepts_the_binary_mode_marker() =>
        Assert.True(Sha256Sums.Matches(Sums, "Other.exe", "FFF000"));
}
