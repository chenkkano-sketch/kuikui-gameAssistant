using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using KuikuiGameAssistant.Models;
using WpfApplication = System.Windows.Application;

namespace KuikuiGameAssistant.Services;

public sealed class UpdateService
{
    private const string GitHubApiBaseUrl = "https://api.github.com/repos/";
    private const string PortableMarkerFile = "portable.marker";
    private static readonly TimeSpan StartupCheckInterval = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(35)
    };

    private readonly AppSettings _settings;

    public UpdateService(AppSettings settings)
    {
        _settings = settings;
    }

    public Version CurrentVersion => GetCurrentVersion();

    public bool IsPortableInstall => File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarkerFile));

    public bool ShouldCheckOnStartup()
    {
        return _settings.AutoCheckUpdates
               && NormalizeRepository(_settings.GitHubRepository) is not null
               && DateTimeOffset.UtcNow - _settings.LastUpdateCheckUtc >= StartupCheckInterval;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var repository = NormalizeRepository(_settings.GitHubRepository);
        if (repository is null)
        {
            return new UpdateCheckResult(false, false, "请先填写 GitHub 仓库，例如 chenkkano-sketch/kuikui-gameAssistant。");
        }

        _settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{GitHubApiBaseUrl}{repository}/releases/latest");
            request.Headers.UserAgent.ParseAdd($"KuikuiGameAssistant/{CurrentVersion}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(false, false, "没有找到 GitHub Release，请先在仓库发布一个版本。");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(false, false, $"检查更新失败：GitHub 返回 {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new UpdateCheckResult(false, false, "检查更新失败：Release 信息为空。");
            }

            var latestVersion = ParseVersion(release.TagName);
            if (latestVersion is null)
            {
                return new UpdateCheckResult(false, false, $"检查更新失败：无法识别版本号 {release.TagName}。");
            }

            if (latestVersion <= CurrentVersion)
            {
                return new UpdateCheckResult(true, false, $"当前已是最新版本 {CurrentVersion}。");
            }

            var asset = SelectAsset(release.Assets);
            if (asset is null)
            {
                return new UpdateCheckResult(false, false, "发现新版本，但 Release 中没有安装包或便携 zip。");
            }

            var update = new UpdateRelease(
                release.TagName,
                latestVersion.ToString(),
                release.HtmlUrl ?? $"https://github.com/{repository}/releases/tag/{release.TagName}",
                release.Body,
                asset);
            return new UpdateCheckResult(true, true, $"发现新版本 {release.TagName}。", update);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, false, $"检查更新失败：{ex.Message}");
        }
    }

    public async Task<UpdateApplyResult> DownloadAndApplyAsync(UpdateRelease release, CancellationToken cancellationToken = default)
    {
        try
        {
            var updatesFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KuikuiGameAssistant",
                "Updates",
                release.TagName.TrimStart('v', 'V'));
            Directory.CreateDirectory(updatesFolder);

            var packagePath = Path.Combine(updatesFolder, SanitizeFileName(release.Asset.Name));
            await DownloadFileAsync(release.Asset.DownloadUrl, packagePath, cancellationToken).ConfigureAwait(false);

            if (release.Asset.Kind == UpdatePackageKind.Installer)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = packagePath,
                    UseShellExecute = true
                });
            }
            else
            {
                StartPortableUpdater(packagePath);
            }

            WpfApplication.Current.Dispatcher.Invoke(() => WpfApplication.Current.Shutdown());
            return new UpdateApplyResult(true, "更新程序已启动，应用即将退出。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateApplyResult(false, $"应用更新失败：{ex.Message}");
        }
    }

    public static string? NormalizeRepository(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            text = text["git@github.com:".Length..];
        }
        else if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
                 && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            text = uri.AbsolutePath.Trim('/');
        }

        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^4];
        }

        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? $"{parts[0]}/{parts[1]}" : null;
    }

    private UpdateAsset? SelectAsset(IReadOnlyList<GitHubAsset>? assets)
    {
        if (assets is null || assets.Count == 0)
        {
            return null;
        }

        var candidates = assets
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.BrowserDownloadUrl))
            .Select(x => (Asset: x, Kind: DetectPackageKind(x.Name!)))
            .Where(x => x.Kind is not null)
            .Select(x => new UpdateAsset(x.Asset.Name!, x.Asset.BrowserDownloadUrl!, x.Asset.Size, x.Kind!.Value))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var preferredKind = IsPortableInstall ? UpdatePackageKind.PortableZip : UpdatePackageKind.Installer;
        return candidates.FirstOrDefault(x => x.Kind == preferredKind)
               ?? candidates.FirstOrDefault(x => x.Kind == UpdatePackageKind.Installer)
               ?? candidates.FirstOrDefault(x => x.Kind == UpdatePackageKind.PortableZip);
    }

    private static UpdatePackageKind? DetectPackageKind(string assetName)
    {
        var lower = assetName.ToLowerInvariant();
        if (lower.EndsWith(".zip", StringComparison.Ordinal))
        {
            return UpdatePackageKind.PortableZip;
        }

        return lower.EndsWith(".exe", StringComparison.Ordinal) || lower.EndsWith(".msi", StringComparison.Ordinal)
            ? UpdatePackageKind.Installer
            : null;
    }

    private static async Task DownloadFileAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return ParseVersion(informational) ?? assembly.GetName().Version ?? new Version(0, 0, 0);
    }

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().TrimStart('v', 'V');
        var metadataIndex = text.IndexOf('+');
        if (metadataIndex >= 0)
        {
            text = text[..metadataIndex];
        }

        var prereleaseIndex = text.IndexOf('-');
        if (prereleaseIndex >= 0)
        {
            text = text[..prereleaseIndex];
        }

        return Version.TryParse(text, out var version) ? version : null;
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        return fileName;
    }

    private static void StartPortableUpdater(string archivePath)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"KuikuiGameAssistant-update-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, PortableUpdaterScript);

        var arguments = string.Join(" ", new[]
        {
            "-NoProfile",
            "-ExecutionPolicy Bypass",
            "-File",
            QuoteProcessArgument(scriptPath),
            "-ProcessId",
            Environment.ProcessId.ToString(),
            "-Archive",
            QuoteProcessArgument(archivePath),
            "-Target",
            QuoteProcessArgument(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            "-Exe",
            QuoteProcessArgument(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "KuikuiGameAssistant.exe"))
        });

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static string QuoteProcessArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private const string PortableUpdaterScript = """
param(
    [int]$ProcessId,
    [string]$Archive,
    [string]$Target,
    [string]$Exe
)

$ErrorActionPreference = 'Stop'
try {
    Wait-Process -Id $ProcessId -Timeout 120
} catch {
}

Start-Sleep -Milliseconds 500
$extractRoot = Join-Path $env:TEMP ("KuikuiGameAssistant-update-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
Expand-Archive -Path $Archive -DestinationPath $extractRoot -Force

$source = $extractRoot
$entries = @(Get-ChildItem -LiteralPath $extractRoot)
if ($entries.Count -eq 1 -and $entries[0].PSIsContainer) {
    $source = $entries[0].FullName
}

Copy-Item -Path (Join-Path $source '*') -Destination $Target -Recurse -Force
if (Test-Path -LiteralPath $Exe) {
    Start-Process -FilePath $Exe
}
Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
""";

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
