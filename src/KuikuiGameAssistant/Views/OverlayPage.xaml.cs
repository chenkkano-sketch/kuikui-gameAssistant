using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.ViewModels;

namespace KuikuiGameAssistant.Views;

public partial class OverlayPage : System.Windows.Controls.UserControl
{
    private readonly OverlaySettings _settings;

    public OverlayPage(OverlaySettings settings)
    {
        InitializeComponent();
        _settings = settings;
        DataContext = _settings;
        _settings.PropertyChanged += Settings_PropertyChanged;
        ApplyLayoutState();
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlaySettings.LayoutMode))
        {
            ApplyLayoutState();
        }
    }

    private void HorizontalLayout_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            _settings.LayoutMode = OverlayLayoutMode.Horizontal;
            SaveSettings();
        }
    }

    private void VerticalLayout_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            _settings.LayoutMode = OverlayLayoutMode.Vertical;
            SaveSettings();
        }
    }

    private void MetricToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            SaveSettings();
        }
    }

    private void ClickThroughToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            SaveSettings();
        }
    }

    private void FullscreenVisibilityToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            SaveSettings();
        }
    }

    private void BackgroundSwatch_Click(object sender, RoutedEventArgs e)
    {
        _settings.BackgroundColor = ColorFromTag(sender);
        SaveSettings();
    }

    private void FontSwatch_Click(object sender, RoutedEventArgs e)
    {
        _settings.FontColor = ColorFromTag(sender);
        SaveSettings();
    }

    private void LabelSwatch_Click(object sender, RoutedEventArgs e)
    {
        _settings.LabelColor = ColorFromTag(sender);
        SaveSettings();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _settings.Reset();
        ApplyLayoutState();
        SaveSettings();
    }

    private void ApplyLayoutState()
    {
        HorizontalLayoutRadio.IsChecked = _settings.LayoutMode == OverlayLayoutMode.Horizontal;
        VerticalLayoutRadio.IsChecked = _settings.LayoutMode == OverlayLayoutMode.Vertical;
        PreviewHorizontalPanel.Visibility = _settings.LayoutMode == OverlayLayoutMode.Horizontal ? Visibility.Visible : Visibility.Collapsed;
        PreviewVerticalPanel.Visibility = _settings.LayoutMode == OverlayLayoutMode.Vertical ? Visibility.Visible : Visibility.Collapsed;
    }

    private static System.Windows.Media.Color ColorFromTag(object sender)
    {
        if (sender is System.Windows.Controls.Button { Tag: string hex }
            && System.Windows.Media.ColorConverter.ConvertFromString(hex) is System.Windows.Media.Color color)
        {
            return color;
        }

        return System.Windows.Media.Colors.White;
    }

    private static void SaveSettings()
    {
        App.Settings.Save(App.OverlaySettings);
    }
}
