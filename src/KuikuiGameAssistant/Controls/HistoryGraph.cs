using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using KuikuiGameAssistant.Services;

namespace KuikuiGameAssistant.Controls;

public sealed class HistoryGraph : FrameworkElement
{
    public static readonly DependencyProperty CpuValuesProperty =
        DependencyProperty.Register(nameof(CpuValues), typeof(IEnumerable), typeof(HistoryGraph),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty GpuValuesProperty =
        DependencyProperty.Register(nameof(GpuValues), typeof(IEnumerable), typeof(HistoryGraph),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty MemoryValuesProperty =
        DependencyProperty.Register(nameof(MemoryValues), typeof(IEnumerable), typeof(HistoryGraph),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    private bool _isThemeSubscribed;

    public HistoryGraph()
    {
        Loaded += HistoryGraph_Loaded;
        Unloaded += HistoryGraph_Unloaded;
    }

    public IEnumerable? CpuValues
    {
        get => (IEnumerable?)GetValue(CpuValuesProperty);
        set => SetValue(CpuValuesProperty, value);
    }

    public IEnumerable? GpuValues
    {
        get => (IEnumerable?)GetValue(GpuValuesProperty);
        set => SetValue(GpuValuesProperty, value);
    }

    public IEnumerable? MemoryValues
    {
        get => (IEnumerable?)GetValue(MemoryValuesProperty);
        set => SetValue(MemoryValuesProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRoundedRectangle(System.Windows.Media.Brushes.Transparent, null, rect, 8, 8);
        var gridPen = CreatePen("GraphGridBrush", "#FFE5E5E5", 1);
        var cpuPen = CreatePen("GraphCpuBrush", "#FF0067C0", 2);
        var gpuPen = CreatePen("GraphGpuBrush", "#FF8E4EC6", 2);
        var memoryPen = CreatePen("GraphMemoryBrush", "#FF0E7A0D", 2);

        for (var i = 1; i < 4; i++)
        {
            var y = rect.Height * i / 4;
            drawingContext.DrawLine(gridPen, new System.Windows.Point(0, y), new System.Windows.Point(rect.Width, y));
        }

        DrawSeries(drawingContext, rect, MemoryValues, memoryPen);
        DrawSeries(drawingContext, rect, GpuValues, gpuPen);
        DrawSeries(drawingContext, rect, CpuValues, cpuPen);
    }

    private static void DrawSeries(DrawingContext context, Rect rect, IEnumerable? source, System.Windows.Media.Pen pen)
    {
        if (source is null)
        {
            return;
        }

        var values = source.Cast<double>().ToArray();
        if (values.Length < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            for (var i = 0; i < values.Length; i++)
            {
                var x = values.Length == 1 ? 0 : rect.Width * i / (values.Length - 1);
                var y = rect.Height - rect.Height * Math.Clamp(values[i], 0, 100) / 100;
                var point = new System.Windows.Point(x, y);

                if (i == 0)
                {
                    stream.BeginFigure(point, false, false);
                }
                else
                {
                    stream.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    private static System.Windows.Media.Pen CreatePen(string resourceKey, string fallbackColor, double thickness)
    {
        var brush = BrushFromResource(resourceKey, fallbackColor);
        var pen = new System.Windows.Media.Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        return pen;
    }

    private static SolidColorBrush BrushFromResource(string resourceKey, string fallbackColor)
    {
        if (System.Windows.Application.Current?.Resources[resourceKey] is SolidColorBrush brush)
        {
            return brush;
        }

        if (System.Windows.Application.Current?.Resources[resourceKey] is System.Windows.Media.Color color)
        {
            return new SolidColorBrush(color);
        }

        return (SolidColorBrush)new BrushConverter().ConvertFromString(fallbackColor)!;
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not HistoryGraph graph)
        {
            return;
        }

        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= graph.CollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += graph.CollectionChanged;
        }

        graph.InvalidateVisual();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void HistoryGraph_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isThemeSubscribed)
        {
            return;
        }

        AppThemeService.ThemeApplied += AppThemeService_ThemeApplied;
        _isThemeSubscribed = true;
    }

    private void HistoryGraph_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!_isThemeSubscribed)
        {
            return;
        }

        AppThemeService.ThemeApplied -= AppThemeService_ThemeApplied;
        _isThemeSubscribed = false;
    }

    private void AppThemeService_ThemeApplied(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            InvalidateVisual();
            return;
        }

        _ = Dispatcher.BeginInvoke(InvalidateVisual);
    }
}
