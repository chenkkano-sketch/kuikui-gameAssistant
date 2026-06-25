using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using KuikuiGameAssistant.Models;
using Forms = System.Windows.Forms;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaColors = System.Windows.Media.Colors;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace KuikuiGameAssistant.Views;

public sealed class GameVignetteWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private readonly WpfRectangle _overlay = new();

    public GameVignetteWindow()
    {
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        Content = _overlay;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStyle = WindowStyle.None;

        var bounds = Forms.SystemInformation.VirtualScreen;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    public void Apply(GameFilterSettings settings)
    {
        var color = ParseColor(settings.VignetteColorHex, MediaColors.Black);
        var alpha = (byte)Math.Round(Math.Clamp(settings.VignetteIntensity, 0, 100) / 100d * 255);
        var edgeColor = MediaColor.FromArgb(alpha, color.R, color.G, color.B);
        var innerOffset = Math.Clamp(1 - settings.VignetteFeather / 100d, 0.05, 0.92);

        _overlay.Fill = new RadialGradientBrush
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
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, exStyle | WsExTransparent | WsExToolWindow | WsExNoActivate);
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

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
