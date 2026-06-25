using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Services;
using KuikuiGameAssistant.ViewModels;

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
        _telemetry.SnapshotUpdated += Telemetry_SnapshotUpdated;
        _settings.PropertyChanged += Settings_PropertyChanged;
        Apply(_telemetry.Latest);
        ApplySettings();
    }

    protected override void OnClosed(EventArgs e)
    {
        _telemetry.SnapshotUpdated -= Telemetry_SnapshotUpdated;
        _settings.PropertyChanged -= Settings_PropertyChanged;
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

    private void Apply(RealtimeSnapshot snapshot)
    {
        var fps = snapshot.FramesPerSecond is null ? "--" : $"{snapshot.FramesPerSecond:0}";
        var cpu = snapshot.CpuLoad is null ? "--%" : $"{snapshot.CpuLoad:0}%";
        var gpu = snapshot.GpuLoad is null ? "--%" : $"{snapshot.GpuLoad:0}%";
        var memory = snapshot.MemoryLoad is null ? "--%" : $"{snapshot.MemoryLoad:0}%";
        var cpuTemperature = FormatTemperature(snapshot.CpuTemperature);
        var gpuTemperature = FormatTemperature(snapshot.GpuTemperature);

        HFpsText.Text = VFpsText.Text = fps;
        HCpuText.Text = VCpuText.Text = cpu;
        HGpuText.Text = VGpuText.Text = gpu;
        HMemoryText.Text = VMemoryText.Text = memory;
        HCpuTempText.Text = VCpuTempText.Text = cpuTemperature;
        HGpuTempText.Text = VGpuTempText.Text = gpuTemperature;
    }

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

        foreach (var label in LabelTextBlocks())
        {
            label.Foreground = _settings.LabelBrush;
            label.FontSize = _settings.LabelFontSize;
        }

        foreach (var value in ValueTextBlocks())
        {
            value.Foreground = _settings.FontBrush;
            value.FontSize = _settings.FontSize;
        }
    }

    private IEnumerable<TextBlock> LabelTextBlocks()
    {
        yield return HFpsLabel;
        yield return HCpuLabel;
        yield return HGpuLabel;
        yield return HMemoryLabel;
        yield return HCpuTempLabel;
        yield return HGpuTempLabel;
        yield return VFpsLabel;
        yield return VCpuLabel;
        yield return VGpuLabel;
        yield return VMemoryLabel;
        yield return VCpuTempLabel;
        yield return VGpuTempLabel;
    }

    private IEnumerable<TextBlock> ValueTextBlocks()
    {
        yield return HFpsText;
        yield return HCpuText;
        yield return HGpuText;
        yield return HMemoryText;
        yield return HCpuTempText;
        yield return HGpuTempText;
        yield return VFpsText;
        yield return VCpuText;
        yield return VGpuText;
        yield return VMemoryText;
        yield return VCpuTempText;
        yield return VGpuTempText;
    }

}
