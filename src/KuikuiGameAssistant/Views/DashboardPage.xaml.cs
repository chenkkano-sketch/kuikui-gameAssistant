using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Services;
using KuikuiGameAssistant.ViewModels;

namespace KuikuiGameAssistant.Views;

public partial class DashboardPage : System.Windows.Controls.UserControl
{
    private readonly TelemetryService _telemetry;
    private readonly DashboardViewModel _viewModel = new();

    public DashboardPage(TelemetryService telemetry)
    {
        InitializeComponent();
        _telemetry = telemetry;
        DataContext = _viewModel;

        _telemetry.SnapshotUpdated += Telemetry_SnapshotUpdated;
        _viewModel.Apply(_telemetry.Latest);
    }

    public void RefreshNow() => _telemetry.RefreshNow();

    private void FpsAssist_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        switch (_viewModel.FpsAction)
        {
            case DashboardFpsAction.EnablePresentMon:
                _telemetry.EnablePresentMon();
                App.Settings.Save(App.OverlaySettings);
                break;
            case DashboardFpsAction.RestartPresentMon:
                _telemetry.RestartPresentMon();
                break;
            case DashboardFpsAction.RestartAsAdmin:
                RestartAsAdmin();
                break;
            case DashboardFpsAction.SelectPresentMonPath:
                SelectPresentMonPath();
                break;
        }

        _telemetry.RefreshNow();
    }

    private void Telemetry_SnapshotUpdated(object? sender, RealtimeSnapshot e)
    {
        Dispatcher.Invoke(() => _viewModel.Apply(e));
    }

    private static void SelectPresentMonPath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 PresentMon 控制台程序",
            Filter = "PresentMon|PresentMon*.exe|可执行文件|*.exe|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        App.Settings.AppSettings.PresentMonPath = dialog.FileName;
        App.Settings.AppSettings.EnablePresentMon = true;
        App.Settings.Save(App.OverlaySettings);
        App.Telemetry.RestartPresentMon();
    }

    private static void RestartAsAdmin()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            System.Windows.MessageBox.Show(
                "无法定位当前程序路径。",
                "管理员重启",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Directory.Exists(AppContext.BaseDirectory)
                    ? AppContext.BaseDirectory
                    : Path.GetDirectoryName(exePath)
            });
            System.Windows.Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            System.Windows.MessageBox.Show(
                "已取消管理员重启。",
                "管理员重启",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"管理员重启失败：{ex.Message}",
                "管理员重启",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }
}
