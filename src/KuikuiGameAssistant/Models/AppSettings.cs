using System.IO;

namespace KuikuiGameAssistant.Models;

public sealed class AppSettings : System.ComponentModel.INotifyPropertyChanged
{
    private bool _startMinimizedToTray;
    private bool _startWithWindows;
    private bool _autoCheckUpdates = true;
    private bool _openScreenshotFolder = true;
    private bool _enablePresentMon = true;
    private int _recordingFrameRate = 15;
    private int _recordingScalePercent = 75;
    private string _presentMonPath = string.Empty;
    private string _screenshotFolder = DefaultScreenshotFolder;
    private string _recordingFolder = DefaultRecordingFolder;
    private string _gitHubRepository = "chenkkano-sketch/kuikui-gameAssistant";
    private string _screenshotHotkeyText = "Ctrl+Shift+S";
    private string _recordingHotkeyText = "Ctrl+Shift+R";
    private string _overlayHotkeyText = "Ctrl+Shift+O";
    private DateTimeOffset _lastUpdateCheckUtc = DateTimeOffset.MinValue;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

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

    public int RecordingFrameRate
    {
        get => _recordingFrameRate;
        set => SetProperty(ref _recordingFrameRate, Math.Clamp(value, 5, 60));
    }

    public int RecordingScalePercent
    {
        get => _recordingScalePercent;
        set => SetProperty(ref _recordingScalePercent, Math.Clamp(value, 25, 100));
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
