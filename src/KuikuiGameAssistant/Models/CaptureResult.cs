namespace KuikuiGameAssistant.Models;

public sealed record CaptureResult(bool Success, string Message, string? FilePath = null);
