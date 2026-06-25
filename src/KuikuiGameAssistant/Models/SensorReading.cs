namespace KuikuiGameAssistant.Models;

public sealed record SensorReading(
    string HardwareName,
    string Name,
    string Type,
    double? Value,
    string Unit);
