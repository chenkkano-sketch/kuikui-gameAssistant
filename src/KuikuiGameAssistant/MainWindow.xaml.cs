using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Services;
using KuikuiGameAssistant.Views;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace KuikuiGameAssistant;

public partial class MainWindow : Window
{
    private readonly DashboardPage _dashboardPage;
    private readonly HardwarePage _hardwarePage;
    private readonly CapturePage _capturePage;
    private readonly OverlayPage _overlayPage;
    private readonly GameFilterPage _filterPage;
    private readonly SettingsPage _settingsPage;
    private readonly HotkeyService _hotkeys = new();
    private Forms.NotifyIcon? _notifyIcon;
    private Drawing.Icon? _trayIcon;
    private OverlayWindow? _overlayWindow;
    private bool _screenshotSelectionOpen;

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

        Navigate("Dashboard");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
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
        App.Settings.AppSettings.PropertyChanged -= AppSettings_PropertyChanged;
        base.OnClosed(e);
    }

    private void Navigate(string page)
    {
        ResetNavButtons();

        switch (page)
        {
            case "Hardware":
                PageHost.Content = _hardwarePage;
                PageTitle.Text = "硬件信息";
                PageSubtitle.Text = "处理器、显卡、主板、硬盘、显示器和内存";
                MarkSelected(HardwareNav);
                _hardwarePage.Refresh();
                break;
            case "Capture":
                PageHost.Content = _capturePage;
                PageTitle.Text = "截图录屏";
                PageSubtitle.Text = "区域截图、MP4 录屏、画质和音频开关";
                MarkSelected(CaptureNav);
                break;
            case "Overlay":
                PageHost.Content = _overlayPage;
                PageTitle.Text = "悬浮窗";
                PageSubtitle.Text = "透明度、颜色、布局和尺寸可视化调节";
                MarkSelected(OverlayNav);
                break;
            case "Settings":
                PageHost.Content = _settingsPage;
                PageTitle.Text = "设置";
                PageSubtitle.Text = "热键、启动项、文件保存位置和采集组件";
                MarkSelected(SettingsNav);
                break;
            case "Filter":
                PageHost.Content = _filterPage;
                PageTitle.Text = "游戏滤镜";
                PageSubtitle.Text = "色彩、亮度、暗角和沉浸预设";
                MarkSelected(FilterNav);
                break;
            default:
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
            button.Background = System.Windows.Media.Brushes.Transparent;
            button.FontWeight = FontWeights.Normal;
        }
    }

    private static void MarkSelected(System.Windows.Controls.Button button)
    {
        button.Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["AccentSoftBrush"];
        button.FontWeight = FontWeights.SemiBold;
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
        if (_overlayWindow is { IsVisible: true })
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
        RegisterConfiguredHotkey(3, App.Settings.AppSettings.OverlayHotkeyText, ToggleOverlay);
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
            or nameof(AppSettings.OverlayHotkeyText))
        {
            RegisterConfiguredHotkeys();
        }

        if (e.PropertyName == nameof(AppSettings.StartWithWindows))
        {
            StartupService.SetEnabled(App.Settings.AppSettings.StartWithWindows);
        }

        if (e.PropertyName == nameof(AppSettings.UseDarkMode))
        {
            AppThemeService.Apply(App.Settings.AppSettings.UseDarkMode);
        }

        App.Settings.Save(App.OverlaySettings);
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

        DragMove();
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
}
