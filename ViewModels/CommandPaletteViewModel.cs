using System.Collections.ObjectModel;
using ClientSSH.Services.Commands;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientSSH.ViewModels;

/// <summary>State of the Ctrl+Shift+P overlay: the query, the ranked results and the highlighted one.</summary>
public sealed partial class CommandPaletteViewModel(ICommandRegistry registry) : ObservableObject
{
    private IReadOnlyList<PaletteCommand> _all = [];

    public ObservableCollection<PaletteCommand> Results { get; } = [];

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial string Query { get; set; } = "";
    [ObservableProperty] public partial int SelectedIndex { get; set; } = -1;
    [ObservableProperty] public partial bool HasNoResults { get; set; }

    public void Open()
    {
        _all = registry.Snapshot(); // fresh: hosts, tabs and closed sessions change between opens
        Query = "";
        Filter();
        IsOpen = true;
    }

    [RelayCommand]
    public void Close() => IsOpen = false;

    /// <summary>Moves the highlight, wrapping around at both ends.</summary>
    public void Move(int delta)
    {
        if (Results.Count == 0) return;
        SelectedIndex = ((SelectedIndex + delta) % Results.Count + Results.Count) % Results.Count;
    }

    /// <summary>Runs the given command, or the highlighted one. The palette closes first so focus goes back.</summary>
    [RelayCommand]
    private void Run(PaletteCommand? command)
    {
        command ??= SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;
        if (command is null) return;
        IsOpen = false;
        command.Execute();
    }

    partial void OnQueryChanged(string value) => Filter();

    private void Filter()
    {
        Results.Clear();
        foreach (var command in FuzzyMatcher.Rank(Query, _all)) Results.Add(command);
        SelectedIndex = Results.Count > 0 ? 0 : -1;
        HasNoResults = Results.Count == 0;
    }
}
