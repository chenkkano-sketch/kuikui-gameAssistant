using System.Collections.ObjectModel;

namespace KuikuiGameAssistant.Models;

public sealed class MonitorModuleConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "监控模块";
    public string Icon { get; set; } = "\uE9D9";
    public string SensorType { get; set; } = "Temperature";
    public string HardwareRole { get; set; } = string.Empty;
    public string SensorId { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Format { get; set; } = "0";
    public string AccentColor { get; set; } = "#2563EB";
    public bool IsEnabled { get; set; } = true;
    public int Order { get; set; }

    public MonitorModuleConfig Clone()
    {
        return new MonitorModuleConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = Title,
            Icon = Icon,
            SensorType = SensorType,
            HardwareRole = HardwareRole,
            SensorId = SensorId,
            Unit = Unit,
            Format = Format,
            AccentColor = AccentColor,
            IsEnabled = true,
            Order = Order
        };
    }

    public static ObservableCollection<MonitorModuleConfig> CreateDefaults()
    {
        var modules = new[]
        {
            Create("FPS", "\uE7C1", "FrameRate", "fps", "builtin/fps", "FPS", "0", "#F97316"),
            Create("帧时间", "\uE7C1", "Time", "frame", "builtin/frame-time", "ms", "0.0", "#64748B"),
            Create("CPU 占用", "\uE950", "Load", "cpu", "builtin/cpu-load", "%", "0", "#2563EB"),
            Create("CPU 温度", "\uE9CA", "Temperature", "cpu", string.Empty, "℃", "0", "#DC2626"),
            Create("CPU 功耗", "\uE945", "Power", "cpu", string.Empty, "W", "0", "#B45309"),
            Create("CPU 电压", "\uE945", "Voltage", "cpu", string.Empty, "V", "0.00", "#7C2D12"),
            Create("CPU 频率", "\uE950", "Clock", "cpu", string.Empty, "MHz", "0", "#0F766E"),
            Create("GPU 占用", "\uE7F8", "Load", "gpu", "builtin/gpu-load", "%", "0", "#7C3AED"),
            Create("GPU 温度", "\uE9CA", "Temperature", "gpu", string.Empty, "℃", "0", "#EA580C"),
            Create("GPU 功耗", "\uE945", "Power", "gpu", string.Empty, "W", "0", "#A21CAF"),
            Create("GPU 电压", "\uE945", "Voltage", "gpu", string.Empty, "V", "0.00", "#9333EA"),
            Create("GPU 频率", "\uE950", "Clock", "gpu", string.Empty, "MHz", "0", "#4F46E5"),
            Create("显存频率", "\uE950", "Clock", "gpu-memory", string.Empty, "MHz", "0", "#0891B2"),
            Create("风扇转速", "\uE7C1", "Fan", string.Empty, string.Empty, "RPM", "0", "#16A34A"),
            Create("硬盘温度", "\uE958", "Temperature", "storage", string.Empty, "℃", "0", "#0F766E"),
            Create("内存", "\uE964", "Load", "memory", "builtin/memory-load", "%", "0", "#059669")
        };

        for (var i = 0; i < modules.Length; i++)
        {
            modules[i].Order = i;
        }

        return new ObservableCollection<MonitorModuleConfig>(modules);
    }

    private static MonitorModuleConfig Create(
        string title,
        string icon,
        string sensorType,
        string hardwareRole,
        string sensorId,
        string unit,
        string format,
        string accentColor)
    {
        return new MonitorModuleConfig
        {
            Title = title,
            Icon = icon,
            SensorType = sensorType,
            HardwareRole = hardwareRole,
            SensorId = sensorId,
            Unit = unit,
            Format = format,
            AccentColor = accentColor
        };
    }
}
