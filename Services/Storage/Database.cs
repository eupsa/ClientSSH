using Microsoft.Data.Sqlite;

namespace ClientSSH.Services.Storage;

/// <summary>The local SQLite file with hosts and connection history. It never leaves the PC.</summary>
public sealed class Database
{
    private const int SchemaVersion = 2;

    private readonly string _connectionString;

    public Database(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        Initialize();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        using var connection = Open();
        using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        var current = (long)version.ExecuteScalar()!;
        if (current >= SchemaVersion) return;

        // Each version adds its own step here, in order.
        if (current < 1)
            Run(connection, """
                CREATE TABLE hosts (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    address TEXT NOT NULL,
                    port INTEGER NOT NULL,
                    user TEXT NOT NULL,
                    group_name TEXT,
                    favorite INTEGER NOT NULL DEFAULT 0,
                    position INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    host_id TEXT NOT NULL,
                    connected_utc TEXT NOT NULL
                );
                CREATE INDEX history_host ON history (host_id, connected_utc);
                """);

        // Version 2: hosts can be Remote Desktop (RDP) instead of SSH. 0 = SSH, so old rows stay SSH.
        if (current < 2) Run(connection, "ALTER TABLE hosts ADD COLUMN kind INTEGER NOT NULL DEFAULT 0;");

        Run(connection, $"PRAGMA user_version = {SchemaVersion};");
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
