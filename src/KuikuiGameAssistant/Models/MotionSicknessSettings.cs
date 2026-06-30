using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KuikuiGameAssistant.Models;

public sealed class MotionSicknessSettings : INotifyPropertyChanged
{
    private bool _isEnabled;
    private bool _showCenterCrosshair = true;
    private bool _showTopBar = true;
    private bool _showBottomBar = true;
    private bool _showLeftBar;
    private bool _showRightBar;
    private string _barColorHex = "#FFFFFF";
    private double _opacity = 70;
    private double _thickness = 3;
    private double _length = 220;
    private double _centerGap = 18;
    private MotionCrosshairStyle _crosshairStyle = MotionCrosshairStyle.Classic;
    private double _crosshairSize = 46;
    private double _crosshairThickness = 3;
    private double _crosshairGap = 18;
    private double _edgeDistance = 18;
    private double _edgeLength = 220;
    private double _edgeThickness = 3;
    private bool _onlyShowInFullscreen = true;
    private bool _tunnelVisionEnabled;
    private string _tunnelColorHex = "#000000";
    private double _tunnelIntensity = 22;
    private double _tunnelFeather = 72;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool ShowCenterCrosshair
    {
        get => _showCenterCrosshair;
        set => SetProperty(ref _showCenterCrosshair, value);
    }

    public bool ShowTopBar
    {
        get => _showTopBar;
        set => SetProperty(ref _showTopBar, value);
    }

    public bool ShowBottomBar
    {
        get => _showBottomBar;
        set => SetProperty(ref _showBottomBar, value);
    }

    public bool ShowLeftBar
    {
        get => _showLeftBar;
        set => SetProperty(ref _showLeftBar, value);
    }

    public bool ShowRightBar
    {
        get => _showRightBar;
        set => SetProperty(ref _showRightBar, value);
    }

    public string BarColorHex
    {
        get => _barColorHex;
        set => SetProperty(ref _barColorHex, string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value.Trim());
    }

    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, Math.Clamp(value, 5, 100));
    }

    public double Thickness
    {
        get => _thickness;
        set => SetProperty(ref _thickness, Math.Clamp(value, 1, 16));
    }

    public double Length
    {
        get => _length;
        set => SetProperty(ref _length, Math.Clamp(value, 40, 640));
    }

    public double CenterGap
    {
        get => _centerGap;
        set => SetProperty(ref _centerGap, Math.Clamp(value, 0, 80));
    }

    public MotionCrosshairStyle CrosshairStyle
    {
        get => _crosshairStyle;
        set => SetProperty(ref _crosshairStyle, value);
    }

    public double CrosshairSize
    {
        get => _crosshairSize;
        set => SetProperty(ref _crosshairSize, Math.Clamp(value, 4, 240));
    }

    public double CrosshairThickness
    {
        get => _crosshairThickness;
        set => SetProperty(ref _crosshairThickness, Math.Clamp(value, 1, 20));
    }

    public double CrosshairGap
    {
        get => _crosshairGap;
        set => SetProperty(ref _crosshairGap, Math.Clamp(value, 0, 100));
    }

    public double EdgeDistance
    {
        get => _edgeDistance;
        set => SetProperty(ref _edgeDistance, Math.Clamp(value, 0, 220));
    }

    public double EdgeLength
    {
        get => _edgeLength;
        set => SetProperty(ref _edgeLength, Math.Clamp(value, 20, 1200));
    }

    public double EdgeThickness
    {
        get => _edgeThickness;
        set => SetProperty(ref _edgeThickness, Math.Clamp(value, 1, 30));
    }

    public bool OnlyShowInFullscreen
    {
        get => _onlyShowInFullscreen;
        set => SetProperty(ref _onlyShowInFullscreen, value);
    }

    public bool TunnelVisionEnabled
    {
        get => _tunnelVisionEnabled;
        set => SetProperty(ref _tunnelVisionEnabled, value);
    }

    public string TunnelColorHex
    {
        get => _tunnelColorHex;
        set => SetProperty(ref _tunnelColorHex, string.IsNullOrWhiteSpace(value) ? "#000000" : value.Trim());
    }

    public double TunnelIntensity
    {
        get => _tunnelIntensity;
        set => SetProperty(ref _tunnelIntensity, Math.Clamp(value, 0, 80));
    }

    public double TunnelFeather
    {
        get => _tunnelFeather;
        set => SetProperty(ref _tunnelFeather, Math.Clamp(value, 10, 100));
    }

    public void Reset()
    {
        IsEnabled = false;
        ShowCenterCrosshair = true;
        ShowTopBar = true;
        ShowBottomBar = true;
        ShowLeftBar = false;
        ShowRightBar = false;
        BarColorHex = "#FFFFFF";
        Opacity = 70;
        Thickness = 3;
        Length = 220;
        CenterGap = 18;
        CrosshairStyle = MotionCrosshairStyle.Classic;
        CrosshairSize = 46;
        CrosshairThickness = 3;
        CrosshairGap = 18;
        EdgeDistance = 18;
        EdgeLength = 220;
        EdgeThickness = 3;
        OnlyShowInFullscreen = true;
        TunnelVisionEnabled = false;
        TunnelColorHex = "#000000";
        TunnelIntensity = 22;
        TunnelFeather = 72;
    }

    public void ApplyGentlePreset()
    {
        IsEnabled = true;
        ShowCenterCrosshair = true;
        ShowTopBar = true;
        ShowBottomBar = true;
        ShowLeftBar = false;
        ShowRightBar = false;
        BarColorHex = "#FFFFFF";
        Opacity = 55;
        Thickness = 2;
        Length = 180;
        CenterGap = 20;
        CrosshairStyle = MotionCrosshairStyle.SmallCross;
        CrosshairSize = 34;
        CrosshairThickness = 2;
        CrosshairGap = 16;
        EdgeDistance = 10;
        EdgeLength = 180;
        EdgeThickness = 2;
        TunnelVisionEnabled = false;
    }

    public void ApplyBalancedPreset()
    {
        IsEnabled = true;
        ShowCenterCrosshair = true;
        ShowTopBar = true;
        ShowBottomBar = true;
        ShowLeftBar = true;
        ShowRightBar = true;
        BarColorHex = "#EAF6FF";
        Opacity = 68;
        Thickness = 3;
        Length = 240;
        CenterGap = 18;
        CrosshairStyle = MotionCrosshairStyle.Classic;
        CrosshairSize = 48;
        CrosshairThickness = 3;
        CrosshairGap = 18;
        EdgeDistance = 8;
        EdgeLength = 260;
        EdgeThickness = 3;
        TunnelVisionEnabled = true;
        TunnelColorHex = "#000000";
        TunnelIntensity = 18;
        TunnelFeather = 76;
    }

    public void ApplyHighContrastPreset()
    {
        IsEnabled = true;
        ShowCenterCrosshair = true;
        ShowTopBar = true;
        ShowBottomBar = true;
        ShowLeftBar = true;
        ShowRightBar = true;
        BarColorHex = "#FFD84D";
        Opacity = 82;
        Thickness = 4;
        Length = 300;
        CenterGap = 16;
        CrosshairStyle = MotionCrosshairStyle.Circle;
        CrosshairSize = 32;
        CrosshairThickness = 4;
        CrosshairGap = 12;
        EdgeDistance = 6;
        EdgeLength = 320;
        EdgeThickness = 4;
        TunnelVisionEnabled = true;
        TunnelColorHex = "#000000";
        TunnelIntensity = 30;
        TunnelFeather = 68;
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
