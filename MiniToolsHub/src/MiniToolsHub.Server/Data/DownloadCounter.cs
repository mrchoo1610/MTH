using Dapper;

namespace MiniToolsHub.Server.Data;

/// <summary>Counts downloads per (app, version, day). Stores no IP and no User-Agent.</summary>
public sealed class DownloadCounter(Db db)
{
    public async Task IncrementAsync(string appId, string version)
    {
        var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await using var c = db.Open();
        await c.ExecuteAsync(
            @"INSERT INTO download_counts(app_id,version,day,count) VALUES(@appId,@version,@day,1)
              ON CONFLICT(app_id,version,day) DO UPDATE SET count=count+1",
            new { appId, version, day });
    }

    public async Task<long> TotalAsync(string appId)
    {
        await using var c = db.Open();
        return await c.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(count),0) FROM download_counts WHERE app_id=@appId", new { appId });
    }

    public async Task<long> TotalAsync(string appId, string version)
    {
        await using var c = db.Open();
        return await c.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(count),0) FROM download_counts WHERE app_id=@appId AND version=@version", new { appId, version });
    }
}
