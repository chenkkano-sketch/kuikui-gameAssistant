namespace KuikuiGameAssistant.ViewModels;

public sealed class TemperatureSensorItemViewModel
{
    public TemperatureSensorItemViewModel(string hardwareName, string sensorName, string hardwareType, double? value)
    {
        HardwareName = hardwareName;
        SensorName = sensorName;
        HardwareType = hardwareType;
        ValueText = value is null ? "--" : $"{value:0.0} ℃";
        IsCpuCandidate = IsLikelyCpuSensor($"{hardwareName} {sensorName} {hardwareType}");
    }

    public string HardwareName { get; }
    public string SensorName { get; }
    public string HardwareType { get; }
    public string ValueText { get; }
    public bool IsCpuCandidate { get; }

    private static bool IsLikelyCpuSensor(string text)
    {
        return text.Contains("cpu", StringComparison.OrdinalIgnoreCase)
               || text.Contains("package", StringComparison.OrdinalIgnoreCase)
               || text.Contains("tctl", StringComparison.OrdinalIgnoreCase)
               || text.Contains("tdie", StringComparison.OrdinalIgnoreCase)
               || text.Contains("ccd", StringComparison.OrdinalIgnoreCase)
               || text.Contains("socket", StringComparison.OrdinalIgnoreCase);
    }
}
