using System.Diagnostics;
using System.Globalization;
using System.IO;
using KuikuiGameAssistant.Models;

namespace KuikuiGameAssistant.Services;

public sealed record PresentMonFrameSnapshot(double? FramesPerSecond, double? FrameTimeMs, string Status);

public sealed class PresentMonService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, Queue<FrameSample>> _samplesByApplication = new(StringComparer.OrdinalIgnoreCase);
    private Process? _process;
    private string[] _header = Array.Empty<string>();
    private PresentMonFrameSnapshot _latest = new(null, null, "PresentMon 未启用");
    private DateTimeOffset? _lastFrameAt;
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
            lock (_syncRoot)
            {
                if (_process is { HasExited: false }
                    && _lastFrameAt is not null
                    && DateTimeOffset.Now - _lastFrameAt > TimeSpan.FromSeconds(2.5))
                {
                    return _latest with { FramesPerSecond = null, FrameTimeMs = null, Status = "PresentMon 等待游戏帧" };
                }

                return _latest;
            }
        }
    }

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

        if (!_settings.EnablePresentMon)
        {
            SetLatest(new PresentMonFrameSnapshot(null, null, "PresentMon 未启用"));
            return;
        }

        var executable = FindPresentMonExecutable(_settings.PresentMonPath);
        if (executable is null)
        {
            SetLatest(new PresentMonFrameSnapshot(null, null, "未找到 PresentMon.exe"));
            return;
        }

        Start(executable);
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
            startInfo.ArgumentList.Add("--v1_metrics");
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
                        _latest = new PresentMonFrameSnapshot(null, null, "PresentMon 已退出");
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
                _latest = new PresentMonFrameSnapshot(null, null, "PresentMon 启动中");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            SetLatest(new PresentMonFrameSnapshot(null, null, $"PresentMon 启动失败：{ex.Message}"));
        }
    }

    private void Stop()
    {
        Process? process;
        lock (_syncRoot)
        {
            process = _process;
            _process = null;
            _header = Array.Empty<string>();
        }

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
            && columns.Any(x => x.Contains("MsBetween", StringComparison.OrdinalIgnoreCase)))
        {
            lock (_syncRoot)
            {
                _header = columns;
                _latest = new PresentMonFrameSnapshot(null, null, "PresentMon 已连接，等待游戏帧");
            }

            return;
        }

        if (!TryParseFrame(columns, out var application, out var frameTimeMs))
        {
            return;
        }

        var now = DateTimeOffset.Now;
        lock (_syncRoot)
        {
            if (!_samplesByApplication.TryGetValue(application, out var samples))
            {
                samples = new Queue<FrameSample>();
                _samplesByApplication[application] = samples;
            }

            samples.Enqueue(new FrameSample(now, frameTimeMs));
            TrimSamples(now);

            var best = _samplesByApplication
                .Where(x => x.Value.Count > 0)
                .OrderByDescending(x => x.Value.Count)
                .FirstOrDefault();

            if (best.Value is null || best.Value.Count == 0)
            {
                _latest = new PresentMonFrameSnapshot(null, null, "PresentMon 等待游戏帧");
                return;
            }

            _lastFrameAt = now;
            _latest = new PresentMonFrameSnapshot(
                FramesPerSecond: best.Value.Count,
                FrameTimeMs: best.Value.Average(x => x.FrameTimeMs),
                Status: $"PresentMon 已连接：{best.Key}");
        }
    }

    private void HandleErrorLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (line.Contains("error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("denied", StringComparison.OrdinalIgnoreCase))
        {
            SetLatest(new PresentMonFrameSnapshot(null, null, $"PresentMon：{line.Trim()}"));
        }
    }

    private bool TryParseFrame(string[] columns, out string application, out double frameTimeMs)
    {
        application = "未知进程";
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
        if (!string.IsNullOrWhiteSpace(app))
        {
            application = Path.GetFileName(app.Trim());
        }

        var value = ReadColumn(
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
        var cutoff = now - TimeSpan.FromSeconds(1);
        foreach (var queue in _samplesByApplication.Values)
        {
            while (queue.Count > 0 && queue.Peek().Timestamp < cutoff)
            {
                queue.Dequeue();
            }
        }
    }

    private void ClearSamples()
    {
        lock (_syncRoot)
        {
            _samplesByApplication.Clear();
            _lastFrameAt = null;
        }
    }

    private void SetLatest(PresentMonFrameSnapshot snapshot)
    {
        lock (_syncRoot)
        {
            _latest = snapshot;
        }
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

    private sealed record FrameSample(DateTimeOffset Timestamp, double FrameTimeMs);
}
