using ClientSSH.Services.Storage;

namespace ClientSSH.Services.Updates;

/// <summary>
/// A file that exists only while the app runs. If it is still there at the next start, the last run did not end
/// normally (crash, kill). Local only. shortcut: a second instance shares the file, fine while the app is single-window.
/// </summary>
public static class RunMarker
{
    private static readonly string MarkerPath = Path.Combine(LocalData.Dir, "running.marker");

    public static bool PreviousRunEndedBadly { get; private set; }

    public static void Begin()
    {
        try
        {
            PreviousRunEndedBadly = File.Exists(MarkerPath);
            Directory.CreateDirectory(LocalData.Dir);
            File.WriteAllText(MarkerPath, "");
            AppDomain.CurrentDomain.ProcessExit += (_, _) => File.Delete(MarkerPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
