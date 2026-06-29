using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using KuikuiGameAssistant.Models;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaFontFamily = System.Windows.Media.FontFamily;
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
        ApplyFont(settings.FontMode);
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

    public static void ApplyFont(AppFontMode mode)
    {
        if (WpfApplication.Current is null)
        {
            return;
        }

        WpfApplication.Current.Resources["AppFont"] = mode switch
        {
            AppFontMode.MicrosoftYaHeiUi => new MediaFontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI"),
            AppFontMode.DengXian => new MediaFontFamily("DengXian, Microsoft YaHei UI, Segoe UI"),
            AppFontMode.SimHei => new MediaFontFamily("SimHei, Microsoft YaHei UI, Segoe UI"),
            AppFontMode.SimSun => new MediaFontFamily("SimSun, Microsoft YaHei UI, Segoe UI"),
            _ => new MediaFontFamily("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI")
        };
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

        if (_settings is not null)
        {
            ApplyFont(_settings.FontMode);
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
        ["WindowBackgroundColor"] = "#FFF4F8FF",
        ["SurfaceColor"] = "#F8FFFFFF",
        ["SurfaceMutedColor"] = "#FFEAF1FF",
        ["InputBackgroundColor"] = "#FFFFFFFF",
        ["TitleBarColor"] = "#F8FFFFFF",
        ["SidebarColor"] = "#F8FFFFFF",
        ["ContentBackgroundColor"] = "#FFF1F5FB",
        ["TextPrimaryColor"] = "#FF101828",
        ["TextSecondaryColor"] = "#FF667085",
        ["BorderColor"] = "#D7D8E2F2",
        ["AccentColor"] = "#FF2563EB",
        ["AccentSoftColor"] = "#FFEAF2FF",
        ["CardShadowColor"] = "#FF62708A",
        ["WindowBackgroundBrush"] = "#FFF4F8FF",
        ["SurfaceBrush"] = "#F8FFFFFF",
        ["SurfaceMutedBrush"] = "#FFEAF1FF",
        ["InputBackgroundBrush"] = "#FFFFFFFF",
        ["TitleBarBrush"] = "#F8FFFFFF",
        ["SidebarBrush"] = "#F8FFFFFF",
        ["ContentBackgroundBrush"] = "#FFF1F5FB",
        ["TextPrimaryBrush"] = "#FF101828",
        ["TextSecondaryBrush"] = "#FF667085",
        ["BorderBrush"] = "#D7D8E2F2",
        ["AccentBrush"] = "#FF2563EB",
        ["AccentSoftBrush"] = "#FFEAF2FF",
        ["PrimaryButtonBrush"] = "#FF2563EB",
        ["AccentStrongBrush"] = "#FF1D4ED8",
        ["AccentCyanBrush"] = "#FF06B6D4",
        ["AccentVioletBrush"] = "#FF7C3AED",
        ["SuccessSoftBrush"] = "#FFE9FBEF",
        ["WarningSoftBrush"] = "#FFFFF3E0",
        ["DangerSoftBrush"] = "#FFFFE9E9",
        ["CardShadowBrush"] = "#2A6B7CA8",
        ["SidebarAccentBrush"] = "#1A2563EB",
        ["PrimaryButtonForegroundBrush"] = "#FFFFFFFF",
        ["ChromeHoverBrush"] = "#100B1220",
        ["ChromePressedBrush"] = "#180B1220",
        ["ControlHoverBrush"] = "#0F2563EB",
        ["ControlPressedBrush"] = "#1A2563EB",
        ["ControlDisabledBrush"] = "#FFE7ECF5",
        ["SwitchTrackBrush"] = "#FFD8E3F3",
        ["SwitchThumbBrush"] = "#FFFFFFFF",
        ["CheckBoxGlyphBrush"] = "#FFFFFFFF",
        ["ComboBoxItemHoverBrush"] = "#102563EB",
        ["ComboBoxItemSelectedBrush"] = "#FFEAF2FF",
        ["SelectionBrush"] = "#552563EB",
        ["ScrollThumbBrush"] = "#FFB9C7DD",
        ["ScrollThumbHoverBrush"] = "#FF8DA2C2",
        ["ScrollThumbPressedBrush"] = "#FF6F86AD",
        ["GraphGridBrush"] = "#FFE3EAF6",
        ["GraphCpuBrush"] = "#FF2563EB",
        ["GraphGpuBrush"] = "#FF7C3AED",
        ["GraphMemoryBrush"] = "#FF059669",
        ["StatusOkBrush"] = "#FF10B981"
    };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["WindowBackgroundColor"] = "#FF202020",
        ["SurfaceColor"] = "#FF2B2B2B",
        ["SurfaceMutedColor"] = "#FF323232",
        ["InputBackgroundColor"] = "#FF1F1F1F",
        ["TitleBarColor"] = "#FF202020",
        ["SidebarColor"] = "#FF202020",
        ["ContentBackgroundColor"] = "#FF252525",
        ["TextPrimaryColor"] = "#FFF5F5F5",
        ["TextSecondaryColor"] = "#FFC9C9C9",
        ["BorderColor"] = "#FF3A3A3A",
        ["AccentColor"] = "#FF4CC2FF",
        ["AccentSoftColor"] = "#FF243B49",
        ["CardShadowColor"] = "#FF000000",
        ["WindowBackgroundBrush"] = "#FF202020",
        ["SurfaceBrush"] = "#FF2B2B2B",
        ["SurfaceMutedBrush"] = "#FF323232",
        ["InputBackgroundBrush"] = "#FF1F1F1F",
        ["TitleBarBrush"] = "#FF202020",
        ["SidebarBrush"] = "#FF202020",
        ["ContentBackgroundBrush"] = "#FF252525",
        ["TextPrimaryBrush"] = "#FFF5F5F5",
        ["TextSecondaryBrush"] = "#FFC9C9C9",
        ["BorderBrush"] = "#FF3A3A3A",
        ["AccentBrush"] = "#FF4CC2FF",
        ["AccentSoftBrush"] = "#FF243B49",
        ["PrimaryButtonBrush"] = "#FF4CC2FF",
        ["AccentStrongBrush"] = "#FF60CDFF",
        ["AccentCyanBrush"] = "#FF4CC2FF",
        ["AccentVioletBrush"] = "#FF8B8B8B",
        ["SuccessSoftBrush"] = "#FF1E3328",
        ["WarningSoftBrush"] = "#FF3A2F1F",
        ["DangerSoftBrush"] = "#FF3A2428",
        ["CardShadowBrush"] = "#66000000",
        ["SidebarAccentBrush"] = "#1F4CC2FF",
        ["PrimaryButtonForegroundBrush"] = "#FF000000",
        ["ChromeHoverBrush"] = "#18FFFFFF",
        ["ChromePressedBrush"] = "#24FFFFFF",
        ["ControlHoverBrush"] = "#14FFFFFF",
        ["ControlPressedBrush"] = "#20FFFFFF",
        ["ControlDisabledBrush"] = "#FF252525",
        ["SwitchTrackBrush"] = "#FF454545",
        ["SwitchThumbBrush"] = "#FFF5F5F5",
        ["CheckBoxGlyphBrush"] = "#FF000000",
        ["ComboBoxItemHoverBrush"] = "#14FFFFFF",
        ["ComboBoxItemSelectedBrush"] = "#FF243B49",
        ["SelectionBrush"] = "#664CC2FF",
        ["ScrollThumbBrush"] = "#FF555555",
        ["ScrollThumbHoverBrush"] = "#FF6A6A6A",
        ["ScrollThumbPressedBrush"] = "#FF808080",
        ["GraphGridBrush"] = "#FF3A3A3A",
        ["GraphCpuBrush"] = "#FF4CC2FF",
        ["GraphGpuBrush"] = "#FFC586C0",
        ["GraphMemoryBrush"] = "#FF6CCB5F",
        ["StatusOkBrush"] = "#FF6CCB5F"
    };
}
