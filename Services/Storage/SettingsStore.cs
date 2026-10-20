using ClientSSH.Models;

namespace ClientSSH.Services.Storage;

public interface ISettingsStore
{
    AppSettings Current { get; }
    void Save(AppSettings settings);

    bool OnboardingDone => Current.OnboardingDone;
    void MarkOnboardingDone() => Save(Current with { OnboardingDone = true });
}

public sealed class SettingsStore : ISettingsStore
{
    public AppSettings Current { get; private set; } =
        LocalData.Read("settings.json", AppJsonContext.Default.AppSettings, new AppSettings());

    public void Save(AppSettings settings)
    {
        Current = settings;
        LocalData.Write("settings.json", AppJsonContext.Default.AppSettings, settings);
    }
}
