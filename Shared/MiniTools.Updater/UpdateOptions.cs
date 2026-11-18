using MiniTools.Contracts;

namespace MiniTools.Updater;

/// <summary>Per-app updater configuration.</summary>
public sealed record UpdateOptions(string AppId, AppVersion CurrentVersion, Uri HubBaseUrl,
                                   string Channel = "stable", string Language = "vi");
