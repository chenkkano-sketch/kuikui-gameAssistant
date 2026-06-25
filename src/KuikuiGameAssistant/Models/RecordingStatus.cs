namespace KuikuiGameAssistant.Models;

public sealed record RecordingStatus(
    bool IsRecording,
    string Message,
    string? FilePath,
    TimeSpan Elapsed,
    int FramesWritten);
