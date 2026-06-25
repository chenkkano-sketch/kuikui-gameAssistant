using System.Runtime.InteropServices;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using KuikuiGameAssistant.Models;

namespace KuikuiGameAssistant.Services;

public sealed class TelemetryService : IDisposable
{
    private const float MinPlausibleTemperature = 20f;
    private const float MaxPlausibleTemperature = 125f;
    private static readonly TimeSpan ForegroundPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BackgroundPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TemperatureDiagnosticsInterval = TimeSpan.FromSeconds(5);
    private readonly System.Threading.Timer _timer;
    private readonly object _syncRoot = new();
    private readonly AppSettings _settings;
    private readonly PresentMonService _presentMon;
    private IReadOnlyList<SensorReading> _lastTemperatureSensors = Array.Empty<SensorReading>();
    private DateTimeOffset _lastTemperatureDiagnosticsAt = DateTimeOffset.MinValue;
    private Computer? _computer;
    private RealtimeSnapshot _latest = EmptySnapshot("传感器服务启动中");
    private string _status = "传感器服务启动中";
    private int _polling;
    private bool _disposed;

    public TelemetryService(AppSettings settings)
    {
        _settings = settings;
        _presentMon = new PresentMonService(settings);
        InitializeHardwareMonitor();
        _timer = new System.Threading.Timer(_ => Poll(), null, TimeSpan.Zero, ForegroundPollInterval);
    }

    public event EventHandler<RealtimeSnapshot>? SnapshotUpdated;

    public bool IsPresentMonEnabled => _settings.EnablePresentMon;

    public RealtimeSnapshot Latest
    {
        get
        {
            lock (_syncRoot)
            {
                return _latest;
            }
        }
    }

    public void RefreshNow() => Poll();

    public void EnablePresentMon()
    {
        if (_settings.EnablePresentMon)
        {
            _presentMon.Restart();
        }
        else
        {
            _settings.EnablePresentMon = true;
        }

        Poll();
    }

    public void RestartPresentMon()
    {
        _presentMon.Restart();
        Poll();
    }

    public void SetBackgroundMode(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        var interval = enabled ? BackgroundPollInterval : ForegroundPollInterval;
        _timer.Change(TimeSpan.Zero, interval);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        _presentMon.Dispose();
        _computer?.Close();
    }

    private void InitializeHardwareMonitor()
    {
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsControllerEnabled = false,
                IsPowerMonitorEnabled = false,
                IsNetworkEnabled = false,
                IsBatteryEnabled = false
            };
            _computer.Open();
            _status = IsAdministrator()
                ? "LibreHardwareMonitor 已连接"
                : "LibreHardwareMonitor 已连接；温度可能需要管理员权限";
        }
        catch (Exception ex)
        {
            _computer = null;
            _status = $"传感器初始化失败：{ex.Message}";
        }
    }

    private void Poll()
    {
        if (_disposed)
        {
            return;
        }

        if (Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }

        try
        {
            var snapshot = ReadSnapshot();
            lock (_syncRoot)
            {
                _latest = snapshot;
            }

            SnapshotUpdated?.Invoke(this, snapshot);
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private RealtimeSnapshot ReadSnapshot()
    {
        try
        {
            if (_computer is null)
            {
                return EmptySnapshot(_status) with { MemoryLoad = TryGetMemory(out var used, out var total), MemoryUsedGb = used, MemoryTotalGb = total };
            }

            foreach (var hardware in _computer.Hardware)
            {
                UpdateHardware(hardware);
            }

            var sensors = EnumerateSensors(_computer.Hardware).ToArray();
            var cpuLoad = FindCpuLoad(sensors);
            var cpuTemp = FindCpuTemperature(sensors);
            var gpuLoad = FindGpuLoad(sensors);
            var gpuTemp = FindGpuTemperature(sensors);
            var diskTemp = FindDiskTemperature(sensors);
            var memoryLoad = TryGetMemory(out var memoryUsed, out var memoryTotal);
            var frameSnapshot = _presentMon.Latest;
            var temperatureSensors = GetTemperatureDiagnostics(sensors);
            var status = BuildStatus(sensors, cpuTemp, gpuTemp, frameSnapshot.Status);

            return new RealtimeSnapshot(
                DateTimeOffset.Now,
                FramesPerSecond: frameSnapshot.FramesPerSecond,
                FrameTimeMs: frameSnapshot.FrameTimeMs,
                CpuLoad: cpuLoad,
                CpuTemperature: cpuTemp?.Value,
                CpuTemperatureSource: cpuTemp?.Source,
                GpuLoad: gpuLoad?.Value,
                GpuLoadSource: gpuLoad?.Source,
                GpuTemperature: gpuTemp?.Value,
                GpuTemperatureSource: gpuTemp?.Source,
                DiskTemperature: diskTemp?.Value,
                DiskTemperatureSource: diskTemp?.Source,
                MemoryLoad: memoryLoad,
                MemoryUsedGb: memoryUsed,
                MemoryTotalGb: memoryTotal,
                TemperatureSensors: temperatureSensors,
                Status: status);
        }
        catch (Exception ex)
        {
            return EmptySnapshot($"采集异常：{ex.Message}");
        }
    }

    private static void UpdateHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var subHardware in hardware.SubHardware)
        {
            UpdateHardware(subHardware);
        }
    }

    private static IEnumerable<(IHardware Hardware, ISensor Sensor)> EnumerateSensors(IEnumerable<IHardware> hardware)
    {
        foreach (var item in hardware)
        {
            foreach (var sensor in item.Sensors)
            {
                yield return (item, sensor);
            }

            foreach (var subHardware in item.SubHardware)
            {
                foreach (var sensor in EnumerateSensors(new[] { subHardware }))
                {
                    yield return sensor;
                }
            }
        }
    }

    private static double? FindCpuLoad(IEnumerable<(IHardware Hardware, ISensor Sensor)> sensors)
    {
        var loadSensors = sensors
            .Where(x => x.Hardware.HardwareType == HardwareType.Cpu && x.Sensor.SensorType == SensorType.Load && x.Sensor.Value is not null)
            .ToArray();

        return loadSensors.FirstOrDefault(x => x.Sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)).Sensor?.Value
            ?? Average(loadSensors.Select(x => x.Sensor.Value));
    }

    private static SensorReadingValue? FindGpuLoad(IEnumerable<(IHardware Hardware, ISensor Sensor)> sensors)
    {
        var gpuTypes = new[] { HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel };
        var loadSensors = sensors
            .Where(x => gpuTypes.Contains(x.Hardware.HardwareType)
                        && x.Sensor.SensorType == SensorType.Load
                        && x.Sensor.Value is not null)
            .ToArray();

        var match = loadSensors.FirstOrDefault(x => x.Sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
        if (match.Sensor is null)
        {
            match = loadSensors.FirstOrDefault(x => x.Sensor.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase));
        }

        if (match.Sensor is not null)
        {
            return new SensorReadingValue(match.Sensor.Value!.Value, match.Hardware.Name);
        }

        if (loadSensors.Length == 0)
        {
            return null;
        }

        return new SensorReadingValue(
            loadSensors.Average(x => x.Sensor.Value!.Value),
            loadSensors.Length == 1 ? loadSensors[0].Hardware.Name : $"平均 {loadSensors.Length} 个 GPU 负载项");
    }

    private static TemperatureReading? FindCpuTemperature(IEnumerable<(IHardware Hardware, ISensor Sensor)> sensors)
    {
        var cpuSensors = sensors
            .Where(x => x.Sensor.SensorType == SensorType.Temperature
                        && x.Sensor.Value is not null
                        && x.Hardware.HardwareType == HardwareType.Cpu)
            .ToArray();

        var motherboardCpuSensors = sensors
            .Where(x => x.Sensor.SensorType == SensorType.Temperature
                        && x.Sensor.Value is not null
                        && x.Hardware.HardwareType == HardwareType.Motherboard
                        && ContainsAny($"{x.Hardware.Name} {x.Sensor.Name}", "cpu", "package", "socket", "tctl", "tdie", "ccd"))
            .ToArray();

        return PickTemperature(cpuSensors, "package", "tctl", "tdie", "cpu", "core max", "core", "ccd")
            ?? PickTemperature(motherboardCpuSensors, "package", "tctl", "tdie", "cpu", "socket", "ccd");
    }

    private static TemperatureReading? FindGpuTemperature(IEnumerable<(IHardware Hardware, ISensor Sensor)> sensors)
    {
        var gpuTypes = new[] { HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel };
        var gpuSensors = sensors
            .Where(x => x.Sensor.SensorType == SensorType.Temperature
                        && x.Sensor.Value is not null
                        && gpuTypes.Contains(x.Hardware.HardwareType))
            .ToArray();

        return PickTemperature(gpuSensors, "gpu core", "hot spot", "junction", "edge", "memory", "gpu");
    }

    private static TemperatureReading? FindDiskTemperature(IEnumerable<(IHardware Hardware, ISensor Sensor)> sensors)
    {
        var storageSensors = sensors
            .Where(x => x.Sensor.SensorType == SensorType.Temperature
                        && x.Sensor.Value is not null
                        && x.Hardware.HardwareType == HardwareType.Storage)
            .Where(IsPlausibleTemperature)
            .ToArray();

        var primarySensors = storageSensors
            .Select(x => new
            {
                Sensor = x,
                Priority = DiskTemperaturePriority(x.Sensor.Name)
            })
            .Where(x => x.Priority < 90)
            .GroupBy(x => x.Sensor.Hardware.Name)
            .Select(group => group
                .OrderBy(x => x.Priority)
                .ThenBy(x => Math.Abs((x.Sensor.Sensor.Value ?? 0) - 45))
                .First().Sensor)
            .OrderByDescending(x => x.Sensor.Value)
            .ToArray();

        return primarySensors.Length == 0 ? null : ToTemperatureReading(primarySensors[0]);
    }

    private static int DiskTemperaturePriority(string sensorName)
    {
        var name = sensorName.Trim();
        if (name.Equals("Temperature", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Drive Temperature", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Composite Temperature", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (ContainsAny(name, "composite", "drive temperature", "airflow"))
        {
            return 1;
        }

        if (name.Equals("Temperature 1", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Temperature #1", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Sensor 1", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (ContainsAny(name, "controller", "temperature 2", "temperature 3", "temperature 4", "sensor 2", "sensor 3", "sensor 4", "nand", "memory"))
        {
            return 100;
        }

        return name.Contains("temperature", StringComparison.OrdinalIgnoreCase) ? 3 : 100;
    }

    private static TemperatureReading? PickTemperature(IEnumerable<(IHardware Hardware, ISensor Sensor)> sensors, params string[] priorities)
    {
        var candidates = sensors
            .Where(IsPlausibleTemperature)
            .ToArray();

        foreach (var priority in priorities)
        {
            var match = candidates.FirstOrDefault(x =>
                $"{x.Hardware.Name} {x.Sensor.Name}".Contains(priority, StringComparison.OrdinalIgnoreCase));
            if (match.Sensor is not null)
            {
                return ToTemperatureReading(match);
            }
        }

        if (candidates.Length == 0)
        {
            return null;
        }

        if (candidates.Length == 1)
        {
            return ToTemperatureReading(candidates[0]);
        }

        return new TemperatureReading(
            candidates.Average(x => x.Sensor.Value!.Value),
            $"平均 {candidates.Length} 个温度项");
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPlausibleTemperature((IHardware Hardware, ISensor Sensor) sensor)
    {
        return sensor.Sensor.Value is >= MinPlausibleTemperature and <= MaxPlausibleTemperature;
    }

    private IReadOnlyList<SensorReading> GetTemperatureDiagnostics((IHardware Hardware, ISensor Sensor)[] sensors)
    {
        var now = DateTimeOffset.Now;
        if (now - _lastTemperatureDiagnosticsAt < TemperatureDiagnosticsInterval)
        {
            return _lastTemperatureSensors;
        }

        _lastTemperatureDiagnosticsAt = now;
        _lastTemperatureSensors = BuildTemperatureDiagnostics(sensors);
        return _lastTemperatureSensors;
    }

    private static IReadOnlyList<SensorReading> BuildTemperatureDiagnostics((IHardware Hardware, ISensor Sensor)[] sensors)
    {
        return sensors
            .Where(x => x.Sensor.SensorType == SensorType.Temperature)
            .OrderBy(x => TemperaturePriority(x.Hardware, x.Sensor))
            .ThenBy(x => x.Hardware.Name)
            .ThenBy(x => x.Sensor.Name)
            .Select(x => new SensorReading(
                x.Hardware.Name,
                x.Sensor.Name,
                x.Hardware.HardwareType.ToString(),
                x.Sensor.Value,
                "℃"))
            .Take(16)
            .ToArray();
    }

    private static int TemperaturePriority(IHardware hardware, ISensor sensor)
    {
        var text = $"{hardware.Name} {sensor.Name}";
        if (hardware.HardwareType == HardwareType.Cpu)
        {
            return 0;
        }

        if (ContainsAny(text, "cpu", "package", "socket", "tctl", "tdie", "ccd"))
        {
            return 1;
        }

        if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
        {
            return 2;
        }

        if (hardware.HardwareType == HardwareType.Storage)
        {
            return 3;
        }

        if (hardware.HardwareType == HardwareType.Motherboard)
        {
            return 4;
        }

        return 5;
    }

    private static TemperatureReading ToTemperatureReading((IHardware Hardware, ISensor Sensor) sensor)
    {
        return new TemperatureReading(sensor.Sensor.Value!.Value, $"{sensor.Hardware.Name} / {sensor.Sensor.Name}");
    }

    private string BuildStatus((IHardware Hardware, ISensor Sensor)[] sensors, TemperatureReading? cpuTemp, TemperatureReading? gpuTemp, string presentMonStatus)
    {
        var tempSensorCount = sensors.Count(x => x.Sensor.SensorType == SensorType.Temperature && x.Sensor.Value is not null);
        var reliableTempCount = sensors.Count(IsPlausibleTemperature);
        var tempStatus = cpuTemp is null && gpuTemp is null
            ? $"未读到可信温度（原始 {tempSensorCount} 项，可信 {reliableTempCount} 项；请确认已管理员运行）"
            : $"可信温度 {reliableTempCount}/{tempSensorCount} 项";

        return $"{_status}；{tempStatus}；{presentMonStatus}";
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static double? Average(IEnumerable<float?> values)
    {
        var numbers = values.Where(x => x is not null).Select(x => (double)x!.Value).ToArray();
        return numbers.Length == 0 ? null : numbers.Average();
    }

    private static double? TryGetMemory(out double? usedGb, out double? totalGb)
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
        {
            usedGb = null;
            totalGb = null;
            return null;
        }

        totalGb = BytesToGb(status.TotalPhys);
        usedGb = BytesToGb(status.TotalPhys - status.AvailPhys);
        return status.MemoryLoad;
    }

    private static double BytesToGb(ulong bytes) => bytes / 1024d / 1024d / 1024d;

    private static RealtimeSnapshot EmptySnapshot(string status) =>
        new(DateTimeOffset.Now, null, null, null, null, null, null, null, null, null, null, null, null, null, null, Array.Empty<SensorReading>(), status);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;

        public MemoryStatusEx()
        {
            Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
        }
    }

    private sealed record TemperatureReading(double Value, string Source);
    private sealed record SensorReadingValue(double Value, string Source);
}
