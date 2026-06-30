using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Services;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaColors = System.Windows.Media.Colors;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfUserControl = System.Windows.Controls.UserControl;
using Forms = System.Windows.Forms;

namespace KuikuiGameAssistant.Views;

public partial class MotionSicknessPage : WpfUserControl
{
    private readonly MotionSicknessSettings _settings;
    private readonly MotionSicknessService _service;
    private bool _syncingCrosshairStyle;

    public MotionSicknessPage(AppSettings appSettings, MotionSicknessService service)
    {
        InitializeComponent();
        _settings = appSettings.MotionSickness;
        _service = service;
        DataContext = _settings;
        CrosshairStyleCombo.ItemsSource = CrosshairStyleOption.CreateDefaults();
        _settings.PropertyChanged += Settings_PropertyChanged;
        ApplySettings();
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplySettings();
        App.Settings.Save(App.OverlaySettings);
        ToastService.ShowSettingsSaved();
    }

    private void GentlePreset_Click(object sender, RoutedEventArgs e) => _settings.ApplyGentlePreset();

    private void BalancedPreset_Click(object sender, RoutedEventArgs e) => _settings.ApplyBalancedPreset();

    private void HighContrastPreset_Click(object sender, RoutedEventArgs e) => _settings.ApplyHighContrastPreset();

    private void ColorWhite_Click(object sender, RoutedEventArgs e) => _settings.BarColorHex = "#FFFFFF";

    private void ColorBlack_Click(object sender, RoutedEventArgs e) => _settings.BarColorHex = "#000000";

    private void ColorCyan_Click(object sender, RoutedEventArgs e) => _settings.BarColorHex = "#70E7FF";

    private void ColorYellow_Click(object sender, RoutedEventArgs e) => _settings.BarColorHex = "#FFD84D";

    private void ColorGreen_Click(object sender, RoutedEventArgs e) => _settings.BarColorHex = "#6CFF9D";

    private void ColorRed_Click(object sender, RoutedEventArgs e) => _settings.BarColorHex = "#FF4D4D";

    private void TunnelColorWhite_Click(object sender, RoutedEventArgs e) => _settings.TunnelColorHex = "#FFFFFF";

    private void TunnelColorBlack_Click(object sender, RoutedEventArgs e) => _settings.TunnelColorHex = "#000000";

    private void TunnelColorGreen_Click(object sender, RoutedEventArgs e) => _settings.TunnelColorHex = "#6CFF9D";

    private void TunnelColorYellow_Click(object sender, RoutedEventArgs e) => _settings.TunnelColorHex = "#FFD84D";

    private void TunnelColorRed_Click(object sender, RoutedEventArgs e) => _settings.TunnelColorHex = "#FF4D4D";

    private void PickBarColor_Click(object sender, RoutedEventArgs e)
    {
        if (TryPickColor(_settings.BarColorHex, out var color))
        {
            _settings.BarColorHex = color;
        }
    }

    private void PickTunnelColor_Click(object sender, RoutedEventArgs e)
    {
        if (TryPickColor(_settings.TunnelColorHex, out var color))
        {
            _settings.TunnelColorHex = color;
        }
    }

    private void CrosshairStyleCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_syncingCrosshairStyle)
        {
            return;
        }

        if (CrosshairStyleCombo.SelectedItem is CrosshairStyleOption option)
        {
            _settings.CrosshairStyle = option.Style;
        }
    }

    private static bool TryPickColor(string currentHex, out string hexColor)
    {
        hexColor = currentHex;
        var current = ParseColor(currentHex, MediaColors.White);
        using var dialog = new Forms.ColorDialog
        {
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return false;
        }

        hexColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        return true;
    }

    private void ApplySettings()
    {
        var barColor = ParseColor(_settings.BarColorHex, MediaColors.White);
        var tunnelColor = ParseColor(_settings.TunnelColorHex, MediaColors.Black);
        BarColorPreview.Background = new SolidColorBrush(barColor);
        TunnelColorPreview.Background = new SolidColorBrush(tunnelColor);
        SyncCrosshairStyleCombo();
        DrawPreview(barColor, tunnelColor);
        _service.Apply(_settings);
        StatusText.Text = _service.StatusText;
    }

    private void SyncCrosshairStyleCombo()
    {
        _syncingCrosshairStyle = true;
        CrosshairStyleCombo.SelectedItem = CrosshairStyleCombo.Items
            .OfType<CrosshairStyleOption>()
            .FirstOrDefault(x => x.Style == _settings.CrosshairStyle);
        _syncingCrosshairStyle = false;
    }

    private void DrawPreview(MediaColor barColor, MediaColor tunnelColor)
    {
        const double previewWidth = 390;
        const double previewHeight = 220;
        var scale = previewHeight / 1080d;
        var edgeThickness = Math.Max(1, _settings.EdgeThickness * scale * 4.2);
        var crosshairThickness = Math.Max(1, _settings.CrosshairThickness * scale * 4.2);
        var edgeLength = Math.Clamp(_settings.EdgeLength * scale * 1.6, 16, 230);
        var edgeDistance = Math.Clamp(_settings.EdgeDistance * scale * 2.6, 0, 52);
        var crosshairSize = Math.Clamp(_settings.CrosshairSize * scale * 3.2, 3, 120);
        var crosshairGap = _settings.CrosshairGap * scale * 2.4;
        var centerX = previewWidth / 2;
        var centerY = previewHeight / 2;
        var brush = new SolidColorBrush(MediaColor.FromArgb(
            (byte)Math.Round(Math.Clamp(_settings.Opacity, 5, 100) / 100d * 255),
            barColor.R,
            barColor.G,
            barColor.B));

        PreviewCanvas.Children.Clear();

        if (_settings.TunnelVisionEnabled)
        {
            var alpha = (byte)Math.Round(Math.Clamp(_settings.TunnelIntensity, 0, 80) / 100d * 255);
            var edgeColor = MediaColor.FromArgb(alpha, tunnelColor.R, tunnelColor.G, tunnelColor.B);
            var innerOffset = Math.Clamp(1 - _settings.TunnelFeather / 100d, 0.05, 0.90);
            PreviewCanvas.Children.Add(new WpfRectangle
            {
                Width = previewWidth,
                Height = previewHeight,
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
                }
            });
        }

        if (_settings.ShowTopBar)
        {
            AddPreviewBar(centerX - edgeLength / 2, edgeDistance, edgeLength, edgeThickness, brush);
        }

        if (_settings.ShowBottomBar)
        {
            AddPreviewBar(centerX - edgeLength / 2, previewHeight - edgeDistance - edgeThickness, edgeLength, edgeThickness, brush);
        }

        if (_settings.ShowLeftBar)
        {
            AddPreviewBar(edgeDistance, centerY - edgeLength / 2, edgeThickness, edgeLength, brush);
        }

        if (_settings.ShowRightBar)
        {
            AddPreviewBar(previewWidth - edgeDistance - edgeThickness, centerY - edgeLength / 2, edgeThickness, edgeLength, brush);
        }

        if (_settings.ShowCenterCrosshair)
        {
            AddPreviewCrosshair(_settings.CrosshairStyle, centerX, centerY, crosshairSize, crosshairThickness, crosshairGap, brush);
        }
    }

    private void AddPreviewBar(double left, double top, double width, double height, MediaBrush brush)
    {
        var bar = new WpfRectangle
        {
            Width = width,
            Height = height,
            RadiusX = Math.Min(width, height) / 2,
            RadiusY = Math.Min(width, height) / 2,
            Fill = brush,
            SnapsToDevicePixels = true
        };
        System.Windows.Controls.Canvas.SetLeft(bar, left);
        System.Windows.Controls.Canvas.SetTop(bar, top);
        PreviewCanvas.Children.Add(bar);
    }

    private void AddPreviewCrosshair(
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
                AddPreviewEllipse(centerX - size / 2, centerY - size / 2, size, size, brush, null, 0);
                break;
            case MotionCrosshairStyle.Rectangle:
                AddPreviewRectangle(centerX - size / 2, centerY - size / 2, size, size, brush, thickness);
                break;
            case MotionCrosshairStyle.Circle:
                AddPreviewEllipse(centerX - size / 2, centerY - size / 2, size, size, null, brush, thickness);
                break;
            case MotionCrosshairStyle.SmallCross:
                var smallArm = Math.Max(3, size / 2);
                var smallGap = Math.Min(gap, size / 2);
                AddPreviewBar(centerX - smallGap - smallArm, centerY - thickness / 2, smallArm, thickness, brush);
                AddPreviewBar(centerX + smallGap, centerY - thickness / 2, smallArm, thickness, brush);
                AddPreviewBar(centerX - thickness / 2, centerY - smallGap - smallArm, thickness, smallArm, brush);
                AddPreviewBar(centerX - thickness / 2, centerY + smallGap, thickness, smallArm, brush);
                break;
            default:
                var arm = Math.Max(6, size);
                AddPreviewBar(centerX - gap - arm, centerY - thickness / 2, arm, thickness, brush);
                AddPreviewBar(centerX + gap, centerY - thickness / 2, arm, thickness, brush);
                AddPreviewBar(centerX - thickness / 2, centerY - gap - arm, thickness, arm, brush);
                AddPreviewBar(centerX - thickness / 2, centerY + gap, thickness, arm, brush);
                break;
        }
    }

    private void AddPreviewEllipse(double left, double top, double width, double height, MediaBrush? fill, MediaBrush? stroke, double strokeThickness)
    {
        var ellipse = new WpfEllipse
        {
            Width = width,
            Height = height,
            Fill = fill ?? MediaBrushes.Transparent,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            SnapsToDevicePixels = true
        };
        System.Windows.Controls.Canvas.SetLeft(ellipse, left);
        System.Windows.Controls.Canvas.SetTop(ellipse, top);
        PreviewCanvas.Children.Add(ellipse);
    }

    private void AddPreviewRectangle(double left, double top, double width, double height, MediaBrush brush, double strokeThickness)
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
            SnapsToDevicePixels = true
        };
        System.Windows.Controls.Canvas.SetLeft(rectangle, left);
        System.Windows.Controls.Canvas.SetTop(rectangle, top);
        PreviewCanvas.Children.Add(rectangle);
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

    private sealed class CrosshairStyleOption
    {
        public string Name { get; init; } = string.Empty;
        public MotionCrosshairStyle Style { get; init; }

        public static IReadOnlyList<CrosshairStyleOption> CreateDefaults()
        {
            return new[]
            {
                new CrosshairStyleOption { Name = "经典十字", Style = MotionCrosshairStyle.Classic },
                new CrosshairStyleOption { Name = "小十字", Style = MotionCrosshairStyle.SmallCross },
                new CrosshairStyleOption { Name = "点状", Style = MotionCrosshairStyle.Dot },
                new CrosshairStyleOption { Name = "矩形", Style = MotionCrosshairStyle.Rectangle },
                new CrosshairStyleOption { Name = "圆形", Style = MotionCrosshairStyle.Circle }
            };
        }
    }
}
