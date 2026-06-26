using System.Windows;
using System.Windows.Media;
using System.Collections.ObjectModel;
using KuikuiGameAssistant.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace KuikuiGameAssistant.ViewModels;

public sealed class OverlaySettings : ObservableObject
{
    private OverlayLayoutMode _layoutMode = OverlayLayoutMode.Horizontal;
    private MediaColor _backgroundColor = MediaColor.FromRgb(11, 18, 32);
    private MediaColor _fontColor = MediaColor.FromRgb(253, 230, 138);
    private MediaColor _labelColor = MediaColor.FromRgb(134, 239, 172);
    private double _backgroundOpacity = 0.9;
    private double _fontSize = 18;
    private double _labelFontSize = 11;
    private double _horizontalWidth = 660;
    private double _horizontalHeight = 58;
    private double _verticalWidth = 132;
    private double _verticalHeight = 292;

    public OverlaySettings()
    {
        ApplyMetricSettings(null);
    }

    public ObservableCollection<OverlayMetricConfig> Metrics { get; } = new();

    public OverlayLayoutMode LayoutMode
    {
        get => _layoutMode;
        set
        {
            if (SetProperty(ref _layoutMode, value))
            {
                NotifyLayoutProperties();
            }
        }
    }

    public MediaColor BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (SetProperty(ref _backgroundColor, value))
            {
                OnPropertyChanged(nameof(BackgroundBrush));
            }
        }
    }

    public MediaColor FontColor
    {
        get => _fontColor;
        set
        {
            if (SetProperty(ref _fontColor, value))
            {
                OnPropertyChanged(nameof(FontBrush));
            }
        }
    }

    public MediaColor LabelColor
    {
        get => _labelColor;
        set
        {
            if (SetProperty(ref _labelColor, value))
            {
                OnPropertyChanged(nameof(LabelBrush));
            }
        }
    }

    public double BackgroundOpacity
    {
        get => _backgroundOpacity;
        set
        {
            var next = Math.Clamp(value, 0.2, 1);
            if (SetProperty(ref _backgroundOpacity, next))
            {
                OnPropertyChanged(nameof(BackgroundBrush));
                OnPropertyChanged(nameof(BackgroundOpacityText));
            }
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (SetProperty(ref _fontSize, Math.Clamp(value, 12, 28)))
            {
                OnPropertyChanged(nameof(FontSizeText));
            }
        }
    }

    public double LabelFontSize
    {
        get => _labelFontSize;
        set
        {
            if (SetProperty(ref _labelFontSize, Math.Clamp(value, 9, 18)))
            {
                OnPropertyChanged(nameof(LabelFontSizeText));
            }
        }
    }

    public double HorizontalWidth
    {
        get => _horizontalWidth;
        set
        {
            if (SetProperty(ref _horizontalWidth, Math.Clamp(value, 480, 880)))
            {
                NotifyLayoutProperties();
            }
        }
    }

    public double HorizontalHeight
    {
        get => _horizontalHeight;
        set
        {
            if (SetProperty(ref _horizontalHeight, Math.Clamp(value, 46, 96)))
            {
                NotifyLayoutProperties();
            }
        }
    }

    public double VerticalWidth
    {
        get => _verticalWidth;
        set
        {
            if (SetProperty(ref _verticalWidth, Math.Clamp(value, 108, 220)))
            {
                NotifyLayoutProperties();
            }
        }
    }

    public double VerticalHeight
    {
        get => _verticalHeight;
        set
        {
            if (SetProperty(ref _verticalHeight, Math.Clamp(value, 220, 440)))
            {
                NotifyLayoutProperties();
            }
        }
    }

    public MediaBrush BackgroundBrush
    {
        get
        {
            var color = BackgroundColor;
            color.A = (byte)Math.Round(BackgroundOpacity * 255);
            return new SolidColorBrush(color);
        }
    }

    public MediaBrush FontBrush => new SolidColorBrush(FontColor);

    public MediaBrush LabelBrush => new SolidColorBrush(LabelColor);

    public double CurrentWidth => LayoutMode == OverlayLayoutMode.Horizontal ? HorizontalWidth : VerticalWidth;

    public double CurrentHeight => LayoutMode == OverlayLayoutMode.Horizontal ? HorizontalHeight : VerticalHeight;

    public string BackgroundOpacityText => $"{BackgroundOpacity:P0}";

    public string FontSizeText => $"{FontSize:0}px";

    public string LabelFontSizeText => $"{LabelFontSize:0}px";

    public string HorizontalSizeText => $"{HorizontalWidth:0} x {HorizontalHeight:0}";

    public string VerticalSizeText => $"{VerticalWidth:0} x {VerticalHeight:0}";

    public string CurrentSizeText => $"{CurrentWidth:0} x {CurrentHeight:0}";

    public string PrimaryResolutionText => $"{(int)SystemParameters.PrimaryScreenWidth} x {(int)SystemParameters.PrimaryScreenHeight}";

    public void ApplyMetricSettings(IEnumerable<OverlayMetricConfig>? metrics)
    {
        var persisted = metrics?
            .GroupBy(x => x.Kind)
            .ToDictionary(x => x.Key, x => x.OrderBy(item => item.Order).First());

        Metrics.Clear();
        foreach (var metric in CreateDefaultMetrics())
        {
            if (persisted is not null && persisted.TryGetValue(metric.Kind, out var saved))
            {
                metric.IsEnabled = saved.IsEnabled;
                metric.Order = saved.Order;
            }

            Metrics.Add(metric);
        }
    }

    public IReadOnlyList<OverlayMetricConfig> SnapshotMetrics()
    {
        return Metrics
            .OrderBy(x => x.Order)
            .Select(x => x.Clone())
            .ToArray();
    }

    public void Reset()
    {
        LayoutMode = OverlayLayoutMode.Horizontal;
        BackgroundColor = MediaColor.FromRgb(11, 18, 32);
        FontColor = MediaColor.FromRgb(253, 230, 138);
        LabelColor = MediaColor.FromRgb(134, 239, 172);
        BackgroundOpacity = 0.9;
        FontSize = 18;
        LabelFontSize = 11;
        HorizontalWidth = 660;
        HorizontalHeight = 58;
        VerticalWidth = 132;
        VerticalHeight = 292;
        ApplyMetricSettings(null);
    }

    private void NotifyLayoutProperties()
    {
        OnPropertyChanged(nameof(CurrentWidth));
        OnPropertyChanged(nameof(CurrentHeight));
        OnPropertyChanged(nameof(HorizontalSizeText));
        OnPropertyChanged(nameof(VerticalSizeText));
        OnPropertyChanged(nameof(CurrentSizeText));
    }

    private static IReadOnlyList<OverlayMetricConfig> CreateDefaultMetrics()
    {
        return
        [
            new()
            {
                Kind = OverlayMetricKind.FramesPerSecond,
                Label = "FPS",
                PreviewValue = "144",
                Order = 0,
                IsEnabled = true
            },
            new()
            {
                Kind = OverlayMetricKind.OnePercentLowFps,
                Label = "1% LOW",
                PreviewValue = "118",
                Order = 1,
                IsEnabled = true
            },
            new()
            {
                Kind = OverlayMetricKind.CpuLoad,
                Label = "CPU",
                PreviewValue = "36%",
                Order = 2,
                IsEnabled = true
            },
            new()
            {
                Kind = OverlayMetricKind.GpuLoad,
                Label = "GPU",
                PreviewValue = "82%",
                Order = 3,
                IsEnabled = true
            },
            new()
            {
                Kind = OverlayMetricKind.MemoryLoad,
                Label = "MEM",
                PreviewValue = "61%",
                Order = 4,
                IsEnabled = true
            },
            new()
            {
                Kind = OverlayMetricKind.CpuTemperature,
                Label = "CPU ℃",
                PreviewValue = "72℃",
                Order = 5,
                IsEnabled = true
            },
            new()
            {
                Kind = OverlayMetricKind.GpuTemperature,
                Label = "GPU ℃",
                PreviewValue = "68℃",
                Order = 6,
                IsEnabled = true
            }
        ];
    }
}
