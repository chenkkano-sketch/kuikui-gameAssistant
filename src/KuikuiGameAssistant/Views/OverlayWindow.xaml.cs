using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Services;
using KuikuiGameAssistant.ViewModels;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace KuikuiGameAssistant.Views;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WmNcHitTest = 0x0084;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;
    private const uint MonitorDefaultToNearest = 2;
    private const int FullscreenTolerancePx = 2;
    private const double PlacementMargin = 16;
    private static readonly IntPtr HtTransparent = new(-1);
    private readonly TelemetryService _telemetry;
    private readonly OverlaySettings _settings;
    private readonly DispatcherTimer _fullscreenVisibilityTimer;
    private HwndSource? _source;
    private bool _isHiddenByFullscreenFilter;
    private bool _isClosing;

    public OverlayWindow(TelemetryService telemetry, OverlaySettings settings)
    {
        InitializeComponent();
        _telemetry = telemetry;
        _settings = settings;
        _fullscreenVisibilityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _fullscreenVisibilityTimer.Tick += (_, _) => UpdateFullscreenVisibility();
        DataContext = this;

        RebuildMetrics();
        _telemetry.SnapshotUpdated += Telemetry_SnapshotUpdated;
        _settings.PropertyChanged += Settings_PropertyChanged;
        _settings.Metrics.CollectionChanged += Metrics_CollectionChanged;

        Apply(_telemetry.Latest);
        ApplySettings();
    }

    public ObservableCollection<OverlayMetricDisplayItem> Metrics { get; } = new();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _source?.AddHook(WndProc);
        ApplyNativeWindowStyles();
        PositionWindow();
        ConfigureFullscreenVisibilityTimer();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _telemetry.SnapshotUpdated -= Telemetry_SnapshotUpdated;
        _settings.PropertyChanged -= Settings_PropertyChanged;
        _settings.Metrics.CollectionChanged -= Metrics_CollectionChanged;
        _fullscreenVisibilityTimer.Stop();
        _source?.RemoveHook(WndProc);
        ClearMetrics();
        base.OnClosed(e);
    }

    private void Telemetry_SnapshotUpdated(object? sender, RealtimeSnapshot e)
    {
        Dispatcher.Invoke(() => Apply(e));
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(() => ApplySettings(e.PropertyName));
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
        if (_settings.IsClickThroughEnabled)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void SetLayoutMode(OverlayLayoutMode layoutMode)
    {
        HorizontalPanel.Visibility = layoutMode == OverlayLayoutMode.Horizontal ? Visibility.Visible : Visibility.Collapsed;
        VerticalPanel.Visibility = layoutMode == OverlayLayoutMode.Vertical ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplySettings(string? changedPropertyName = null)
    {
        SetLayoutMode(_settings.LayoutMode);
        Width = _settings.CurrentWidth;
        Height = _settings.CurrentHeight;
        RootBorder.Padding = _settings.LayoutMode == OverlayLayoutMode.Horizontal
            ? new Thickness(6, 4, 6, 4)
            : new Thickness(6, 5, 6, 5);
        RootBorder.Background = _settings.BackgroundBrush;
        RootBorder.IsHitTestVisible = !_settings.IsClickThroughEnabled;
        ApplyNativeWindowStyles();
        ConfigureFullscreenVisibilityTimer();
        if (ShouldReposition(changedPropertyName))
        {
            PositionWindow();
        }

        foreach (var metric in Metrics)
        {
            metric.LabelBrush = _settings.LabelBrush;
            metric.ValueBrush = _settings.FontBrush;
            metric.LabelFontSize = _settings.LabelFontSize;
            metric.ValueFontSize = _settings.FontSize;
        }
    }

    private static bool ShouldReposition(string? propertyName)
    {
        return propertyName is null
            or nameof(OverlaySettings.LayoutMode)
            or nameof(OverlaySettings.HorizontalWidth)
            or nameof(OverlaySettings.HorizontalHeight)
            or nameof(OverlaySettings.VerticalWidth)
            or nameof(OverlaySettings.VerticalHeight)
            or nameof(OverlaySettings.Placement);
    }

    private void PositionWindow()
    {
        var bounds = GetPlacementBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var width = Width > 0 ? Width : _settings.CurrentWidth;
        var height = Height > 0 ? Height : _settings.CurrentHeight;
        var horizontalCenter = bounds.Left + (bounds.Width - width) / 2;
        var verticalCenter = bounds.Top + (bounds.Height - height) / 2;

        var left = _settings.Placement switch
        {
            OverlayPlacement.TopLeft or OverlayPlacement.Left or OverlayPlacement.BottomLeft => bounds.Left + PlacementMargin,
            OverlayPlacement.TopRight or OverlayPlacement.Right or OverlayPlacement.BottomRight => bounds.Right - width - PlacementMargin,
            _ => horizontalCenter
        };

        var top = _settings.Placement switch
        {
            OverlayPlacement.TopLeft or OverlayPlacement.Top or OverlayPlacement.TopRight => bounds.Top + PlacementMargin,
            OverlayPlacement.BottomLeft or OverlayPlacement.Bottom or OverlayPlacement.BottomRight => bounds.Bottom - height - PlacementMargin,
            _ => verticalCenter
        };

        Left = ClampToBounds(left, bounds.Left, bounds.Right - width);
        Top = ClampToBounds(top, bounds.Top, bounds.Bottom - height);
    }

    private System.Windows.Rect GetPlacementBounds()
    {
        if (TryGetForegroundFullscreenBounds(out var fullscreenBounds))
        {
            return fullscreenBounds;
        }

        if (TryGetOverlayMonitorWorkArea(out var monitorWorkArea))
        {
            return monitorWorkArea;
        }

        return SystemParameters.WorkArea;
    }

    private bool TryGetOverlayMonitorWorkArea(out System.Windows.Rect bounds)
    {
        bounds = default;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo();
        monitorInfo.Size = Marshal.SizeOf<MonitorInfo>();
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        bounds = NativeRectToDeviceIndependentRect(monitorInfo.WorkArea);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private System.Windows.Rect NativeRectToDeviceIndependentRect(NativeRect rect)
    {
        var topLeft = PointFromDevice(rect.Left, rect.Top);
        var bottomRight = PointFromDevice(rect.Right, rect.Bottom);
        return new System.Windows.Rect(topLeft, bottomRight);
    }

    private System.Windows.Point PointFromDevice(int x, int y)
    {
        var point = new System.Windows.Point(x, y);
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget is null
            ? point
            : source.CompositionTarget.TransformFromDevice.Transform(point);
    }

    private static double ClampToBounds(double value, double minimum, double maximum)
    {
        return maximum < minimum ? minimum : Math.Clamp(value, minimum, maximum);
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

    private void ConfigureFullscreenVisibilityTimer()
    {
        if (_source is null || _isClosing)
        {
            return;
        }

        if (_settings.OnlyShowInFullscreen)
        {
            if (!_fullscreenVisibilityTimer.IsEnabled)
            {
                _fullscreenVisibilityTimer.Start();
            }

            UpdateFullscreenVisibility();
            return;
        }

        _fullscreenVisibilityTimer.Stop();
        ShowAfterFullscreenFilter();
    }

    private void UpdateFullscreenVisibility()
    {
        if (_isClosing)
        {
            return;
        }

        if (!_settings.OnlyShowInFullscreen || IsForegroundFullscreenWindow())
        {
            ShowAfterFullscreenFilter();
            return;
        }

        HideForFullscreenFilter();
    }

    private void HideForFullscreenFilter()
    {
        _isHiddenByFullscreenFilter = true;
        if (IsVisible)
        {
            Hide();
        }
    }

    private void ShowAfterFullscreenFilter()
    {
        if (!_isHiddenByFullscreenFilter)
        {
            return;
        }

        _isHiddenByFullscreenFilter = false;
        PositionWindow();
        Show();
        Topmost = true;
        ApplyNativeWindowStyles();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmNcHitTest && _settings.IsClickThroughEnabled)
        {
            handled = true;
            return HtTransparent;
        }

        return IntPtr.Zero;
    }

    private void ApplyNativeWindowStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var exStyle = GetWindowLong(hwnd, GwlExStyle);
        var nextStyle = exStyle | WsExToolWindow | WsExNoActivate;
        nextStyle = _settings.IsClickThroughEnabled
            ? nextStyle | WsExTransparent
            : nextStyle & ~WsExTransparent;

        if (nextStyle != exStyle)
        {
            _ = SetWindowLong(hwnd, GwlExStyle, nextStyle);
        }
    }

    private bool IsForegroundFullscreenWindow()
    {
        return TryGetForegroundFullscreenBounds(out _);
    }

    private bool TryGetForegroundFullscreenBounds(out System.Windows.Rect fullscreenBounds)
    {
        fullscreenBounds = default;
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        var overlayHandle = new WindowInteropHelper(this).Handle;
        if (foreground == overlayHandle)
        {
            return false;
        }

        if (!IsWindowVisible(foreground) || IsIconic(foreground) || IsShellWindow(foreground) || IsWindowCloaked(foreground))
        {
            return false;
        }

        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo();
        monitorInfo.Size = Marshal.SizeOf<MonitorInfo>();
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        if (!TryGetWindowBounds(foreground, out var bounds))
        {
            return false;
        }

        if (!CoversMonitor(bounds, monitorInfo.Monitor))
        {
            return false;
        }

        fullscreenBounds = NativeRectToDeviceIndependentRect(bounds);
        return fullscreenBounds.Width > 0 && fullscreenBounds.Height > 0;
    }

    private static bool TryGetWindowBounds(IntPtr hwnd, out NativeRect bounds)
    {
        if (TryGetExtendedFrameBounds(hwnd, out bounds))
        {
            return true;
        }

        return GetWindowRect(hwnd, out bounds);
    }

    private static bool TryGetExtendedFrameBounds(IntPtr hwnd, out NativeRect bounds)
    {
        try
        {
            return DwmGetWindowAttributeRect(
                hwnd,
                DwmwaExtendedFrameBounds,
                out bounds,
                Marshal.SizeOf<NativeRect>()) == 0
                && bounds.Width > 0
                && bounds.Height > 0;
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        bounds = default;
        return false;
    }

    private static bool IsWindowCloaked(IntPtr hwnd)
    {
        try
        {
            return DwmGetWindowAttributeInt(
                hwnd,
                DwmwaCloaked,
                out var cloaked,
                Marshal.SizeOf<int>()) == 0
                && cloaked != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static bool CoversMonitor(NativeRect window, NativeRect monitor)
    {
        return window.Left <= monitor.Left + FullscreenTolerancePx
               && window.Top <= monitor.Top + FullscreenTolerancePx
               && window.Right >= monitor.Right - FullscreenTolerancePx
               && window.Bottom >= monitor.Bottom - FullscreenTolerancePx;
    }

    private static bool IsShellWindow(IntPtr hwnd)
    {
        var className = new StringBuilder(128);
        if (GetClassName(hwnd, className, className.Capacity) <= 0)
        {
            return false;
        }

        return className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd";
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeRect(IntPtr hwnd, int dwAttribute, out NativeRect pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeInt(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;

        public int Height => Bottom - Top;
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
