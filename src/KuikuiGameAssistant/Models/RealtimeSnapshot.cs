namespace KuikuiGameAssistant.Models;

public sealed record RealtimeSnapshot(
    DateTimeOffset Timestamp,
    double? FramesPerSecond,
    double? FrameTimeMs,
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
    IReadOnlyList<SensorReading> TemperatureSensors,
    string Status);
