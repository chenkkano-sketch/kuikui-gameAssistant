namespace KuikuiGameAssistant.ViewModels;

public sealed class MetricTileViewModel : ObservableObject
{
    private string _valueText = "--";
    private string _subtitle = "等待数据";

    public MetricTileViewModel(string icon, string title, string unit, System.Windows.Media.Brush accentBrush)
    {
        Icon = icon;
        Title = title;
        Unit = unit;
        AccentBrush = accentBrush;
    }

    public string Icon { get; }
    public string Title { get; }
    public string Unit { get; }
    public System.Windows.Media.Brush AccentBrush { get; }

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
}
