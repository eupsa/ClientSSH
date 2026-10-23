using ClientSSH.Services.Commands;
using Xunit;

namespace ClientSSH.Tests;

public class FuzzyMatcherTests
{
    private static PaletteCommand Cmd(string title, string keywords = "") => new(title, title, () => { }, keywords);

    private static string[] Rank(string query, params PaletteCommand[] commands) =>
        FuzzyMatcher.Rank(query, commands).Select(c => c.Title).ToArray();

    [Theory]
    [InlineData("gts", "Go to Settings")]
    [InlineData("THEME", "Theme: Dark")]
    [InlineData("configuracoes", "Ir para Configurações")]
    [InlineData("th da", "Theme: Dark")]
    [InlineData("bad", "xbad b")] // preferring the word start "b" would strand "ad"; must still match
    public void Matches_in_order_ignoring_case_accents_and_spaces(string query, string text) =>
        Assert.NotNull(FuzzyMatcher.Score(query, text));

    [Theory]
    [InlineData("xyz", "Go to Settings")]
    [InlineData("gts", "Settings")] // out of order
    [InlineData("hostss", "Hosts")]
    public void Rejects_what_is_not_a_subsequence(string query, string text) =>
        Assert.Null(FuzzyMatcher.Score(query, text));

    [Fact]
    public void Word_starts_beat_mid_word_matches() =>
        Assert.Equal(["Go to Settings", "Reset all hosts"], Rank("set", Cmd("Reset all hosts"), Cmd("Go to Settings")));

    [Fact]
    public void Consecutive_letters_beat_scattered_ones() =>
        Assert.Equal(["Update host", "Unpaired dock"], Rank("upd", Cmd("Unpaired dock"), Cmd("Update host")));

    [Fact]
    public void Initials_find_multi_word_titles() =>
        Assert.Equal("Restore closed session", Rank("rcs", Cmd("Open remote access"), Cmd("Restore closed session"))[0]);

    [Fact]
    public void Empty_query_keeps_registration_order() =>
        Assert.Equal(["b", "a", "c"], Rank("  ", Cmd("b"), Cmd("a"), Cmd("c")));

    [Fact]
    public void Keywords_match_but_rank_below_an_equal_title_match()
    {
        var byKeyword = Cmd("Tema: escuro", keywords: "theme dark");
        var byTitle = Cmd("Theme: Dark");
        Assert.Equal(["Theme: Dark", "Tema: escuro"], Rank("theme", byKeyword, byTitle));
        Assert.Equal(["Tema: escuro"], Rank("dark", byKeyword, Cmd("Close tab")));
    }

    [Fact]
    public void Ties_keep_registration_order() =>
        Assert.Equal(["Open SSH: web", "Open SSH: db"], Rank("open", Cmd("Open SSH: web"), Cmd("Open SSH: db")));
}

public class CommandRegistryTests
{
    [Fact]
    public void Sources_are_read_again_on_every_snapshot_and_ids_are_unique()
    {
        var registry = new CommandRegistry();
        var hosts = new List<string> { "web" };
        registry.Register(new PaletteCommand("go.hosts", "Go to Hosts", () => { }));
        registry.Register(() => hosts.Select(h => new PaletteCommand("open." + h, "Open SSH: " + h, () => { })));
        registry.Register(new PaletteCommand("go.hosts", "Duplicate", () => { }));

        Assert.Equal(["Go to Hosts", "Open SSH: web"], registry.Snapshot().Select(c => c.Title));
        hosts.Add("db");
        Assert.Equal(3, registry.Snapshot().Count);
    }
}
