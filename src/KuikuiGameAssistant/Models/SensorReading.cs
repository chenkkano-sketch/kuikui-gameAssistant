namespace KuikuiGameAssistant.Models;

public sealed record SensorReading(
    string Id,
    string HardwareName,
    string Name,
    string HardwareType,
    string SensorType,
    double? Value,
    string Unit)
{
    public SensorReading(string hardwareName, string name, string hardwareType, double? value, string unit)
        : this(BuildId("local", hardwareName, hardwareType, name, "Temperature"), hardwareName, name, hardwareType, "Temperature", value, unit)
    {
    }

    public string Type => HardwareType;

    public string DisplayName => string.IsNullOrWhiteSpace(HardwareName)
        ? Name
        : $"{HardwareName} / {Name}";

    public static string BuildId(string provider, string hardwareName, string hardwareType, string sensorName, string sensorType)
    {
        return $"{Normalize(provider)}/{Normalize(hardwareType)}/{Normalize(hardwareName)}/{Normalize(sensorType)}/{Normalize(sensorName)}";
    }

    private static string Normalize(string value)
    {
        var chars = (value ?? string.Empty).Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
