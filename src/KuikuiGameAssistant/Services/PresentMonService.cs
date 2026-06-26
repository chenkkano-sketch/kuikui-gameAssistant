using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using KuikuiGameAssistant.Models;

namespace KuikuiGameAssistant.Services;

public sealed record PresentMonFrameSnapshot(double? FramesPerSecond, double? FrameTimeMs, string Status)
{
    public double? OnePercentLowFps { get; init; }
}

public sealed class PresentMonService : IDisposable
{
    private static readonly TimeSpan ConsoleCurrentFpsWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ConsoleOnePercentLowWindow = TimeSpan.FromSeconds(30);
    private readonly AppSettings _settings;
    private readonly object _syncRoot = new();
    private readonly Dictionary<FrameTarget, Queue<FrameSample>> _samplesByTarget = new();
    private readonly KuikuiTelemetryServiceClient _serviceClient = new();
    private PresentMonApiFrameProvider? _apiProvider;
    private FrameTarget? _lastConsoleTarget;
    private Process? _process;
    private string[] _header = Array.Empty<string>();
    private PresentMonFrameSnapshot _latest = new(null, null, "PresentMon 未启用");
    private DateTimeOffset? _lastFrameAt;
    private string? _lastErrorStatus;
    private string? _consoleFallbackReason;
    private bool _disposed;

    public PresentMonService(AppSettings settings)
    {
        _settings = settings;
        _settings.PropertyChanged += Settings_PropertyChanged;
        Configure();
    }

    public PresentMonFrameSnapshot Latest
    {
        get
        {
            if (!_settings.EnablePresentMon)
            {
                return new PresentMonFrameSnapshot(null, null, "FPS 采集未启用");
            }

            if (_serviceClient.TryReadLatest(out var serviceSnapshot))
            {
                return new PresentMonFrameSnapshot(
                    serviceSnapshot.FramesPerSecond,
                    serviceSnapshot.FrameTimeMs,
                    serviceSnapshot.Status)
                {
                    OnePercentLowFps = serviceSnapshot.OnePercentLowFps
                };
            }

            if (!UseInProcessFallback)
            {
                return new PresentMonFrameSnapshot(null, null, "FPS 引擎未运行，请安装或修复后台服务");
            }

            PresentMonApiFrameProvider? apiProvider;
            lock (_syncRoot)
            {
                apiProvider = _apiProvider;
            }

            if (apiProvider is not null)
            {
                return apiProvider.Latest;
            }

            lock (_syncRoot)
            {
                if (_process is { HasExited: false }
                    && _lastFrameAt is not null
                    && DateTimeOffset.Now - _lastFrameAt > TimeSpan.FromSeconds(2.5))
                {
                    return _latest with { FramesPerSecond = null, FrameTimeMs = null, Status = BuildConsoleStatus("PresentMon console 等待游戏帧") };
                }

                return _latest;
            }
        }
    }

    public void Restart() => Configure();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.PropertyChanged -= Settings_PropertyChanged;
        Stop();
    }

    private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.EnablePresentMon) or nameof(AppSettings.PresentMonPath))
        {
            Configure();
        }
    }

    private void Configure()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        ClearSamples();
        _consoleFallbackReason = null;

        if (!_settings.EnablePresentMon)
        {
            SetLatest(new PresentMonFrameSnapshot(null, null, "PresentMon 未启用"));
            return;
        }

        if (!UseInProcessFallback)
        {
            SetLatest(new PresentMonFrameSnapshot(null, null, "FPS 引擎未运行，请安装或修复后台服务"));
            return;
        }

        if (TryStartApiProvider(out var apiProvider, out var fallbackReason))
        {
            lock (_syncRoot)
            {
                _apiProvider = apiProvider;
                _latest = new PresentMonFrameSnapshot(null, null, "PresentMon Service API 已连接，等待游戏窗口");
            }

            return;
        }

        _consoleFallbackReason = fallbackReason;
        var executable = FindPresentMonExecutable(_settings.PresentMonPath);
        if (executable is null)
        {
            SetLatest(new PresentMonFrameSnapshot(null, null, BuildConsoleStatus("未找到 PresentMon.exe")));
            return;
        }

        Start(executable);
    }

    private static bool UseInProcessFallback =>
        string.Equals(Environment.GetEnvironmentVariable("KUIKUI_USE_INPROCESS_PRESENTMON"), "1", StringComparison.OrdinalIgnoreCase);

    private static bool TryStartApiProvider(out PresentMonApiFrameProvider? provider, out string fallbackReason)
    {
        provider = new PresentMonApiFrameProvider();
        if (provider.TryStart(out fallbackReason))
        {
            return true;
        }

        provider.Dispose();
        provider = null;
        return false;
    }

    private void Start(string executable)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
            };

            startInfo.ArgumentList.Add("--output_stdout");
            startInfo.ArgumentList.Add("--no_console_stats");
            startInfo.ArgumentList.Add("--v2_metrics");
            startInfo.ArgumentList.Add("--exclude_dropped");
            startInfo.ArgumentList.Add("--stop_existing_session");

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, e) => HandleOutputLine(e.Data);
            process.ErrorDataReceived += (_, e) => HandleErrorLine(e.Data);
            process.Exited += (_, _) =>
            {
                lock (_syncRoot)
                {
                    if (!_disposed && ReferenceEquals(_process, process))
                    {
                        _latest = new PresentMonFrameSnapshot(null, null, _lastErrorStatus ?? BuildExitStatus(process));
                    }
                }
            };

            if (!process.Start())
            {
                process.Dispose();
                SetLatest(new PresentMonFrameSnapshot(null, null, "PresentMon 启动失败"));
                return;
            }

            lock (_syncRoot)
            {
                _process = process;
                _latest = new PresentMonFrameSnapshot(null, null, BuildConsoleStatus("PresentMon console 启动中"));
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            SetLatest(new PresentMonFrameSnapshot(null, null, BuildConsoleStatus($"PresentMon console 启动失败：{ex.Message}")));
        }
    }

    private void Stop()
    {
        Process? process;
        PresentMonApiFrameProvider? apiProvider;
        lock (_syncRoot)
        {
            apiProvider = _apiProvider;
            _apiProvider = null;
            process = _process;
            _process = null;
            _header = Array.Empty<string>();
        }

        apiProvider?.Dispose();

        if (process is null)
        {
            return;
        }

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
        finally
        {
            process.Dispose();
        }
    }

    private void HandleOutputLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var columns = SplitCsvLine(line);
        if (columns.Length < 4)
        {
            return;
        }

        if (columns.Any(x => x.Equals("Application", StringComparison.OrdinalIgnoreCase))
            && columns.Any(x => x.Contains("MsBetween", StringComparison.OrdinalIgnoreCase)
                                || x.Contains("FrameTime", StringComparison.OrdinalIgnoreCase)))
        {
            lock (_syncRoot)
            {
                _header = columns;
                _latest = new PresentMonFrameSnapshot(null, null, BuildConsoleStatus("PresentMon console 已连接，等待游戏帧"));
            }

            return;
        }

        if (!TryParseFrame(columns, out var target, out var frameTimeMs))
        {
            return;
        }

        var now = DateTimeOffset.Now;
        lock (_syncRoot)
        {
            if (!_samplesByTarget.TryGetValue(target, out var samples))
            {
                samples = new Queue<FrameSample>();
                _samplesByTarget[target] = samples;
            }

            samples.Enqueue(new FrameSample(now, frameTimeMs));
            TrimSamples(now);

            var best = SelectConsoleTarget();
            if (best is null || !_samplesByTarget.TryGetValue(best, out var bestSamples) || bestSamples.Count == 0)
            {
                _latest = new PresentMonFrameSnapshot(null, null, BuildConsoleStatus("PresentMon console 等待游戏帧"));
                return;
            }

            _lastConsoleTarget = best;
            _lastFrameAt = now;
            var currentSamples = bestSamples
                .Where(x => now - x.Timestamp <= ConsoleCurrentFpsWindow)
                .ToArray();
            if (currentSamples.Length == 0)
            {
                currentSamples = bestSamples.ToArray();
            }

            var averageFrameTimeMs = currentSamples.Average(x => x.FrameTimeMs);
            _latest = new PresentMonFrameSnapshot(
                FramesPerSecond: averageFrameTimeMs <= 0 ? null : 1000d / averageFrameTimeMs,
                FrameTimeMs: averageFrameTimeMs,
                Status: BuildConsoleStatus($"PresentMon console：{best.Label}"))
            {
                OnePercentLowFps = CalculateOnePercentLowFps(bestSamples)
            };
        }
    }

    private void HandleErrorLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var status = BuildErrorStatus(line);
        if (status is not null)
        {
            lock (_syncRoot)
            {
                _lastErrorStatus = status;
                _latest = new PresentMonFrameSnapshot(null, null, status);
            }
        }
    }

    private bool TryParseFrame(string[] columns, out FrameTarget target, out double frameTimeMs)
    {
        target = new FrameTarget(0, "未知进程");
        frameTimeMs = 0;

        string? ReadColumn(params string[] names)
        {
            foreach (var name in names)
            {
                var index = Array.FindIndex(_header, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0 && index < columns.Length)
                {
                    return columns[index];
                }
            }

            return null;
        }

        var app = ReadColumn("Application", "ProcessName");
        var application = "未知进程";
        if (!string.IsNullOrWhiteSpace(app))
        {
            application = Path.GetFileName(app.Trim());
        }

        var processId = 0;
        var pid = ReadColumn("ProcessID", "ProcessId", "Process ID");
        if (!string.IsNullOrWhiteSpace(pid)
            && int.TryParse(pid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPid)
            && parsedPid > 0)
        {
            processId = parsedPid;
        }

        target = new FrameTarget(processId, application);

        var value = ReadColumn(
            "PresentedFrameTime",
            "FrameTime-Presents",
            "MsBetweenPresents",
            "MsBetweenDisplayChange",
            "msBetweenPresents",
            "msBetweenDisplayChange",
            "FrameTime",
            "FrameTimeMs");

        return value is not null
               && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out frameTimeMs)
               && frameTimeMs is > 0 and < 1000;
    }

    private void TrimSamples(DateTimeOffset now)
    {
        var cutoff = now - ConsoleOnePercentLowWindow;
        foreach (var queue in _samplesByTarget.Values)
        {
            while (queue.Count > 0 && queue.Peek().Timestamp < cutoff)
            {
                queue.Dequeue();
            }
        }
    }

    private static double? CalculateOnePercentLowFps(IEnumerable<FrameSample> samples)
    {
        var frameTimes = samples
            .Select(x => x.FrameTimeMs)
            .Where(x => double.IsFinite(x) && x is > 0 and < 1000)
            .OrderByDescending(x => x)
            .ToArray();
        if (frameTimes.Length < 10)
        {
            return null;
        }

        var worstCount = Math.Max(1, (int)Math.Ceiling(frameTimes.Length * 0.01d));
        var worstAverageFrameTimeMs = frameTimes.Take(worstCount).Average();
        return worstAverageFrameTimeMs <= 0 ? null : 1000d / worstAverageFrameTimeMs;
    }

    private FrameTarget? SelectConsoleTarget()
    {
        if (TryGetForegroundTarget(out var foregroundPid, out _))
        {
            var foregroundTarget = _samplesByTarget
                .Where(x => x.Key.ProcessId == foregroundPid && x.Value.Count > 0)
                .OrderByDescending(x => x.Value.Count)
                .Select(x => x.Key)
                .FirstOrDefault();

            if (foregroundTarget is not null)
            {
                return foregroundTarget;
            }
        }

        if (_lastConsoleTarget is not null
            && _samplesByTarget.TryGetValue(_lastConsoleTarget, out var previousSamples)
            && previousSamples.Count > 0)
        {
            return _lastConsoleTarget;
        }

        return _samplesByTarget
            .Where(x => x.Value.Count > 0 && !IsBlockedConsoleTarget(x.Key))
            .OrderByDescending(x => x.Value.Count)
            .Select(x => x.Key)
            .FirstOrDefault();
    }

    private static bool IsBlockedConsoleTarget(FrameTarget target)
    {
        if (target.ProcessId == Environment.ProcessId)
        {
            return true;
        }

        return target.Application.Contains("PresentMon", StringComparison.OrdinalIgnoreCase)
               || target.Application.Equals("KuikuiGameAssistant.exe", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearSamples()
    {
        lock (_syncRoot)
        {
            _samplesByTarget.Clear();
            _lastConsoleTarget = null;
            _lastFrameAt = null;
            _lastErrorStatus = null;
        }
    }

    private void SetLatest(PresentMonFrameSnapshot snapshot)
    {
        lock (_syncRoot)
        {
            _latest = snapshot;
        }
    }

    private string BuildConsoleStatus(string status)
    {
        return string.IsNullOrWhiteSpace(_consoleFallbackReason)
            ? status
            : $"{status}；Service API 不可用，已回退：{_consoleFallbackReason}";
    }

    private static string? FindPresentMonExecutable(string configuredPath)
    {
        foreach (var candidate in EnumerateCandidatePaths(configuredPath))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (Directory.Exists(candidate))
            {
                var executable = Directory.EnumerateFiles(candidate, "PresentMon*.exe", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (executable is not null)
                {
                    return executable;
                }
            }
        }

        return null;
    }

    private static string? BuildErrorStatus(string line)
    {
        var text = line.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (text.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            || text.Contains("administrative privileges", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Performance Log Users", StringComparison.OrdinalIgnoreCase))
        {
            return "PresentMon console 需要管理员权限或加入 Performance Log Users 组";
        }

        return text.Contains("error", StringComparison.OrdinalIgnoreCase)
               || text.Contains("failed", StringComparison.OrdinalIgnoreCase)
               || text.Contains("denied", StringComparison.OrdinalIgnoreCase)
            ? $"PresentMon console：{text}"
            : null;
    }

    private static string BuildExitStatus(Process process)
    {
        try
        {
            return process.ExitCode == 0
                ? "PresentMon console 已退出"
                : $"PresentMon console 已退出，退出码 {process.ExitCode}";
        }
        catch
        {
            return "PresentMon console 已退出";
        }
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return Environment.ExpandEnvironmentVariables(configuredPath.Trim('"', ' '));
        }

        yield return Path.Combine(AppContext.BaseDirectory, "PresentMon.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon");
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "PresentMon"));
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "PresentMon"));
        yield return Path.Combine(Environment.CurrentDirectory, "tools", "PresentMon");

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var item in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(item, "PresentMon.exe");
        }
    }

    private static bool TryGetForegroundTarget(out int processId, out string application)
    {
        processId = 0;
        application = string.Empty;

        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(window, out var pid);
        if (pid == 0 || pid == Environment.ProcessId)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (process.HasExited)
            {
                return false;
            }

            processId = (int)pid;
            application = Path.GetFileName(process.MainModule?.FileName) ?? $"{process.ProcessName}.exe";
            return true;
        }
        catch
        {
            processId = (int)pid;
            application = $"PID {pid}";
            return true;
        }
    }

    private static string GetProcessLabel(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return Path.GetFileName(process.MainModule?.FileName) ?? $"{process.ProcessName}.exe";
        }
        catch
        {
            return $"PID {processId}";
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static string[] SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        values.Add(current.ToString());
        return values.ToArray();
    }

    private sealed class PresentMonApiFrameProvider : IDisposable
    {
        private const int MaxSwapChains = 8;
        private const double QueryWindowMs = 1000;
        private const double QueryOffsetMs = 1020;
        private const uint EtwFlushPeriodMs = 16;

        private PresentMonApiLibrary? _api;
        private IntPtr _session;
        private IntPtr _query;
        private PmQueryElement[] _elements = Array.Empty<PmQueryElement>();
        private byte[] _blob = Array.Empty<byte>();
        private int? _trackedProcessId;
        private string _trackedApplication = string.Empty;
        private PresentMonFrameSnapshot _latest = new(null, null, "PresentMon Service API 启动中");
        private bool _hasFrameTimeMetric = true;
        private bool _disposed;

        public PresentMonFrameSnapshot Latest
        {
            get
            {
                if (_disposed || _api is null || _session == IntPtr.Zero || _query == IntPtr.Zero)
                {
                    return new PresentMonFrameSnapshot(null, null, "PresentMon Service API 未连接");
                }

                return Poll();
            }
        }

        public bool TryStart(out string fallbackReason)
        {
            fallbackReason = string.Empty;

            if (!PresentMonApiLibrary.TryLoad(out var api, out fallbackReason) || api is null)
            {
                return false;
            }

            _api = api;
            var status = api.OpenSession(out _session);
            if (status != PmStatus.Success)
            {
                fallbackReason = $"无法打开 Service 会话（{FormatStatus(status)}）";
                return false;
            }

            _ = api.SetEtwFlushPeriod(_session, EtwFlushPeriodMs);

            if (!TryRegisterQuery(api, out fallbackReason))
            {
                return false;
            }

            var blobSize = checked((int)(_elements[^1].DataOffset + _elements[^1].DataSize));
            if (blobSize <= 0)
            {
                fallbackReason = "Service API 返回了空查询布局";
                return false;
            }

            _blob = new byte[blobSize * MaxSwapChains];
            _latest = new PresentMonFrameSnapshot(null, null, "PresentMon Service API 已连接，等待游戏窗口");
            return true;
        }

        private bool TryRegisterQuery(PresentMonApiLibrary api, out string fallbackReason)
        {
            _hasFrameTimeMetric = true;
            _elements =
            [
                new PmQueryElement(PmMetric.PresentedFps, PmStat.Avg),
                new PmQueryElement(PmMetric.PresentedFrameTime, PmStat.Avg)
            ];

            var status = api.RegisterDynamicQuery(
                _session,
                out _query,
                _elements,
                (ulong)_elements.Length,
                QueryWindowMs,
                QueryOffsetMs);

            if (status == PmStatus.Success)
            {
                fallbackReason = string.Empty;
                return true;
            }

            _hasFrameTimeMetric = false;
            _elements = [new PmQueryElement(PmMetric.PresentedFps, PmStat.Avg)];
            status = api.RegisterDynamicQuery(
                _session,
                out _query,
                _elements,
                (ulong)_elements.Length,
                QueryWindowMs,
                QueryOffsetMs);

            if (status == PmStatus.Success)
            {
                fallbackReason = string.Empty;
                return true;
            }

            fallbackReason = $"无法注册 FPS 查询（{FormatStatus(status)}）";
            return false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                StopTracking();
            }
            catch
            {
            }

            try
            {
                if (_query != IntPtr.Zero && _api is not null)
                {
                    _ = _api.FreeDynamicQuery(_query);
                }
            }
            catch
            {
            }

            try
            {
                if (_session != IntPtr.Zero && _api is not null)
                {
                    _ = _api.CloseSession(_session);
                }
            }
            catch
            {
            }

            _query = IntPtr.Zero;
            _session = IntPtr.Zero;
            _api?.Dispose();
            _api = null;
        }

        private PresentMonFrameSnapshot Poll()
        {
            if (!TrySelectTarget(out var processId, out var application))
            {
                _latest = _latest with
                {
                    FramesPerSecond = null,
                    FrameTimeMs = null,
                    Status = "PresentMon Service API 已连接，等待游戏窗口"
                };
                return _latest;
            }

            if (!EnsureTracking(processId, application, out var trackingStatus))
            {
                _latest = new PresentMonFrameSnapshot(null, null, trackingStatus);
                return _latest;
            }

            Array.Clear(_blob);
            var swapChainCount = (uint)MaxSwapChains;
            var status = _api!.PollDynamicQuery(_query, (uint)processId, _blob, ref swapChainCount);
            if (status != PmStatus.Success)
            {
                if (status is PmStatus.InvalidPid or PmStatus.BadHandle)
                {
                    StopTracking();
                }

                _latest = new PresentMonFrameSnapshot(
                    null,
                    null,
                    $"PresentMon Service API 查询失败：{FormatStatus(status)}");
                return _latest;
            }

            if (swapChainCount == 0 || !TryReadBestSwapChain((int)Math.Min(swapChainCount, MaxSwapChains), out var fps, out var frameTimeMs))
            {
                _latest = new PresentMonFrameSnapshot(
                    null,
                    null,
                    $"PresentMon Service API：{_trackedApplication}，等待帧数据");
                return _latest;
            }

            _latest = new PresentMonFrameSnapshot(
                fps,
                frameTimeMs,
                $"PresentMon Service API：{_trackedApplication}");
            return _latest;
        }

        private bool TrySelectTarget(out int processId, out string application)
        {
            if (TryGetForegroundTarget(out processId, out application))
            {
                return true;
            }

            if (_trackedProcessId is { } tracked && IsProcessAlive(tracked))
            {
                processId = tracked;
                application = string.IsNullOrWhiteSpace(_trackedApplication)
                    ? GetProcessLabel(tracked)
                    : _trackedApplication;
                return true;
            }

            processId = 0;
            application = string.Empty;
            return false;
        }

        private bool EnsureTracking(int processId, string application, out string status)
        {
            status = string.Empty;
            if (_trackedProcessId == processId)
            {
                return true;
            }

            StopTracking();

            var result = _api!.StartTrackingProcess(_session, (uint)processId);
            if (result is not PmStatus.Success and not PmStatus.AlreadyTrackingProcess)
            {
                status = $"PresentMon Service API 无法跟踪 {application}：{FormatStatus(result)}";
                return false;
            }

            _trackedProcessId = processId;
            _trackedApplication = application;
            return true;
        }

        private void StopTracking()
        {
            if (_trackedProcessId is { } processId && _api is not null && _session != IntPtr.Zero)
            {
                _ = _api.StopTrackingProcess(_session, (uint)processId);
            }

            _trackedProcessId = null;
            _trackedApplication = string.Empty;
        }

        private bool TryReadBestSwapChain(int swapChainCount, out double fps, out double frameTimeMs)
        {
            fps = 0;
            frameTimeMs = 0;
            var blobSize = checked((int)(_elements[^1].DataOffset + _elements[^1].DataSize));
            var fpsOffset = checked((int)_elements[0].DataOffset);
            var frameTimeOffset = _hasFrameTimeMetric ? checked((int)_elements[1].DataOffset) : 0;

            for (var i = 0; i < swapChainCount; i++)
            {
                var baseOffset = i * blobSize;
                var candidateFps = BitConverter.ToDouble(_blob, baseOffset + fpsOffset);
                var candidateFrameTime = _hasFrameTimeMetric
                    ? BitConverter.ToDouble(_blob, baseOffset + frameTimeOffset)
                    : 1000d / candidateFps;

                if (!IsPlausibleFrameMetric(candidateFps, candidateFrameTime))
                {
                    continue;
                }

                if (candidateFps > fps)
                {
                    fps = candidateFps;
                    frameTimeMs = candidateFrameTime;
                }
            }

            return fps > 0 && frameTimeMs > 0;
        }

        private static bool IsPlausibleFrameMetric(double fps, double frameTimeMs)
        {
            return double.IsFinite(fps)
                   && double.IsFinite(frameTimeMs)
                   && fps is > 0 and < 2000
                   && frameTimeMs is > 0 and < 1000;
        }

        private static string FormatStatus(PmStatus status) => status switch
        {
            PmStatus.Success => "成功",
            PmStatus.ServiceError => "服务未运行或不可用",
            PmStatus.PipeError => "管道通信失败",
            PmStatus.SessionNotOpen => "会话未打开",
            PmStatus.InvalidPid => "无效进程",
            PmStatus.QueryMalformed => "查询不兼容",
            PmStatus.FeatureDisabled => "功能未启用",
            PmStatus.MiddlewareVersionLow => "API 版本过低",
            PmStatus.MiddlewareVersionHigh => "API 版本过高",
            _ => status.ToString()
        };
    }

    private sealed class PresentMonApiLibrary : IDisposable
    {
        private const string DllName = "PresentMonAPI2.dll";
        private readonly IntPtr _library;
        private bool _disposed;

        private PresentMonApiLibrary(IntPtr library)
        {
            _library = library;
            OpenSession = Load<PmOpenSession>("pmOpenSession");
            CloseSession = Load<PmCloseSession>("pmCloseSession");
            StartTrackingProcess = Load<PmStartTrackingProcess>("pmStartTrackingProcess");
            StopTrackingProcess = Load<PmStopTrackingProcess>("pmStopTrackingProcess");
            SetEtwFlushPeriod = Load<PmSetEtwFlushPeriod>("pmSetEtwFlushPeriod");
            RegisterDynamicQuery = Load<PmRegisterDynamicQuery>("pmRegisterDynamicQuery");
            FreeDynamicQuery = Load<PmFreeDynamicQuery>("pmFreeDynamicQuery");
            PollDynamicQuery = Load<PmPollDynamicQuery>("pmPollDynamicQuery");
        }

        public PmOpenSession OpenSession { get; }
        public PmCloseSession CloseSession { get; }
        public PmStartTrackingProcess StartTrackingProcess { get; }
        public PmStopTrackingProcess StopTrackingProcess { get; }
        public PmSetEtwFlushPeriod SetEtwFlushPeriod { get; }
        public PmRegisterDynamicQuery RegisterDynamicQuery { get; }
        public PmFreeDynamicQuery FreeDynamicQuery { get; }
        public PmPollDynamicQuery PollDynamicQuery { get; }

        public static bool TryLoad(out PresentMonApiLibrary? api, out string reason)
        {
            api = null;
            foreach (var candidate in EnumerateCandidateDllPaths())
            {
                if (!NativeLibrary.TryLoad(candidate, out var handle))
                {
                    continue;
                }

                try
                {
                    api = new PresentMonApiLibrary(handle);
                    reason = string.Empty;
                    return true;
                }
                catch (Exception ex)
                {
                    NativeLibrary.Free(handle);
                    reason = $"API DLL 不兼容：{ex.Message}";
                    return false;
                }
            }

            reason = "未找到 PresentMonAPI2.dll";
            return false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            NativeLibrary.Free(_library);
        }

        private T Load<T>(string name) where T : Delegate
        {
            var address = NativeLibrary.GetExport(_library, name);
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        private static IEnumerable<string> EnumerateCandidateDllPaths()
        {
            yield return DllName;
            yield return Path.Combine(AppContext.BaseDirectory, DllName);
            yield return Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon", DllName);

            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                     })
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                yield return Path.Combine(root, "Intel", "PresentMon", DllName);
                yield return Path.Combine(root, "Intel", "PresentMon", "SDK", DllName);
            }

            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var item in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return Path.Combine(item, DllName);
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PmStatus PmOpenSession(out IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PmStatus PmCloseSession(IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PmStatus PmStartTrackingProcess(IntPtr handle, uint processId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PmStatus PmStopTrackingProcess(IntPtr handle, uint processId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PmStatus PmSetEtwFlushPeriod(IntPtr handle, uint periodMs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PmStatus PmRegisterDynamicQuery(
        IntPtr sessionHandle,
        out IntPtr queryHandle,
        [In, Out] PmQueryElement[] elements,
        ulong numElements,
        double windowSizeMs,
        double metricOffsetMs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PmStatus PmFreeDynamicQuery(IntPtr queryHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate PmStatus PmPollDynamicQuery(
        IntPtr queryHandle,
        uint processId,
        [Out] byte[] blob,
        ref uint numSwapChains);

    private enum PmStatus
    {
        Success,
        Failure,
        BadArgument,
        BadHandle,
        ServiceError,
        InvalidEtlFile,
        InvalidPid,
        AlreadyTrackingProcess,
        UnableToCreateNsm,
        InvalidAdapterId,
        OutOfRange,
        InsufficientBuffer,
        PipeError,
        SessionNotOpen,
        MiddlewareMissingPath,
        NonexistentFilePath,
        MiddlewareInvalidSignature,
        MiddlewareMissingEndpoint,
        MiddlewareVersionLow,
        MiddlewareVersionHigh,
        MiddlewareServiceMismatch,
        QueryMalformed,
        ModeMismatch,
        FeatureDisabled
    }

    private enum PmMetric
    {
        DisplayedFps = 11,
        PresentedFps = 12,
        ApplicationFps = 62,
        PresentedFrameTime = 87
    }

    private enum PmStat
    {
        None,
        Avg
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PmQueryElement
    {
        public PmMetric Metric;
        public PmStat Stat;
        public uint DeviceId;
        public uint ArrayIndex;
        public ulong DataOffset;
        public ulong DataSize;

        public PmQueryElement(PmMetric metric, PmStat stat)
        {
            Metric = metric;
            Stat = stat;
            DeviceId = 0;
            ArrayIndex = 0;
            DataOffset = 0;
            DataSize = 0;
        }
    }

    private sealed record FrameTarget(int ProcessId, string Application)
    {
        public string Label => ProcessId > 0 ? $"{Application} ({ProcessId})" : Application;
    }

    private sealed record FrameSample(DateTimeOffset Timestamp, double FrameTimeMs);
}
