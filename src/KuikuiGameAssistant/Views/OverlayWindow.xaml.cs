using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Services;
using KuikuiGameAssistant.ViewModels;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace KuikuiGameAssistant.Views;

public partial class OverlayWindow : Window
{
    private readonly TelemetryService _telemetry;
    private readonly OverlaySettings _settings;

    public OverlayWindow(TelemetryService telemetry, OverlaySettings settings)
    {
        InitializeComponent();
        _telemetry = telemetry;
        _settings = settings;
        DataContext = this;

        RebuildMetrics();
        _telemetry.SnapshotUpdated += Telemetry_SnapshotUpdated;
        _settings.PropertyChanged += Settings_PropertyChanged;
        _settings.Metrics.CollectionChanged += Metrics_CollectionChanged;

        Apply(_telemetry.Latest);
        ApplySettings();
    }

    public ObservableCollection<OverlayMetricDisplayItem> Metrics { get; } = new();

    protected override void OnClosed(EventArgs e)
    {
        _telemetry.SnapshotUpdated -= Telemetry_SnapshotUpdated;
        _settings.PropertyChanged -= Settings_PropertyChanged;
        _settings.Metrics.CollectionChanged -= Metrics_CollectionChanged;
        ClearMetrics();
        base.OnClosed(e);
    }

    private void Telemetry_SnapshotUpdated(object? sender, RealtimeSnapshot e)
    {
        Dispatcher.Invoke(() => Apply(e));
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(ApplySettings);
    }

    private void Metrics_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            RebuildMetrics();
            Apply(_telemetry.Latest);
            ApplySettings();
        });
    }

    private void Apply(RealtimeSnapshot snapshot)
    {
        foreach (var metric in Metrics)
        {
            metric.Value = FormatMetric(metric.Kind, snapshot);
        }
    }

    private static string FormatMetric(OverlayMetricKind kind, RealtimeSnapshot snapshot)
    {
        return kind switch
        {
            OverlayMetricKind.FramesPerSecond => FormatNumber(snapshot.FramesPerSecond),
            OverlayMetricKind.OnePercentLowFps => FormatNumber(snapshot.OnePercentLowFps),
            OverlayMetricKind.CpuLoad => FormatPercent(snapshot.CpuLoad),
            OverlayMetricKind.GpuLoad => FormatPercent(snapshot.GpuLoad),
            OverlayMetricKind.MemoryLoad => FormatPercent(snapshot.MemoryLoad),
            OverlayMetricKind.CpuTemperature => FormatTemperature(snapshot.CpuTemperature),
            OverlayMetricKind.GpuTemperature => FormatTemperature(snapshot.GpuTemperature),
            _ => "--"
        };
    }

    private static string FormatNumber(double? value) => value is null ? "--" : $"{value:0}";

    private static string FormatPercent(double? value) => value is null ? "--%" : $"{value:0}%";

    private static string FormatTemperature(double? temperature) => temperature is null ? "--℃" : $"{temperature:0}℃";

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void SetLayoutMode(OverlayLayoutMode layoutMode)
    {
        HorizontalPanel.Visibility = layoutMode == OverlayLayoutMode.Horizontal ? Visibility.Visible : Visibility.Collapsed;
        VerticalPanel.Visibility = layoutMode == OverlayLayoutMode.Vertical ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplySettings()
    {
        SetLayoutMode(_settings.LayoutMode);
        Width = _settings.CurrentWidth;
        Height = _settings.CurrentHeight;
        RootBorder.Padding = _settings.LayoutMode == OverlayLayoutMode.Horizontal
            ? new Thickness(10)
            : new Thickness(12, 8, 12, 12);
        RootBorder.Background = _settings.BackgroundBrush;

        foreach (var metric in Metrics)
        {
            metric.LabelBrush = _settings.LabelBrush;
            metric.ValueBrush = _settings.FontBrush;
            metric.LabelFontSize = _settings.LabelFontSize;
            metric.ValueFontSize = _settings.FontSize;
        }
    }

    private void RebuildMetrics()
    {
        ClearMetrics();
        foreach (var metric in _settings.Metrics.OrderBy(x => x.Order))
        {
            Metrics.Add(new OverlayMetricDisplayItem(metric));
        }
    }

    private void ClearMetrics()
    {
        foreach (var metric in Metrics)
        {
            metric.Dispose();
        }

        Metrics.Clear();
    }
}

public sealed class OverlayMetricDisplayItem : ObservableObject, IDisposable
{
    private readonly OverlayMetricConfig _config;
    private string _value = "--";
    private MediaBrush _labelBrush = MediaBrushes.White;
    private MediaBrush _valueBrush = MediaBrushes.White;
    private double _labelFontSize = 11;
    private double _valueFontSize = 18;

    public OverlayMetricDisplayItem(OverlayMetricConfig config)
    {
        _config = config;
        _config.PropertyChanged += Config_PropertyChanged;
    }

    public OverlayMetricKind Kind => _config.Kind;

    public string Label => _config.Label;

    public Visibility Visibility => _config.IsEnabled ? Visibility.Visible : Visibility.Collapsed;

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public MediaBrush LabelBrush
    {
        get => _labelBrush;
        set => SetProperty(ref _labelBrush, value);
    }

    public MediaBrush ValueBrush
    {
        get => _valueBrush;
        set => SetProperty(ref _valueBrush, value);
    }

    public double LabelFontSize
    {
        get => _labelFontSize;
        set => SetProperty(ref _labelFontSize, value);
    }

    public double ValueFontSize
    {
        get => _valueFontSize;
        set => SetProperty(ref _valueFontSize, value);
    }

    public void Dispose()
    {
        _config.PropertyChanged -= Config_PropertyChanged;
    }

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverlayMetricConfig.IsEnabled))
        {
            OnPropertyChanged(nameof(Visibility));
        }
    }
}
