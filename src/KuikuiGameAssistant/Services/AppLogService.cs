using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;

namespace KuikuiGameAssistant.Services;

public static class AppLogService
{
    private static readonly object SyncRoot = new();

    public static string LogFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KuikuiGameAssistant",
        "Logs");

    public static string CurrentLogPath { get; } = Path.Combine(
        LogFolder,
        $"startup-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");

    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    public static void WriteStartupHeader()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";

        Info($"Version: {version}");
        Info($"Process: {Environment.ProcessPath}");
        Info($"BaseDirectory: {AppContext.BaseDirectory}");
        Info($"OS: {Environment.OSVersion}");
        Info($"Is64BitProcess: {Environment.Is64BitProcess}");
        Info($"IsAdministrator: {IsAdministrator()}");
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
            lock (SyncRoot)
            {
                File.AppendAllText(CurrentLogPath, line + Environment.NewLine);
                if (exception is not null)
                {
                    File.AppendAllText(CurrentLogPath, exception + Environment.NewLine);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to write app log: {ex}");
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
