using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using SharpAvi.Codecs;
using SharpAvi.Output;
using System.Windows.Forms;
using KuikuiGameAssistant.Models;

namespace KuikuiGameAssistant.Services;

public sealed class CaptureService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly object _recordingSync = new();
    private CancellationTokenSource? _recordingCts;
    private Task? _recordingTask;
    private string? _recordingPath;

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
                return _recordingCts is not null;
            }
        }
    }

    public Task<CaptureResult> CapturePrimaryScreenAsync()
    {
        var bounds = Screen.PrimaryScreen?.Bounds;
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
                var virtualScreen = SystemInformation.VirtualScreen;
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
                bitmap.Save(path, ImageFormat.Png);

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
                bitmap.Save(path, ImageFormat.Png);

                return new CaptureResult(true, "截图已保存", path);
            }
            catch (Exception ex)
            {
                return new CaptureResult(false, $"截图失败：{ex.Message}");
            }
        });
    }

    public CaptureResult StartRecording(int framesPerSecond, int scalePercent)
    {
        lock (_recordingSync)
        {
            if (_recordingCts is not null)
            {
                return new CaptureResult(false, "录屏已经在进行中", _recordingPath);
            }

            var screen = Screen.PrimaryScreen;
            if (screen is null)
            {
                return new CaptureResult(false, "未发现主显示器");
            }

            var folder = ResolveFolder(_settings.RecordingFolder, AppSettings.DefaultRecordingFolder);
            Directory.CreateDirectory(folder);

            _recordingPath = Path.Combine(folder, $"kuikui_recording_{DateTime.Now:yyyyMMdd_HHmmss}.avi");
            _recordingCts = new CancellationTokenSource();
            _recordingTask = Task.Run(() => RecordPrimaryScreen(screen.Bounds, _recordingPath, framesPerSecond, scalePercent, _recordingCts.Token));
            RaiseRecordingStatus(true, "录屏已开始", _recordingPath, TimeSpan.Zero, 0);

            return new CaptureResult(true, "录屏已开始", _recordingPath);
        }
    }

    public async Task<CaptureResult> StopRecordingAsync()
    {
        CancellationTokenSource? cts;
        Task? task;
        string? path;

        lock (_recordingSync)
        {
            if (_recordingCts is null)
            {
                return new CaptureResult(false, "当前没有正在录制的视频");
            }

            cts = _recordingCts;
            task = _recordingTask;
            path = _recordingPath;
            _recordingCts = null;
            _recordingTask = null;
        }

        cts.Cancel();

        try
        {
            if (task is not null)
            {
                await task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            cts.Dispose();
            RaiseRecordingStatus(false, $"录屏保存失败：{ex.Message}", path, TimeSpan.Zero, 0);
            return new CaptureResult(false, $"录屏保存失败：{ex.Message}", path);
        }

        cts.Dispose();
        RaiseRecordingStatus(false, "录屏已保存", path, TimeSpan.Zero, 0);
        return new CaptureResult(true, "录屏已保存", path);
    }

    public async Task<CaptureResult> ToggleRecordingAsync(int framesPerSecond, int scalePercent)
    {
        if (IsRecording)
        {
            return await StopRecordingAsync().ConfigureAwait(false);
        }

        return StartRecording(framesPerSecond, scalePercent);
    }

    public void Dispose()
    {
        if (IsRecording)
        {
            StopRecordingAsync().GetAwaiter().GetResult();
        }
    }

    private void RecordPrimaryScreen(Rectangle bounds, string path, int framesPerSecond, int scalePercent, CancellationToken cancellationToken)
    {
        framesPerSecond = Math.Clamp(framesPerSecond, 5, 60);
        scalePercent = Math.Clamp(scalePercent, 25, 100);

        var width = MakeEven(bounds.Width * scalePercent / 100);
        var height = MakeEven(bounds.Height * scalePercent / 100);
        var frameInterval = TimeSpan.FromMilliseconds(1000d / framesPerSecond);
        var started = DateTimeOffset.Now;
        var framesWritten = 0;

        using var writer = new AviWriter(path)
        {
            FramesPerSecond = framesPerSecond,
            EmitIndex1 = true
        };
        var stream = writer.AddMJpegWpfVideoStream(width, height, quality: 85);

        using var sourceBitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        using var frameBitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        using var sourceGraphics = Graphics.FromImage(sourceBitmap);
        using var frameGraphics = Graphics.FromImage(frameBitmap);
        frameGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

        var frameBuffer = new byte[width * height * 4];
        var nextFrameAt = DateTimeOffset.Now;

        while (!cancellationToken.IsCancellationRequested)
        {
            sourceGraphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            frameGraphics.DrawImage(sourceBitmap, new Rectangle(0, 0, width, height));

            CopyBitmapToTopDownBgr32(frameBitmap, frameBuffer);
            stream.WriteFrame(true, frameBuffer, 0, frameBuffer.Length);
            framesWritten++;

            var elapsed = DateTimeOffset.Now - started;
            if (framesWritten == 1 || framesWritten % framesPerSecond == 0)
            {
                RaiseRecordingStatus(true, $"录制中 {elapsed:mm\\:ss}", path, elapsed, framesWritten);
            }

            nextFrameAt += frameInterval;
            var delay = nextFrameAt - DateTimeOffset.Now;
            if (delay > TimeSpan.Zero)
            {
                cancellationToken.WaitHandle.WaitOne(delay);
            }
        }
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

    private static void CopyBitmapToTopDownBgr32(Bitmap bitmap, byte[] destination)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);

        try
        {
            var stride = Math.Abs(data.Stride);
            var rowBytes = bitmap.Width * 4;
            var temp = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, temp, 0, temp.Length);

            for (var y = 0; y < bitmap.Height; y++)
            {
                var sourceRow = data.Stride > 0 ? y : bitmap.Height - 1 - y;
                Buffer.BlockCopy(temp, sourceRow * stride, destination, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private void RaiseRecordingStatus(bool isRecording, string message, string? path, TimeSpan elapsed, int framesWritten)
    {
        RecordingStatusChanged?.Invoke(this, new RecordingStatus(isRecording, message, path, elapsed, framesWritten));
    }
}
