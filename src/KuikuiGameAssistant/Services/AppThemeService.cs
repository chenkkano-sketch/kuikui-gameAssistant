using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WpfApplication = System.Windows.Application;

namespace KuikuiGameAssistant.Services;

public static class AppThemeService
{
    public static void Apply(bool darkMode)
    {
        if (WpfApplication.Current is null)
        {
            return;
        }

        var palette = darkMode ? DarkPalette : LightPalette;
        foreach (var (key, color) in palette)
        {
            var brushColor = ColorFrom(color);
            if (WpfApplication.Current.Resources[key] is SolidColorBrush brush)
            {
                if (!brush.IsFrozen)
                {
                    brush.Color = brushColor;
                    continue;
                }
            }

            WpfApplication.Current.Resources[key] = new SolidColorBrush(brushColor);
        }
    }

    private static MediaColor ColorFrom(string hex)
    {
        return (MediaColor)MediaColorConverter.ConvertFromString(hex)!;
    }

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["WindowBackgroundBrush"] = "#FFF6F8FB",
        ["SurfaceBrush"] = "#FFFFFFFF",
        ["SurfaceMutedBrush"] = "#FFF1F4F8",
        ["InputBackgroundBrush"] = "#FFFFFFFF",
        ["TitleBarBrush"] = "#FFF8FAFD",
        ["SidebarBrush"] = "#FFEEF3F9",
        ["TextPrimaryBrush"] = "#FF172033",
        ["TextSecondaryBrush"] = "#FF667085",
        ["BorderBrush"] = "#FFE4E9F1",
        ["AccentBrush"] = "#FF2563EB",
        ["AccentSoftBrush"] = "#FFEAF1FF",
        ["ScrollThumbBrush"] = "#FFC8D0DC",
        ["ScrollThumbHoverBrush"] = "#FFAEB8C6"
    };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["WindowBackgroundBrush"] = "#FF0F172A",
        ["SurfaceBrush"] = "#FF162033",
        ["SurfaceMutedBrush"] = "#FF202B40",
        ["InputBackgroundBrush"] = "#FF111827",
        ["TitleBarBrush"] = "#FF111827",
        ["SidebarBrush"] = "#FF0B1220",
        ["TextPrimaryBrush"] = "#FFF8FAFC",
        ["TextSecondaryBrush"] = "#FF9CA3AF",
        ["BorderBrush"] = "#FF2B3A52",
        ["AccentBrush"] = "#FF60A5FA",
        ["AccentSoftBrush"] = "#FF1D355A",
        ["ScrollThumbBrush"] = "#FF475569",
        ["ScrollThumbHoverBrush"] = "#FF64748B"
    };
}
