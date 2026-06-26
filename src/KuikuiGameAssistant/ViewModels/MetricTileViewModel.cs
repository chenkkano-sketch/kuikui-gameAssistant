namespace KuikuiGameAssistant.ViewModels;

public sealed class MetricTileViewModel : ObservableObject
{
    private string _title;
    private string _unit;
    private string _valueText = "--";
    private string _subtitle = "等待数据";
    private string _selectedSensorId = string.Empty;
    private bool _canSelectSensor;

    public MetricTileViewModel(
        string id,
        string icon,
        string title,
        string unit,
        System.Windows.Media.Brush accentBrush,
        KuikuiGameAssistant.Models.MonitorModuleConfig? config = null)
    {
        Id = id;
        Icon = icon;
        _title = title;
        _unit = unit;
        AccentBrush = accentBrush;
        Config = config;
        _selectedSensorId = config?.SensorId ?? string.Empty;
    }

    public string Id { get; }
    public string Icon { get; }
    public System.Windows.Media.Brush AccentBrush { get; }
    public KuikuiGameAssistant.Models.MonitorModuleConfig? Config { get; }
    public System.Collections.ObjectModel.ObservableCollection<SensorOptionViewModel> SensorOptions { get; } = new();

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Unit
    {
        get => _unit;
        set => SetProperty(ref _unit, value);
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        set => SetProperty(ref _subtitle, value);
    }

    public string SelectedSensorId
    {
        get => _selectedSensorId;
        set
        {
            if (!SetProperty(ref _selectedSensorId, value ?? string.Empty))
            {
                return;
            }

            if (Config is not null)
            {
                Config.SensorId = _selectedSensorId;
            }
        }
    }

    public bool CanSelectSensor
    {
        get => _canSelectSensor;
        set => SetProperty(ref _canSelectSensor, value);
    }
}

public sealed record SensorOptionViewModel(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record MonitorModuleTemplateViewModel(
    string Title,
    string SensorType,
    string HardwareRole,
    string Unit)
{
    public override string ToString() => Title;
}
