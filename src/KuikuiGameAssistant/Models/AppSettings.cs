using System.IO;

namespace KuikuiGameAssistant.Models;

public sealed class AppSettings : System.ComponentModel.INotifyPropertyChanged
{
    private bool _startMinimizedToTray;
    private bool _startWithWindows;
    private bool _closeToTray = true;
    private bool _autoCheckUpdates = true;
    private bool _openScreenshotFolder = true;
    private bool _enablePresentMon = true;
    private bool _memoryOptimizedDefaultsApplied;
    private AppThemeMode _themeMode = AppThemeMode.System;
    private bool _recordHdr;
    private bool _recordSystemAudio;
    private bool _recordMicrophone;
    private bool _showMouseCursorInRecording = true;
    private int _recordingFrameRate = 30;
    private int _recordingScalePercent = 100;
    private int _recordingBitrateKbps = 8000;
    private string _presentMonPath = string.Empty;
    private string _screenshotFolder = DefaultScreenshotFolder;
    private string _recordingFolder = DefaultRecordingFolder;
    private string _gitHubRepository = DefaultGitHubRepository;
    private string _screenshotHotkeyText = "Ctrl+Shift+S";
    private string _recordingHotkeyText = "Ctrl+Shift+R";
    private string _overlayHotkeyText = "Ctrl+Shift+O";
    private DateTimeOffset _lastUpdateCheckUtc = DateTimeOffset.MinValue;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public const string DefaultGitHubRepository = "chenkkano-sketch/kuikui-gameAssistant";

    public static string DefaultScreenshotFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "KuikuiGameAssistant",
        "Screenshots");

    public static string DefaultRecordingFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "KuikuiGameAssistant",
        "Recordings");

    public bool StartMinimizedToTray
    {
        get => _startMinimizedToTray;
        set => SetProperty(ref _startMinimizedToTray, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set => SetProperty(ref _closeToTray, value);
    }

    public bool AutoCheckUpdates
    {
        get => _autoCheckUpdates;
        set => SetProperty(ref _autoCheckUpdates, value);
    }

    public bool OpenScreenshotFolder
    {
        get => _openScreenshotFolder;
        set => SetProperty(ref _openScreenshotFolder, value);
    }

    public bool EnablePresentMon
    {
        get => _enablePresentMon;
        set => SetProperty(ref _enablePresentMon, value);
    }

    public bool MemoryOptimizedDefaultsApplied
    {
        get => _memoryOptimizedDefaultsApplied;
        set => SetProperty(ref _memoryOptimizedDefaultsApplied, value);
    }

    public AppThemeMode ThemeMode
    {
        get => _themeMode;
        set => SetProperty(ref _themeMode, value);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool UseDarkMode
    {
        get => ThemeMode == AppThemeMode.Dark;
        set => ThemeMode = value ? AppThemeMode.Dark : AppThemeMode.Light;
    }

    public int RecordingFrameRate
    {
        get => _recordingFrameRate;
        set => SetProperty(ref _recordingFrameRate, NearestSupported(value, 30, 60, 120));
    }

    public int RecordingScalePercent
    {
        get => _recordingScalePercent;
        set => SetProperty(ref _recordingScalePercent, Math.Clamp(value, 25, 100));
    }

    public int RecordingBitrateKbps
    {
        get => _recordingBitrateKbps;
        set => SetProperty(ref _recordingBitrateKbps, NearestSupported(value, 4000, 8000, 12000, 16000));
    }

    public bool RecordHdr
    {
        get => _recordHdr;
        set => SetProperty(ref _recordHdr, value);
    }

    public bool RecordSystemAudio
    {
        get => _recordSystemAudio;
        set => SetProperty(ref _recordSystemAudio, value);
    }

    public bool RecordMicrophone
    {
        get => _recordMicrophone;
        set => SetProperty(ref _recordMicrophone, value);
    }

    public bool ShowMouseCursorInRecording
    {
        get => _showMouseCursorInRecording;
        set => SetProperty(ref _showMouseCursorInRecording, value);
    }

    public string PresentMonPath
    {
        get => _presentMonPath;
        set => SetProperty(ref _presentMonPath, value ?? string.Empty);
    }

    public string ScreenshotFolder
    {
        get => _screenshotFolder;
        set => SetProperty(ref _screenshotFolder, value ?? string.Empty);
    }

    public string RecordingFolder
    {
        get => _recordingFolder;
        set => SetProperty(ref _recordingFolder, value ?? string.Empty);
    }

    public string GitHubRepository
    {
        get => _gitHubRepository;
        set => SetProperty(ref _gitHubRepository, value ?? string.Empty);
    }

    public string ScreenshotHotkeyText
    {
        get => _screenshotHotkeyText;
        set => SetProperty(ref _screenshotHotkeyText, value ?? string.Empty);
    }

    public string RecordingHotkeyText
    {
        get => _recordingHotkeyText;
        set => SetProperty(ref _recordingHotkeyText, value ?? string.Empty);
    }

    public string OverlayHotkeyText
    {
        get => _overlayHotkeyText;
        set => SetProperty(ref _overlayHotkeyText, value ?? string.Empty);
    }

    public DateTimeOffset LastUpdateCheckUtc
    {
        get => _lastUpdateCheckUtc;
        set => SetProperty(ref _lastUpdateCheckUtc, value);
    }

    public GameFilterSettings GameFilter { get; set; } = new();

    public System.Collections.ObjectModel.ObservableCollection<MonitorModuleConfig> MonitorModules { get; set; } = MonitorModuleConfig.CreateDefaults();

    private static int NearestSupported(int value, params int[] supportedValues)
    {
        var nearest = supportedValues[0];
        var nearestDistance = Math.Abs(value - nearest);
        foreach (var supportedValue in supportedValues)
        {
            var distance = Math.Abs(value - supportedValue);
            if (distance < nearestDistance)
            {
                nearest = supportedValue;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private void SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}
