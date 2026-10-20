using System.Globalization;
using ClientSSH.Models;

namespace ClientSSH.Services.Storage;

public interface IHostStore
{
    /// <summary>Favorites first, then the saved order.</summary>
    List<Host> Load();

    /// <summary>Replaces the saved hosts with this list, keeping its order.</summary>
    void Save(IEnumerable<Host> hosts);
}

public sealed class HostStore(Database database) : IHostStore
{
    public List<Host> Load()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, address, port, user, group_name, favorite,
                   (SELECT MAX(connected_utc) FROM history WHERE host_id = hosts.id)
            FROM hosts
            ORDER BY favorite DESC, position
            """;

        var hosts = new List<Host>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            hosts.Add(new Host(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt32(6) != 0,
                reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return hosts;
    }

    public void Save(IEnumerable<Host> hosts)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        using (var clear = connection.CreateCommand())
        {
            clear.CommandText = "DELETE FROM hosts";
            clear.ExecuteNonQuery();
        }

        var position = 0;
        foreach (var host in hosts)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO hosts (id, name, address, port, user, group_name, favorite, position)
                VALUES ($id, $name, $address, $port, $user, $group, $favorite, $position)
                """;
            insert.Parameters.AddWithValue("$id", host.Id.ToString());
            insert.Parameters.AddWithValue("$name", host.Name);
            insert.Parameters.AddWithValue("$address", host.Address);
            insert.Parameters.AddWithValue("$port", host.Port);
            insert.Parameters.AddWithValue("$user", host.User);
            insert.Parameters.AddWithValue("$group", (object?)host.Group ?? DBNull.Value);
            insert.Parameters.AddWithValue("$favorite", host.Favorite ? 1 : 0);
            insert.Parameters.AddWithValue("$position", position++);
            insert.ExecuteNonQuery();
        }

        // History of removed hosts goes with them.
        using (var prune = connection.CreateCommand())
        {
            prune.CommandText = "DELETE FROM history WHERE host_id NOT IN (SELECT id FROM hosts)";
            prune.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
