using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using KuikuiGameAssistant.Models;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WpfApplication = System.Windows.Application;
using WpfUiApplicationTheme = Wpf.Ui.Appearance.ApplicationTheme;
using WpfUiApplicationThemeManager = Wpf.Ui.Appearance.ApplicationThemeManager;
using WpfUiThemesDictionary = Wpf.Ui.Markup.ThemesDictionary;
using WpfUiWindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace KuikuiGameAssistant.Services;

public static class AppThemeService
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";
    private static AppSettings? _settings;
    private static bool _isListening;

    public static event EventHandler? ThemeApplied;

    public static AppThemeMode CurrentMode { get; private set; } = AppThemeMode.System;

    public static bool IsDark { get; private set; }

    public static void Start(AppSettings settings)
    {
        _settings = settings;
        if (!_isListening)
        {
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            _isListening = true;
        }

        Apply(settings.ThemeMode);
    }

    public static void Stop()
    {
        if (_isListening)
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            _isListening = false;
        }

        _settings = null;
    }

    public static void Apply(AppThemeMode mode)
    {
        CurrentMode = mode;
        ApplyResolvedTheme(mode == AppThemeMode.System ? IsSystemAppThemeDark() : mode == AppThemeMode.Dark);
    }

    public static void Apply(bool darkMode)
    {
        Apply(darkMode ? AppThemeMode.Dark : AppThemeMode.Light);
    }

    private static void ApplyResolvedTheme(bool darkMode)
    {
        if (WpfApplication.Current is null)
        {
            return;
        }

        IsDark = darkMode;
        try
        {
            foreach (var dictionary in WpfApplication.Current.Resources.MergedDictionaries.OfType<WpfUiThemesDictionary>())
            {
                dictionary.Theme = darkMode ? WpfUiApplicationTheme.Dark : WpfUiApplicationTheme.Light;
            }

            WpfUiApplicationThemeManager.Apply(
                darkMode ? WpfUiApplicationTheme.Dark : WpfUiApplicationTheme.Light,
                WpfUiWindowBackdropType.None,
                false);
        }
        catch
        {
        }

        var palette = darkMode ? DarkPalette : LightPalette;
        foreach (var (key, color) in palette)
        {
            SetResourceColor(key, ColorFrom(color));
        }

        ThemeApplied?.Invoke(null, EventArgs.Empty);
    }

    private static void SetResourceColor(string key, MediaColor color)
    {
        if (key.EndsWith("Color", StringComparison.Ordinal))
        {
            WpfApplication.Current.Resources[key] = color;
            return;
        }

        if (WpfApplication.Current.Resources[key] is SolidColorBrush brush)
        {
            if (!brush.IsFrozen)
            {
                brush.Color = color;
                return;
            }
        }

        WpfApplication.Current.Resources[key] = new SolidColorBrush(color);
    }

    private static bool IsSystemAppThemeDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            var value = key?.GetValue(AppsUseLightThemeValue);
            return value switch
            {
                int intValue => intValue == 0,
                long longValue => longValue == 0,
                string text when int.TryParse(text, out var parsed) => parsed == 0,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        var app = WpfApplication.Current;
        if (app is null)
        {
            return;
        }

        if (app.Dispatcher.CheckAccess())
        {
            ApplySystemThemeIfNeeded();
            return;
        }

        _ = app.Dispatcher.BeginInvoke(ApplySystemThemeIfNeeded);
    }

    private static void ApplySystemThemeIfNeeded()
    {
        if (_settings?.ThemeMode == AppThemeMode.System)
        {
            Apply(AppThemeMode.System);
        }
    }

    private static MediaColor ColorFrom(string hex)
    {
        return (MediaColor)MediaColorConverter.ConvertFromString(hex)!;
    }

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["WindowBackgroundColor"] = "#FFF3F3F3",
        ["SurfaceColor"] = "#FFFFFFFF",
        ["SurfaceMutedColor"] = "#FFF8F8F8",
        ["InputBackgroundColor"] = "#FFFFFFFF",
        ["TitleBarColor"] = "#FFF3F3F3",
        ["SidebarColor"] = "#FFF8F8F8",
        ["TextPrimaryColor"] = "#FF1A1A1A",
        ["TextSecondaryColor"] = "#FF5F5F5F",
        ["BorderColor"] = "#FFE5E5E5",
        ["AccentColor"] = "#FF0067C0",
        ["AccentSoftColor"] = "#FFE5F2FF",
        ["WindowBackgroundBrush"] = "#FFF3F3F3",
        ["SurfaceBrush"] = "#FFFFFFFF",
        ["SurfaceMutedBrush"] = "#FFF8F8F8",
        ["InputBackgroundBrush"] = "#FFFFFFFF",
        ["TitleBarBrush"] = "#FFF3F3F3",
        ["SidebarBrush"] = "#FFF8F8F8",
        ["TextPrimaryBrush"] = "#FF1A1A1A",
        ["TextSecondaryBrush"] = "#FF5F5F5F",
        ["BorderBrush"] = "#FFE5E5E5",
        ["AccentBrush"] = "#FF0067C0",
        ["AccentSoftBrush"] = "#FFE5F2FF",
        ["PrimaryButtonForegroundBrush"] = "#FFFFFFFF",
        ["ChromeHoverBrush"] = "#10000000",
        ["ChromePressedBrush"] = "#18000000",
        ["ControlHoverBrush"] = "#0F000000",
        ["ControlPressedBrush"] = "#18000000",
        ["ControlDisabledBrush"] = "#FFF1F1F1",
        ["SwitchTrackBrush"] = "#FFE5E5E5",
        ["SwitchThumbBrush"] = "#FFFFFFFF",
        ["CheckBoxGlyphBrush"] = "#FFFFFFFF",
        ["ComboBoxItemHoverBrush"] = "#0F000000",
        ["ComboBoxItemSelectedBrush"] = "#FFE5F2FF",
        ["SelectionBrush"] = "#660067C0",
        ["ScrollThumbBrush"] = "#FFC8C8C8",
        ["ScrollThumbHoverBrush"] = "#FFA8A8A8",
        ["ScrollThumbPressedBrush"] = "#FF8A8A8A",
        ["GraphGridBrush"] = "#FFE5E5E5",
        ["GraphCpuBrush"] = "#FF0067C0",
        ["GraphGpuBrush"] = "#FF8E4EC6",
        ["GraphMemoryBrush"] = "#FF0E7A0D",
        ["StatusOkBrush"] = "#FF0E7A0D"
    };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["WindowBackgroundColor"] = "#FF202020",
        ["SurfaceColor"] = "#FF2B2B2B",
        ["SurfaceMutedColor"] = "#FF333333",
        ["InputBackgroundColor"] = "#FF1F1F1F",
        ["TitleBarColor"] = "#FF202020",
        ["SidebarColor"] = "#FF1B1B1B",
        ["TextPrimaryColor"] = "#FFF3F3F3",
        ["TextSecondaryColor"] = "#FFC9C9C9",
        ["BorderColor"] = "#FF3D3D3D",
        ["AccentColor"] = "#FF60CDFF",
        ["AccentSoftColor"] = "#FF14384A",
        ["WindowBackgroundBrush"] = "#FF202020",
        ["SurfaceBrush"] = "#FF2B2B2B",
        ["SurfaceMutedBrush"] = "#FF333333",
        ["InputBackgroundBrush"] = "#FF1F1F1F",
        ["TitleBarBrush"] = "#FF202020",
        ["SidebarBrush"] = "#FF1B1B1B",
        ["TextPrimaryBrush"] = "#FFF3F3F3",
        ["TextSecondaryBrush"] = "#FFC9C9C9",
        ["BorderBrush"] = "#FF3D3D3D",
        ["AccentBrush"] = "#FF60CDFF",
        ["AccentSoftBrush"] = "#FF14384A",
        ["PrimaryButtonForegroundBrush"] = "#FF000000",
        ["ChromeHoverBrush"] = "#FF333333",
        ["ChromePressedBrush"] = "#FF3A3A3A",
        ["ControlHoverBrush"] = "#FF383838",
        ["ControlPressedBrush"] = "#FF454545",
        ["ControlDisabledBrush"] = "#FF2A2A2A",
        ["SwitchTrackBrush"] = "#FF4A4A4A",
        ["SwitchThumbBrush"] = "#FFF3F3F3",
        ["CheckBoxGlyphBrush"] = "#FF000000",
        ["ComboBoxItemHoverBrush"] = "#FF383838",
        ["ComboBoxItemSelectedBrush"] = "#FF14384A",
        ["SelectionBrush"] = "#664CC2FF",
        ["ScrollThumbBrush"] = "#FF5A5A5A",
        ["ScrollThumbHoverBrush"] = "#FF707070",
        ["ScrollThumbPressedBrush"] = "#FF858585",
        ["GraphGridBrush"] = "#FF3A3A3A",
        ["GraphCpuBrush"] = "#FF60CDFF",
        ["GraphGpuBrush"] = "#FFD0A2F7",
        ["GraphMemoryBrush"] = "#FF6CCB5F",
        ["StatusOkBrush"] = "#FF6CCB5F"
    };
}
