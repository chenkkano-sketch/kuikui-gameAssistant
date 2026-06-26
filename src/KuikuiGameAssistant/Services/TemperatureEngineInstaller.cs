using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace KuikuiGameAssistant.Services;

public static class TemperatureEngineInstaller
{
    private const string PawnIoPackageId = "namazso.PawnIO";
    private const string PawnIoReleaseUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest";

    public static bool InstallOrRepairWithUi()
    {
        if (!TelemetryEngineServiceInstaller.IsAdministrator())
        {
            var answer = System.Windows.MessageBox.Show(
                "CPU 温度需要安装官方 PawnIO 低层硬件驱动，并重启后台遥测服务。\n\n来源：namazso.PawnIO（winget / GitHub 官方发布）\n安装过程可能弹出管理员确认。\n\n是否继续？",
                "修复温度引擎",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);
            if (answer != System.Windows.MessageBoxResult.Yes)
            {
                return false;
            }

            return RelaunchElevated();
        }

        try
        {
            if (!IsPawnIoInstalled())
            {
                InstallPawnIo();
            }

            TelemetryEngineServiceInstaller.InstallOrRepair();
            System.Windows.MessageBox.Show(
                "温度引擎已修复，后台遥测服务已重启。",
                "盔盔游戏助手",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex)
        {
            AppLogService.Error("Installing temperature engine failed.", ex);
            System.Windows.MessageBox.Show(
                $"温度引擎修复失败：{ex.Message}",
                "盔盔游戏助手",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);

            OpenPawnIoReleasePage();
            return false;
        }
    }

    public static bool IsPawnIoInstalled()
    {
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            return services?.GetSubKeyNames()
                .Any(x => x.Contains("Pawn", StringComparison.OrdinalIgnoreCase)
                          || x.Contains("PawnIO", StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool RelaunchElevated()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            System.Windows.MessageBox.Show(
                "无法定位当前程序路径。",
                "盔盔游戏助手",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--install-temperature-engine",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
            System.Windows.MessageBox.Show(
                "已启动管理员安装流程。\n\n请确认 UAC，并在随后弹出的 PowerShell 窗口中完成 PawnIO 安装。",
                "修复温度引擎",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void InstallPawnIo()
    {
        AppLogService.Info("Starting visible PawnIO installer via winget.");
        var script = $$"""
            $ErrorActionPreference = 'Continue'
            Write-Host ''
            Write-Host 'KuiKui 温度引擎修复'
            Write-Host '正在安装官方 PawnIO 低层硬件驱动...'
            Write-Host '来源：{{PawnIoReleaseUrl}}'
            Write-Host ''
            $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
            if ($null -eq $winget) {
                Write-Host '未找到 winget。即将打开 PawnIO 官方发布页，请手动安装后回到软件再点一次修复。'
                Start-Process '{{PawnIoReleaseUrl}}'
                Write-Host ''
                Read-Host '按回车关闭此窗口'
                exit 2
            }

            winget install --id {{PawnIoPackageId}} -e --accept-package-agreements --accept-source-agreements
            $code = $LASTEXITCODE
            Write-Host ''
            if ($code -eq 0 -or $code -eq 3010 -or $code -eq 1641) {
                Write-Host 'PawnIO 安装命令已完成。'
            } else {
                Write-Host "PawnIO 安装失败，退出码：$code"
            }
            Write-Host ''
            Read-Host '按回车关闭此窗口'
            exit $code
            """;

        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var process = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        }) ?? throw new InvalidOperationException("无法启动温度引擎安装窗口。");

        process.WaitForExit();
        AppLogService.Info($"PawnIO installer process exited with code {process.ExitCode}.");
        if (process.ExitCode is not (0 or 3010 or 1641))
        {
            throw new InvalidOperationException($"PawnIO 安装未完成，退出码 {process.ExitCode}。");
        }
    }

    private static void OpenPawnIoReleasePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(PawnIoReleaseUrl)
            {
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }
}
