using System.Windows;
using KuikuiGameAssistant.Services;
using KuikuiGameAssistant.ViewModels;

namespace KuikuiGameAssistant;

public partial class App : System.Windows.Application
{
    public static TelemetryService Telemetry { get; private set; } = null!;
    public static HardwareInfoService HardwareInfo { get; private set; } = null!;
    public static CaptureService Capture { get; private set; } = null!;
    public static UpdateService Updates { get; private set; } = null!;
    public static OverlaySettings OverlaySettings { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        OverlaySettings = new OverlaySettings();
        Settings = new SettingsService();
        Settings.Load(OverlaySettings);
        Telemetry = new TelemetryService(Settings.AppSettings);
        HardwareInfo = new HardwareInfoService();
        Capture = new CaptureService(Settings.AppSettings);
        Updates = new UpdateService(Settings.AppSettings);
        StartupService.SetEnabled(Settings.AppSettings.StartWithWindows);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        _ = CheckUpdatesOnStartupAsync(mainWindow);
        if (Settings.AppSettings.StartMinimizedToTray)
        {
            mainWindow.HideToTray();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Settings.Save(OverlaySettings);
        Capture.Dispose();
        Telemetry.Dispose();
        base.OnExit(e);
    }

    private static async Task CheckUpdatesOnStartupAsync(Window owner)
    {
        if (!Updates.ShouldCheckOnStartup())
        {
            return;
        }

        await Task.Delay(1800);
        var result = await Updates.CheckForUpdatesAsync();
        if (!result.UpdateAvailable || result.Release is null)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            owner,
            $"{result.Message}\n\n是否现在下载并更新？",
            "发现新版本",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (answer == MessageBoxResult.Yes)
        {
            await Updates.DownloadAndApplyAsync(result.Release);
        }
    }
}
