namespace KuikuiGameAssistant.Models;

public enum UpdatePackageKind
{
    Installer,
    PortableZip
}

public sealed record UpdateAsset(
    string Name,
    string DownloadUrl,
    long SizeBytes,
    UpdatePackageKind Kind);

public sealed record UpdateRelease(
    string TagName,
    string VersionText,
    string HtmlUrl,
    string? Notes,
    UpdateAsset Asset);

public sealed record UpdateCheckResult(
    bool Success,
    bool UpdateAvailable,
    string Message,
    UpdateRelease? Release = null);

public sealed record UpdateDownloadProgress(long BytesReceived, long? TotalBytes, string Phase)
{
    public double? Percent => TotalBytes is > 0
        ? Math.Min(100d, BytesReceived * 100d / TotalBytes.Value)
        : null;
}

public sealed record PendingUpdateInfo(
    string TagName,
    string VersionText,
    string PackagePath,
    string AssetName,
    UpdatePackageKind Kind,
    long SizeBytes,
    DateTimeOffset StagedAtUtc);

public sealed record UpdateStageResult(bool Success, string Message);

public sealed record UpdateApplyResult(bool Success, string Message);
