using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace KuikuiGameAssistant.Services;

public static class TelemetryEngineServiceInstaller
{
    private const string ServiceName = "KuikuiTelemetryService";
    private const string ServiceDisplayName = "Kuikui Telemetry Service";
    private const string ServiceExecutableName = "KuikuiTelemetryService.exe";
    private const string ServiceProjectName = "KuikuiTelemetryService";
    private const string TargetFramework = "net8.0-windows10.0.17763.0";

    public static bool InstallOrRepairWithUi()
    {
        if (!IsAdministrator())
        {
            return RelaunchElevated();
        }

        try
        {
            InstallOrRepair();
            System.Windows.MessageBox.Show(
                "后台遥测服务已修复并启动。",
                "盔盔游戏助手",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex)
        {
            AppLogService.Error("Installing telemetry service failed.", ex);
            System.Windows.MessageBox.Show(
                $"FPS 引擎修复失败：{ex.Message}",
                "盔盔游戏助手",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }
    }

    public static void InstallOrRepair()
    {
        var serviceExe = FindServiceExecutable()
            ?? throw new FileNotFoundException("未找到 KuikuiTelemetryService.exe。");

        if (ServiceExists())
        {
            StopServiceIfRunning();
            RunSc(ignoreExitCode: false, "config", ServiceName, "binPath=", Quote(serviceExe), "start=", "auto", "DisplayName=", Quote(ServiceDisplayName));
        }
        else
        {
            RunSc(ignoreExitCode: false, "create", ServiceName, "binPath=", Quote(serviceExe), "start=", "auto", "DisplayName=", Quote(ServiceDisplayName));
        }

        RunSc(ignoreExitCode: true, "failure", ServiceName, "reset=", "60", "actions=", "restart/3000/restart/10000/none/0");
        RunSc(ignoreExitCode: false, "start", ServiceName);
    }

    private static void StopServiceIfRunning()
    {
        var state = QueryServiceState();
        if (state is null or "STOPPED")
        {
            return;
        }

        RunSc(ignoreExitCode: true, "stop", ServiceName);
        var deadline = DateTimeOffset.Now.AddSeconds(20);
        while (DateTimeOffset.Now < deadline)
        {
            Thread.Sleep(500);
            state = QueryServiceState();
            if (state is null or "STOPPED")
            {
                return;
            }
        }

        throw new TimeoutException("后台遥测服务停止超时，请稍后重试。");
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
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
                Arguments = "--install-telemetry-service",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool ServiceExists()
    {
        return RunSc(ignoreExitCode: true, "query", ServiceName) == 0;
    }

    private static string? QueryServiceState()
    {
        var startInfo = new ProcessStartInfo("sc.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("query");
        startInfo.ArgumentList.Add(ServiceName);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 sc.exe。");
        process.WaitForExit(5000);
        var output = process.StandardOutput.ReadToEnd();
        if (process.ExitCode != 0)
        {
            return null;
        }

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? null : parts[^1];
        }

        return null;
    }

    private static int RunSc(bool ignoreExitCode, params string[] args)
    {
        var startInfo = new ProcessStartInfo("sc.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 sc.exe。");
        process.WaitForExit(15000);
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0 && !ignoreExitCode)
        {
            throw new InvalidOperationException($"sc.exe {string.Join(' ', args)} 失败：{output}{error}");
        }

        return process.ExitCode;
    }

    private static string? FindServiceExecutable()
    {
        foreach (var candidate in EnumerateServiceExecutablePaths())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateServiceExecutablePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in EnumerateServiceExecutablePathCandidates())
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate);
            }
            catch
            {
                continue;
            }

            if (seen.Add(fullPath))
            {
                yield return fullPath;
            }
        }
    }

    private static IEnumerable<string> EnumerateServiceExecutablePathCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "service", ServiceExecutableName);
        yield return Path.Combine(AppContext.BaseDirectory, ServiceExecutableName);

        var directories = EnumerateBaseDirectories().ToArray();

        foreach (var directory in directories)
        {
            yield return Path.Combine(directory.FullName, "service", ServiceExecutableName);
            yield return Path.Combine(directory.FullName, "artifacts", "dev-service-next", "service", ServiceExecutableName);
            yield return Path.Combine(directory.FullName, "artifacts", "dev-service", "service", ServiceExecutableName);
            yield return Path.Combine(directory.FullName, "artifacts", "publish", "win-x64", "service", ServiceExecutableName);
            yield return Path.Combine(directory.FullName, "artifacts", "portable", "KuikuiGameAssistant", "service", ServiceExecutableName);
        }

        foreach (var directory in directories)
        {
            yield return Path.Combine(directory.FullName, "src", ServiceProjectName, "bin", "x64", "Release", TargetFramework, "win-x64", ServiceExecutableName);
            yield return Path.Combine(directory.FullName, ServiceProjectName, "bin", "x64", "Release", TargetFramework, "win-x64", ServiceExecutableName);
        }

        foreach (var directory in directories)
        {
            yield return Path.Combine(directory.FullName, "src", ServiceProjectName, "bin", "x64", "Debug", TargetFramework, ServiceExecutableName);
            yield return Path.Combine(directory.FullName, ServiceProjectName, "bin", "x64", "Debug", TargetFramework, ServiceExecutableName);
        }
    }

    private static IEnumerable<DirectoryInfo> EnumerateBaseDirectories()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            yield return directory;
            directory = directory.Parent;
        }
    }

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}
