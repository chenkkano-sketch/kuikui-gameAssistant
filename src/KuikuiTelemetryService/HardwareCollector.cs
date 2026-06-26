using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace KuikuiTelemetryService;

internal sealed class HardwareCollector : IDisposable
{
    private const float MinPlausibleTemperature = 15f;
    private const float MaxPlausibleTemperature = 125f;
    private static readonly TimeSpan SnapshotCacheTtl = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan TemperatureDiagnosticsInterval = TimeSpan.FromSeconds(5);
    private readonly ILogger<HardwareCollector> _logger;
    private readonly object _syncRoot = new();
    private readonly UpdateVisitor _updateVisitor = new();
    private Computer? _computer;
    private HardwareSnapshot _latest = HardwareSnapshot.Empty("温度服务启动中");
    private IReadOnlyList<TelemetrySensorReading> _lastTemperatureSensors = Array.Empty<TelemetrySensorReading>();
    private IReadOnlyList<TelemetrySensorReading> _lastSensors = Array.Empty<TelemetrySensorReading>();
    private DateTimeOffset _lastSnapshotAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastTemperatureDiagnosticsAt = DateTimeOffset.MinValue;
    private string _status = "温度服务启动中";
    private bool _disposed;

    public HardwareCollector(ILogger<HardwareCollector> logger)
    {
        _logger = logger;
        InitializeHardwareMonitor();
    }

    public HardwareSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            var now = DateTimeOffset.Now;
            if (now - _lastSnapshotAt < SnapshotCacheTtl)
            {
                return _latest;
            }

            _latest = ReadSnapshot(now);
            _lastSnapshotAt = now;
            return _latest;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
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
            _computer.Accept(_updateVisitor);
            _status = "温度服务已连接：LibreHardwareMonitor 0.9.7";
        }
        catch (Exception ex)
        {
            _computer = null;
            _status = $"温度服务初始化失败：{ex.Message}";
            _logger.LogError(ex, "Failed to initialize hardware collector.");
        }
    }

    private HardwareSnapshot ReadSnapshot(DateTimeOffset now)
    {
        try
        {
            if (_computer is null)
            {
                return HardwareSnapshot.Empty(_status) with
                {
                    MemoryLoad = TryGetMemory(out var used, out var total),
                    MemoryUsedGb = used,
                    MemoryTotalGb = total
                };
            }

            _computer.Accept(_updateVisitor);
            var sensors = EnumerateSensors(_computer.Hardware).ToArray();
            var cpuLoad = FindCpuLoad(sensors);
            var cpuTemp = FindCpuTemperature(sensors);
            var gpuLoad = FindGpuLoad(sensors);
            var gpuTemp = FindGpuTemperature(sensors);
            var diskTemp = FindDiskTemperature(sensors);
            var memoryLoad = TryGetMemory(out var memoryUsed, out var memoryTotal);
            var temperatureSensors = GetTemperatureDiagnostics(sensors, now);
            var sensorCatalog = BuildSensorCatalog(sensors);
            var status = BuildStatus(sensors, cpuTemp, gpuTemp);

            return new HardwareSnapshot(
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
                Sensors: sensorCatalog,
                Status: status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hardware collector poll failed.");
            return HardwareSnapshot.Empty($"温度服务采集异常：{ex.Message}");
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
            .Where(x => x.Hardware.HardwareType == HardwareType.Cpu
                        && x.Sensor.SensorType == SensorType.Load
                        && x.Sensor.Value is not null)
            .ToArray();

        return loadSensors.FirstOrDefault(x => x.Sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)).Sensor?.Value
            ?? Average(loadSensors.Select(x => x.Sensor.Value));
    }

    private static TemperatureReading? FindCpuTemperature(IEnumerable<(IHardware Hardware, ISensor Sensor)> sensors)
    {
        var candidates = sensors
            .Where(x => x.Sensor.SensorType == SensorType.Temperature
                        && x.Sensor.Value is not null
                        && IsCpuTemperatureCandidate(x))
            .ToArray();

        return PickScoredTemperature(candidates, CpuTemperatureScore, "CPU 主温度");
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

        return loadSensors.Length == 0
            ? null
            : new SensorReadingValue(loadSensors.Average(x => x.Sensor.Value!.Value), loadSensors[0].Hardware.Name);
    }

    private static TemperatureReading? FindGpuTemperature(IEnumerable<(IHardware Hardware, ISensor Sensor)> sensors)
    {
        var gpuTypes = new[] { HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel };
        var gpuSensors = sensors
            .Where(x => x.Sensor.SensorType == SensorType.Temperature
                        && x.Sensor.Value is not null
                        && gpuTypes.Contains(x.Hardware.HardwareType))
            .ToArray();

        return PickScoredTemperature(gpuSensors, GpuTemperatureScore, "GPU 主温度");
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

    private static bool IsCpuTemperatureCandidate((IHardware Hardware, ISensor Sensor) sensor)
    {
        if (sensor.Hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.Storage)
        {
            return false;
        }

        if (sensor.Hardware.HardwareType == HardwareType.Cpu)
        {
            return true;
        }

        var text = $"{sensor.Hardware.Name} {sensor.Sensor.Name}";
        if (ContainsAny(text, "gpu", "vram", "ssd", "nvme", "hdd", "drive", "pch", "chipset", "ambient", "system", "motherboard"))
        {
            return ContainsAny(text, "cpu", "socket", "package", "peci");
        }

        return ContainsAny(
            text,
            "cpu package",
            "package",
            "cpu",
            "socket",
            "peci",
            "tctl",
            "tdie",
            "ccd",
            "core max",
            "max core",
            "p-core",
            "e-core");
    }

    private static int CpuTemperatureScore((IHardware Hardware, ISensor Sensor) sensor)
    {
        var text = $"{sensor.Hardware.Name} {sensor.Sensor.Name}";
        var name = sensor.Sensor.Name.Trim();
        if (ContainsAny(text, "cpu package", "package"))
        {
            return 0;
        }

        if (ContainsAny(text, "tctl/tdie", "tctl", "tdie"))
        {
            return 1;
        }

        if (ContainsAny(text, "core max", "max core", "cpu max"))
        {
            return 2;
        }

        if (ContainsAny(text, "ccd max", "ccd"))
        {
            return 3;
        }

        if (ContainsAny(text, "peci", "cpu socket", "socket"))
        {
            return 4;
        }

        if (name.Equals("CPU", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CPU Temperature", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (ContainsAny(name, "p-core", "e-core", "core"))
        {
            return 10;
        }

        return 100;
    }

    private static int GpuTemperatureScore((IHardware Hardware, ISensor Sensor) sensor)
    {
        var name = sensor.Sensor.Name.Trim();
        if (ContainsAny(name, "gpu core", "core", "edge"))
        {
            return 0;
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

    private IReadOnlyList<TelemetrySensorReading> GetTemperatureDiagnostics(
        (IHardware Hardware, ISensor Sensor)[] sensors,
        DateTimeOffset now)
    {
        if (now - _lastTemperatureDiagnosticsAt < TemperatureDiagnosticsInterval)
        {
            return _lastTemperatureSensors;
        }

        _lastTemperatureDiagnosticsAt = now;
        _lastTemperatureSensors = BuildTemperatureDiagnostics(sensors);
        return _lastTemperatureSensors;
    }

    private static IReadOnlyList<TelemetrySensorReading> BuildTemperatureDiagnostics(
        (IHardware Hardware, ISensor Sensor)[] sensors)
    {
        return sensors
            .Where(x => x.Sensor.SensorType == SensorType.Temperature)
            .OrderBy(x => TemperaturePriority(x))
            .ThenBy(x => x.Hardware.Name)
            .ThenBy(x => x.Sensor.Name)
            .Select(ToTelemetrySensorReading)
            .Take(24)
            .ToArray();
    }

    private IReadOnlyList<TelemetrySensorReading> BuildSensorCatalog(
        (IHardware Hardware, ISensor Sensor)[] sensors)
    {
        var readings = sensors
            .Where(x => IsSupportedSensorType(x.Sensor.SensorType))
            .OrderBy(x => SensorTypePriority(x.Sensor.SensorType))
            .ThenBy(x => HardwarePriority(x.Hardware.HardwareType))
            .ThenBy(x => x.Hardware.Name)
            .ThenBy(x => x.Sensor.Name)
            .Select(ToTelemetrySensorReading)
            .ToArray();

        _lastSensors = readings;
        return readings;
    }

    private static TelemetrySensorReading ToTelemetrySensorReading((IHardware Hardware, ISensor Sensor) sensor)
    {
        var hardwareType = sensor.Hardware.HardwareType.ToString();
        var sensorType = sensor.Sensor.SensorType.ToString();
        return new TelemetrySensorReading(
            BuildSensorId(sensor.Hardware.Name, hardwareType, sensor.Sensor.Name, sensorType),
            sensor.Hardware.Name,
            sensor.Sensor.Name,
            hardwareType,
            sensorType,
            sensor.Sensor.Value,
            SensorUnit(sensor.Sensor.SensorType));
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

    private static string BuildSensorId(string hardwareName, string hardwareType, string sensorName, string sensorType)
    {
        return $"lhm/{NormalizeId(hardwareType)}/{NormalizeId(hardwareName)}/{NormalizeId(sensorType)}/{NormalizeId(sensorName)}";
    }

    private static string NormalizeId(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int TemperaturePriority((IHardware Hardware, ISensor Sensor) sensor)
    {
        if (IsCpuTemperatureCandidate(sensor))
        {
            return 0;
        }

        if (sensor.Hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
        {
            return 1;
        }

        if (sensor.Hardware.HardwareType == HardwareType.Storage)
        {
            return 2;
        }

        if (sensor.Hardware.HardwareType == HardwareType.Motherboard)
        {
            return 3;
        }

        return 4;
    }

    private string BuildStatus(
        (IHardware Hardware, ISensor Sensor)[] sensors,
        TemperatureReading? cpuTemp,
        TemperatureReading? gpuTemp)
    {
        var tempSensorCount = sensors.Count(x => x.Sensor.SensorType == SensorType.Temperature && x.Sensor.Value is not null);
        var reliableTempCount = sensors.Count(x => x.Sensor.SensorType == SensorType.Temperature && IsPlausibleTemperature(x));
        var cpuCandidateCount = sensors.Count(x => x.Sensor.SensorType == SensorType.Temperature && IsCpuTemperatureCandidate(x));
        var cpuCandidateValueCount = sensors.Count(x => x.Sensor.SensorType == SensorType.Temperature && x.Sensor.Value is not null && IsCpuTemperatureCandidate(x));
        var pawnIoStatus = cpuTemp is null && cpuCandidateCount > 0 && !IsPawnIoInstalled()
            ? "；温度引擎需要修复：PawnIO 未安装"
            : string.Empty;
        var tempStatus = cpuTemp is null && gpuTemp is null
            ? $"未读到可信温度（原始 {tempSensorCount} 项，可信 {reliableTempCount} 项；CPU 候选 {cpuCandidateValueCount}/{cpuCandidateCount} 项{pawnIoStatus}）"
            : $"可信温度 {reliableTempCount}/{tempSensorCount} 项；CPU 候选 {cpuCandidateValueCount}/{cpuCandidateCount} 项{pawnIoStatus}";

        return $"{_status}；{tempStatus}";
    }

    private static bool IsPawnIoInstalled()
    {
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            return services?.GetSubKeyNames()
                .Any(x => x.Contains("Pawn", StringComparison.OrdinalIgnoreCase)
                          || x.Contains("PawnIO", StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch
        {
            return false;
        }
    }

    private static TemperatureReading ToTemperatureReading((IHardware Hardware, ISensor Sensor) sensor, string? prefix = null)
    {
        var source = $"{sensor.Hardware.Name} / {sensor.Sensor.Name}";
        return new TemperatureReading(
            sensor.Sensor.Value!.Value,
            string.IsNullOrWhiteSpace(prefix) ? source : $"{prefix}：{source}");
    }

    private static string TemperatureRole(int score)
    {
        return score switch
        {
            0 => "封装",
            1 => "Tctl/Tdie",
            <= 5 => "高可信",
            10 => "核心回退",
            30 => "显存回退",
            _ => "传感器回退"
        };
    }

    private static bool IsPlausibleTemperature((IHardware Hardware, ISensor Sensor) sensor)
    {
        return sensor.Sensor.Value is >= MinPlausibleTemperature and <= MaxPlausibleTemperature;
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
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
            Length = (uint) Marshal.SizeOf(typeof(MemoryStatusEx));
        }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
            {
                subHardware.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor)
        {
        }

        public void VisitParameter(IParameter parameter)
        {
        }
    }

    private sealed record TemperatureReading(double Value, string Source);

    private sealed record SensorReadingValue(double Value, string Source);
}

internal sealed record HardwareSnapshot(
    double? CpuLoad,
    double? CpuTemperature,
    string? CpuTemperatureSource,
    double? GpuLoad,
    string? GpuLoadSource,
    double? GpuTemperature,
    string? GpuTemperatureSource,
    double? DiskTemperature,
    string? DiskTemperatureSource,
    double? MemoryLoad,
    double? MemoryUsedGb,
    double? MemoryTotalGb,
    IReadOnlyList<TelemetrySensorReading> TemperatureSensors,
    IReadOnlyList<TelemetrySensorReading> Sensors,
    string Status)
{
    public static HardwareSnapshot Empty(string status)
    {
        return new HardwareSnapshot(
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
            status);
    }
}
