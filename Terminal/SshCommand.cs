using System.Text.RegularExpressions;
using ClientSSH.Models;

namespace ClientSSH.Terminal;

/// <summary>Builds the Windows OpenSSH command line. Host data is user input, so it is validated before it reaches a process.</summary>
public static partial class SshCommand
{
    public static string SshPath => Path.Combine(Environment.SystemDirectory, "OpenSSH", "ssh.exe");

    public static bool IsInstalled => File.Exists(SshPath);

    // A leading '-' would let a host name act as an ssh option (e.g. -oProxyCommand=...).
    public static bool IsValidAddress(string address) => AddressPattern().IsMatch(address) && address[0] != '-';

    public static bool IsValidUser(string user) => UserPattern().IsMatch(user) && user[0] != '-';

    public static bool IsValid(Host host) =>
        IsValidAddress(host.Address) && IsValidUser(host.User) && host.Port is >= 1 and <= 65535;

    public static string Build(Host host)
    {
        if (!IsValid(host)) throw new ArgumentException("Invalid host data.", nameof(host));
        return $"\"{SshPath}\" -p {host.Port} -- {host.User}@{host.Address}";
    }

    [GeneratedRegex(@"^[A-Za-z0-9._:%\[\]-]+$")]
    private static partial Regex AddressPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._\\@-]+$")]
    private static partial Regex UserPattern();
}
