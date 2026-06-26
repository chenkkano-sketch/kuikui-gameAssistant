using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Services;

namespace KuikuiGameAssistant.ViewModels;

public enum DashboardFpsAction
{
    None,
    EnablePresentMon,
    RestartPresentMon,
    RestartAsAdmin,
    RepairTelemetryService,
    RepairTemperatureEngine,
    SelectPresentMonPath
}

public sealed class DashboardViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private string _updatedText = "等待采集";
    private string _statusText = "传感器服务准备中";
    private string _temperatureDiagnosticText = "等待温度传感器";
    private string _fpsAssistTitle = "FPS 采集未就绪";
    private string _fpsAssistText = "可在这里快速启动 PresentMon。";
    private string _fpsAssistButtonText = "启动 FPS 采集";
    private Visibility _fpsAssistVisibility = Visibility.Collapsed;
    private DashboardFpsAction _fpsAction = DashboardFpsAction.None;
    private IReadOnlyList<SensorReading>? _lastTemperatureSensors;
    private MonitorModuleTemplateViewModel? _selectedModuleTemplate;

    public DashboardViewModel(AppSettings settings)
    {
        _settings = settings;
        if (_settings.MonitorModules is null || _settings.MonitorModules.Count == 0)
        {
            _settings.MonitorModules = MonitorModuleConfig.CreateDefaults();
        }

        ModuleTemplates = MonitorModuleConfig.CreateDefaults()
            .Select(x => new MonitorModuleTemplateViewModel(x.Title, x.SensorType, x.HardwareRole, x.Unit))
            .ToArray();
        _selectedModuleTemplate = ModuleTemplates.FirstOrDefault();
        RebuildModules();
        AppThemeService.ThemeApplied += AppThemeService_ThemeApplied;
    }

    public ObservableCollection<MetricTileViewModel> Metrics { get; } = new();
    public ObservableCollection<double> CpuHistory { get; } = new();
    public ObservableCollection<double> GpuHistory { get; } = new();
    public ObservableCollection<double> MemoryHistory { get; } = new();
    public ObservableCollection<TemperatureSensorItemViewModel> TemperatureSensors { get; } = new();
    public IReadOnlyList<MonitorModuleTemplateViewModel> ModuleTemplates { get; }

    public MonitorModuleTemplateViewModel? SelectedModuleTemplate
    {
        get => _selectedModuleTemplate;
        set => SetProperty(ref _selectedModuleTemplate, value);
    }

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

    public string FpsAssistTitle
    {
        get => _fpsAssistTitle;
        set => SetProperty(ref _fpsAssistTitle, value);
    }

    public string FpsAssistText
    {
        get => _fpsAssistText;
        set => SetProperty(ref _fpsAssistText, value);
    }

    public string FpsAssistButtonText
    {
        get => _fpsAssistButtonText;
        set => SetProperty(ref _fpsAssistButtonText, value);
    }

    public Visibility FpsAssistVisibility
    {
        get => _fpsAssistVisibility;
        set => SetProperty(ref _fpsAssistVisibility, value);
    }

    public DashboardFpsAction FpsAction
    {
        get => _fpsAction;
        set => SetProperty(ref _fpsAction, value);
    }

    public bool AddSelectedModule()
    {
        if (SelectedModuleTemplate is null)
        {
            return false;
        }

        var template = MonitorModuleConfig.CreateDefaults()
            .FirstOrDefault(x => x.Title == SelectedModuleTemplate.Title
                                 && x.SensorType == SelectedModuleTemplate.SensorType
                                 && x.HardwareRole == SelectedModuleTemplate.HardwareRole);
        if (template is null)
        {
            return false;
        }

        var module = template.Clone();
        module.Order = _settings.MonitorModules.Count == 0
            ? 0
            : _settings.MonitorModules.Max(x => x.Order) + 1;
        _settings.MonitorModules.Add(module);
        Metrics.Add(CreateMetric(module));
        return true;
    }

    public bool RemoveModule(string moduleId)
    {
        if (_settings.MonitorModules.Count <= 1)
        {
            return false;
        }

        var config = _settings.MonitorModules.FirstOrDefault(x => x.Id == moduleId);
        if (config is not null)
        {
            _settings.MonitorModules.Remove(config);
        }

        var metric = Metrics.FirstOrDefault(x => x.Id == moduleId);
        if (metric is not null)
        {
            Metrics.Remove(metric);
        }

        return config is not null || metric is not null;
    }

    public void Apply(RealtimeSnapshot snapshot)
    {
        ApplyFpsAssist(snapshot);
        ApplyModules(snapshot);

        AddPoint(CpuHistory, snapshot.CpuLoad);
        AddPoint(GpuHistory, snapshot.GpuLoad);
        AddPoint(MemoryHistory, snapshot.MemoryLoad);

        UpdatedText = $"更新于 {snapshot.Timestamp:HH:mm:ss}";
        StatusText = snapshot.Status;
        ApplyTemperatureDiagnostics(snapshot);
    }

    private void RebuildModules()
    {
        Metrics.Clear();
        foreach (var module in _settings.MonitorModules
                     .Where(x => x.IsEnabled)
                     .OrderBy(x => x.Order))
        {
            Metrics.Add(CreateMetric(module));
        }
    }

    private static MetricTileViewModel CreateMetric(MonitorModuleConfig module)
    {
        return new MetricTileViewModel(
            module.Id,
            string.IsNullOrWhiteSpace(module.Icon) ? "\uE9D9" : module.Icon,
            module.Title,
            module.Unit,
            BrushFrom(module.AccentColor),
            module);
    }

    private void ApplyModules(RealtimeSnapshot snapshot)
    {
        var sensors = snapshot.Sensors;
        foreach (var metric in Metrics)
        {
            var config = metric.Config;
            if (config is null)
            {
                continue;
            }

            var options = BuildSensorOptions(config, sensors);
            UpdateSensorOptions(metric, options);
            var sensor = ResolveSensor(config, sensors, options);
            metric.CanSelectSensor = options.Count > 0;

            if (sensor is null)
            {
                metric.ValueText = "--";
                metric.Subtitle = options.Count == 0 ? "暂无可用传感器" : "传感器离线";
                metric.Unit = string.IsNullOrWhiteSpace(config.Unit) ? UnitFor(config.SensorType) : config.Unit;
                continue;
            }

            metric.SelectedSensorId = sensor.Id;
            metric.Unit = string.IsNullOrWhiteSpace(sensor.Unit) ? UnitFor(config.SensorType) : sensor.Unit;
            metric.ValueText = Format(sensor.Value, FormatFor(config, sensor));
            metric.Subtitle = sensor.DisplayName;
        }
    }

    private static IReadOnlyList<SensorReading> BuildSensorOptions(MonitorModuleConfig config, IReadOnlyList<SensorReading> sensors)
    {
        return sensors
            .Where(x => SensorTypeMatches(x, config.SensorType))
            .Where(x => RoleMatches(x, config.HardwareRole))
            .OrderBy(x => SensorScore(x, config))
            .ThenBy(x => x.DisplayName)
            .ToArray();
    }

    private static void UpdateSensorOptions(MetricTileViewModel metric, IReadOnlyList<SensorReading> sensors)
    {
        if (metric.SensorOptions.Count == sensors.Count
            && metric.SensorOptions.Select(x => x.Id).SequenceEqual(sensors.Select(x => x.Id), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        metric.SensorOptions.Clear();
        foreach (var sensor in sensors)
        {
            metric.SensorOptions.Add(new SensorOptionViewModel(sensor.Id, sensor.DisplayName));
        }
    }

    private static SensorReading? ResolveSensor(
        MonitorModuleConfig config,
        IReadOnlyList<SensorReading> sensors,
        IReadOnlyList<SensorReading> options)
    {
        if (!string.IsNullOrWhiteSpace(config.SensorId))
        {
            var exact = sensors.FirstOrDefault(x => x.Id.Equals(config.SensorId, StringComparison.OrdinalIgnoreCase));
            if (exact is not null && SensorTypeMatches(exact, config.SensorType))
            {
                return exact;
            }
        }

        var best = options
            .OrderBy(x => x.Value is null ? 1 : 0)
            .ThenBy(x => SensorScore(x, config))
            .FirstOrDefault();
        if (best is not null)
        {
            config.SensorId = best.Id;
        }

        return best;
    }

    private static bool SensorTypeMatches(SensorReading sensor, string sensorType)
    {
        return sensor.SensorType.Equals(sensorType, StringComparison.OrdinalIgnoreCase)
               || (sensorType.Equals("FrameRate", StringComparison.OrdinalIgnoreCase) && sensor.Id.Equals("builtin/fps", StringComparison.OrdinalIgnoreCase))
               || (sensorType.Equals("Time", StringComparison.OrdinalIgnoreCase) && sensor.Id.Equals("builtin/frame-time", StringComparison.OrdinalIgnoreCase));
    }

    private static bool RoleMatches(SensorReading sensor, string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return true;
        }

        var text = $"{sensor.Id} {sensor.HardwareName} {sensor.Name} {sensor.HardwareType}";
        return role.ToLowerInvariant() switch
        {
            "fps" => sensor.Id.Equals("builtin/fps", StringComparison.OrdinalIgnoreCase),
            "frame" => sensor.Id.Equals("builtin/frame-time", StringComparison.OrdinalIgnoreCase),
            "cpu" => ContainsAny(text, "cpu", "core ultra", "intel core", "ryzen") && !ContainsAny(text, "gpu", "graphics"),
            "gpu" => ContainsAny(text, "gpu", "nvidia", "radeon", "graphics", "geforce", "vram"),
            "gpu-memory" => ContainsAny(text, "gpu", "nvidia", "radeon", "graphics", "geforce", "vram")
                            && ContainsAny(text, "memory", "mem", "vram", "显存"),
            "storage" => ContainsAny(text, "storage", "ssd", "nvme", "hdd", "drive", "disk"),
            "memory" => sensor.Id.StartsWith("builtin/memory", StringComparison.OrdinalIgnoreCase)
                        || ContainsAny(text, "memory", "ram"),
            _ => true
        };
    }

    private static int SensorScore(SensorReading sensor, MonitorModuleConfig config)
    {
        var text = $"{sensor.HardwareName} {sensor.Name} {sensor.HardwareType}";
        var score = sensor.Value is null ? 1000 : 0;
        if (sensor.Id.StartsWith("builtin/", StringComparison.OrdinalIgnoreCase))
        {
            score += config.SensorId.Equals(sensor.Id, StringComparison.OrdinalIgnoreCase) ? -100 : 50;
        }

        if (config.SensorType.Equals("Temperature", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsAny(text, "cpu package", "package")) return score;
            if (ContainsAny(text, "core max", "max core", "gpu core", "edge")) return score + 1;
            if (ContainsAny(text, "tctl", "tdie", "socket", "hot spot", "hotspot", "junction")) return score + 5;
            if (ContainsAny(text, "memory", "vram")) return score + 20;
        }

        if (config.SensorType.Equals("Power", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsAny(text, "package", "total", "board", "gpu power")) return score;
            if (ContainsAny(text, "cores", "core")) return score + 5;
        }

        if (config.SensorType.Equals("Voltage", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsAny(text, "vcore", "core", "gpu core")) return score;
        }

        if (config.SensorType.Equals("Clock", StringComparison.OrdinalIgnoreCase))
        {
            if (config.HardwareRole.Equals("gpu-memory", StringComparison.OrdinalIgnoreCase))
            {
                return ContainsAny(text, "memory", "mem", "vram") ? score : score + 100;
            }

            if (ContainsAny(text, "core max", "max", "graphics", "gpu core", "core")) return score;
            if (ContainsAny(text, "memory", "mem", "vram")) return score + 40;
        }

        return score + 20;
    }

    private static string FormatFor(MonitorModuleConfig config, SensorReading sensor)
    {
        if (!string.IsNullOrWhiteSpace(config.Format))
        {
            return config.Format;
        }

        return sensor.SensorType.Equals("Voltage", StringComparison.OrdinalIgnoreCase) ? "0.00" : "0";
    }

    private static string Format(double? value, string format) => value is null ? "--" : value.Value.ToString(format);

    private static string UnitFor(string sensorType)
    {
        return sensorType switch
        {
            "FrameRate" => "FPS",
            "Time" => "ms",
            "Temperature" => "℃",
            "Load" => "%",
            "Power" => "W",
            "Voltage" => "V",
            "Fan" => "RPM",
            "Clock" => "MHz",
            "Data" => "GB",
            "SmallData" => "MB",
            _ => string.Empty
        };
    }

    private static string SnapshotPresentMonStatus(string status)
    {
        var fpsIndex = status.LastIndexOf("FPS", StringComparison.OrdinalIgnoreCase);
        if (fpsIndex >= 0)
        {
            return status[fpsIndex..];
        }

        var markerIndex = status.LastIndexOf("PresentMon", StringComparison.OrdinalIgnoreCase);
        return markerIndex >= 0 ? status[markerIndex..] : "等待 FPS 采集";
    }

    private void ApplyFpsAssist(RealtimeSnapshot snapshot)
    {
        if (snapshot.Status.Contains("后台遥测服务需要升级", StringComparison.OrdinalIgnoreCase))
        {
            FpsAssistVisibility = Visibility.Visible;
            FpsAssistTitle = "后台遥测服务需要升级";
            FpsAssistText = "当前服务只返回 FPS，没有温度采集。点击一次更新后台服务，之后 CPU 温度会走常驻服务读取。";
            FpsAssistButtonText = "升级遥测服务";
            FpsAction = DashboardFpsAction.RepairTelemetryService;
            return;
        }

        if (snapshot.Status.Contains("温度引擎需要修复", StringComparison.OrdinalIgnoreCase)
            || snapshot.Status.Contains("PawnIO 未安装", StringComparison.OrdinalIgnoreCase))
        {
            FpsAssistVisibility = Visibility.Visible;
            FpsAssistTitle = "温度引擎需要修复";
            FpsAssistText = "CPU 温度需要低层硬件驱动。点击一次安装官方 PawnIO 并重启后台遥测服务。";
            FpsAssistButtonText = "修复温度引擎";
            FpsAction = DashboardFpsAction.RepairTemperatureEngine;
            return;
        }

        if (snapshot.FramesPerSecond is not null)
        {
            FpsAssistVisibility = Visibility.Collapsed;
            FpsAction = DashboardFpsAction.None;
            return;
        }

        var status = SnapshotPresentMonStatus(snapshot.Status);
        FpsAssistVisibility = Visibility.Visible;

        if (status.Contains("未启用", StringComparison.OrdinalIgnoreCase))
        {
            FpsAssistTitle = "FPS 采集未开启";
            FpsAssistText = "点击后会立即启用 FPS 引擎，并开始等待游戏帧。";
            FpsAssistButtonText = "启用 FPS 采集";
            FpsAction = DashboardFpsAction.EnablePresentMon;
            return;
        }

        if (status.Contains("FPS 引擎未运行", StringComparison.OrdinalIgnoreCase)
            || status.Contains("修复后台服务", StringComparison.OrdinalIgnoreCase))
        {
            FpsAssistTitle = "FPS 引擎需要修复";
            FpsAssistText = "点击一次完成后台服务安装和启动，以后打开软件会自动采集 FPS。";
            FpsAssistButtonText = "修复 FPS 引擎";
            FpsAction = DashboardFpsAction.RepairTelemetryService;
            return;
        }

        if (status.Contains("需要管理员权限", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Performance Log Users", StringComparison.OrdinalIgnoreCase)
            || status.Contains("access denied", StringComparison.OrdinalIgnoreCase))
        {
            FpsAssistTitle = "FPS 采集需要权限";
            FpsAssistText = "点击一次修复后台服务权限，以后无需每次管理员启动。";
            FpsAssistButtonText = "修复 FPS 引擎";
            FpsAction = DashboardFpsAction.RepairTelemetryService;
            return;
        }

        if (status.Contains("未找到", StringComparison.OrdinalIgnoreCase))
        {
            FpsAssistTitle = "未找到 PresentMon";
            FpsAssistText = "选择 PresentMon.exe 后即可在实时监控页读取真实 FPS。";
            FpsAssistButtonText = "选择程序";
            FpsAction = DashboardFpsAction.SelectPresentMonPath;
            return;
        }

        FpsAssistTitle = "FPS 暂无数据";
        FpsAssistText = status.Contains("等待游戏帧", StringComparison.OrdinalIgnoreCase)
            ? "FPS 引擎已就绪，打开游戏或切到游戏窗口后会自动显示帧率。"
            : status;
        FpsAssistButtonText = "重新启动采集";
        FpsAction = DashboardFpsAction.RestartPresentMon;
    }

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
            ? "CPU 温度未命中；可在模块卡片中手动选择 CPU Package / Core Max / Socket。"
            : $"CPU 温度来源：{snapshot.CpuTemperatureSource}";

        if (ReferenceEquals(_lastTemperatureSensors, snapshot.TemperatureSensors))
        {
            return;
        }

        _lastTemperatureSensors = snapshot.TemperatureSensors;
        TemperatureSensors.Clear();
        foreach (var sensor in snapshot.TemperatureSensors)
        {
            TemperatureSensors.Add(new TemperatureSensorItemViewModel(
                sensor.HardwareName,
                sensor.Name,
                sensor.HardwareType,
                sensor.Value));
        }
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private void AppThemeService_ThemeApplied(object? sender, EventArgs e)
    {
        RebuildModules();
    }

    private static SolidColorBrush BrushFrom(string hex)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
            if (AppThemeService.IsDark)
            {
                color = LiftForDarkSurface(color);
            }

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            var brush = System.Windows.Application.Current?.Resources["AccentBrush"] is SolidColorBrush accentBrush
                ? new SolidColorBrush(accentBrush.Color)
                : (SolidColorBrush)new BrushConverter().ConvertFromString("#FF0067C0")!;
            brush.Freeze();
            return brush;
        }
    }

    private static System.Windows.Media.Color LiftForDarkSurface(System.Windows.Media.Color color)
    {
        var luminance = RelativeLuminance(color);
        if (luminance >= 0.34)
        {
            return color;
        }

        var amount = Math.Clamp((0.34 - luminance) * 1.35, 0.22, 0.56);
        return System.Windows.Media.Color.FromRgb(
            (byte)Math.Round(color.R + (255 - color.R) * amount),
            (byte)Math.Round(color.G + (255 - color.G) * amount),
            (byte)Math.Round(color.B + (255 - color.B) * amount));
    }

    private static double RelativeLuminance(System.Windows.Media.Color color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255d;
            return normalized <= 0.03928
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
