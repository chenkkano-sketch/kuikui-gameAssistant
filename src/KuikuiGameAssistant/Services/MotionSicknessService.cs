using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Views;

namespace KuikuiGameAssistant.Services;

public sealed class MotionSicknessService : IDisposable
{
    private MotionSicknessWindow? _window;

    public string StatusText { get; private set; } = "辅助线未启用";

    public void Apply(MotionSicknessSettings settings)
    {
        if (!settings.IsEnabled)
        {
            Hide();
            StatusText = "辅助线未启用";
            return;
        }

        _window ??= new MotionSicknessWindow();
        _window.Apply(settings);
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        StatusText = BuildStatusText(settings);
    }

    public void Dispose()
    {
        Hide();
    }

    private void Hide()
    {
        if (_window is null)
        {
            return;
        }

        _window.Close();
        _window = null;
    }

    private static string BuildStatusText(MotionSicknessSettings settings)
    {
        var edgeCount = new[] { settings.ShowTopBar, settings.ShowBottomBar, settings.ShowLeftBar, settings.ShowRightBar }
            .Count(x => x);
        string text;
        if (settings.ShowCenterCrosshair && edgeCount > 0)
        {
            text = $"中心准心和 {edgeCount} 条边缘参考线已启用";
        }
        else if (settings.ShowCenterCrosshair)
        {
            text = "中心准心已启用";
        }
        else
        {
            text = edgeCount > 0 ? $"{edgeCount} 条边缘参考线已启用" : "已启用，但没有选择显示元素";
        }

        return settings.TunnelVisionEnabled ? $"{text}；隧道视野已启用" : text;
    }
}
