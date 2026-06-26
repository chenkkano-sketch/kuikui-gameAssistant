namespace KuikuiTelemetryService;

internal sealed record TelemetryRequest(
    int? TargetProcessId,
    string? TargetApplication);

internal sealed record TelemetrySnapshot(
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
    IReadOnlyList<TelemetrySensorReading> TemperatureSensors,
    IReadOnlyList<TelemetrySensorReading> Sensors,
    string Status,
    bool EngineRunning,
    int SchemaVersion,
    IReadOnlyList<string> Capabilities)
{
    public double? OnePercentLowFps { get; init; }
}

internal sealed record TelemetrySensorReading(
    string Id,
    string HardwareName,
    string Name,
    string Type,
    string SensorType,
    double? Value,
    string Unit);
