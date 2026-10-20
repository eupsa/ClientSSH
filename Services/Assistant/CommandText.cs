namespace ClientSSH.Services.Assistant;

/// <summary>Turns a model reply into text that is safe to type at a prompt.</summary>
public static class CommandText
{
    /// <summary>
    /// First non-empty line, without code fences or backticks, and without control characters. A newline or an escape
    /// sequence typed into the terminal would run or edit things behind the user's back, so none can get through.
    /// </summary>
    public static string Clean(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return "";

        foreach (var raw in reply.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("```", StringComparison.Ordinal)) continue;

            line = line.Trim('`').Trim();
            return new string(line.Where(c => !char.IsControl(c)).ToArray());
        }
        return "";
    }
}
