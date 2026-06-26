using System.Windows;
using System.Windows.Threading;
using KuikuiGameAssistant.Services;
using KuikuiGameAssistant.ViewModels;

namespace KuikuiGameAssistant;

public partial class App : System.Windows.Application
{
    public static TelemetryService Telemetry { get; private set; } = null!;
    public static HardwareInfoService HardwareInfo { get; private set; } = null!;
    public static CaptureService Capture { get; private set; } = null!;
    public static UpdateService Updates { get; private set; } = null!;
    public static GameFilterService GameFilters { get; private set; } = null!;
    public static OverlaySettings OverlaySettings { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterGlobalExceptionHandlers();
        AppLogService.WriteStartupHeader();
        base.OnStartup(e);

        try
        {
            if (e.Args.Any(x => x.Equals("--install-telemetry-service", StringComparison.OrdinalIgnoreCase)))
            {
                AppLogService.Info("Installing telemetry service from elevated helper.");
                TelemetryEngineServiceInstaller.InstallOrRepairWithUi();
                Shutdown();
                return;
            }

            if (e.Args.Any(x => x.Equals("--install-temperature-engine", StringComparison.OrdinalIgnoreCase)))
            {
                AppLogService.Info("Installing temperature engine from elevated helper.");
                TemperatureEngineInstaller.InstallOrRepairWithUi();
                Shutdown();
                return;
            }

            AppLogService.Info("Creating overlay settings.");
            OverlaySettings = new OverlaySettings();

            AppLogService.Info("Loading settings.");
            Settings = new SettingsService();
            Settings.Load(OverlaySettings);
            AppThemeService.Start(Settings.AppSettings);

            AppLogService.Info("Starting telemetry service.");
            Telemetry = new TelemetryService(Settings.AppSettings);

            AppLogService.Info("Creating app services.");
            HardwareInfo = new HardwareInfoService();
            Capture = new CaptureService(Settings.AppSettings);
            Updates = new UpdateService(Settings.AppSettings);
            GameFilters = new GameFilterService();
            GameFilters.Apply(Settings.AppSettings.GameFilter);

            AppLogService.Info("Applying startup registration.");
            StartupService.SetEnabled(Settings.AppSettings.StartWithWindows);

            AppLogService.Info("Creating main window.");
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            _ = CheckUpdatesOnStartupAsync(mainWindow);
            if (Settings.AppSettings.StartMinimizedToTray)
            {
                mainWindow.HideToTray();
            }

            AppLogService.Info("Startup completed.");
        }
        catch (Exception ex)
        {
            AppLogService.Error("Startup failed.", ex);
            System.Windows.MessageBox.Show(
                $"盔盔游戏助手启动失败。\n\n错误：{ex.Message}\n\n日志：{AppLogService.CurrentLogPath}",
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Settings?.Save(OverlaySettings);
        }
        catch (Exception ex)
        {
            AppLogService.Error("Saving settings on exit failed.", ex);
        }

        try
        {
            Capture?.Dispose();
            Telemetry?.Dispose();
            GameFilters?.Dispose();
            AppThemeService.Stop();
        }
        catch (Exception ex)
        {
            AppLogService.Error("Disposing services failed.", ex);
        }

        AppLogService.Info("Application exited.");
        base.OnExit(e);
    }

    private static async Task CheckUpdatesOnStartupAsync(Window owner)
    {
        try
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

            var asset = result.Release.Asset;
            var answer = System.Windows.MessageBox.Show(
                owner,
                $"{result.Message}\n\n文件：{asset.Name}\n下载链接：{asset.DownloadUrl}\n\n是否现在下载并更新？",
                "发现新版本",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes)
            {
                await Updates.DownloadAndApplyAsync(result.Release);
            }
        }
        catch (Exception ex)
        {
            AppLogService.Error("Startup update check failed.", ex);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogService.Error("Unhandled UI exception.", e.Exception);
        e.Handled = true;
        System.Windows.MessageBox.Show(
            $"发生了一个未处理错误，程序已尽量继续运行。\n\n错误：{e.Exception.Message}\n\n日志：{AppLogService.CurrentLogPath}",
            "盔盔游戏助手",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLogService.Error("Unhandled domain exception.", e.ExceptionObject as Exception);
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogService.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }
}
