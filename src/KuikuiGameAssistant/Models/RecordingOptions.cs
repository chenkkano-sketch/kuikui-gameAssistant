namespace KuikuiGameAssistant.Models;

public sealed record RecordingOptions(
    int FramesPerSecond,
    int ScalePercent,
    int BitrateKbps,
    bool EnableHdr,
    bool RecordSystemAudio,
    bool RecordMicrophone,
    bool ShowCursor)
{
    public static RecordingOptions FromSettings(AppSettings settings)
    {
        return new RecordingOptions(
            settings.RecordingFrameRate,
            settings.RecordingScalePercent,
            settings.RecordingBitrateKbps,
            settings.RecordHdr,
            settings.RecordSystemAudio,
            settings.RecordMicrophone,
            settings.ShowMouseCursorInRecording);
    }
    public bool RequestsHdr => EnableHdr;
}
