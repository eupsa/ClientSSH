using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ClientSSH.Services.Storage;

/// <summary>All app data lives in %LOCALAPPDATA%\ClientSSH and never leaves the PC.</summary>
internal static class LocalData
{
    /// <summary>CLIENTSSH_DATA_DIR moves all data elsewhere (portable use, or a separate profile for testing).</summary>
    public static readonly string Dir = Environment.GetEnvironmentVariable("CLIENTSSH_DATA_DIR") is { Length: > 0 } custom
        ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClientSSH");

    public static T Read<T>(string file, JsonTypeInfo<T> info, T fallback)
    {
        var path = Path.Combine(Dir, file);
        if (!File.Exists(path)) return fallback;
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), info) ?? fallback;
        }
        catch (JsonException)
        {
            // Keep the unreadable file instead of overwriting it on the next save.
            File.Move(path, path + ".bad", overwrite: true);
            return fallback;
        }
    }

    public static void Write<T>(string file, JsonTypeInfo<T> info, T value)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, file), JsonSerializer.Serialize(value, info));
    }
}
