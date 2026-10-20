namespace ClientSSH.Services.Updates;

/// <summary>Reads a SHA256SUMS.txt ("hash  file name" per line) and checks a file hash against it.</summary>
public static class Sha256Sums
{
    public static bool Matches(string sums, string fileName, string actualHex)
    {
        foreach (var line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == fileName)
                return string.Equals(parts[0], actualHex, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}
