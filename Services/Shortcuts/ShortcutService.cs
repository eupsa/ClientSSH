using ClientSSH.Services.Customization;
using ClientSSH.Services.Storage;
using ClientSSH.ViewModels;
using Windows.System;

namespace ClientSSH.Services.Shortcuts;

/// <summary>
/// Keyboard shortcuts that open shell profiles. Like Windows Terminal, profile N gets Ctrl+Shift+N (1-9) unless the
/// user set another chord, or none, in settings. Other shortcut groups can add their own action ids here.
/// </summary>
public sealed class ShortcutService(ISettingsStore settings, ProfileService profiles, ProfilesViewModel profilesViewModel)
{
    public const string ProfileAction = "profile:";

    /// <summary>The chord text for a profile, "" when it has none.</summary>
    public string ChordFor(string profileId)
    {
        if (settings.Current.Shortcuts?.TryGetValue(ProfileAction + profileId, out var custom) == true) return custom;
        var index = profiles.Profiles.ToList().FindIndex(p => p.Id == profileId);
        return index is >= 0 and < 9 ? $"Ctrl+Shift+{index + 1}" : "";
    }

    /// <summary>Saves a chord ("" turns the shortcut off). Returns false, saving nothing, for text that is not a chord.</summary>
    public bool SetChord(string profileId, string text)
    {
        text = text.Trim();
        string value;
        if (text.Length == 0) value = "";
        else if (KeyChord.TryParse(text, out var chord)) value = chord.ToString();
        else return false;

        var map = new Dictionary<string, string>(settings.Current.Shortcuts ?? []) { [ProfileAction + profileId] = value };
        settings.Save(settings.Current with { Shortcuts = map });
        return true;
    }

    /// <summary>Runs the action bound to the pressed keys. True when one ran (the key press is then handled).</summary>
    public bool TryHandle(VirtualKey key, bool ctrl, bool shift, bool alt)
    {
        if (KeyName(key) is not { } name || !(ctrl || alt)) return false;
        var pressed = new KeyChord(ctrl, shift, alt, name);
        foreach (var profile in profiles.Profiles)
        {
            if (!KeyChord.TryParse(ChordFor(profile.Id), out var chord) || chord != pressed) continue;
            profilesViewModel.OpenProfileCommand.Execute(profile);
            return true;
        }

        return false;
    }

    private static string? KeyName(VirtualKey key) => key switch
    {
        >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((char)('0' + (key - VirtualKey.Number0))).ToString(),
        >= VirtualKey.A and <= VirtualKey.Z => ((char)key).ToString(),
        >= VirtualKey.F1 and <= VirtualKey.F24 => $"F{key - VirtualKey.F1 + 1}",
        _ => null,
    };
}
