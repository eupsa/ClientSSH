using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ClientSSH.Terminal;

public enum ShellKind { WindowsPowerShell, PowerShell, Cmd, Wsl, GitBash }

/// <summary>A local shell that opens in a terminal tab, like an SSH host but on this PC.</summary>
/// <param name="Id">Stable key for settings: "powershell", "pwsh", "cmd", "gitbash" or "wsl:&lt;distro&gt;".</param>
/// <param name="Arguments">Built here from constants and validated names, never from free text.</param>
public sealed record ShellProfile(string Id, string Name, ShellKind Kind, string Executable, string Arguments = "")
{
    /// <summary>Appearance key shared by every SSH host that has no profile of its own.</summary>
    public const string SshId = "ssh";

    public string CommandLine => Arguments.Length == 0 ? $"\"{Executable}\"" : $"\"{Executable}\" {Arguments}";

    // Screen readers and list items read ToString: say the name, not the record dump.
    public override string ToString() => Name;
}

/// <summary>Where to look for shells. Tests pass fake folders and a fake file check.</summary>
public sealed record ShellEnvironment(
    string SystemDirectory,
    string ProgramFiles,
    string ProgramFilesX86,
    string LocalAppData,
    Func<string, bool> FileExists,
    Func<IReadOnlyList<string>> WslDistros)
{
    public static ShellEnvironment Real { get; } = new(
        Environment.SystemDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        File.Exists,
        ShellProfiles.ListWslDistros);
}

/// <summary>Finds the shells installed on this PC. Only what exists is offered.</summary>
public static partial class ShellProfiles
{
    public static IReadOnlyList<ShellProfile> Detect(ShellEnvironment env)
    {
        var profiles = new List<ShellProfile>();

        string? First(params string[] candidates) => candidates.FirstOrDefault(p => IsSafeExecutable(p) && env.FileExists(p));

        if (First(Path.Combine(env.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")) is { } powershell)
            profiles.Add(new("powershell", "Windows PowerShell", ShellKind.WindowsPowerShell, powershell, "-NoLogo"));

        if (First(
                Path.Combine(env.ProgramFiles, "PowerShell", "7", "pwsh.exe"),
                Path.Combine(env.ProgramFiles, "PowerShell", "7-preview", "pwsh.exe"),
                Path.Combine(env.LocalAppData, "Microsoft", "WindowsApps", "pwsh.exe")) is { } pwsh)
            profiles.Add(new("pwsh", "PowerShell 7", ShellKind.PowerShell, pwsh, "-NoLogo"));

        if (First(Path.Combine(env.SystemDirectory, "cmd.exe")) is { } cmd)
            profiles.Add(new("cmd", "CMD", ShellKind.Cmd, cmd));

        if (First(Path.Combine(env.SystemDirectory, "wsl.exe")) is { } wsl)
            foreach (var distro in env.WslDistros().Where(IsValidDistro).Distinct(StringComparer.OrdinalIgnoreCase))
                profiles.Add(new($"wsl:{distro}", distro, ShellKind.Wsl, wsl, $"-d {distro} --cd ~"));

        if (First(
                Path.Combine(env.ProgramFiles, "Git", "bin", "bash.exe"),
                Path.Combine(env.ProgramFilesX86, "Git", "bin", "bash.exe"),
                Path.Combine(env.LocalAppData, "Programs", "Git", "bin", "bash.exe")) is { } bash)
            profiles.Add(new("gitbash", "Git Bash", ShellKind.GitBash, bash, "--login -i"));

        return profiles;
    }

    /// <summary>The path goes between quotes on a command line: it must be absolute, an .exe and quote-free.</summary>
    public static bool IsSafeExecutable(string path) =>
        Path.IsPathFullyQualified(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
        path.IndexOfAny(['"', '\r', '\n', '\0']) < 0;

    /// <summary>Distro names reach the wsl.exe command line unquoted, so only plain names pass (no '-' start, no spaces).</summary>
    public static bool IsValidDistro(string name) => DistroPattern().IsMatch(name) && name[0] != '-' &&
        // Docker Desktop's internal distros are not shells for people (Windows Terminal hides them too).
        !name.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses <c>wsl.exe -l -q</c>: one name per line, sometimes with NULs or a BOM left from UTF-16.</summary>
    public static IReadOnlyList<string> ParseWslList(string output) =>
        output.Replace("\0", "").Replace("﻿", "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsValidDistro).ToList();

    /// <summary>Asks wsl.exe for the installed distros. Empty when WSL is missing, broken or slow to answer.</summary>
    public static IReadOnlyList<string> ListWslDistros()
    {
        var wsl = Path.Combine(Environment.SystemDirectory, "wsl.exe");
        if (!File.Exists(wsl)) return [];
        try
        {
            using var process = Process.Start(new ProcessStartInfo(wsl, "-l -q")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Unicode, // wsl.exe writes UTF-16
            });
            if (process is null) return [];
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                process.Kill();
                return [];
            }

            return process.ExitCode == 0 ? ParseWslList(output.Result) : [];
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return [];
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex DistroPattern();
}
