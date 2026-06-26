namespace KuikuiGameAssistant.Models;

public sealed class GameFilterPreset
{
    public string Name { get; set; } = "未命名预设";
    public bool IsEnabled { get; set; }
    public double Brightness { get; set; }
    public double Contrast { get; set; }
    public double Grayscale { get; set; }
    public double Saturation { get; set; } = 100;
    public double Hue { get; set; }
    public bool VignetteEnabled { get; set; }
    public string VignetteColorHex { get; set; } = "#000000";
    public double VignetteIntensity { get; set; } = 35;
    public double VignetteFeather { get; set; } = 65;

    public static GameFilterPreset FromSettings(GameFilterSettings settings, string name)
    {
        return new GameFilterPreset
        {
            Name = name,
            IsEnabled = settings.IsEnabled,
            Brightness = settings.Brightness,
            Contrast = settings.Contrast,
            Grayscale = settings.Grayscale,
            Saturation = settings.Saturation,
            Hue = settings.Hue,
            VignetteEnabled = settings.VignetteEnabled,
            VignetteColorHex = settings.VignetteColorHex,
            VignetteIntensity = settings.VignetteIntensity,
            VignetteFeather = settings.VignetteFeather
        };
    }

    public override string ToString() => Name;
}
