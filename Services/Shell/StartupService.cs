using Microsoft.Win32;
using Windows.ApplicationModel;

namespace ClientSSH.Services.Shell;

public enum StartupState { Disabled, Enabled, Blocked }

public interface IStartupService
{
    Task<StartupState> GetAsync();
    Task<StartupState> SetAsync(bool enable);
}

/// <summary>
/// Start with Windows. Packaged (MSIX) uses the manifest StartupTask; unpackaged falls back to the
/// per-user Run key. In both cases the OS is the source of truth, nothing is stored in our settings.
/// </summary>
public sealed class StartupService : IStartupService
{
    public const string StartupArgument = "--startup";

    private const string TaskId = "ClientSSHStartup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "ClientSSH";

    private static bool IsPackaged
    {
        get
        {
            try { _ = Package.Current.Id; return true; }
            catch (InvalidOperationException) { return false; }
        }
    }

    public async Task<StartupState> GetAsync() =>
        IsPackaged ? Map((await StartupTask.GetAsync(TaskId)).State) : GetRegistry();

    public async Task<StartupState> SetAsync(bool enable)
    {
        if (!IsPackaged) return SetRegistry(enable);

        var task = await StartupTask.GetAsync(TaskId);
        if (enable) return Map(await task.RequestEnableAsync());

        task.Disable();
        return Map(task.State);
    }

    private static StartupState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => StartupState.Enabled,
        StartupTaskState.Disabled => StartupState.Disabled,
        _ => StartupState.Blocked,
    };

    private static StartupState GetRegistry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunName) is null ? StartupState.Disabled : StartupState.Enabled;
    }

    private static StartupState SetRegistry(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable) key.SetValue(RunName, $"\"{Environment.ProcessPath}\" {StartupArgument}");
        else key.DeleteValue(RunName, throwOnMissingValue: false);
        return enable ? StartupState.Enabled : StartupState.Disabled;
    }
}
