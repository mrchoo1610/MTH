using MiniTools.Contracts;

namespace MiniTools.Updater;

public enum UpdateCheckStatus { NotChecked, UpToDate, Available, Failed, Disabled }

/// <summary>Thrown when a download fails verification (signature, size, sha256, zip-slip) or updates are disabled.</summary>
public sealed class UpdateVerificationException : Exception
{
    public UpdateVerificationException(string message) : base(message) { }
}

/// <summary>A verified, extracted update ready for <see cref="UpdateClient.ApplyAndRestart"/>.</summary>
public sealed class PreparedUpdate
{
    public required ReleaseManifest Manifest { get; init; }
    public required string PackagePath { get; init; }
    public required string FilesDir { get; init; }
    public AppVersion Version => AppVersion.Parse(Manifest.Version);
}
