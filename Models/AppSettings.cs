using ClientSSH.Services.Assistant;

namespace ClientSSH.Models;

public enum ThemeChoice { System, Light, Dark }

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
    Dictionary<string, string>? Shortcuts = null)
{
    // Shortcuts: action name to chord text ("NewTab": "Ctrl+Shift+T"); only the ones changed from the defaults (Services/Shortcuts).

    /// <summary>Open source: users can point the update check at their own fork.</summary>
    public const string DefaultUpdateRepo = "eupsa/client-ssh";
}
