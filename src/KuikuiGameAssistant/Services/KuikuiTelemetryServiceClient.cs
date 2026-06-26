using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using KuikuiGameAssistant.Models;

namespace KuikuiGameAssistant.Services;

internal sealed record KuikuiTelemetrySnapshot(
    DateTimeOffset Timestamp,
    double? FramesPerSecond,
    double? FrameTimeMs,
    int? ProcessId,
    string? Application,
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
    IReadOnlyList<KuikuiTemperatureSensorReading>? TemperatureSensors,
    IReadOnlyList<KuikuiSensorReading>? Sensors,
    string Status,
    bool EngineRunning,
    int SchemaVersion,
    IReadOnlyList<string>? Capabilities)
{
    public double? OnePercentLowFps { get; init; }

    public const int RequiredHardwareSchemaVersion = 2;

    public bool SupportsHardwareTelemetry =>
        SchemaVersion >= RequiredHardwareSchemaVersion
        && Capabilities?.Any(x => x.Equals("hardware", StringComparison.OrdinalIgnoreCase)) == true
        && Capabilities.Any(x => x.Equals("temperature", StringComparison.OrdinalIgnoreCase));

    public bool IsLegacyService => EngineRunning && SchemaVersion < RequiredHardwareSchemaVersion;

    public IReadOnlyList<SensorReading> ToSensorReadings()
    {
        return TemperatureSensors is null
            ? Array.Empty<SensorReading>()
            : TemperatureSensors
                .Select(x => x.ToSensorReading())
                .ToArray();
    }

    public IReadOnlyList<SensorReading> ToSensors()
    {
        return Sensors is null
            ? ToSensorReadings()
            : Sensors
                .Select(x => x.ToSensorReading())
                .ToArray();
    }
}

internal sealed record KuikuiTemperatureSensorReading(
    string? Id,
    string HardwareName,
    string Name,
    string Type,
    string? SensorType,
    double? Value,
    string Unit)
{
    public SensorReading ToSensorReading()
    {
        var sensorType = string.IsNullOrWhiteSpace(SensorType) ? "Temperature" : SensorType!;
        var id = string.IsNullOrWhiteSpace(Id)
            ? SensorReading.BuildId("service", HardwareName, Type, Name, sensorType)
            : Id!;
        return new SensorReading(id, HardwareName, Name, Type, sensorType, Value, Unit);
    }
}

internal sealed record KuikuiSensorReading(
    string? Id,
    string HardwareName,
    string Name,
    string Type,
    string? SensorType,
    double? Value,
    string Unit)
{
    public SensorReading ToSensorReading()
    {
        var sensorType = string.IsNullOrWhiteSpace(SensorType) ? "Unknown" : SensorType!;
        var id = string.IsNullOrWhiteSpace(Id)
            ? SensorReading.BuildId("service", HardwareName, Type, Name, sensorType)
            : Id!;
        return new SensorReading(id, HardwareName, Name, Type, sensorType, Value, Unit);
    }
}

internal sealed class KuikuiTelemetryServiceClient
{
    private const string PipeName = "kuikui-gameassistant-telemetry-v2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool TryReadLatest(out KuikuiTelemetrySnapshot snapshot)
    {
        snapshot = new KuikuiTelemetrySnapshot(
            DateTimeOffset.Now,
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
            null,
            null,
            null,
            null,
            Array.Empty<KuikuiTemperatureSensorReading>(),
            Array.Empty<KuikuiSensorReading>(),
            "FPS 引擎未运行",
            false,
            0,
            Array.Empty<string>());

        try
        {
            TryGetForegroundTarget(out var processId, out var application);
            var request = new TelemetryRequest(processId > 0 ? processId : null, application);

            using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.None);
            pipe.Connect(120);

            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true)
            {
                AutoFlush = true
            };

            writer.WriteLine(JsonSerializer.Serialize(request, JsonOptions));
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            snapshot = JsonSerializer.Deserialize<KuikuiTelemetrySnapshot>(line, JsonOptions) ?? snapshot;
            return true;
        }
        catch
        {
            return false;
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private sealed record TelemetryRequest(int? TargetProcessId, string? TargetApplication);
}
