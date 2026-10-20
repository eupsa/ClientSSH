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
    bool SaveHistory = false)
{
    /// <summary>Open source: users can point the update check at their own fork.</summary>
    public const string DefaultUpdateRepo = "eupsa/client-ssh";
}
