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
    private const string GitHubWebBaseUrl = "https://github.com/";
    private const string PackageBaseName = "KuikuiGameAssistant";
    private const string StableInstallerAssetName = PackageBaseName + "-setup.exe";
    private const string StablePortableAssetName = PackageBaseName + "-win-x64-portable.zip";
    private const string PortableMarkerFile = "portable.marker";
    private const string PendingUpdateManifestName = "pending-update.json";
    private const int DownloadBufferSize = 81920;
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

    public string RepositoryUrl => $"{GitHubWebBaseUrl}{RepositoryOrDefault}";

    public string ReleaseListUrl => $"{RepositoryUrl}/releases";

    public string LatestReleaseUrl => $"{ReleaseListUrl}/latest";

    public string InstallerDirectDownloadUrl => BuildLatestDownloadUrl(RepositoryOrDefault, StableInstallerAssetName);

    public string PortableDirectDownloadUrl => BuildLatestDownloadUrl(RepositoryOrDefault, StablePortableAssetName);

    public string PreferredDirectDownloadUrl => IsPortableInstall ? PortableDirectDownloadUrl : InstallerDirectDownloadUrl;

    public string CurrentInstallerDownloadUrl => BuildDownloadUrl(
        RepositoryOrDefault,
        $"v{CurrentVersion}",
        $"{PackageBaseName}-{CurrentVersion}-setup.exe");

    public string CurrentPortableDownloadUrl => BuildDownloadUrl(
        RepositoryOrDefault,
        $"v{CurrentVersion}",
        $"{PackageBaseName}-{CurrentVersion}-win-x64-portable.zip");

    private static string UpdatesRootFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KuikuiGameAssistant",
        "Updates");

    private static string PendingUpdateManifestPath => Path.Combine(UpdatesRootFolder, PendingUpdateManifestName);

    private string RepositoryOrDefault => NormalizeRepository(_settings.GitHubRepository) ?? AppSettings.DefaultGitHubRepository;

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
                var fallback = await TryCheckViaLatestRedirectAsync(repository, cancellationToken).ConfigureAwait(false);
                return fallback ?? new UpdateCheckResult(false, false, BuildGitHubFailureMessage(response));
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
            var fallback = await TryCheckViaLatestRedirectAsync(repository, cancellationToken).ConfigureAwait(false);
            return fallback ?? new UpdateCheckResult(false, false, $"检查更新失败：{ex.Message}");
        }
    }

    public async Task<UpdateStageResult> DownloadAndStageAsync(
        UpdateRelease release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updatesFolder = Path.Combine(UpdatesRootFolder, release.TagName.TrimStart('v', 'V'));
            Directory.CreateDirectory(updatesFolder);

            var packagePath = Path.Combine(updatesFolder, SanitizeFileName(release.Asset.Name));
            await DownloadFileAsync(
                release.Asset.DownloadUrl,
                packagePath,
                release.Asset.SizeBytes,
                progress,
                cancellationToken).ConfigureAwait(false);

            var pendingUpdate = new PendingUpdateInfo(
                release.TagName,
                release.VersionText,
                packagePath,
                release.Asset.Name,
                release.Asset.Kind,
                release.Asset.SizeBytes,
                DateTimeOffset.UtcNow);
            await WritePendingUpdateAsync(pendingUpdate, cancellationToken).ConfigureAwait(false);
            progress?.Report(new UpdateDownloadProgress(
                new FileInfo(packagePath).Length,
                new FileInfo(packagePath).Length,
                "更新包已下载，等待重启更新..."));

            return new UpdateStageResult(true, $"更新包已下载：{release.TagName}。下次打开软件时会自动更新。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateStageResult(false, BuildDownloadFailureMessage(ex, release.Asset.DownloadUrl));
        }
    }

    public async Task<UpdateApplyResult> DownloadAndApplyAsync(
        UpdateRelease release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stageResult = await DownloadAndStageAsync(release, progress, cancellationToken).ConfigureAwait(false);
        return stageResult.Success
            ? ApplyPendingUpdateAndShutdown()
            : new UpdateApplyResult(false, stageResult.Message);
    }

    public PendingUpdateInfo? GetPendingUpdate()
    {
        try
        {
            if (!File.Exists(PendingUpdateManifestPath))
            {
                return null;
            }

            var json = File.ReadAllText(PendingUpdateManifestPath);
            var pendingUpdate = JsonSerializer.Deserialize<PendingUpdateInfo>(json, JsonOptions);
            if (pendingUpdate is null
                || string.IsNullOrWhiteSpace(pendingUpdate.PackagePath)
                || !File.Exists(pendingUpdate.PackagePath))
            {
                ClearPendingUpdate();
                return null;
            }

            var pendingVersion = ParseVersion(pendingUpdate.VersionText);
            if (pendingVersion is not null && pendingVersion <= CurrentVersion)
            {
                ClearPendingUpdate();
                return null;
            }

            return pendingUpdate;
        }
        catch
        {
            ClearPendingUpdate();
            return null;
        }
    }

    public UpdateApplyResult ApplyPendingUpdateAndShutdown()
    {
        var pendingUpdate = GetPendingUpdate();
        if (pendingUpdate is null)
        {
            return new UpdateApplyResult(false, "没有待应用的更新包。");
        }

        try
        {
            ClearPendingUpdate();
            StartUpdatePackage(pendingUpdate.PackagePath, pendingUpdate.Kind);
            WpfApplication.Current.Dispatcher.Invoke(() => WpfApplication.Current.Shutdown());
            return new UpdateApplyResult(true, "更新程序已启动，应用即将退出。");
        }
        catch (Exception ex)
        {
            WritePendingUpdateBestEffort(pendingUpdate);
            return new UpdateApplyResult(false, $"启动更新失败：{GetHelpfulExceptionMessage(ex)}");
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

    private async Task<UpdateCheckResult?> TryCheckViaLatestRedirectAsync(string repository, CancellationToken cancellationToken)
    {
        try
        {
            var releasePageUrl = $"{GitHubWebBaseUrl}{repository}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, releasePageUrl);
            request.Headers.UserAgent.ParseAdd($"KuikuiGameAssistant/{CurrentVersion}");

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var tagName = ExtractTagName(response.RequestMessage?.RequestUri);
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return null;
            }

            var latestVersion = ParseVersion(tagName);
            if (latestVersion is null)
            {
                return null;
            }

            if (latestVersion <= CurrentVersion)
            {
                return new UpdateCheckResult(true, false, $"当前已是最新版本 {CurrentVersion}。");
            }

            var asset = await SelectDeterministicAssetAsync(repository, tagName, latestVersion, cancellationToken).ConfigureAwait(false);
            if (asset is null)
            {
                return new UpdateCheckResult(
                    false,
                    false,
                    $"发现新版本 {tagName}，但无法自动确认下载包。请点击 GitHub 按钮手动下载 Release。");
            }

            var update = new UpdateRelease(
                tagName,
                latestVersion.ToString(),
                $"{GitHubWebBaseUrl}{repository}/releases/tag/{Uri.EscapeDataString(tagName)}",
                "通过 GitHub Releases 备用线路发现更新。",
                asset);
            return new UpdateCheckResult(true, true, $"发现新版本 {tagName}。", update);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<UpdateAsset?> SelectDeterministicAssetAsync(
        string repository,
        string tagName,
        Version latestVersion,
        CancellationToken cancellationToken)
    {
        var versionText = latestVersion.ToString();
        var candidates = new[]
        {
            new UpdateAssetCandidate($"{PackageBaseName}-{versionText}-setup.exe", UpdatePackageKind.Installer),
            new UpdateAssetCandidate($"{PackageBaseName}-{versionText}-win-x64-portable.zip", UpdatePackageKind.PortableZip),
            new UpdateAssetCandidate(StableInstallerAssetName, UpdatePackageKind.Installer),
            new UpdateAssetCandidate(StablePortableAssetName, UpdatePackageKind.PortableZip)
        };

        var preferredKind = IsPortableInstall ? UpdatePackageKind.PortableZip : UpdatePackageKind.Installer;
        foreach (var candidate in candidates
                     .OrderBy(x => x.Kind == preferredKind ? 0 : 1)
                     .ThenBy(x => x.Kind == UpdatePackageKind.Installer ? 0 : 1))
        {
            var url = BuildDownloadUrl(repository, tagName, candidate.Name);
            var size = await TryGetDownloadSizeAsync(url, cancellationToken).ConfigureAwait(false);
            if (size is not null)
            {
                return new UpdateAsset(candidate.Name, url, size.Value, candidate.Kind);
            }
        }

        return null;
    }

    private static async Task<long?> TryGetDownloadSizeAsync(string downloadUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, downloadUrl);
            request.Headers.UserAgent.ParseAdd("KuikuiGameAssistant");

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? response.Content.Headers.ContentLength ?? 0
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildDownloadUrl(string repository, string tagName, string assetName)
    {
        return $"{GitHubWebBaseUrl}{repository}/releases/download/{Uri.EscapeDataString(tagName)}/{Uri.EscapeDataString(assetName)}";
    }

    private static string BuildLatestDownloadUrl(string repository, string assetName)
    {
        return $"{GitHubWebBaseUrl}{repository}/releases/latest/download/{Uri.EscapeDataString(assetName)}";
    }

    private static string? ExtractTagName(Uri? uri)
    {
        if (uri is null)
        {
            return null;
        }

        const string marker = "/releases/tag/";
        var path = uri.AbsolutePath;
        var markerIndex = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var tagName = path[(markerIndex + marker.Length)..].Trim('/');
        return string.IsNullOrWhiteSpace(tagName) ? null : Uri.UnescapeDataString(tagName);
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

    private static async Task DownloadFileAsync(
        string downloadUrl,
        string destinationPath,
        long expectedSizeBytes,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var tempPath = destinationPath + ".download";
        TryDeleteFile(tempPath);

        try
        {
            await DownloadFileWithHttpClientAsync(
                downloadUrl,
                tempPath,
                expectedSizeBytes,
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableDownloadException(ex))
        {
            TryDeleteFile(tempPath);
            progress?.Report(new UpdateDownloadProgress(
                0,
                expectedSizeBytes > 0 ? expectedSizeBytes : null,
                "内置下载器连接失败，正在切换系统下载器..."));

            await DownloadFileWithCurlAsync(
                downloadUrl,
                tempPath,
                expectedSizeBytes,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, destinationPath, true);
        var finalSize = new FileInfo(destinationPath).Length;
        progress?.Report(new UpdateDownloadProgress(finalSize, finalSize, "下载完成，正在准备下次更新..."));
    }

    private static async Task WritePendingUpdateAsync(PendingUpdateInfo pendingUpdate, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(UpdatesRootFolder);
        await using var stream = new FileStream(
            PendingUpdateManifestPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            DownloadBufferSize,
            useAsync: true);
        await JsonSerializer.SerializeAsync(stream, pendingUpdate, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static void WritePendingUpdateBestEffort(PendingUpdateInfo pendingUpdate)
    {
        try
        {
            Directory.CreateDirectory(UpdatesRootFolder);
            File.WriteAllText(PendingUpdateManifestPath, JsonSerializer.Serialize(pendingUpdate, JsonOptions));
        }
        catch
        {
        }
    }

    private static void ClearPendingUpdate()
    {
        TryDeleteFile(PendingUpdateManifestPath);
    }

    private static async Task DownloadFileWithHttpClientAsync(
        string downloadUrl,
        string tempPath,
        long expectedSizeBytes,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        if ((totalBytes is null or <= 0) && expectedSizeBytes > 0)
        {
            totalBytes = expectedSizeBytes;
        }

        progress?.Report(new UpdateDownloadProgress(0, totalBytes, "正在下载更新包..."));
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            DownloadBufferSize,
            useAsync: true);

        var buffer = new byte[DownloadBufferSize];
        var bytesReceived = 0L;
        var nextReportAt = DateTimeOffset.UtcNow;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            bytesReceived += read;
            if (DateTimeOffset.UtcNow >= nextReportAt || bytesReceived == totalBytes)
            {
                progress?.Report(new UpdateDownloadProgress(bytesReceived, totalBytes, "正在下载更新包..."));
                nextReportAt = DateTimeOffset.UtcNow.AddMilliseconds(120);
            }
        }

        progress?.Report(new UpdateDownloadProgress(bytesReceived, totalBytes, "正在校验下载包..."));
    }

    private static async Task DownloadFileWithCurlAsync(
        string downloadUrl,
        string tempPath,
        long expectedSizeBytes,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalBytes = expectedSizeBytes > 0 ? expectedSizeBytes : (long?)null;
        progress?.Report(new UpdateDownloadProgress(0, totalBytes, "正在使用系统下载器下载更新包..."));

        using var process = Process.Start(CreateCurlDownloadStartInfo(downloadUrl, tempPath))
            ?? throw new IOException("无法启动系统下载器 curl.exe。");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            while (!process.HasExited)
            {
                if (File.Exists(tempPath))
                {
                    var currentSize = new FileInfo(tempPath).Length;
                    progress?.Report(new UpdateDownloadProgress(currentSize, totalBytes, "正在使用系统下载器下载更新包..."));
                }

                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var detail = TrimForStatus(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
            throw new IOException(string.IsNullOrWhiteSpace(detail)
                ? $"系统下载器失败，退出码 {process.ExitCode}。"
                : $"系统下载器失败，退出码 {process.ExitCode}：{detail}");
        }

        var finalSize = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;
        if (finalSize <= 0)
        {
            throw new IOException("系统下载器没有生成有效安装包。");
        }

        progress?.Report(new UpdateDownloadProgress(finalSize, totalBytes ?? finalSize, "正在校验下载包..."));
    }

    private static ProcessStartInfo CreateCurlDownloadStartInfo(string downloadUrl, string tempPath)
    {
        var startInfo = new ProcessStartInfo("curl.exe")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        foreach (var argument in new[]
                 {
                     "--location",
                     "--fail",
                     "--show-error",
                     "--silent",
                     "--ssl-no-revoke",
                     "--connect-timeout",
                     "20",
                     "--retry",
                     "3",
                     "--retry-delay",
                     "2",
                     "--output",
                     tempPath,
                     downloadUrl
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static bool IsRecoverableDownloadException(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException
                or IOException
                or WebException
                or TimeoutException
                or OperationCanceledException
                or System.ComponentModel.Win32Exception)
            {
                return true;
            }

            if (current.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("connection", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildDownloadFailureMessage(Exception ex, string downloadUrl)
    {
        var detail = GetHelpfulExceptionMessage(ex);
        var prefix = IsRecoverableDownloadException(ex)
            ? "应用更新失败：下载包时网络连接失败。"
            : "应用更新失败：";

        return $"{prefix}{detail}\n\n可以点击 Release 或复制直链手动下载：{downloadUrl}";
    }

    private static string GetHelpfulExceptionMessage(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var message = current.Message.Trim();
            if (string.IsNullOrWhiteSpace(message)
                || message.Equals("See inner exception.", StringComparison.OrdinalIgnoreCase)
                || messages.Contains(message))
            {
                continue;
            }

            messages.Add(message);
        }

        return messages.Count == 0 ? "未知错误。" : string.Join("；", messages);
    }

    private static string TrimForStatus(string value)
    {
        value = value.ReplaceLineEndings(" ").Trim();
        return value.Length <= 240 ? value : value[..240] + "...";
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static void StartUpdatePackage(string packagePath, UpdatePackageKind kind)
    {
        if (kind == UpdatePackageKind.Installer)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = packagePath,
                UseShellExecute = true
            });
            return;
        }

        StartPortableUpdater(packagePath);
    }

    private static string BuildGitHubFailureMessage(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden && IsRateLimited(response))
        {
            var resetText = TryFormatRateLimitReset(response);
            return string.IsNullOrWhiteSpace(resetText)
                ? "检查更新失败：GitHub 请求次数已用完，暂时无法判断是不是最新版本。请稍后再试，或点击 GitHub 按钮手动查看 Release。"
                : $"检查更新失败：GitHub 请求次数已用完，暂时无法判断是不是最新版本。预计 {resetText} 后可重试，也可以点击 GitHub 按钮手动查看 Release。";
        }

        return $"检查更新失败：GitHub 返回 {(int)response.StatusCode} {response.ReasonPhrase}，无法判断是不是最新版本。请稍后再试，或点击 GitHub 按钮手动查看 Release。";
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues)
            && remainingValues.Any(value => value == "0"))
        {
            return true;
        }

        return response.ReasonPhrase?.Contains("rate", StringComparison.OrdinalIgnoreCase) == true
               || response.ReasonPhrase?.Contains("limit", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? TryFormatRateLimitReset(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
            || !long.TryParse(resetValues.FirstOrDefault(), out var resetUnixSeconds))
        {
            return null;
        }

        var resetAt = DateTimeOffset.FromUnixTimeSeconds(resetUnixSeconds).ToLocalTime();
        var remaining = resetAt - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return null;
        }

        return remaining.TotalMinutes < 1
            ? "不到 1 分钟"
            : $"{Math.Ceiling(remaining.TotalMinutes)} 分钟";
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

    private sealed record UpdateAssetCandidate(string Name, UpdatePackageKind Kind);
}
