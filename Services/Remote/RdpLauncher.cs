using System.Diagnostics;
using ClientSSH.Models;
using ClientSSH.Terminal;

namespace ClientSSH.Services.Remote;

/// <summary>
/// Remote Desktop with the Windows client (mstsc), straight from this PC to the other one: no server in between and
/// no password handled here (Windows asks for it). Host data is user input and ends up in a file mstsc reads, so it is
/// validated first: a line break in a field would add settings of its own.
/// </summary>
public static class RdpLauncher
{
    public static bool IsValid(Host host) =>
        SshCommand.IsValidAddress(host.Address) && host.Port is >= 1 and <= 65535 &&
        (host.User.Length == 0 || SshCommand.IsValidUser(host.User));

    /// <summary>The .rdp file content for this host.</summary>
    public static string BuildFile(Host host)
    {
        if (!IsValid(host)) throw new ArgumentException("Invalid host data.", nameof(host));

        var lines = new List<string> { $"full address:s:{host.Address}:{host.Port}" };
        if (host.User.Length > 0) lines.Add($"username:s:{host.User}");
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>Starts mstsc. Throws InvalidOperationException (invalid data) or Win32Exception (mstsc missing).</summary>
    public static void Open(Host host)
    {
        if (!IsValid(host)) throw new InvalidOperationException("Invalid host or user.");

        var dir = Path.Combine(Path.GetTempPath(), "ClientSSH-rdp");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{host.Id}.rdp");
        File.WriteAllText(file, BuildFile(host));

        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "mstsc.exe")) { UseShellExecute = false };
        info.ArgumentList.Add(file);
        Process.Start(info);

        // mstsc has read it by now: do not leave host names lying in the temp folder.
        _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ =>
        {
            try { File.Delete(file); } catch (IOException) { }
        });
    }
}
