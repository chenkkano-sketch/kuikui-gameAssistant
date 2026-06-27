using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Services;
using KuikuiGameAssistant.ViewModels;

namespace KuikuiGameAssistant.Views;

public partial class OverlayPage : System.Windows.Controls.UserControl
{
    private readonly OverlaySettings _settings;
    private bool _isApplyingBatch;

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

        if (e.PropertyName == nameof(OverlaySettings.Placement))
        {
            ApplyPlacementState();
        }

        if (!_isApplyingBatch && IsLoaded && ShouldSaveOnPropertyChanged(e.PropertyName))
        {
            SaveSettings();
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

    private void PlacementButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: OverlayPlacement placement })
        {
            _settings.Placement = placement;
            ApplyPlacementState();
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
        _isApplyingBatch = true;
        try
        {
            _settings.Reset();
            ApplyLayoutState();
            ApplyPlacementState();
            SaveSettings();
        }
        finally
        {
            _isApplyingBatch = false;
        }
    }

    private void ApplyLayoutState()
    {
        HorizontalLayoutRadio.IsChecked = _settings.LayoutMode == OverlayLayoutMode.Horizontal;
        VerticalLayoutRadio.IsChecked = _settings.LayoutMode == OverlayLayoutMode.Vertical;
        PreviewHorizontalPanel.Visibility = _settings.LayoutMode == OverlayLayoutMode.Horizontal ? Visibility.Visible : Visibility.Collapsed;
        PreviewVerticalPanel.Visibility = _settings.LayoutMode == OverlayLayoutMode.Vertical ? Visibility.Visible : Visibility.Collapsed;
        ApplyPlacementState();
    }

    private void ApplyPlacementState()
    {
        foreach (var button in PlacementButtons())
        {
            var isSelected = button.Tag is OverlayPlacement placement && placement == _settings.Placement;
            button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
            button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, isSelected ? "AccentSoftBrush" : "InputBackgroundBrush");
            button.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, isSelected ? "AccentBrush" : "BorderBrush");
            button.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, isSelected ? "AccentBrush" : "TextPrimaryBrush");
        }

        PreviewOverlay.HorizontalAlignment = _settings.Placement switch
        {
            OverlayPlacement.TopLeft or OverlayPlacement.Left or OverlayPlacement.BottomLeft => System.Windows.HorizontalAlignment.Left,
            OverlayPlacement.TopRight or OverlayPlacement.Right or OverlayPlacement.BottomRight => System.Windows.HorizontalAlignment.Right,
            _ => System.Windows.HorizontalAlignment.Center
        };

        PreviewOverlay.VerticalAlignment = _settings.Placement switch
        {
            OverlayPlacement.TopLeft or OverlayPlacement.Top or OverlayPlacement.TopRight => System.Windows.VerticalAlignment.Top,
            OverlayPlacement.BottomLeft or OverlayPlacement.Bottom or OverlayPlacement.BottomRight => System.Windows.VerticalAlignment.Bottom,
            _ => System.Windows.VerticalAlignment.Center
        };
    }

    private System.Windows.Controls.Button[] PlacementButtons()
    {
        return
        [
            PlacementTopLeftButton,
            PlacementTopButton,
            PlacementTopRightButton,
            PlacementLeftButton,
            PlacementCenterButton,
            PlacementRightButton,
            PlacementBottomLeftButton,
            PlacementBottomButton,
            PlacementBottomRightButton
        ];
    }

    private static bool ShouldSaveOnPropertyChanged(string? propertyName)
    {
        return propertyName is nameof(OverlaySettings.BackgroundOpacity)
            or nameof(OverlaySettings.FontSize)
            or nameof(OverlaySettings.LabelFontSize)
            or nameof(OverlaySettings.HorizontalWidth)
            or nameof(OverlaySettings.HorizontalHeight)
            or nameof(OverlaySettings.VerticalWidth)
            or nameof(OverlaySettings.VerticalHeight);
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
        ToastService.ShowSettingsSaved();
    }
}
