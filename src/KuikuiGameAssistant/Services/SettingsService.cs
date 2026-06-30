using System.IO;
using System.Text.Json;
using System.Windows.Media;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.ViewModels;
using MediaColor = System.Windows.Media.Color;

namespace KuikuiGameAssistant.Services;

public sealed class SettingsService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public string SettingsFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KuikuiGameAssistant");

    public string SettingsPath => Path.Combine(SettingsFolder, "settings.json");

    public AppSettings AppSettings { get; private set; } = new();

    public void Load(OverlaySettings overlaySettings)
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            var json = File.ReadAllText(SettingsPath);
            var document = JsonSerializer.Deserialize<PersistedSettings>(json);
            if (document is null)
            {
                return;
            }

            AppSettings = document.App ?? new AppSettings();
            AppSettings.GameFilter ??= new GameFilterSettings();
            AppSettings.GameFilter.Presets ??= new System.Collections.ObjectModel.ObservableCollection<GameFilterPreset>();
            AppSettings.MotionSickness ??= new MotionSicknessSettings();
            AppSettings.MonitorModules ??= MonitorModuleConfig.CreateDefaults();
            if (AppSettings.MonitorModules.Count == 0)
            {
                AppSettings.MonitorModules = MonitorModuleConfig.CreateDefaults();
            }

            if (!HasAppProperty(json, nameof(AppSettings.ThemeMode)))
            {
                AppSettings.ThemeMode = TryReadAppBool(json, nameof(AppSettings.UseDarkMode), out var legacyDarkMode) && legacyDarkMode
                    ? AppThemeMode.Dark
                    : AppThemeMode.System;
            }

            if (!HasAppProperty(json, nameof(AppSettings.EnablePresentMon)))
            {
                AppSettings.EnablePresentMon = true;
            }

            if (UpdateService.NormalizeRepository(AppSettings.GitHubRepository) is null)
            {
                AppSettings.GitHubRepository = AppSettings.DefaultGitHubRepository;
            }

            if (!AppSettings.MemoryOptimizedDefaultsApplied)
            {
                AppSettings.MemoryOptimizedDefaultsApplied = true;
            }

            ApplyOverlaySettings(overlaySettings, document.Overlay);
        }
        catch
        {
            AppSettings = new AppSettings();
        }
    }

    public void Save(OverlaySettings overlaySettings)
    {
        Directory.CreateDirectory(SettingsFolder);
        var document = new PersistedSettings
        {
            App = AppSettings,
            Overlay = FromOverlaySettings(overlaySettings)
        };

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(document, _jsonOptions));
    }

    private static void ApplyOverlaySettings(OverlaySettings settings, PersistedOverlaySettings? persisted)
    {
        if (persisted is null)
        {
            return;
        }

        settings.LayoutMode = persisted.LayoutMode;
        settings.BackgroundColor = ParseColor(persisted.BackgroundColor, settings.BackgroundColor);
        settings.FontColor = ParseColor(persisted.FontColor, settings.FontColor);
        settings.LabelColor = ParseColor(persisted.LabelColor, settings.LabelColor);
        settings.BackgroundOpacity = persisted.BackgroundOpacity;
        settings.FontSize = persisted.FontSize;
        settings.LabelFontSize = persisted.LabelFontSize;
        settings.HorizontalWidth = persisted.HorizontalWidth;
        settings.HorizontalHeight = persisted.HorizontalHeight;
        settings.VerticalWidth = persisted.VerticalWidth;
        settings.VerticalHeight = persisted.VerticalHeight;
        settings.Placement = persisted.Placement ?? OverlayPlacement.Center;
        settings.IsClickThroughEnabled = persisted.IsClickThroughEnabled;
        settings.OnlyShowInFullscreen = persisted.OnlyShowInFullscreen ?? true;
        if (persisted.Metrics is null)
        {
            settings.HorizontalWidth = Math.Max(settings.HorizontalWidth, 660);
            settings.VerticalHeight = Math.Max(settings.VerticalHeight, 292);
        }

        settings.ApplyMetricSettings(persisted.Metrics?.Select(x => new OverlayMetricConfig
        {
            Kind = x.Kind,
            IsEnabled = x.IsEnabled,
            Order = x.Order
        }));
    }

    private static PersistedOverlaySettings FromOverlaySettings(OverlaySettings settings)
    {
        return new PersistedOverlaySettings
        {
            LayoutMode = settings.LayoutMode,
            BackgroundColor = ColorToString(settings.BackgroundColor),
            FontColor = ColorToString(settings.FontColor),
            LabelColor = ColorToString(settings.LabelColor),
            BackgroundOpacity = settings.BackgroundOpacity,
            FontSize = settings.FontSize,
            LabelFontSize = settings.LabelFontSize,
            HorizontalWidth = settings.HorizontalWidth,
            HorizontalHeight = settings.HorizontalHeight,
            VerticalWidth = settings.VerticalWidth,
            VerticalHeight = settings.VerticalHeight,
            Placement = settings.Placement,
            IsClickThroughEnabled = settings.IsClickThroughEnabled,
            OnlyShowInFullscreen = settings.OnlyShowInFullscreen,
            Metrics = settings.SnapshotMetrics()
                .Select(x => new PersistedOverlayMetric
                {
                    Kind = x.Kind,
                    IsEnabled = x.IsEnabled,
                    Order = x.Order
                })
                .ToArray()
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
            return (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(value)!;
        }
        catch
        {
            return fallback;
        }
    }

    private static string ColorToString(MediaColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool HasAppProperty(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("App", out var app)
                   && app.TryGetProperty(propertyName, out _);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadAppBool(string json, string propertyName, out bool value)
    {
        value = false;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("App", out var app)
                || !app.TryGetProperty(propertyName, out var property)
                || property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                return false;
            }

            value = property.GetBoolean();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class PersistedSettings
    {
        public AppSettings? App { get; set; }
        public PersistedOverlaySettings? Overlay { get; set; }
    }

    private sealed class PersistedOverlaySettings
    {
        public KuikuiGameAssistant.Models.OverlayLayoutMode LayoutMode { get; set; }
        public string? BackgroundColor { get; set; }
        public string? FontColor { get; set; }
        public string? LabelColor { get; set; }
        public double BackgroundOpacity { get; set; }
        public double FontSize { get; set; }
        public double LabelFontSize { get; set; }
        public double HorizontalWidth { get; set; }
        public double HorizontalHeight { get; set; }
        public double VerticalWidth { get; set; }
        public double VerticalHeight { get; set; }
        public OverlayPlacement? Placement { get; set; }
        public bool IsClickThroughEnabled { get; set; }
        public bool? OnlyShowInFullscreen { get; set; }
        public IReadOnlyList<PersistedOverlayMetric>? Metrics { get; set; }
    }

    private sealed class PersistedOverlayMetric
    {
        public OverlayMetricKind Kind { get; set; }
        public bool IsEnabled { get; set; } = true;
        public int Order { get; set; }
    }
}
