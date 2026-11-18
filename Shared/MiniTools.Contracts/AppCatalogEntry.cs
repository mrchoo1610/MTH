namespace MiniTools.Contracts;

public sealed record AppCatalogEntry(string AppId, string Name, ReleaseManifest? Latest, long TotalDownloads);
