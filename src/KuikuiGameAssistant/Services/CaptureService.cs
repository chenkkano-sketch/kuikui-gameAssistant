using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using KuikuiGameAssistant.Models;
using ScreenRecorderLib;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using Forms = System.Windows.Forms;
using ThreadingTimer = System.Threading.Timer;

namespace KuikuiGameAssistant.Services;

public sealed class CaptureService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly object _recordingSync = new();
    private Recorder? _recorder;
    private TaskCompletionSource<CaptureResult>? _recordingCompletion;
    private ThreadingTimer? _recordingStatusTimer;
    private string? _recordingPath;
    private DateTimeOffset _recordingStartedAt;
    private RecordingOptions? _activeOptions;

    public event EventHandler<RecordingStatus>? RecordingStatusChanged;

    public CaptureService(AppSettings settings)
    {
        _settings = settings;
    }

    public bool IsRecording
    {
        get
        {
            lock (_recordingSync)
            {
                return _recorder is not null;
            }
        }
    }

    public Task<CaptureResult> CapturePrimaryScreenAsync()
    {
        var bounds = Forms.Screen.PrimaryScreen?.Bounds;
        return bounds is null
            ? Task.FromResult(new CaptureResult(false, "未发现主显示器"))
            : CaptureRegionAsync(bounds.Value);
    }

    public Task<CaptureResult> CaptureRegionAsync(Rectangle bounds)
    {
        return Task.Run(() =>
        {
            try
            {
                var virtualScreen = Forms.SystemInformation.VirtualScreen;
                bounds = Rectangle.Intersect(bounds, virtualScreen);
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    return new CaptureResult(false, "截图区域无效");
                }

                var folder = ResolveFolder(_settings.ScreenshotFolder, AppSettings.DefaultScreenshotFolder);
                Directory.CreateDirectory(folder);

                var fileName = $"kuikui_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var path = Path.Combine(folder, fileName);

                using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                bitmap.Save(path, DrawingImageFormat.Png);

                return new CaptureResult(true, "截图已保存", path);
            }
            catch (Exception ex)
            {
                return new CaptureResult(false, $"截图失败：{ex.Message}");
            }
        });
    }

    public Task<CaptureResult> SaveScreenshotAsync(Bitmap bitmap)
    {
        return Task.Run(() =>
        {
            try
            {
                var folder = ResolveFolder(_settings.ScreenshotFolder, AppSettings.DefaultScreenshotFolder);
                Directory.CreateDirectory(folder);

                var fileName = $"kuikui_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var path = Path.Combine(folder, fileName);
                bitmap.Save(path, DrawingImageFormat.Png);

                return new CaptureResult(true, "截图已保存", path);
            }
            catch (Exception ex)
            {
                return new CaptureResult(false, $"截图失败：{ex.Message}");
            }
        });
    }

    public CaptureResult StartRecording(RecordingOptions options)
    {
        lock (_recordingSync)
        {
            if (_recorder is not null)
            {
                return new CaptureResult(false, "录屏已经在进行中", _recordingPath);
            }

            var screen = Forms.Screen.PrimaryScreen;
            if (screen is null)
            {
                return new CaptureResult(false, "未发现主显示器");
            }

            var folder = ResolveFolder(_settings.RecordingFolder, AppSettings.DefaultRecordingFolder);
            Directory.CreateDirectory(folder);

            _recordingPath = Path.Combine(folder, $"kuikui_recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            _recordingCompletion = new TaskCompletionSource<CaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _recordingStartedAt = DateTimeOffset.Now;
            _activeOptions = options;

            try
            {
                _recorder = Recorder.CreateRecorder(CreateRecorderOptions(screen.Bounds, options));
                _recorder.OnRecordingComplete += Recorder_OnRecordingComplete;
                _recorder.OnRecordingFailed += Recorder_OnRecordingFailed;
                _recorder.OnStatusChanged += Recorder_OnStatusChanged;
                _recorder.Record(_recordingPath);
                _recordingStatusTimer = new ThreadingTimer(_ => RaiseRecordingProgress(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                CleanupRecorder();
                return new CaptureResult(false, $"录屏启动失败：{ex.Message}", _recordingPath);
            }

            var message = BuildStartMessage(options);
            RaiseRecordingStatus(true, message, _recordingPath, TimeSpan.Zero, 0);
            return new CaptureResult(true, message, _recordingPath);
        }
    }

    public async Task<CaptureResult> StopRecordingAsync()
    {
        Recorder? recorder;
        TaskCompletionSource<CaptureResult>? completion;

        lock (_recordingSync)
        {
            if (_recorder is null || _recordingCompletion is null)
            {
                return new CaptureResult(false, "当前没有正在录制的视频");
            }

            recorder = _recorder;
            completion = _recordingCompletion;
        }

        try
        {
            recorder.Stop();
        }
        catch (Exception ex)
        {
            CompleteRecording(new CaptureResult(false, $"停止录屏失败：{ex.Message}", _recordingPath));
        }

        var timeout = Task.Delay(TimeSpan.FromSeconds(20));
        var completed = await Task.WhenAny(completion.Task, timeout).ConfigureAwait(false);
        if (completed == completion.Task)
        {
            return await completion.Task.ConfigureAwait(false);
        }

        return new CaptureResult(false, "录屏停止超时，文件可能仍在写入", _recordingPath);
    }

    public async Task<CaptureResult> ToggleRecordingAsync(RecordingOptions options)
    {
        if (IsRecording)
        {
            return await StopRecordingAsync().ConfigureAwait(false);
        }

        return StartRecording(options);
    }

    public void Dispose()
    {
        if (IsRecording)
        {
            StopRecordingAsync().GetAwaiter().GetResult();
        }
    }

    private static RecorderOptions CreateRecorderOptions(Rectangle bounds, RecordingOptions options)
    {
        var width = MakeEven(bounds.Width * Math.Clamp(options.ScalePercent, 25, 100) / 100);
        var height = MakeEven(bounds.Height * Math.Clamp(options.ScalePercent, 25, 100) / 100);
        var audioEnabled = options.RecordSystemAudio || options.RecordMicrophone;

        return new RecorderOptions
        {
            SourceOptions = new SourceOptions
            {
                RecordingSources = new List<RecordingSourceBase>
                {
                    new DisplayRecordingSource(DisplayRecordingSource.MainMonitor)
                }
            },
            OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video,
                OutputFrameSize = new ScreenSize(width, height),
                Stretch = StretchMode.Uniform
            },
            AudioOptions = new AudioOptions
            {
                IsAudioEnabled = audioEnabled,
                IsOutputDeviceEnabled = options.RecordSystemAudio,
                IsInputDeviceEnabled = options.RecordMicrophone,
                Bitrate = AudioBitrate.bitrate_128kbps,
                Channels = AudioChannels.Stereo,
                InputVolume = 0.85f,
                OutputVolume = 0.85f
            },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Bitrate = Math.Clamp(options.BitrateKbps, 4000, 16000) * 1000,
                Framerate = Math.Clamp(options.FramesPerSecond, 30, 120),
                IsFixedFramerate = true,
                Encoder = new H264VideoEncoder
                {
                    BitrateMode = H264BitrateControlMode.CBR,
                    EncoderProfile = H264Profile.Main
                },
                IsFragmentedMp4Enabled = true,
                IsHardwareEncodingEnabled = true,
                IsLowLatencyEnabled = false,
                IsMp4FastStartEnabled = false,
                IsThrottlingDisabled = false
            },
            MouseOptions = new MouseOptions
            {
                IsMousePointerEnabled = options.ShowCursor,
                IsMouseClicksDetected = false,
                MouseClickDetectionMode = MouseDetectionMode.Polling
            },
            OverlayOptions = new OverLayOptions
            {
                Overlays = new List<RecordingOverlayBase>()
            },
            SnapshotOptions = new SnapshotOptions
            {
                SnapshotsWithVideo = false,
                SnapshotsIntervalMillis = 1000,
                SnapshotFormat = ScreenRecorderLib.ImageFormat.PNG
            },
            LogOptions = new LogOptions
            {
                IsLogEnabled = true,
                LogFilePath = AppLogService.CurrentLogPath,
                LogSeverityLevel = ScreenRecorderLib.LogLevel.Debug
            }
        };
    }

    private void Recorder_OnRecordingComplete(object? sender, RecordingCompleteEventArgs e)
    {
        CompleteRecording(new CaptureResult(true, "录屏已保存", e.FilePath));
    }

    private void Recorder_OnRecordingFailed(object? sender, RecordingFailedEventArgs e)
    {
        CompleteRecording(new CaptureResult(false, $"录屏保存失败：{e.Error}", _recordingPath));
    }

    private void Recorder_OnStatusChanged(object? sender, RecordingStatusEventArgs e)
    {
        AppLogService.Info($"Recorder status changed: {e.Status}");
    }

    private void CompleteRecording(CaptureResult result)
    {
        TaskCompletionSource<CaptureResult>? completion;
        TimeSpan elapsed;

        lock (_recordingSync)
        {
            elapsed = DateTimeOffset.Now - _recordingStartedAt;
            completion = _recordingCompletion;
            CleanupRecorder();
        }

        RaiseRecordingStatus(false, result.Message, result.FilePath, elapsed, EstimateFramesWritten(elapsed));
        completion?.TrySetResult(result);
    }

    private void CleanupRecorder()
    {
        _recordingStatusTimer?.Dispose();
        _recordingStatusTimer = null;

        if (_recorder is not null)
        {
            _recorder.OnRecordingComplete -= Recorder_OnRecordingComplete;
            _recorder.OnRecordingFailed -= Recorder_OnRecordingFailed;
            _recorder.OnStatusChanged -= Recorder_OnStatusChanged;
            (_recorder as IDisposable)?.Dispose();
        }

        _recorder = null;
        _recordingCompletion = null;
        _recordingPath = null;
        _activeOptions = null;
    }

    private void RaiseRecordingProgress()
    {
        string? path;
        TimeSpan elapsed;
        int frames;

        lock (_recordingSync)
        {
            if (_recorder is null)
            {
                return;
            }

            path = _recordingPath;
            elapsed = DateTimeOffset.Now - _recordingStartedAt;
            frames = EstimateFramesWritten(elapsed);
        }

        RaiseRecordingStatus(true, $"录制中 {elapsed:mm\\:ss}", path, elapsed, frames);
    }

    private int EstimateFramesWritten(TimeSpan elapsed)
    {
        var fps = _activeOptions?.FramesPerSecond ?? _settings.RecordingFrameRate;
        return Math.Max(0, (int)Math.Round(elapsed.TotalSeconds * fps));
    }

    private static int MakeEven(int value)
    {
        value = Math.Max(2, value);
        return value % 2 == 0 ? value : value - 1;
    }

    private static string ResolveFolder(string folder, string fallback)
    {
        return string.IsNullOrWhiteSpace(folder)
            ? fallback
            : Environment.ExpandEnvironmentVariables(folder.Trim());
    }

    private static string BuildStartMessage(RecordingOptions options)
    {
        var message = $"录屏已开始：{options.FramesPerSecond} FPS，{options.BitrateKbps} kbps，MP4/H.264";
        if (options.RecordSystemAudio)
        {
            message += "，系统声音";
        }

        if (options.RecordMicrophone)
        {
            message += "，麦克风";
        }

        if (options.ShowCursor)
        {
            message += "，显示鼠标";
        }

        if (options.EnableHdr)
        {
            message += "。HDR 开关已保存，当前编码器按 SDR H.264 输出";
        }

        return message;
    }

    private void RaiseRecordingStatus(bool isRecording, string message, string? path, TimeSpan elapsed, int framesWritten)
    {
        RecordingStatusChanged?.Invoke(this, new RecordingStatus(isRecording, message, path, elapsed, framesWritten));
    }
}
