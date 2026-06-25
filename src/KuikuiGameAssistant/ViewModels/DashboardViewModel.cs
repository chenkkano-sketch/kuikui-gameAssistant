using System.Collections.ObjectModel;
using System.Windows.Media;
using KuikuiGameAssistant.Models;

namespace KuikuiGameAssistant.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly MetricTileViewModel _fps;
    private readonly MetricTileViewModel _cpuLoad;
    private readonly MetricTileViewModel _cpuTemperature;
    private readonly MetricTileViewModel _gpuLoad;
    private readonly MetricTileViewModel _gpuTemperature;
    private readonly MetricTileViewModel _diskTemperature;
    private readonly MetricTileViewModel _memory;

    private string _updatedText = "等待采集";
    private string _statusText = "传感器服务准备中";
    private string _temperatureDiagnosticText = "等待温度传感器";

    public DashboardViewModel()
    {
        _fps = new MetricTileViewModel("\uE7C1", "FPS", "帧/秒", BrushFrom("#F97316"));
        _cpuLoad = new MetricTileViewModel("\uE950", "CPU 占用", "%", BrushFrom("#2563EB"));
        _cpuTemperature = new MetricTileViewModel("\uE9CA", "CPU 温度", "℃", BrushFrom("#DC2626"));
        _gpuLoad = new MetricTileViewModel("\uE7F8", "GPU 占用", "%", BrushFrom("#7C3AED"));
        _gpuTemperature = new MetricTileViewModel("\uE9CA", "GPU 温度", "℃", BrushFrom("#EA580C"));
        _diskTemperature = new MetricTileViewModel("\uE958", "硬盘温度", "℃", BrushFrom("#0F766E"));
        _memory = new MetricTileViewModel("\uE964", "内存", "%", BrushFrom("#059669"));

        Metrics = new ObservableCollection<MetricTileViewModel>
        {
            _fps,
            _cpuLoad,
            _cpuTemperature,
            _gpuLoad,
            _gpuTemperature,
            _diskTemperature,
            _memory
        };
    }

    public ObservableCollection<MetricTileViewModel> Metrics { get; }
    public ObservableCollection<double> CpuHistory { get; } = new();
    public ObservableCollection<double> GpuHistory { get; } = new();
    public ObservableCollection<double> MemoryHistory { get; } = new();
    public ObservableCollection<TemperatureSensorItemViewModel> TemperatureSensors { get; } = new();

    public string UpdatedText
    {
        get => _updatedText;
        set => SetProperty(ref _updatedText, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string TemperatureDiagnosticText
    {
        get => _temperatureDiagnosticText;
        set => SetProperty(ref _temperatureDiagnosticText, value);
    }

    public void Apply(RealtimeSnapshot snapshot)
    {
        _fps.ValueText = Format(snapshot.FramesPerSecond, "0");
        _fps.Subtitle = snapshot.FrameTimeMs is null ? "等待 PresentMon 接入" : $"{snapshot.FrameTimeMs:0.0} ms frame time";

        _cpuLoad.ValueText = Format(snapshot.CpuLoad, "0");
        _cpuLoad.Subtitle = "LibreHardwareMonitor";

        _cpuTemperature.ValueText = Format(snapshot.CpuTemperature, "0");
        _cpuTemperature.Subtitle = snapshot.CpuTemperature is null
            ? "未读到可信 CPU 温度"
            : snapshot.CpuTemperatureSource ?? "CPU package / core";

        _gpuLoad.ValueText = Format(snapshot.GpuLoad, "0");
        _gpuLoad.Subtitle = snapshot.GpuLoad is null
            ? "未读到 GPU 负载"
            : snapshot.GpuLoadSource ?? "GPU core";

        _gpuTemperature.ValueText = Format(snapshot.GpuTemperature, "0");
        _gpuTemperature.Subtitle = snapshot.GpuTemperature is null
            ? "未读到可信 GPU 温度"
            : snapshot.GpuTemperatureSource ?? "GPU core";

        _diskTemperature.ValueText = Format(snapshot.DiskTemperature, "0");
        _diskTemperature.Subtitle = snapshot.DiskTemperature is null
            ? "未读到硬盘温度"
            : snapshot.DiskTemperatureSource ?? "硬盘主温度";

        _memory.ValueText = Format(snapshot.MemoryLoad, "0");
        _memory.Subtitle = snapshot.MemoryUsedGb is null || snapshot.MemoryTotalGb is null
            ? "系统内存"
            : $"{snapshot.MemoryUsedGb:0.0} / {snapshot.MemoryTotalGb:0.0} GB";

        AddPoint(CpuHistory, snapshot.CpuLoad);
        AddPoint(GpuHistory, snapshot.GpuLoad);
        AddPoint(MemoryHistory, snapshot.MemoryLoad);

        UpdatedText = $"更新于 {snapshot.Timestamp:HH:mm:ss}";
        StatusText = snapshot.Status;
        ApplyTemperatureDiagnostics(snapshot);
    }

    private static string Format(double? value, string format) => value is null ? "--" : value.Value.ToString(format);

    private static void AddPoint(ObservableCollection<double> history, double? value)
    {
        history.Add(Math.Clamp(value ?? 0, 0, 100));
        while (history.Count > 60)
        {
            history.RemoveAt(0);
        }
    }

    private void ApplyTemperatureDiagnostics(RealtimeSnapshot snapshot)
    {
        TemperatureDiagnosticText = snapshot.CpuTemperature is null
            ? "CPU 温度未命中；优先看是否存在 CPU Package / Tctl / Tdie / Socket。"
            : $"CPU 温度来源：{snapshot.CpuTemperatureSource}";

        TemperatureSensors.Clear();
        foreach (var sensor in snapshot.TemperatureSensors)
        {
            TemperatureSensors.Add(new TemperatureSensorItemViewModel(
                sensor.HardwareName,
                sensor.Name,
                sensor.Type,
                sensor.Value));
        }
    }

    private static SolidColorBrush BrushFrom(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
