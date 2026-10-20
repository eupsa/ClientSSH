using System.Globalization;

namespace ClientSSH.Services.Storage;

public interface IHistoryStore
{
    /// <summary>Raised after <see cref="Clear"/>.</summary>
    event Action? Cleared;

    /// <summary>Remembers that a connection to the host started now. Stores only the host and the time.</summary>
    DateTime Record(Guid hostId);

    void Clear();
}

public sealed class HistoryStore(Database database) : IHistoryStore
{
    // shortcut: a flat cap on total rows keeps the table small; add per-host limits if the list grows large.
    private const int MaxRows = 1000;

    public event Action? Cleared;

    public DateTime Record(Guid hostId)
    {
        var now = DateTime.UtcNow;
        using var connection = database.Open();
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO history (host_id, connected_utc) VALUES ($host, $time)";
        insert.Parameters.AddWithValue("$host", hostId.ToString());
        insert.Parameters.AddWithValue("$time", now.ToString("o", CultureInfo.InvariantCulture));
        insert.ExecuteNonQuery();

        using var trim = connection.CreateCommand();
        trim.CommandText = "DELETE FROM history WHERE id NOT IN (SELECT id FROM history ORDER BY id DESC LIMIT $max)";
        trim.Parameters.AddWithValue("$max", MaxRows);
        trim.ExecuteNonQuery();
        return now;
    }

    public void Clear()
    {
        using var connection = database.Open();
        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM history";
        delete.ExecuteNonQuery();
        Cleared?.Invoke();
    }
}
