using Microsoft.Data.Sqlite;
using RetroHub.Core.Data;
using RetroHub.Core.Library;

namespace RetroHub.Core.Tests.Data;

/// <summary>A private temp folder with a database file path; deletes everything on dispose.</summary>
internal sealed class TempDatabase : IDisposable
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "RetroHubTests", "data-" + Guid.NewGuid().ToString("N"));
    public string File => Path.Combine(Folder, "database", "retrohub.db");

    public TempDatabase() => Directory.CreateDirectory(Folder);

    public SqliteGameRepository Open() => new(File);

    /// <summary>Runs a scalar query on the file with an unpooled connection (independent from the repository).</summary>
    public object? Scalar(string sql)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = File, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        for (var i = 0; i < 5; i++)
        {
            try { if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true); return; }
            catch (IOException) { Thread.Sleep(50); }
            catch (UnauthorizedAccessException) { Thread.Sleep(50); }
        }
    }

    public static Game NewGame(string path, string title, string system = "nes", Action<Game>? setup = null)
    {
        var g = new Game
        {
            SystemId = system,
            Path = path,
            Title = title,
            Size = 1024,
            FileTimeUtc = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        };
        setup?.Invoke(g);
        return g;
    }
}
