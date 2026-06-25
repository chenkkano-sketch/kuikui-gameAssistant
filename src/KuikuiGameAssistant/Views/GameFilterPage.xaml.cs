using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Services;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace KuikuiGameAssistant.Views;

public partial class GameFilterPage : WpfUserControl
{
    private readonly AppSettings _appSettings;
    private readonly GameFilterSettings _settings;
    private readonly GameFilterService _filterService;
    private bool _applyingTheme;

    public GameFilterPage(AppSettings appSettings, GameFilterService filterService)
    {
        InitializeComponent();
        _appSettings = appSettings;
        _settings = appSettings.GameFilter;
        _filterService = filterService;
        DataContext = _settings;
        _applyingTheme = true;
        DarkModeCheckBox.IsChecked = appSettings.UseDarkMode;
        _applyingTheme = false;

        _settings.PropertyChanged += Settings_PropertyChanged;
        _settings.Presets.CollectionChanged += Presets_CollectionChanged;
        ApplyFilter();
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyFilter();
        SaveSettings();
    }

    private void Presets_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SaveSettings();
    }

    private void SavePreset_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(PresetNameBox.Text)
            ? $"预设 {_settings.Presets.Count + 1}"
            : PresetNameBox.Text.Trim();

        var existing = _settings.Presets.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var preset = GameFilterPreset.FromSettings(_settings, name);
        if (existing is not null)
        {
            var index = _settings.Presets.IndexOf(existing);
            _settings.Presets[index] = preset;
        }
        else
        {
            _settings.Presets.Add(preset);
        }

        PresetComboBox.SelectedItem = preset;
    }

    private void LoadPreset_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (PresetComboBox.SelectedItem is GameFilterPreset preset)
        {
            _settings.ApplyPreset(preset);
            PresetNameBox.Text = preset.Name;
        }
    }

    private void DeletePreset_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (PresetComboBox.SelectedItem is GameFilterPreset preset)
        {
            _settings.Presets.Remove(preset);
        }
    }

    private void Reset_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _settings.Reset();
    }

    private void DarkModeCheckBox_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_applyingTheme)
        {
            return;
        }

        _applyingTheme = true;
        _appSettings.UseDarkMode = DarkModeCheckBox.IsChecked == true;
        AppThemeService.Apply(_appSettings.UseDarkMode);
        SaveSettings();
        _applyingTheme = false;
    }

    private void ApplyFilter()
    {
        VignetteColorPreview.Background = new SolidColorBrush(ParseColor(_settings.VignetteColorHex, Colors.Black));
        _filterService.Apply(_settings);
        FilterStatusText.Text = _filterService.StatusText;
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

    private static void SaveSettings()
    {
        App.Settings.Save(App.OverlaySettings);
    }
}
