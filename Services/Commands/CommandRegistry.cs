namespace ClientSSH.Services.Commands;

/// <summary>
/// Everything the command palette can run. Features add their commands here (usually at startup);
/// the palette asks for a fresh <see cref="Snapshot"/> each time it opens.
/// </summary>
public interface ICommandRegistry
{
    /// <summary>Adds a command that is always listed.</summary>
    void Register(PaletteCommand command);

    /// <summary>Adds a source asked again on every open, for lists that change (saved hosts, open tabs).</summary>
    void Register(Func<IEnumerable<PaletteCommand>> source);

    /// <summary>Every command, in registration order. The first command with a given id wins.</summary>
    IReadOnlyList<PaletteCommand> Snapshot();
}

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly List<Func<IEnumerable<PaletteCommand>>> _sources = [];

    public void Register(PaletteCommand command) => _sources.Add(() => [command]);

    public void Register(Func<IEnumerable<PaletteCommand>> source) => _sources.Add(source);

    public IReadOnlyList<PaletteCommand> Snapshot() => _sources.SelectMany(s => s()).DistinctBy(c => c.Id).ToList();
}
