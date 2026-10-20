using System.Security.Cryptography;
using System.Text;
using ClientSSH.Services.Storage;

namespace ClientSSH.Services.Shell;

/// <summary>
/// One running app per data folder. A second launch signals the first (which shows its window) and exits.
/// Keyed by the data folder, so a separate profile (CLIENTSSH_DATA_DIR) can still run side by side.
/// </summary>
public static class SingleInstance
{
    private static Mutex? _owner;
    private static EventWaitHandle? _showSignal;

    /// <summary>True for the first instance. For a second one it already asked the first to show itself.</summary>
    public static bool TryAcquire()
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(LocalData.Dir.ToLowerInvariant())))[..16];
        _owner = new Mutex(initiallyOwned: true, $@"Local\ClientSSH-{id}", out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\ClientSSH-show-{id}");

        if (first) return true;
        _showSignal.Set();
        return false;
    }

    /// <summary>Runs on a thread-pool thread each time another launch is attempted.</summary>
    public static void OnSecondLaunch(Action action) =>
        ThreadPool.RegisterWaitForSingleObject(_showSignal!, (_, _) => action(), null, Timeout.Infinite, executeOnlyOnce: false);
}
