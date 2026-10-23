using System.Collections.ObjectModel;
using ClientSSH.Services.Localization;
using ClientSSH.Services.Shortcuts;
using ClientSSH.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientSSH.ViewModels;

/// <summary>The keyboard shortcuts list in Settings. Saves only the chords that differ from the defaults.</summary>
public sealed partial class ShortcutsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;

    public ObservableCollection<ShortcutItem> Items { get; } = [];

    [ObservableProperty] public partial string Error { get; set; } = "";
    [ObservableProperty] public partial bool HasError { get; set; }

    public ShortcutsViewModel(ISettingsStore store)
    {
        _store = store;
        Load();
    }

    /// <summary>Binds a pressed chord to the item, unless it would steal typing or is already used by another action.</summary>
    public void Assign(ShortcutItem item, KeyChord chord)
    {
        item.IsRecording = false;
        if (!chord.IsUsable)
        {
            ShowError(Loc.Get("Shortcuts_NeedsModifier"));
            return;
        }

        if (Items.FirstOrDefault(i => i != item && i.Chord == chord) is { } other)
        {
            ShowError(string.Format(Loc.Get("Shortcuts_InUse"), chord, other.Label));
            return;
        }

        HasError = false;
        item.Chord = chord;
        var overrides = ShortcutMap.Overrides(Items.ToDictionary(i => i.Action, i => i.Chord));
        foreach (var entry in ProfileEntries()) overrides[entry.Key] = entry.Value;
        _store.Save(_store.Current with { Shortcuts = overrides.Count == 0 ? null : overrides });
    }

    [RelayCommand]
    private void Reset()
    {
        var kept = ProfileEntries().ToDictionary(e => e.Key, e => e.Value);
        _store.Save(_store.Current with { Shortcuts = kept.Count == 0 ? null : kept });
        HasError = false;
        Load();
    }

    private void Load()
    {
        var map = new ShortcutMap(_store.Current.Shortcuts);
        Items.Clear();
        foreach (var action in Enum.GetValues<ShortcutAction>())
            Items.Add(new ShortcutItem(action, Loc.Get($"Shortcut_{action}"), map[action]));
    }

    private void ShowError(string message)
    {
        Error = message;
        HasError = true;
    }

    // Profile shortcuts (Services/Shortcuts/ShortcutService) share AppSettings.Shortcuts with these.
    private IEnumerable<KeyValuePair<string, string>> ProfileEntries() =>
        (_store.Current.Shortcuts ?? []).Where(e => e.Key.StartsWith(ShortcutService.ProfileAction, StringComparison.Ordinal));
}

public sealed partial class ShortcutItem(ShortcutAction action, string label, KeyChord chord) : ObservableObject
{
    public ShortcutAction Action { get; } = action;
    public string Label { get; } = label;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Display))] public partial KeyChord Chord { get; set; } = chord;

    /// <summary>Waiting for the user to press the new chord.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Display))] public partial bool IsRecording { get; set; }

    public string Display => IsRecording ? Loc.Get("Shortcuts_PressKeys") : Chord.ToString();

}

