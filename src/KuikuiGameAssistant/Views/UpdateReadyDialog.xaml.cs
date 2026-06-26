using System.Windows;
using KuikuiGameAssistant.Models;

namespace KuikuiGameAssistant.Views;

public partial class UpdateReadyDialog : Window
{
    public UpdateReadyDialog(UpdateRelease release)
    {
        InitializeComponent();
        MessageTextBlock.Text =
            $"{release.TagName} 已经下载完成。\n\n稍后再说：下次打开软件时会自动更新。\n立即重启更新：关闭当前软件并马上启动更新程序。";
    }

    public bool RestartRequested { get; private set; }

    private void RestartNow_Click(object sender, RoutedEventArgs e)
    {
        RestartRequested = true;
        DialogResult = true;
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        RestartRequested = false;
        DialogResult = false;
    }
}
