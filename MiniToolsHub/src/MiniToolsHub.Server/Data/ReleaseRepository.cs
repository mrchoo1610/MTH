using Dapper;
using Microsoft.Data.Sqlite;
using MiniTools.Contracts;

namespace MiniToolsHub.Server.Data;

public sealed class ReleaseRepository(Db db)
{
    private sealed class Row { public string manifest_json { get; set; } = ""; }

    public async Task<bool> ExistsAsync(string appId, string version)
    {
        await using var c = db.Open();
        return await c.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM releases WHERE app_id=@appId AND version=@version", new { appId, version }) > 0;
    }

    /// <summary>Returns false when (appId, version) already exists.</summary>
    public async Task<bool> InsertAsync(ReleaseManifest m)
    {
        await using var c = db.Open();
        try
        {
            await c.ExecuteAsync(
                @"INSERT INTO releases(app_id,version,channel,file_name,size_bytes,sha256,manifest_json,published_at)
                  VALUES(@AppId,@Version,@Channel,@FileName,@SizeBytes,@Sha256,@Json,@Published)",
                new
                {
                    m.AppId, m.Version, m.Channel, m.FileName, m.SizeBytes, m.Sha256,
                    Json = ManifestJson.Serialize(m),
                    Published = m.PublishedAt.UtcDateTime.ToString("O"),
                });
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return false;
        }
    }

    public async Task<bool> YankAsync(string appId, string version)
    {
        await using var c = db.Open();
        return await c.ExecuteAsync("UPDATE releases SET is_yanked=1 WHERE app_id=@appId AND version=@version", new { appId, version }) > 0;
    }

    /// <summary>Non-yanked releases of an app, newest version first.</summary>
    public async Task<List<ReleaseManifest>> ListAsync(string appId, string? channel = null, int limit = 50)
    {
        await using var c = db.Open();
        var rows = await c.QueryAsync<Row>(
            "SELECT manifest_json FROM releases WHERE app_id=@appId AND is_yanked=0", new { appId });
        return Sort(rows, channel).Take(limit).ToList();
    }

    public async Task<List<ReleaseManifest>> ListAllAsync()
    {
        await using var c = db.Open();
        var rows = await c.QueryAsync<Row>("SELECT manifest_json FROM releases WHERE is_yanked=0");
        return rows.Select(r => ManifestJson.Deserialize(r.manifest_json)!)
                   .OrderByDescending(m => m.PublishedAt).ToList();
    }

    /// <summary>Latest release. channel "stable" = stable only; "beta" = newest of beta or stable.</summary>
    public async Task<ReleaseManifest?> GetLatestAsync(string appId, string channel)
    {
        var list = await ListAsync(appId, channel, 1);
        return list.FirstOrDefault();
    }

    public async Task<ReleaseManifest?> GetAsync(string appId, string version)
    {
        await using var c = db.Open();
        var row = await c.QuerySingleOrDefaultAsync<Row>(
            "SELECT manifest_json FROM releases WHERE app_id=@appId AND version=@version AND is_yanked=0",
            new { appId, version });
        return row is null ? null : ManifestJson.Deserialize(row.manifest_json);
    }

    private static IEnumerable<ReleaseManifest> Sort(IEnumerable<Row> rows, string? channel) =>
        rows.Select(r => ManifestJson.Deserialize(r.manifest_json)!)
            .Where(m => channel is null || channel == "beta" || m.Channel == "stable")
            .OrderByDescending(m => AppVersion.Parse(m.Version));
}
