using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;

namespace KuikuiTelemetryService;

internal sealed class FrameCollector : IDisposable
{
    private static readonly Guid DxgiProviderGuid = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3d9ProviderGuid = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private static readonly TimeSpan CurrentFpsWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan OnePercentLowWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleFrameTimeout = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan ProcessNameCacheTtl = TimeSpan.FromSeconds(30);
    private const string SessionName = "KuikuiGameAssistantFrameCollector";
    private const ulong PresentEventsKeyword = 0x8000000000000002;
    private const int DxgiPresentTask = 9;
    private const int D3d9PresentTask = 1;
    private const int StopOpcode = 2;
    private const double MinimumFrameTimeMs = 0.1;
    private const double MaximumFrameTimeMs = 1000;

    private readonly ILogger<FrameCollector> _logger;
    private readonly object _syncRoot = new();
    private readonly Dictionary<int, FrameStream> _streamsByProcessId = new();
    private readonly Dictionary<int, ProcessNameCacheEntry> _processNamesById = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private TraceEventSession? _session;
    private Task? _processingTask;
    private int? _lastProcessId;
    private DateTimeOffset? _lastFrameAt;
    private string _status = "FPS ETW 引擎启动中";
    private bool _permissionBlocked;
    private bool _disposed;

    public FrameCollector(ILogger<FrameCollector> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_permissionBlocked || IsRunning)
            {
                return;
            }
        }

        StartEtwSession();
    }

    public TelemetrySnapshot GetSnapshot(int? targetProcessId, string? targetApplication)
    {
        Start();

        lock (_syncRoot)
        {
            var now = DateTimeOffset.Now;
            TrimSamples(now);

            if (_permissionBlocked)
            {
                return CreateFrameSnapshot(
                    now,
                    null,
                    null,
                    targetProcessId,
                    targetApplication,
                    _status,
                    false);
            }

            var stream = SelectStream(targetProcessId);
            if (stream is null)
            {
                if (targetProcessId is > 0)
                {
                    return CreateFrameSnapshot(
                        now,
                        null,
                        null,
                        targetProcessId,
                        NormalizeApplicationName(targetApplication),
                        IsRunning
                            ? $"FPS ETW 引擎已连接：{BuildLabel(targetProcessId.Value, targetApplication)}，等待帧数据"
                            : _status,
                        IsRunning);
                }

                return CreateFrameSnapshot(
                    now,
                    null,
                    null,
                    null,
                    NormalizeApplicationName(targetApplication),
                    IsRunning ? "FPS ETW 引擎已就绪，等待游戏帧" : _status,
                    IsRunning);
            }

            if (stream.Samples.Count == 0)
            {
                return CreateFrameSnapshot(
                    now,
                    null,
                    null,
                    stream.ProcessId,
                    stream.Application,
                    $"FPS ETW 引擎已连接：{stream.Label}，等待帧数据",
                    IsRunning);
            }

            _lastProcessId = stream.ProcessId;
            var currentSamples = stream.Samples
                .Where(x => now - x.Timestamp <= CurrentFpsWindow)
                .ToArray();
            if (currentSamples.Length == 0)
            {
                currentSamples = stream.Samples.ToArray();
            }

            var frameTimeMs = currentSamples.Average(x => x.FrameTimeMs);
            double? fps = frameTimeMs <= 0 ? null : 1000d / frameTimeMs;
            var onePercentLowFps = CalculateOnePercentLowFps(stream.Samples);
            return CreateFrameSnapshot(
                now,
                fps,
                frameTimeMs,
                stream.ProcessId,
                stream.Application,
                $"FPS ETW 引擎已连接：{stream.Label}",
                IsRunning,
                onePercentLowFps);
        }
    }

    private static TelemetrySnapshot CreateFrameSnapshot(
        DateTimeOffset timestamp,
        double? framesPerSecond,
        double? frameTimeMs,
        int? processId,
        string? application,
        string status,
        bool engineRunning,
        double? onePercentLowFps = null)
    {
        return new TelemetrySnapshot(
            timestamp,
            framesPerSecond,
            frameTimeMs,
            processId,
            application,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<TelemetrySensorReading>(),
            Array.Empty<TelemetrySensorReading>(),
            status,
            engineRunning,
            TelemetryConstants.SchemaVersion,
            TelemetryConstants.Capabilities)
        {
            OnePercentLowFps = onePercentLowFps
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();
        StopEtwSession();
        _disposeCts.Dispose();
    }

    private bool IsRunning => _session is not null && _processingTask is { IsCompleted: false };

    private void StartEtwSession()
    {
        TraceEventSession? session = null;

        try
        {
            session = new TraceEventSession(CreateSessionName())
            {
                StopOnDispose = true
            };

            session.EnableProvider(DxgiProviderGuid, TraceEventLevel.Verbose, PresentEventsKeyword);
            session.EnableProvider(D3d9ProviderGuid, TraceEventLevel.Verbose, PresentEventsKeyword);
            session.Source.Dynamic.All += OnEtwEvent;

            var processingTask = Task.Factory.StartNew(
                () => ProcessSession(session),
                _disposeCts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            lock (_syncRoot)
            {
                _permissionBlocked = false;
                _session = session;
                _processingTask = processingTask;
                _streamsByProcessId.Clear();
                _lastProcessId = null;
                _lastFrameAt = null;
                _status = "FPS ETW 引擎已就绪";
            }

            _logger.LogInformation("Native ETW frame collector started.");
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            session?.Dispose();
            lock (_syncRoot)
            {
                _permissionBlocked = true;
                _status = "FPS ETW 引擎权限不足，请安装/修复后台服务";
            }

            _logger.LogWarning(ex, "Native ETW frame collector needs elevated service permissions.");
        }
        catch (Exception ex)
        {
            session?.Dispose();
            lock (_syncRoot)
            {
                _status = $"FPS ETW 引擎启动失败：{ex.Message}";
            }

            _logger.LogError(ex, "Failed to start native ETW frame collector.");
        }
    }

    private void StopEtwSession()
    {
        TraceEventSession? session;
        lock (_syncRoot)
        {
            session = _session;
            _session = null;
            _processingTask = null;
        }

        if (session is null)
        {
            return;
        }

        try
        {
            session.Source.StopProcessing();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to request ETW source shutdown.");
        }

        try
        {
            session.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to stop ETW session cleanly.");
        }
    }

    private void ProcessSession(TraceEventSession session)
    {
        Exception? failure = null;
        try
        {
            session.Source.Process();
        }
        catch (Exception ex) when (_disposed || _disposeCts.IsCancellationRequested)
        {
            failure = ex;
        }
        catch (Exception ex)
        {
            failure = ex;
            _logger.LogWarning(ex, "Native ETW frame collector stopped unexpectedly.");
        }
        finally
        {
            OnEtwSessionEnded(session, failure);
        }
    }

    private void OnEtwSessionEnded(TraceEventSession session, Exception? failure)
    {
        lock (_syncRoot)
        {
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            _session = null;
            _processingTask = null;

            if (_disposed)
            {
                return;
            }

            if (failure is not null && IsAccessDenied(failure))
            {
                _permissionBlocked = true;
                _status = "FPS ETW 引擎权限不足，请安装/修复后台服务";
                return;
            }

            _status = failure is null
                ? "FPS ETW 引擎已停止"
                : $"FPS ETW 引擎异常：{failure.Message}";
        }
    }

    private void OnEtwEvent(TraceEvent data)
    {
        if (!IsPresentStopEvent(data) || data.ProcessID <= 0)
        {
            return;
        }

        var processId = data.ProcessID;
        var eventTimestamp = new DateTimeOffset(data.TimeStamp);
        var now = DateTimeOffset.Now;
        var application = ResolveApplicationName(data, processId, now);

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            if (!_streamsByProcessId.TryGetValue(processId, out var stream))
            {
                stream = new FrameStream(processId, application);
                _streamsByProcessId[processId] = stream;
            }
            else if (!string.IsNullOrWhiteSpace(application) && stream.Application == "未知进程")
            {
                stream.Application = application;
            }

            if (stream.LastPresentAt is not null)
            {
                var frameTimeMs = (eventTimestamp - stream.LastPresentAt.Value).TotalMilliseconds;
                if (frameTimeMs is >= MinimumFrameTimeMs and <= MaximumFrameTimeMs)
                {
                    stream.Samples.Enqueue(new FrameSample(now, frameTimeMs));
                    _lastFrameAt = now;
                    _lastProcessId = processId;
                }
            }

            stream.LastPresentAt = eventTimestamp;
            TrimSamples(now);
        }
    }

    private FrameStream? SelectStream(int? targetProcessId)
    {
        if (targetProcessId is > 0
            && _streamsByProcessId.TryGetValue(targetProcessId.Value, out var requested)
            && requested.Samples.Count > 0)
        {
            return requested;
        }

        if (_lastProcessId is not null
            && _streamsByProcessId.TryGetValue(_lastProcessId.Value, out var previous)
            && previous.Samples.Count > 0
            && !IsBlockedTarget(previous))
        {
            return previous;
        }

        return _streamsByProcessId.Values
            .Where(x => x.Samples.Count > 0 && !IsBlockedTarget(x))
            .OrderByDescending(x => x.Samples.Count)
            .FirstOrDefault();
    }

    private void TrimSamples(DateTimeOffset now)
    {
        var cutoff = now - OnePercentLowWindow;
        foreach (var stream in _streamsByProcessId.Values)
        {
            while (stream.Samples.Count > 0 && stream.Samples.Peek().Timestamp < cutoff)
            {
                stream.Samples.Dequeue();
            }
        }

        if (_lastFrameAt is not null && now - _lastFrameAt > StaleFrameTimeout)
        {
            _lastProcessId = null;
        }
    }

    private static double? CalculateOnePercentLowFps(IEnumerable<FrameSample> samples)
    {
        var frameTimes = samples
            .Select(x => x.FrameTimeMs)
            .Where(x => double.IsFinite(x) && x is >= MinimumFrameTimeMs and <= MaximumFrameTimeMs)
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

    private string ResolveApplicationName(TraceEvent data, int processId, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(data.ProcessName))
        {
            return NormalizeApplicationName(data.ProcessName);
        }

        lock (_syncRoot)
        {
            if (_processNamesById.TryGetValue(processId, out var cached)
                && now - cached.Timestamp < ProcessNameCacheTtl)
            {
                return cached.Name;
            }
        }

        var name = "未知进程";
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!string.IsNullOrWhiteSpace(process.ProcessName))
            {
                name = NormalizeApplicationName(process.ProcessName);
            }
        }
        catch (ArgumentException)
        {
        }
        catch (Win32Exception)
        {
        }
        catch (InvalidOperationException)
        {
        }

        lock (_syncRoot)
        {
            _processNamesById[processId] = new ProcessNameCacheEntry(name, now);
        }

        return name;
    }

    private static bool IsPresentStopEvent(TraceEvent data)
    {
        if (data.ProviderGuid == DxgiProviderGuid)
        {
            return IsTaskAndStop(data, DxgiPresentTask)
                   || IsNamedPresentStop(data, "DXGI");
        }

        if (data.ProviderGuid == D3d9ProviderGuid)
        {
            return IsTaskAndStop(data, D3d9PresentTask)
                   || IsNamedPresentStop(data, "D3D9");
        }

        return false;
    }

    private static bool IsTaskAndStop(TraceEvent data, int task)
    {
        return (int) data.Task == task && (int) data.Opcode == StopOpcode;
    }

    private static bool IsNamedPresentStop(TraceEvent data, string provider)
    {
        return data.ProviderName.Contains(provider, StringComparison.OrdinalIgnoreCase)
               && data.TaskName.Equals("Present", StringComparison.OrdinalIgnoreCase)
               && data.OpcodeName.Equals("Stop", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockedTarget(FrameStream stream)
    {
        return stream.Application.Contains("Kuikui", StringComparison.OrdinalIgnoreCase)
               || stream.Application.Contains("PresentMon", StringComparison.OrdinalIgnoreCase)
               || stream.Application.Contains("dwm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAccessDenied(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
        {
            return true;
        }

        if ((uint) ex.HResult == 0x80070005)
        {
            return true;
        }

        if (ex.Message.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ex.InnerException is not null && IsAccessDenied(ex.InnerException);
    }

    private static string CreateSessionName()
    {
        return SessionName;
    }

    private static string BuildLabel(int processId, string? application)
    {
        return $"{NormalizeApplicationName(application)} ({processId})";
    }

    private static string NormalizeApplicationName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "未知进程";
        }

        var name = Path.GetFileName(value.Trim());
        return string.IsNullOrWhiteSpace(name) ? value.Trim() : name;
    }

    private sealed class FrameStream
    {
        public FrameStream(int processId, string application)
        {
            ProcessId = processId;
            Application = NormalizeApplicationName(application);
        }

        public int ProcessId { get; }

        public string Application { get; set; }

        public Queue<FrameSample> Samples { get; } = new();

        public DateTimeOffset? LastPresentAt { get; set; }

        public string Label => $"{Application} ({ProcessId})";
    }

    private sealed record FrameSample(DateTimeOffset Timestamp, double FrameTimeMs);

    private sealed record ProcessNameCacheEntry(string Name, DateTimeOffset Timestamp);
}
