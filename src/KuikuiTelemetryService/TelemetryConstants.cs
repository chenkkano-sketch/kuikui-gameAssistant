namespace KuikuiTelemetryService;

internal static class TelemetryConstants
{
    public const int SchemaVersion = 2;
    public static readonly string[] Capabilities = ["frames", "hardware", "temperature", "one-percent-low-fps"];
    public const string ServiceName = "KuikuiTelemetryService";
    public const string ServiceDisplayName = "Kuikui Telemetry Service";
    public const string PipeName = "kuikui-gameassistant-telemetry-v2";
}
