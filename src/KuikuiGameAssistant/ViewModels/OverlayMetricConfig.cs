namespace KuikuiGameAssistant.ViewModels;

public enum OverlayMetricKind
{
    FramesPerSecond,
    OnePercentLowFps,
    CpuLoad,
    GpuLoad,
    MemoryLoad,
    CpuTemperature,
    GpuTemperature
}

public sealed class OverlayMetricConfig : ObservableObject
{
    private bool _isEnabled = true;
    private int _order;

    public OverlayMetricKind Kind { get; set; }

    public string Label { get; set; } = string.Empty;

    public string PreviewValue { get; set; } = string.Empty;

    public int Order
    {
        get => _order;
        set => SetProperty(ref _order, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public OverlayMetricConfig Clone()
    {
        return new OverlayMetricConfig
        {
            Kind = Kind,
            Label = Label,
            PreviewValue = PreviewValue,
            Order = Order,
            IsEnabled = IsEnabled
        };
    }
}
