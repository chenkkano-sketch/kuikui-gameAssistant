using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using KuikuiGameAssistant.Models;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaColors = System.Windows.Media.Colors;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace KuikuiGameAssistant.Views;

public sealed class MotionSicknessWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;
    private const uint MonitorDefaultToNearest = 2;
    private const int FullscreenTolerancePx = 2;
    private readonly Canvas _canvas = new();
    private readonly DispatcherTimer _fullscreenVisibilityTimer;
    private MotionSicknessSettings? _settings;
    private bool _isHiddenByFullscreenFilter;

    public MotionSicknessWindow()
    {
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        Content = _canvas;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        _fullscreenVisibilityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _fullscreenVisibilityTimer.Tick += FullscreenVisibilityTimer_Tick;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Loaded += MotionSicknessWindow_Loaded;
        SizeChanged += MotionSicknessWindow_SizeChanged;
    }

    public void Apply(MotionSicknessSettings settings)
    {
        _settings = settings;
        Redraw();
        ConfigureFullscreenVisibilityTimer();
    }

    protected override void OnClosed(EventArgs e)
    {
        Loaded -= MotionSicknessWindow_Loaded;
        SizeChanged -= MotionSicknessWindow_SizeChanged;
        _fullscreenVisibilityTimer.Stop();
        _fullscreenVisibilityTimer.Tick -= FullscreenVisibilityTimer_Tick;
        base.OnClosed(e);
    }

    private void MotionSicknessWindow_Loaded(object sender, RoutedEventArgs e) => Redraw();

    private void MotionSicknessWindow_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        if (_settings is not { } settings)
        {
            return;
        }

        var color = ParseColor(settings.BarColorHex, MediaColors.White);
        var alpha = (byte)Math.Round(Math.Clamp(settings.Opacity, 5, 100) / 100d * 255);
        var brush = new SolidColorBrush(MediaColor.FromArgb(alpha, color.R, color.G, color.B));
        var edgeThickness = Math.Clamp(settings.EdgeThickness, 1, 30);
        var crosshairThickness = Math.Clamp(settings.CrosshairThickness, 1, 20);
        var screenWidth = ActualWidth > 0 ? ActualWidth : Width;
        var screenHeight = ActualHeight > 0 ? ActualHeight : Height;
        var edgeLength = Math.Clamp(settings.EdgeLength, 20, Math.Max(20, Math.Min(screenWidth, screenHeight)));
        var edgeDistance = Math.Clamp(settings.EdgeDistance, 0, 220);
        var crosshairSize = Math.Clamp(settings.CrosshairSize, 4, 240);
        var crosshairGap = Math.Clamp(settings.CrosshairGap, 0, 100);
        var centerX = screenWidth / 2;
        var centerY = screenHeight / 2;

        _canvas.Children.Clear();

        if (settings.TunnelVisionEnabled)
        {
            AddTunnelVision(settings, screenWidth, screenHeight);
        }

        if (settings.ShowTopBar)
        {
            AddHorizontalBar(centerX - edgeLength / 2, edgeDistance, edgeLength, edgeThickness, brush);
        }

        if (settings.ShowBottomBar)
        {
            AddHorizontalBar(centerX - edgeLength / 2, screenHeight - edgeDistance - edgeThickness, edgeLength, edgeThickness, brush);
        }

        if (settings.ShowLeftBar)
        {
            AddVerticalBar(edgeDistance, centerY - edgeLength / 2, edgeThickness, edgeLength, brush);
        }

        if (settings.ShowRightBar)
        {
            AddVerticalBar(screenWidth - edgeDistance - edgeThickness, centerY - edgeLength / 2, edgeThickness, edgeLength, brush);
        }

        if (settings.ShowCenterCrosshair)
        {
            AddCrosshair(settings.CrosshairStyle, centerX, centerY, crosshairSize, crosshairThickness, crosshairGap, brush);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, exStyle | WsExTransparent | WsExToolWindow | WsExNoActivate);
        ConfigureFullscreenVisibilityTimer();
    }

    private void FullscreenVisibilityTimer_Tick(object? sender, EventArgs e) => UpdateFullscreenVisibility();

    private void ConfigureFullscreenVisibilityTimer()
    {
        if (_settings is null || new WindowInteropHelper(this).Handle == IntPtr.Zero)
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
        if (_settings is null)
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
        Show();
        Topmost = true;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var exStyle = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, exStyle | WsExTransparent | WsExToolWindow | WsExNoActivate);
        }
    }

    private void AddHorizontalBar(double left, double top, double width, double height, MediaBrush brush)
    {
        var bar = CreateBar(width, height, brush);
        Canvas.SetLeft(bar, left);
        Canvas.SetTop(bar, top);
        _canvas.Children.Add(bar);
    }

    private void AddTunnelVision(MotionSicknessSettings settings, double screenWidth, double screenHeight)
    {
        var color = ParseColor(settings.TunnelColorHex, MediaColors.Black);
        var alpha = (byte)Math.Round(Math.Clamp(settings.TunnelIntensity, 0, 80) / 100d * 255);
        var edgeColor = MediaColor.FromArgb(alpha, color.R, color.G, color.B);
        var innerOffset = Math.Clamp(1 - settings.TunnelFeather / 100d, 0.05, 0.90);
        var overlay = new WpfRectangle
        {
            Width = screenWidth,
            Height = screenHeight,
            Fill = new RadialGradientBrush
            {
                Center = new WpfPoint(0.5, 0.5),
                GradientOrigin = new WpfPoint(0.5, 0.5),
                RadiusX = 0.78,
                RadiusY = 0.78,
                GradientStops =
                {
                    new GradientStop(MediaColors.Transparent, 0),
                    new GradientStop(MediaColors.Transparent, innerOffset),
                    new GradientStop(edgeColor, 1)
                }
            },
            IsHitTestVisible = false
        };
        Canvas.SetLeft(overlay, 0);
        Canvas.SetTop(overlay, 0);
        _canvas.Children.Add(overlay);
    }

    private void AddVerticalBar(double left, double top, double width, double height, MediaBrush brush)
    {
        var bar = CreateBar(width, height, brush);
        Canvas.SetLeft(bar, left);
        Canvas.SetTop(bar, top);
        _canvas.Children.Add(bar);
    }

    private void AddCrosshair(
        MotionCrosshairStyle style,
        double centerX,
        double centerY,
        double size,
        double thickness,
        double gap,
        MediaBrush brush)
    {
        switch (style)
        {
            case MotionCrosshairStyle.Dot:
                AddEllipse(centerX - size / 2, centerY - size / 2, size, size, brush, null, 0);
                break;
            case MotionCrosshairStyle.Rectangle:
                AddOutlinedRectangle(centerX - size / 2, centerY - size / 2, size, size, brush, thickness);
                break;
            case MotionCrosshairStyle.Circle:
                AddEllipse(centerX - size / 2, centerY - size / 2, size, size, null, brush, thickness);
                break;
            case MotionCrosshairStyle.SmallCross:
                var smallArm = Math.Max(3, size / 2);
                var smallGap = Math.Min(gap, size / 2);
                AddHorizontalBar(centerX - smallGap - smallArm, centerY - thickness / 2, smallArm, thickness, brush);
                AddHorizontalBar(centerX + smallGap, centerY - thickness / 2, smallArm, thickness, brush);
                AddVerticalBar(centerX - thickness / 2, centerY - smallGap - smallArm, thickness, smallArm, brush);
                AddVerticalBar(centerX - thickness / 2, centerY + smallGap, thickness, smallArm, brush);
                break;
            default:
                var arm = Math.Max(6, size);
                AddHorizontalBar(centerX - gap - arm, centerY - thickness / 2, arm, thickness, brush);
                AddHorizontalBar(centerX + gap, centerY - thickness / 2, arm, thickness, brush);
                AddVerticalBar(centerX - thickness / 2, centerY - gap - arm, thickness, arm, brush);
                AddVerticalBar(centerX - thickness / 2, centerY + gap, thickness, arm, brush);
                break;
        }
    }

    private void AddEllipse(double left, double top, double width, double height, MediaBrush? fill, MediaBrush? stroke, double strokeThickness)
    {
        var ellipse = new WpfEllipse
        {
            Width = width,
            Height = height,
            Fill = fill ?? MediaBrushes.Transparent,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        Canvas.SetLeft(ellipse, left);
        Canvas.SetTop(ellipse, top);
        _canvas.Children.Add(ellipse);
    }

    private void AddOutlinedRectangle(double left, double top, double width, double height, MediaBrush brush, double strokeThickness)
    {
        var rectangle = new WpfRectangle
        {
            Width = width,
            Height = height,
            RadiusX = Math.Min(4, width / 4),
            RadiusY = Math.Min(4, height / 4),
            Fill = MediaBrushes.Transparent,
            Stroke = brush,
            StrokeThickness = strokeThickness,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        Canvas.SetLeft(rectangle, left);
        Canvas.SetTop(rectangle, top);
        _canvas.Children.Add(rectangle);
    }

    private static WpfRectangle CreateBar(double width, double height, MediaBrush brush)
    {
        return new WpfRectangle
        {
            Width = width,
            Height = height,
            RadiusX = Math.Min(width, height) / 2,
            RadiusY = Math.Min(width, height) / 2,
            Fill = brush,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
    }

    private static MediaColor ParseColor(string? value, MediaColor fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return (MediaColor)MediaColorConverter.ConvertFromString(value)!;
        }
        catch
        {
            return fallback;
        }
    }

    private bool IsForegroundFullscreenWindow()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        var ownHandle = new WindowInteropHelper(this).Handle;
        if (foreground == ownHandle)
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
        if (!GetMonitorInfo(monitor, ref monitorInfo) || !TryGetWindowBounds(foreground, out var bounds))
        {
            return false;
        }

        return CoversMonitor(bounds, monitorInfo.Monitor);
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
