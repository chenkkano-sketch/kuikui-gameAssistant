using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Services;
using KuikuiGameAssistant.Views;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace KuikuiGameAssistant;

public partial class MainWindow : Window
{
    private const double NormalWindowCornerRadius = 10;
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private readonly DashboardPage _dashboardPage;
    private readonly HardwarePage _hardwarePage;
    private readonly CapturePage _capturePage;
    private readonly OverlayPage _overlayPage;
    private readonly GameFilterPage _filterPage;
    private readonly SettingsPage _settingsPage;
    private readonly HotkeyService _hotkeys = new();
    private readonly DispatcherTimer _toastHideTimer = new() { Interval = TimeSpan.FromSeconds(1.8) };
    private Forms.NotifyIcon? _notifyIcon;
    private Drawing.Icon? _trayIcon;
    private OverlayWindow? _overlayWindow;
    private bool _screenshotSelectionOpen;
    private string _currentPage = "Dashboard";

    public MainWindow()
    {
        InitializeComponent();

        _dashboardPage = new DashboardPage(App.Telemetry);
        _hardwarePage = new HardwarePage(App.HardwareInfo);
        _capturePage = new CapturePage(App.Capture, App.Settings.AppSettings);
        _overlayPage = new OverlayPage(App.OverlaySettings);
        _filterPage = new GameFilterPage(App.Settings.AppSettings, App.GameFilters);
        _settingsPage = new SettingsPage(App.Settings.AppSettings, App.Updates);
        App.Settings.AppSettings.PropertyChanged += AppSettings_PropertyChanged;
        AppThemeService.ThemeApplied += AppThemeService_ThemeApplied;
        ToastService.ToastRequested += ToastService_ToastRequested;
        StateChanged += MainWindow_StateChanged;
        _toastHideTimer.Tick += ToastHideTimer_Tick;

        Navigate("Dashboard");
        ApplyBackgroundMode(App.Settings.AppSettings.BackgroundMode);
        UpdateWindowFrameShape();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyNativeWindowCorners();
        UpdateWindowFrameShape();

        try
        {
            _hotkeys.Attach(this);
            RegisterConfiguredHotkeys();
        }
        catch (Exception ex)
        {
            AppLogService.Error("Hotkey initialization failed.", ex);
        }

        try
        {
            InitializeTrayIcon();
        }
        catch (Exception ex)
        {
            AppLogService.Error("Tray icon initialization failed.", ex);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            App.Settings.Save(App.OverlaySettings);
        }
        catch (Exception ex)
        {
            AppLogService.Error("Saving settings while closing failed.", ex);
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkeys.Dispose();
        _notifyIcon?.Dispose();
        _trayIcon?.Dispose();
        _toastHideTimer.Stop();
        _toastHideTimer.Tick -= ToastHideTimer_Tick;
        App.Settings.AppSettings.PropertyChanged -= AppSettings_PropertyChanged;
        AppThemeService.ThemeApplied -= AppThemeService_ThemeApplied;
        ToastService.ToastRequested -= ToastService_ToastRequested;
        StateChanged -= MainWindow_StateChanged;
        base.OnClosed(e);
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        UpdateWindowFrameShape();
        ApplyNativeWindowCorners();
    }

    private void UpdateWindowFrameShape()
    {
        var maximized = WindowState == WindowState.Maximized;
        var radius = maximized ? 0 : NormalWindowCornerRadius;
        WindowFrameBorder.CornerRadius = new CornerRadius(radius);
        WindowFrameBorder.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);

        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (chrome is not null)
        {
            chrome.CornerRadius = new CornerRadius(radius);
        }
    }

    private void ApplyNativeWindowCorners()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var preference = (int)(WindowState == WindowState.Maximized
                ? DwmWindowCornerPreference.DoNotRound
                : DwmWindowCornerPreference.Round);
            _ = DwmSetWindowAttribute(
                hwnd,
                DwmWindowCornerPreferenceAttribute,
                ref preference,
                Marshal.SizeOf<int>());
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private void Navigate(string page)
    {
        ResetNavButtons();

        switch (page)
        {
            case "Hardware":
                _currentPage = "Hardware";
                PageHost.Content = _hardwarePage;
                PageTitle.Text = "硬件信息";
                PageSubtitle.Text = "处理器、显卡、主板、硬盘、显示器和内存";
                MarkSelected(HardwareNav);
                _hardwarePage.Refresh();
                break;
            case "Capture":
                _currentPage = "Capture";
                PageHost.Content = _capturePage;
                PageTitle.Text = "截图录屏";
                PageSubtitle.Text = "区域截图、MP4 录屏、画质和音频开关";
                MarkSelected(CaptureNav);
                break;
            case "Overlay":
                _currentPage = "Overlay";
                PageHost.Content = _overlayPage;
                PageTitle.Text = "悬浮窗";
                PageSubtitle.Text = "透明度、颜色、布局和尺寸可视化调节";
                MarkSelected(OverlayNav);
                break;
            case "Settings":
                _currentPage = "Settings";
                PageHost.Content = _settingsPage;
                PageTitle.Text = "设置";
                PageSubtitle.Text = "热键、启动项、文件保存位置和采集组件";
                MarkSelected(SettingsNav);
                break;
            case "Filter":
                _currentPage = "Filter";
                PageHost.Content = _filterPage;
                PageTitle.Text = "游戏滤镜";
                PageSubtitle.Text = "色彩、亮度、暗角和沉浸预设";
                MarkSelected(FilterNav);
                break;
            default:
                _currentPage = "Dashboard";
                PageHost.Content = _dashboardPage;
                PageTitle.Text = "实时监控";
                PageSubtitle.Text = "帧率、负载、温度和内存状态";
                MarkSelected(DashboardNav);
                break;
        }
    }

    private void ResetNavButtons()
    {
        foreach (var button in new[] { DashboardNav, HardwareNav, CaptureNav, OverlayNav, FilterNav, SettingsNav })
        {
            button.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            button.FontWeight = FontWeights.Normal;
        }
    }

    private static void MarkSelected(System.Windows.Controls.Button button)
    {
        button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentSoftBrush");
        button.FontWeight = FontWeights.SemiBold;
    }

    private void ApplySelectedNavState()
    {
        ResetNavButtons();
        MarkSelected(_currentPage switch
        {
            "Hardware" => HardwareNav,
            "Capture" => CaptureNav,
            "Overlay" => OverlayNav,
            "Settings" => SettingsNav,
            "Filter" => FilterNav,
            _ => DashboardNav
        });
    }

    private void AppThemeService_ThemeApplied(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyBackgroundMode(App.Settings.AppSettings.BackgroundMode);
            ApplySelectedNavState();
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            ApplyBackgroundMode(App.Settings.AppSettings.BackgroundMode);
            ApplySelectedNavState();
        });
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string page })
        {
            Navigate(page);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        switch (PageHost.Content)
        {
            case HardwarePage hardwarePage:
                hardwarePage.Refresh();
                break;
            case DashboardPage dashboardPage:
                dashboardPage.RefreshNow();
                break;
        }
    }

    private void Overlay_Click(object sender, RoutedEventArgs e)
    {
        ToggleOverlay();
    }

    public void HideToTray()
    {
        Hide();
        UpdateTelemetryActivity();
    }

    private void ToggleOverlay()
    {
        if (_overlayWindow is not null)
        {
            _overlayWindow.Close();
            return;
        }

        _overlayWindow = new OverlayWindow(App.Telemetry, App.OverlaySettings);
        _overlayWindow.Closed += (_, _) =>
        {
            _overlayWindow = null;
            OverlayButtonText.Text = "悬浮窗";
            OverlayButton.ToolTip = "打开悬浮监控窗";
            UpdateTelemetryActivity();
        };
        _overlayWindow.Show();
        UpdateTelemetryActivity();
        OverlayButtonText.Text = "关闭";
        OverlayButton.ToolTip = "关闭悬浮监控窗";
    }

    private async void CaptureScreenshotFromHotkey()
    {
        _ = await CaptureRegionFromUserAsync();
    }

    private async void ToggleRecordingFromHotkey()
    {
        _ = await App.Capture.ToggleRecordingAsync(RecordingOptions.FromSettings(App.Settings.AppSettings));
    }

    private void InitializeTrayIcon()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => ShowFromTray());
        menu.Items.Add("切换悬浮窗", null, (_, _) => Dispatcher.Invoke(ToggleOverlay));
        menu.Items.Add("截图", null, (_, _) => Dispatcher.Invoke(CaptureScreenshotFromHotkey));
        menu.Items.Add("开始/停止录屏", null, (_, _) => Dispatcher.Invoke(ToggleRecordingFromHotkey));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));

        _trayIcon = LoadAppIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "盔盔游戏助手",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private static Drawing.Icon LoadAppIcon()
    {
        var iconUri = new Uri("pack://application:,,,/Assets/AppIcon.ico", UriKind.Absolute);
        var resourceInfo = System.Windows.Application.GetResourceStream(iconUri);
        if (resourceInfo is null)
        {
            return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
        }

        using var stream = resourceInfo.Stream;
        using var icon = new Drawing.Icon(stream);
        return (Drawing.Icon)icon.Clone();
    }

    private void RegisterConfiguredHotkeys()
    {
        RegisterConfiguredHotkey(1, App.Settings.AppSettings.ScreenshotHotkeyText, CaptureScreenshotFromHotkey);
        RegisterConfiguredHotkey(2, App.Settings.AppSettings.RecordingHotkeyText, ToggleRecordingFromHotkey);

        if (App.Settings.AppSettings.OverlayHotkeyEnabled)
        {
            RegisterConfiguredHotkey(3, App.Settings.AppSettings.OverlayHotkeyText, ToggleOverlay);
        }
        else
        {
            _hotkeys.Unregister(3);
        }
    }

    private void RegisterConfiguredHotkey(int id, string hotkeyText, Action handler)
    {
        if (!HotkeyService.TryParseHotkey(hotkeyText, out var modifiers, out var virtualKey))
        {
            _hotkeys.Unregister(id);
            return;
        }

        if (!_hotkeys.Register(id, modifiers | HotkeyModifiers.NoRepeat, virtualKey, handler))
        {
            AppLogService.Info($"Hotkey registration skipped or failed: {hotkeyText}");
        }
    }

    private void AppSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.ScreenshotHotkeyText)
            or nameof(AppSettings.RecordingHotkeyText)
            or nameof(AppSettings.OverlayHotkeyText)
            or nameof(AppSettings.OverlayHotkeyEnabled))
        {
            RegisterConfiguredHotkeys();
        }

        if (e.PropertyName == nameof(AppSettings.StartWithWindows))
        {
            StartupService.SetEnabled(App.Settings.AppSettings.StartWithWindows);
        }

        if (e.PropertyName == nameof(AppSettings.ThemeMode))
        {
            AppThemeService.Apply(App.Settings.AppSettings.ThemeMode);
        }

        if (e.PropertyName == nameof(AppSettings.BackgroundMode))
        {
            ApplyBackgroundMode(App.Settings.AppSettings.BackgroundMode);
        }

        if (e.PropertyName == nameof(AppSettings.FontMode))
        {
            AppThemeService.ApplyFont(App.Settings.AppSettings.FontMode);
        }

        App.Settings.Save(App.OverlaySettings);
        if (ShouldShowSettingsToast(e.PropertyName))
        {
            ToastService.ShowSettingsSaved();
        }
    }

    private static bool ShouldShowSettingsToast(string? propertyName)
    {
        return propertyName is not nameof(AppSettings.LastUpdateCheckUtc)
            and not nameof(AppSettings.MemoryOptimizedDefaultsApplied);
    }

    private void ApplyBackgroundMode(AppBackgroundMode mode)
    {
        static System.Windows.Media.Color ColorFrom(string value) =>
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value)!;

        switch (mode)
        {
            case AppBackgroundMode.Solid:
                WindowBackgroundStart.Color = (System.Windows.Media.Color)FindResource("WindowBackgroundColor");
                WindowBackgroundMiddle.Color = (System.Windows.Media.Color)FindResource("WindowBackgroundColor");
                WindowBackgroundEnd.Color = (System.Windows.Media.Color)FindResource("WindowBackgroundColor");
                BackgroundGlowPrimary.Opacity = 0;
                BackgroundGlowSecondary.Opacity = 0;
                BackgroundGlowTertiary.Opacity = 0;
                break;
            case AppBackgroundMode.Acrylic:
                WindowBackgroundStart.Color = AppThemeService.IsDark ? ColorFrom("#EE2B2B2B") : ColorFrom("#F4FFFFFF");
                WindowBackgroundMiddle.Color = AppThemeService.IsDark ? ColorFrom("#EE202020") : ColorFrom("#EEF4F8FF");
                WindowBackgroundEnd.Color = AppThemeService.IsDark ? ColorFrom("#EE303030") : ColorFrom("#EEF8FBFF");
                BackgroundGlowPrimary.Opacity = AppThemeService.IsDark ? 0.10 : 0.28;
                BackgroundGlowSecondary.Opacity = AppThemeService.IsDark ? 0.04 : 0.16;
                BackgroundGlowTertiary.Opacity = AppThemeService.IsDark ? 0 : 0.08;
                break;
            case AppBackgroundMode.MicaAlt:
                WindowBackgroundStart.Color = AppThemeService.IsDark ? ColorFrom("#FF242424") : ColorFrom("#FFF8FAFC");
                WindowBackgroundMiddle.Color = AppThemeService.IsDark ? ColorFrom("#FF202020") : ColorFrom("#FFEFF4FA");
                WindowBackgroundEnd.Color = AppThemeService.IsDark ? ColorFrom("#FF2A2A2A") : ColorFrom("#FFF6F8FC");
                BackgroundGlowPrimary.Opacity = AppThemeService.IsDark ? 0.06 : 0.10;
                BackgroundGlowSecondary.Opacity = AppThemeService.IsDark ? 0.03 : 0.08;
                BackgroundGlowTertiary.Opacity = 0;
                break;
            default:
                WindowBackgroundStart.Color = (System.Windows.Media.Color)FindResource("SurfaceColor");
                WindowBackgroundMiddle.Color = (System.Windows.Media.Color)FindResource("WindowBackgroundColor");
                WindowBackgroundEnd.Color = (System.Windows.Media.Color)FindResource("SurfaceMutedColor");
                BackgroundGlowPrimary.Opacity = AppThemeService.IsDark ? 0.12 : 0.72;
                BackgroundGlowSecondary.Opacity = AppThemeService.IsDark ? 0.04 : 0.18;
                BackgroundGlowTertiary.Opacity = AppThemeService.IsDark ? 0 : 0.14;
                break;
        }
    }

    private void ToastService_ToastRequested(object? sender, ToastRequestedEventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            ShowToast(e.Message);
            return;
        }

        _ = Dispatcher.BeginInvoke(() => ShowToast(e.Message));
    }

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastHost.Visibility = Visibility.Visible;
        _toastHideTimer.Stop();

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        ToastHost.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)) { EasingFunction = easing });
        ToastTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)) { EasingFunction = easing });

        _toastHideTimer.Start();
    }

    private void ToastHideTimer_Tick(object? sender, EventArgs e)
    {
        _toastHideTimer.Stop();
        HideToast();
    }

    private void HideToast()
    {
        var opacityAnimation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        opacityAnimation.Completed += (_, _) =>
        {
            if (ToastHost.Opacity <= 0.01)
            {
                ToastHost.Visibility = Visibility.Collapsed;
            }
        };

        ToastHost.BeginAnimation(OpacityProperty, opacityAnimation);
        ToastTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(10, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
    }

    private async Task<CaptureResult?> CaptureRegionFromUserAsync()
    {
        if (_screenshotSelectionOpen)
        {
            return null;
        }

        _screenshotSelectionOpen = true;
        try
        {
            using var selector = new ScreenshotSelectionWindow();
            if (selector.ShowDialog() != Forms.DialogResult.OK || selector.SelectedBounds is not { } bounds)
            {
                return null;
            }

            using var bitmap = selector.TakeSelectedBitmap();
            if (selector.CompletionAction == ScreenshotCompletionAction.CopyToClipboard)
            {
                return bitmap is null
                    ? new CaptureResult(false, "截图复制失败：截图数据为空")
                    : CopyBitmapToClipboard(bitmap);
            }

            return bitmap is null
                ? await App.Capture.CaptureRegionAsync(bounds)
                : await App.Capture.SaveScreenshotAsync(bitmap);
        }
        finally
        {
            _screenshotSelectionOpen = false;
        }
    }

    private static CaptureResult CopyBitmapToClipboard(Drawing.Bitmap bitmap)
    {
        try
        {
            using var clipboardBitmap = (Drawing.Bitmap)bitmap.Clone();
            Forms.Clipboard.SetImage(clipboardBitmap);
            return new CaptureResult(true, "截图已复制到剪切板");
        }
        catch (Exception ex)
        {
            return new CaptureResult(false, $"截图复制失败：{ex.Message}");
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        UpdateTelemetryActivity();
    }

    private void UpdateTelemetryActivity()
    {
        var hasVisibleTelemetrySurface = IsVisible || _overlayWindow is { IsVisible: true };
        App.Telemetry.SetBackgroundMode(!hasVisibleTelemetrySurface);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            RestoreFromMaximizedDrag(e);
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void RestoreFromMaximizedDrag(MouseButtonEventArgs e)
    {
        var mouseInWindow = e.GetPosition(this);
        var mouseInTitleBar = e.GetPosition(TitleBar);
        var mouseOnScreen = PointToScreen(mouseInWindow);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
        {
            mouseOnScreen = source.CompositionTarget.TransformFromDevice.Transform(mouseOnScreen);
        }

        var restoredWidth = RestoreBounds.Width > 0 ? RestoreBounds.Width : Width;
        var xRatio = ActualWidth > 0 ? Math.Clamp(mouseInWindow.X / ActualWidth, 0, 1) : 0.5;
        var titleHeight = TitleBar.ActualHeight > 0 ? TitleBar.ActualHeight : 42;
        var yOffset = Math.Clamp(mouseInTitleBar.Y, 8, Math.Max(8, titleHeight - 8));

        WindowState = WindowState.Normal;
        Left = mouseOnScreen.X - restoredWidth * xRatio;
        Top = mouseOnScreen.Y - yOffset;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (App.Settings.AppSettings.CloseToTray)
        {
            HideToTray();
            return;
        }

        Close();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    private enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }
}
