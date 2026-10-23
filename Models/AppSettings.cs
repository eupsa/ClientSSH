using ClientSSH.Services.Assistant;

namespace ClientSSH.Models;

public enum ThemeChoice { System, Light, Dark }

/// <summary>Window background: the brand gradient, or the Windows Mica / Acrylic material showing the desktop through.</summary>
public enum WindowBackdrop { Brand, Mica, Acrylic }

public sealed record AppSettings(
    bool OnboardingDone = false,
    ThemeChoice Theme = ThemeChoice.System,
    int TerminalFontSize = 14,
    string Language = "pt-BR",
    bool StartMinimized = false,
    bool CloseToTray = false,
    string UpdateRepo = AppSettings.DefaultUpdateRepo,
    bool SaveHistory = false,
    bool AutoUpdateCheck = true,
    AiProvider AiProvider = AiProvider.Claude,
    string AiModel = "",
    WindowState? Window = null,
    bool RemoteListen = true,
    TerminalAppearance? TerminalDefaults = null,
    Dictionary<string, TerminalAppearance>? ProfileAppearance = null,
    Dictionary<string, string>? HostProfiles = null,
    Dictionary<string, string>? Shortcuts = null,
    WindowBackdrop Backdrop = WindowBackdrop.Brand)
{
    // TerminalDefaults: global terminal appearance. ProfileAppearance: per-profile overrides keyed by profile id
    // (ShellProfile.Id, or "ssh" for hosts). HostProfiles: host id -> profile id whose appearance the host uses.
    // Shortcuts: action id -> key chord text ("" turns a default off). All null in older settings files.

    /// <summary>Open source: users can point the update check at their own fork.</summary>
    public const string DefaultUpdateRepo = "eupsa/client-ssh";
}
