using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace MiniToolsHub.Server.Data;

/// <summary>SQLite connection factory + schema bootstrap. One short-lived connection per operation.</summary>
public sealed class Db
{
    private readonly string _connectionString;

    public Db(IOptions<HubOptions> options)
    {
        var dir = Path.GetFullPath(options.Value.DataDir);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(options.Value.FilesRoot);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dir, "hub.db"),
            Pooling = false,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    public void Migrate()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("Migrations.sql", StringComparison.Ordinal));
        using var reader = new StreamReader(asm.GetManifestResourceStream(name)!);
        var sql = reader.ReadToEnd();
        using var c = Open();
        using var wal = c.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL;";
        wal.ExecuteNonQuery();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
