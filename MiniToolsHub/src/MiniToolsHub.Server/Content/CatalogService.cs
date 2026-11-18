using MiniTools.Contracts;
using MiniToolsHub.Server.Data;

namespace MiniToolsHub.Server.Content;

public sealed record ToolCard(ToolContent Tool, ReleaseManifest? Latest, long TotalDownloads);

/// <summary>Joins static tool content with release + download data.</summary>
public sealed class CatalogService(ToolContentStore content, ReleaseRepository releases, DownloadCounter downloads)
{
    public async Task<List<ToolCard>> GetCardsAsync()
    {
        var list = new List<ToolCard>();
        foreach (var t in content.All)
            list.Add(new ToolCard(t, await releases.GetLatestAsync(t.AppId, "stable"), await downloads.TotalAsync(t.AppId)));
        return list;
    }

    public async Task<ToolCard?> GetCardAsync(string appId)
    {
        var t = content.Get(appId);
        return t is null ? null : new ToolCard(t, await releases.GetLatestAsync(appId, "stable"), await downloads.TotalAsync(appId));
    }

    public static string DownloadPath(ReleaseManifest m) => $"/dl/{m.AppId}/{m.Version}/{m.FileName}";
}
