namespace KuikuiGameAssistant.Services;

public static class ToastService
{
    public static event EventHandler<ToastRequestedEventArgs>? ToastRequested;

    public static void Show(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        ToastRequested?.Invoke(null, new ToastRequestedEventArgs(message));
    }

    public static void ShowSettingsSaved() => Show("设置已保存");
}

public sealed class ToastRequestedEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}
