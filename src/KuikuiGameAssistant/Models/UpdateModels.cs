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

public sealed record UpdateApplyResult(bool Success, string Message);
