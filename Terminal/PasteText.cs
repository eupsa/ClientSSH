namespace ClientSSH.Terminal;

/// <summary>Turns clipboard text into what a terminal expects to receive.</summary>
public static class PasteText
{
    /// <summary>
    /// Line breaks become Enter (CR). Other control characters, escape included, are dropped: pasted text must not
    /// be able to send terminal control sequences. Tab stays.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var normalized = text.Replace("\r\n", "\r").Replace('\n', '\r');
        return new string(normalized.Where(c => c == '\r' || c == '\t' || !char.IsControl(c)).ToArray());
    }
}
