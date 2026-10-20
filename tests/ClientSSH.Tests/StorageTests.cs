using System.Text.Json;
using ClientSSH.Models;
using ClientSSH.Services.Storage;
using Xunit;

namespace ClientSSH.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clientssh-tests-" + Guid.NewGuid());
    private readonly Database _database;

    public StorageTests()
    {
        _database = new Database(Path.Combine(_folder, "test.db"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static Host NewHost(string name, bool favorite = false) =>
        new(Guid.NewGuid(), name, name + ".local", 22, "root", Favorite: favorite);

    [Fact]
    public void SavesAndLoadsHostsInOrder()
    {
        var store = new HostStore(_database);
        var a = NewHost("a");
        var b = NewHost("b");
        store.Save([a, b]);

        Assert.Equal([a, b], store.Load());
    }

    [Fact]
    public void FavoritesLoadFirst()
    {
        var store = new HostStore(_database);
        var a = NewHost("a");
        var b = NewHost("b", favorite: true);
        store.Save([a, b]);

        Assert.Equal(["b", "a"], store.Load().Select(h => h.Name));
    }

    [Fact]
    public void SaveReplacesTheList()
    {
        var store = new HostStore(_database);
        var keep = NewHost("keep");
        store.Save([keep, NewHost("drop")]);
        store.Save([keep]);

        Assert.Equal([keep], store.Load());
    }

    [Fact]
    public void HistoryShowsLastConnectionAndIsClearable()
    {
        var store = new HostStore(_database);
        var history = new HistoryStore(_database);
        var host = NewHost("a");
        store.Save([host]);
        Assert.Null(store.Load()[0].LastConnectedUtc);

        var when = history.Record(host.Id);
        var loaded = store.Load()[0].LastConnectedUtc;
        Assert.NotNull(loaded);
        Assert.True((loaded.Value - when).Duration() < TimeSpan.FromSeconds(1));

        var cleared = false;
        history.Cleared += () => cleared = true;
        history.Clear();
        Assert.True(cleared);
        Assert.Null(store.Load()[0].LastConnectedUtc);
    }

    [Fact]
    public void RemovingAHostRemovesItsHistory()
    {
        var store = new HostStore(_database);
        var history = new HistoryStore(_database);
        var host = NewHost("a");
        store.Save([host]);
        history.Record(host.Id);
        store.Save([]);
        store.Save([host]);

        Assert.Null(store.Load()[0].LastConnectedUtc);
    }

    [Fact]
    public void ImportsLegacyJsonOnceAndKeepsABackup()
    {
        var json = Path.Combine(_folder, "hosts.json");
        File.WriteAllText(json, """[{"Id":"11111111-1111-1111-1111-111111111111","Name":"old","Address":"x","Port":2222,"User":"u","Group":null}]""");

        var info = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<Host>>)JsonSerializerOptions.Default.GetTypeInfo(typeof(List<Host>));
        Assert.True(LegacyHostsImport.Run(_database, json, info));

        Assert.Equal("old", new HostStore(_database).Load().Single().Name);
        Assert.False(File.Exists(json));
        Assert.True(File.Exists(json + ".migrated"));
        Assert.False(LegacyHostsImport.Run(_database, json, info));
    }

    [Fact]
    public void LeavesUnreadableLegacyFileAlone()
    {
        var json = Path.Combine(_folder, "hosts.json");
        File.WriteAllText(json, "not json");

        var info = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<Host>>)JsonSerializerOptions.Default.GetTypeInfo(typeof(List<Host>));
        Assert.False(LegacyHostsImport.Run(_database, json, info));
        Assert.True(File.Exists(json));
    }
}
