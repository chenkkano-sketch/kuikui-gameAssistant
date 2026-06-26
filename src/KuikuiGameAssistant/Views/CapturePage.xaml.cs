using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace KuikuiGameAssistant.Views;

public partial class CapturePage : System.Windows.Controls.UserControl
{
    private readonly CaptureService _captureService;
    private readonly AppSettings _settings;

    public CapturePage(CaptureService captureService, AppSettings settings)
    {
        InitializeComponent();
        _captureService = captureService;
        _settings = settings;
        DataContext = _settings;
        FrameRateCombo.ItemsSource = new[]
        {
            new SelectionOption(30, "30 FPS"),
            new SelectionOption(60, "60 FPS"),
            new SelectionOption(120, "120 FPS")
        };
        BitrateCombo.ItemsSource = new[]
        {
            new SelectionOption(4000, "4000 kbps（流畅）"),
            new SelectionOption(8000, "8000 kbps（高清）"),
            new SelectionOption(12000, "12000 kbps（超清）"),
            new SelectionOption(16000, "16000 kbps（极致）")
        };
        ScaleCombo.ItemsSource = new[]
        {
            new SelectionOption(75, "75%（平衡）"),
            new SelectionOption(100, "100%（原始）")
        };
        _captureService.RecordingStatusChanged += CaptureService_RecordingStatusChanged;
        UpdateRecordingHint();
        _settings.PropertyChanged += Settings_PropertyChanged;
    }

    private async void Screenshot_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ScreenshotStatus.Text = "拖拽选择截图区域...";
        using var selector = new ScreenshotSelectionWindow();
        if (selector.ShowDialog() != Forms.DialogResult.OK || selector.SelectedBounds is not { } bounds)
        {
            ScreenshotStatus.Text = "已取消截图";
            return;
        }

        ScreenshotStatus.Text = "正在处理截图...";
        using var bitmap = selector.TakeSelectedBitmap();
        if (selector.CompletionAction == ScreenshotCompletionAction.CopyToClipboard)
        {
            var copyResult = bitmap is null
                ? new CaptureResult(false, "截图复制失败：截图数据为空")
                : CopyBitmapToClipboard(bitmap);
            ScreenshotStatus.Text = copyResult.Message;
            return;
        }

        var result = bitmap is null
            ? await _captureService.CaptureRegionAsync(bounds)
            : await _captureService.SaveScreenshotAsync(bitmap);
        ScreenshotStatus.Text = result.FilePath is null ? result.Message : $"{result.Message}：{result.FilePath}";

        if (result.Success && result.FilePath is not null)
        {
            if (_settings.OpenScreenshotFolder)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.GetDirectoryName(result.FilePath)!,
                    UseShellExecute = true
                });
            }
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

    private async void Record_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        RecordButton.IsEnabled = false;
        var result = await _captureService.ToggleRecordingAsync(RecordingOptions.FromSettings(_settings));
        RecordStatus.Text = result.FilePath is null ? result.Message : $"{result.Message}：{result.FilePath}";
        RecordButton.IsEnabled = true;

        if (result.Success && !_captureService.IsRecording && result.FilePath is not null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetDirectoryName(result.FilePath)!,
                UseShellExecute = true
            });
        }
    }

    private void CaptureService_RecordingStatusChanged(object? sender, RecordingStatus e)
    {
        Dispatcher.Invoke(() =>
        {
            RecordButtonText.Text = e.IsRecording ? "停止录屏" : "开始录屏";
            RecordStatus.Text = e.FilePath is null
                ? e.Message
                : $"{e.Message}  帧数 {e.FramesWritten}  {e.FilePath}";
        });
    }

    private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var recordingOptionChanged = e.PropertyName is nameof(AppSettings.RecordingHotkeyText)
            or nameof(AppSettings.RecordingFrameRate)
            or nameof(AppSettings.RecordingScalePercent)
            or nameof(AppSettings.RecordingBitrateKbps)
            or nameof(AppSettings.RecordHdr)
            or nameof(AppSettings.RecordSystemAudio)
            or nameof(AppSettings.RecordMicrophone)
            or nameof(AppSettings.ShowMouseCursorInRecording);

        if (recordingOptionChanged && !_captureService.IsRecording)
        {
            Dispatcher.Invoke(UpdateRecordingHint);
        }
    }

    private void UpdateRecordingHint()
    {
        var hint = $"快捷键：{_settings.RecordingHotkeyText}。{_settings.RecordingFrameRate} FPS，{_settings.RecordingBitrateKbps} kbps，{_settings.RecordingScalePercent}% 分辨率";
        var options = RecordingOptions.FromSettings(_settings);
        if (options.ShowCursor)
        {
            hint += "，显示鼠标";
        }

        if (options.RequestsHdr)
        {
            hint += "。HDR 开关已保存，当前编码器按 SDR H.264 输出";
        }

        RecordStatus.Text = hint;
    }

    private void BrowseScreenshotFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        PickFolder("选择截图保存目录", _settings.ScreenshotFolder, folder => _settings.ScreenshotFolder = folder);
    }

    private void BrowseRecordingFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        PickFolder("选择视频保存目录", _settings.RecordingFolder, folder => _settings.RecordingFolder = folder);
    }

    private static void PickFolder(string title, string currentFolder, Action<string> apply)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(currentFolder) ? currentFolder : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            apply(dialog.SelectedPath);
        }
    }

    private sealed record SelectionOption(int Value, string Label)
    {
        public override string ToString() => Label;
    }
}
