using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ClientSSH.Models;

namespace ClientSSH.Services.Storage;

/// <summary>One-time move of the old hosts.json into the database. The old file is kept, renamed, as a backup.</summary>
public static class LegacyHostsImport
{
    public static bool Run(Database database, string jsonPath, JsonTypeInfo<List<Host>> hostsInfo)
    {
        if (!File.Exists(jsonPath)) return false;

        var store = new HostStore(database);
        if (store.Load().Count > 0) return false;

        List<Host>? hosts;
        try
        {
            hosts = JsonSerializer.Deserialize(File.ReadAllText(jsonPath), hostsInfo);
        }
        catch (JsonException)
        {
            // Unreadable file: leave it where it is instead of losing it.
            return false;
        }

        store.Save(hosts ?? []);
        File.Move(jsonPath, jsonPath + ".migrated", overwrite: true);
        return true;
    }
}
