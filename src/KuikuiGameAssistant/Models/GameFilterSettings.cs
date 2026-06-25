using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KuikuiGameAssistant.Models;

public sealed class GameFilterSettings : INotifyPropertyChanged
{
    private bool _isEnabled;
    private double _brightness;
    private double _contrast;
    private double _grayscale;
    private double _saturation = 100;
    private double _hue;
    private bool _vignetteEnabled;
    private string _vignetteColorHex = "#000000";
    private double _vignetteIntensity = 35;
    private double _vignetteFeather = 65;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GameFilterPreset> Presets { get; set; } = new();

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public double Brightness
    {
        get => _brightness;
        set => SetProperty(ref _brightness, Math.Clamp(value, -100, 100));
    }

    public double Contrast
    {
        get => _contrast;
        set => SetProperty(ref _contrast, Math.Clamp(value, -100, 100));
    }

    public double Grayscale
    {
        get => _grayscale;
        set => SetProperty(ref _grayscale, Math.Clamp(value, 0, 100));
    }

    public double Saturation
    {
        get => _saturation;
        set => SetProperty(ref _saturation, Math.Clamp(value, 0, 200));
    }

    public double Hue
    {
        get => _hue;
        set => SetProperty(ref _hue, Math.Clamp(value, -180, 180));
    }

    public bool VignetteEnabled
    {
        get => _vignetteEnabled;
        set => SetProperty(ref _vignetteEnabled, value);
    }

    public string VignetteColorHex
    {
        get => _vignetteColorHex;
        set => SetProperty(ref _vignetteColorHex, string.IsNullOrWhiteSpace(value) ? "#000000" : value.Trim());
    }

    public double VignetteIntensity
    {
        get => _vignetteIntensity;
        set => SetProperty(ref _vignetteIntensity, Math.Clamp(value, 0, 100));
    }

    public double VignetteFeather
    {
        get => _vignetteFeather;
        set => SetProperty(ref _vignetteFeather, Math.Clamp(value, 0, 100));
    }

    public void Reset()
    {
        IsEnabled = false;
        Brightness = 0;
        Contrast = 0;
        Grayscale = 0;
        Saturation = 100;
        Hue = 0;
        VignetteEnabled = false;
        VignetteColorHex = "#000000";
        VignetteIntensity = 35;
        VignetteFeather = 65;
    }

    public void ApplyPreset(GameFilterPreset preset)
    {
        IsEnabled = preset.IsEnabled;
        Brightness = preset.Brightness;
        Contrast = preset.Contrast;
        Grayscale = preset.Grayscale;
        Saturation = preset.Saturation;
        Hue = preset.Hue;
        VignetteEnabled = preset.VignetteEnabled;
        VignetteColorHex = preset.VignetteColorHex;
        VignetteIntensity = preset.VignetteIntensity;
        VignetteFeather = preset.VignetteFeather;
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
