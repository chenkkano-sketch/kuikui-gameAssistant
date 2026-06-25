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
        var result = await _captureService.ToggleRecordingAsync(_settings.RecordingFrameRate, _settings.RecordingScalePercent);
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
        if (e.PropertyName == nameof(AppSettings.RecordingHotkeyText) && !_captureService.IsRecording)
        {
            Dispatcher.Invoke(UpdateRecordingHint);
        }
    }

    private void UpdateRecordingHint()
    {
        RecordStatus.Text = $"快捷键：{_settings.RecordingHotkeyText}。默认 {_settings.RecordingFrameRate} FPS，{_settings.RecordingScalePercent}% 缩放。";
    }
}
