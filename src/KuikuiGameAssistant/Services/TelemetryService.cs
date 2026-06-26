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
    private readonly KuikuiTelemetryServiceClient _serviceClient = new();
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
                IsControllerEnabled = true,
                IsPowerMonitorEnabled = true,
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
            var hasServiceSnapshot = _serviceClient.TryReadLatest(out var serviceSnapshot);
            var hasHardwareService = hasServiceSnapshot && serviceSnapshot.SupportsHardwareTelemetry;
            var serviceStatus = hasServiceSnapshot && serviceSnapshot.IsLegacyService
                ? $"后台遥测服务需要升级：当前服务只返回 FPS，未包含温度采集；{serviceSnapshot.Status}"
                : serviceSnapshot.Status;
            if (_computer is null)
            {
                var emptyFrameSnapshot = hasServiceSnapshot
                    ? ToFrameSnapshot(serviceSnapshot)
                    : _presentMon.Latest;
                var localMemoryLoad = TryGetMemory(out var localMemoryUsed, out var localMemoryTotal);
                var serviceSensorsWithoutLocalHardware = hasHardwareService ? serviceSnapshot.ToSensors() : Array.Empty<SensorReading>();
                var mergedSensorsWithoutLocalHardware = AddSyntheticSensors(
                    serviceSensorsWithoutLocalHardware,
                    emptyFrameSnapshot,
                    hasHardwareService ? serviceSnapshot.CpuLoad : null,
                    hasHardwareService ? serviceSnapshot.CpuTemperature : null,
                    hasHardwareService ? serviceSnapshot.GpuLoad : null,
                    hasHardwareService ? serviceSnapshot.GpuTemperature : null,
                    hasHardwareService ? serviceSnapshot.DiskTemperature : null,
                    (hasHardwareService ? serviceSnapshot.MemoryLoad : null) ?? localMemoryLoad,
                    (hasHardwareService ? serviceSnapshot.MemoryUsedGb : null) ?? localMemoryUsed);
                return EmptySnapshot(hasServiceSnapshot ? serviceStatus : _status) with
                {
                    FramesPerSecond = emptyFrameSnapshot.FramesPerSecond,
                    OnePercentLowFps = emptyFrameSnapshot.OnePercentLowFps,
                    FrameTimeMs = emptyFrameSnapshot.FrameTimeMs,
                    CpuLoad = hasHardwareService ? serviceSnapshot.CpuLoad : null,
                    CpuTemperature = hasHardwareService ? serviceSnapshot.CpuTemperature : null,
                    CpuTemperatureSource = hasHardwareService ? serviceSnapshot.CpuTemperatureSource : null,
                    GpuLoad = hasHardwareService ? serviceSnapshot.GpuLoad : null,
                    GpuLoadSource = hasHardwareService ? serviceSnapshot.GpuLoadSource : null,
                    GpuTemperature = hasHardwareService ? serviceSnapshot.GpuTemperature : null,
                    GpuTemperatureSource = hasHardwareService ? serviceSnapshot.GpuTemperatureSource : null,
                    DiskTemperature = hasHardwareService ? serviceSnapshot.DiskTemperature : null,
                    DiskTemperatureSource = hasHardwareService ? serviceSnapshot.DiskTemperatureSource : null,
                    MemoryLoad = (hasHardwareService ? serviceSnapshot.MemoryLoad : null) ?? localMemoryLoad,
                    MemoryUsedGb = (hasHardwareService ? serviceSnapshot.MemoryUsedGb : null) ?? localMemoryUsed,
                    MemoryTotalGb = (hasHardwareService ? serviceSnapshot.MemoryTotalGb : null) ?? localMemoryTotal,
                    TemperatureSensors = hasHardwareService ? serviceSnapshot.ToSensorReadings() : Array.Empty<SensorReading>(),
                    Sensors = mergedSensorsWithoutLocalHardware
                };
            }

            foreach (var hardware in _computer.Hardware)
            {
                UpdateHardware(hardware);
            }

            var sensors = EnumerateSensors(_computer.Hardware).ToArray();
            var cpuLoad = (hasHardwareService ? serviceSnapshot.CpuLoad : null) ?? FindCpuLoad(sensors);
            var cpuTemp = ToTemperatureReading(
                    hasHardwareService ? serviceSnapshot.CpuTemperature : null,
                    hasHardwareService ? serviceSnapshot.CpuTemperatureSource : null)
                ?? FindCpuTemperature(sensors);
            var gpuLoad = ToSensorReadingValue(
                    hasHardwareService ? serviceSnapshot.GpuLoad : null,
                    hasHardwareService ? serviceSnapshot.GpuLoadSource : null)
                ?? FindGpuLoad(sensors);
            var gpuTemp = ToTemperatureReading(
                    hasHardwareService ? serviceSnapshot.GpuTemperature : null,
                    hasHardwareService ? serviceSnapshot.GpuTemperatureSource : null)
                ?? FindGpuTemperature(sensors);
            var diskTemp = ToTemperatureReading(
                    hasHardwareService ? serviceSnapshot.DiskTemperature : null,
                    hasHardwareService ? serviceSnapshot.DiskTemperatureSource : null)
                ?? FindDiskTemperature(sensors);
            var memoryLoad = TryGetMemory(out var memoryUsed, out var memoryTotal);
            var frameSnapshot = hasServiceSnapshot
                ? ToFrameSnapshot(serviceSnapshot)
                : _presentMon.Latest;
            var serviceTemperatureSensors = hasHardwareService
                ? serviceSnapshot.ToSensorReadings()
                : Array.Empty<SensorReading>();
            var temperatureSensors = serviceTemperatureSensors.Count > 0
                ? serviceTemperatureSensors
                : GetTemperatureDiagnostics(sensors);
            var hardwareSensors = hasHardwareService
                ? serviceSnapshot.ToSensors()
                : BuildSensorCatalog(sensors);
            var sensorCatalog = AddSyntheticSensors(
                hardwareSensors,
                frameSnapshot,
                cpuLoad,
                cpuTemp?.Value,
                gpuLoad?.Value,
                gpuTemp?.Value,
                diskTemp?.Value,
                (hasHardwareService ? serviceSnapshot.MemoryLoad : null) ?? memoryLoad,
                (hasHardwareService ? serviceSnapshot.MemoryUsedGb : null) ?? memoryUsed);
            var status = hasServiceSnapshot
                ? serviceStatus
                : BuildStatus(sensors, cpuTemp, gpuTemp, frameSnapshot.Status);

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
                MemoryLoad: (hasHardwareService ? serviceSnapshot.MemoryLoad : null) ?? memoryLoad,
                MemoryUsedGb: (hasHardwareService ? serviceSnapshot.MemoryUsedGb : null) ?? memoryUsed,
                MemoryTotalGb: (hasHardwareService ? serviceSnapshot.MemoryTotalGb : null) ?? memoryTotal,
                TemperatureSensors: temperatureSensors,
                Sensors: sensorCatalog,
                Status: status)
            {
                OnePercentLowFps = frameSnapshot.OnePercentLowFps
            };
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

        return PickCpuTemperature(cpuSensors)
            ?? PickMotherboardCpuTemperature(motherboardCpuSensors);
    }

    private static TemperatureReading? FindGpuTemperature(IEnumerable<(IHardware Hardware, ISensor Sensor)> sensors)
    {
        var gpuTypes = new[] { HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel };
        var gpuSensors = sensors
            .Where(x => x.Sensor.SensorType == SensorType.Temperature
                        && x.Sensor.Value is not null
                        && gpuTypes.Contains(x.Hardware.HardwareType))
            .ToArray();

        return PickGpuTemperature(gpuSensors);
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

    private static TemperatureReading? PickCpuTemperature(IEnumerable<(IHardware Hardware, ISensor Sensor)> sensors)
    {
        return PickScoredTemperature(
            sensors,
            CpuTemperatureScore,
            "CPU 主温度");
    }

    private static TemperatureReading? PickMotherboardCpuTemperature(IEnumerable<(IHardware Hardware, ISensor Sensor)> sensors)
    {
        return PickScoredTemperature(
            sensors,
            MotherboardCpuTemperatureScore,
            "主板 CPU 温度回退");
    }

    private static TemperatureReading? PickGpuTemperature(IEnumerable<(IHardware Hardware, ISensor Sensor)> sensors)
    {
        return PickScoredTemperature(
            sensors,
            GpuTemperatureScore,
            "GPU 主温度");
    }

    private static TemperatureReading? PickScoredTemperature(
        IEnumerable<(IHardware Hardware, ISensor Sensor)> sensors,
        Func<(IHardware Hardware, ISensor Sensor), int> score,
        string sourcePrefix)
    {
        var best = sensors
            .Where(IsPlausibleTemperature)
            .Select(x => new
            {
                Sensor = x,
                Score = score(x)
            })
            .Where(x => x.Score < 100)
            .OrderBy(x => x.Score)
            .ThenByDescending(x => x.Sensor.Sensor.Value)
            .FirstOrDefault();

        return best is null
            ? null
            : ToTemperatureReading(best.Sensor, $"{sourcePrefix} / {TemperatureRole(best.Score)}");
    }

    private static int CpuTemperatureScore((IHardware Hardware, ISensor Sensor) sensor)
    {
        var name = sensor.Sensor.Name.Trim();
        if (ContainsAny(name, "package", "cpu package"))
        {
            return 0;
        }

        if (ContainsAny(name, "tctl/tdie", "tctl", "tdie"))
        {
            return 1;
        }

        if (ContainsAny(name, "core max", "max core", "cpu max"))
        {
            return 2;
        }

        if (ContainsAny(name, "ccd max", "ccd"))
        {
            return 3;
        }

        if (name.Equals("CPU", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CPU Temperature", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        return name.Contains("core", StringComparison.OrdinalIgnoreCase) ? 10 : 100;
    }

    private static int MotherboardCpuTemperatureScore((IHardware Hardware, ISensor Sensor) sensor)
    {
        var text = $"{sensor.Hardware.Name} {sensor.Sensor.Name}";
        if (ContainsAny(text, "cpu package", "package", "tctl", "tdie"))
        {
            return 20;
        }

        if (ContainsAny(text, "cpu socket", "socket", "cpu"))
        {
            return 25;
        }

        return 100;
    }

    private static int GpuTemperatureScore((IHardware Hardware, ISensor Sensor) sensor)
    {
        var name = sensor.Sensor.Name.Trim();
        if (ContainsAny(name, "gpu core", "core"))
        {
            return 0;
        }

        if (ContainsAny(name, "edge"))
        {
            return 1;
        }

        if (ContainsAny(name, "hot spot", "hotspot", "junction"))
        {
            return 10;
        }

        if (ContainsAny(name, "memory", "vram"))
        {
            return 30;
        }

        return name.Contains("temperature", StringComparison.OrdinalIgnoreCase) ? 50 : 100;
    }

    private static string TemperatureRole(int score)
    {
        return score switch
        {
            0 => "核心/封装",
            1 => "核心/边缘",
            <= 4 => "高可信",
            10 => "热点/核心回退",
            <= 25 => "主板回退",
            30 => "显存回退",
            _ => "传感器回退"
        };
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

    private static IReadOnlyList<SensorReading> BuildSensorCatalog((IHardware Hardware, ISensor Sensor)[] sensors)
    {
        return sensors
            .Where(x => IsSupportedSensorType(x.Sensor.SensorType))
            .OrderBy(x => SensorTypePriority(x.Sensor.SensorType))
            .ThenBy(x => HardwarePriority(x.Hardware.HardwareType))
            .ThenBy(x => x.Hardware.Name)
            .ThenBy(x => x.Sensor.Name)
            .Select(ToSensorReading)
            .ToArray();
    }

    private static SensorReading ToSensorReading((IHardware Hardware, ISensor Sensor) sensor)
    {
        var hardwareType = sensor.Hardware.HardwareType.ToString();
        var sensorType = sensor.Sensor.SensorType.ToString();
        return new SensorReading(
            SensorReading.BuildId("local", sensor.Hardware.Name, hardwareType, sensor.Sensor.Name, sensorType),
            sensor.Hardware.Name,
            sensor.Sensor.Name,
            hardwareType,
            sensorType,
            sensor.Sensor.Value,
            SensorUnit(sensor.Sensor.SensorType));
    }

    private static IReadOnlyList<SensorReading> AddSyntheticSensors(
        IReadOnlyList<SensorReading> hardwareSensors,
        PresentMonFrameSnapshot frameSnapshot,
        double? cpuLoad,
        double? cpuTemperature,
        double? gpuLoad,
        double? gpuTemperature,
        double? diskTemperature,
        double? memoryLoad,
        double? memoryUsedGb)
    {
        var sensors = new List<SensorReading>(hardwareSensors.Count + 8)
        {
            new("builtin/fps", "FPS 引擎", "FPS", "Frame", "FrameRate", frameSnapshot.FramesPerSecond, "FPS"),
            new("builtin/one-percent-low-fps", "FPS 引擎", "1% Low", "Frame", "FrameRate", frameSnapshot.OnePercentLowFps, "FPS"),
            new("builtin/frame-time", "FPS 引擎", "帧时间", "Frame", "Time", frameSnapshot.FrameTimeMs, "ms"),
            new("builtin/cpu-load", "CPU", "CPU 占用", "Cpu", "Load", cpuLoad, "%"),
            new("builtin/gpu-load", "GPU", "GPU 占用", "Gpu", "Load", gpuLoad, "%"),
            new("builtin/memory-load", "内存", "内存占用", "Memory", "Load", memoryLoad, "%"),
            new("builtin/memory-used", "内存", "已用内存", "Memory", "Data", memoryUsedGb, "GB"),
            new("builtin/cpu-temperature", "CPU", "CPU 温度", "Cpu", "Temperature", cpuTemperature, "℃"),
            new("builtin/gpu-temperature", "GPU", "GPU 温度", "Gpu", "Temperature", gpuTemperature, "℃"),
            new("builtin/disk-temperature", "硬盘", "硬盘温度", "Storage", "Temperature", diskTemperature, "℃")
        };

        sensors.AddRange(hardwareSensors);
        return sensors
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool IsSupportedSensorType(SensorType sensorType)
    {
        return sensorType is SensorType.Temperature
            or SensorType.Load
            or SensorType.Power
            or SensorType.Voltage
            or SensorType.Fan
            or SensorType.Clock
            or SensorType.Data
            or SensorType.SmallData;
    }

    private static int SensorTypePriority(SensorType sensorType)
    {
        return sensorType switch
        {
            SensorType.Temperature => 0,
            SensorType.Load => 1,
            SensorType.Power => 2,
            SensorType.Voltage => 3,
            SensorType.Fan => 4,
            SensorType.Clock => 5,
            SensorType.Data => 6,
            SensorType.SmallData => 7,
            _ => 100
        };
    }

    private static int HardwarePriority(HardwareType hardwareType)
    {
        return hardwareType switch
        {
            HardwareType.Cpu => 0,
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => 1,
            HardwareType.Memory => 2,
            HardwareType.Storage => 3,
            HardwareType.Motherboard => 4,
            _ => 10
        };
    }

    private static string SensorUnit(SensorType sensorType)
    {
        return sensorType switch
        {
            SensorType.Temperature => "℃",
            SensorType.Load => "%",
            SensorType.Power => "W",
            SensorType.Voltage => "V",
            SensorType.Fan => "RPM",
            SensorType.Clock => "MHz",
            SensorType.Data => "GB",
            SensorType.SmallData => "MB",
            _ => string.Empty
        };
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

    private static TemperatureReading ToTemperatureReading((IHardware Hardware, ISensor Sensor) sensor, string? prefix = null)
    {
        var source = $"{sensor.Hardware.Name} / {sensor.Sensor.Name}";
        return new TemperatureReading(
            sensor.Sensor.Value!.Value,
            string.IsNullOrWhiteSpace(prefix) ? source : $"{prefix}：{source}");
    }

    private static TemperatureReading? ToTemperatureReading(double? value, string? source)
    {
        return value is null
            ? null
            : new TemperatureReading(value.Value, string.IsNullOrWhiteSpace(source) ? "后台温度服务" : source);
    }

    private static SensorReadingValue? ToSensorReadingValue(double? value, string? source)
    {
        return value is null
            ? null
            : new SensorReadingValue(value.Value, string.IsNullOrWhiteSpace(source) ? "后台温度服务" : source);
    }

    private static PresentMonFrameSnapshot ToFrameSnapshot(KuikuiTelemetrySnapshot snapshot)
    {
        return new PresentMonFrameSnapshot(
            snapshot.FramesPerSecond,
            snapshot.FrameTimeMs,
            snapshot.Status)
        {
            OnePercentLowFps = snapshot.OnePercentLowFps
        };
    }

    private string BuildStatus((IHardware Hardware, ISensor Sensor)[] sensors, TemperatureReading? cpuTemp, TemperatureReading? gpuTemp, string presentMonStatus)
    {
        var tempSensorCount = sensors.Count(x => x.Sensor.SensorType == SensorType.Temperature && x.Sensor.Value is not null);
        var reliableTempCount = sensors.Count(x => x.Sensor.SensorType == SensorType.Temperature && IsPlausibleTemperature(x));
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
        new(DateTimeOffset.Now, null, null, null, null, null, null, null, null, null, null, null, null, null, null, Array.Empty<SensorReading>(), Array.Empty<SensorReading>(), status);

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
