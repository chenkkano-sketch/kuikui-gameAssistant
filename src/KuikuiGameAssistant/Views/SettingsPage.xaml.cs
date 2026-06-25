using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Services;
using Forms = System.Windows.Forms;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace KuikuiGameAssistant.Views;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    private readonly Dictionary<WpfTextBox, string> _hotkeyOriginalTexts = new();
    private readonly UpdateService _updateService;

    public SettingsPage(AppSettings settings, UpdateService updateService)
    {
        InitializeComponent();
        _updateService = updateService;
        DataContext = settings;
        UpdateStatusText.Text = $"当前版本 {_updateService.CurrentVersion}，更新源 {settings.GitHubRepository}";
    }

    private async void CheckUpdate_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查 GitHub Release...";

        try
        {
            var result = await _updateService.CheckForUpdatesAsync();
            UpdateStatusText.Text = result.Message;
            if (!result.UpdateAvailable || result.Release is null)
            {
                return;
            }

            var asset = result.Release.Asset;
            var answer = System.Windows.MessageBox.Show(
                System.Windows.Window.GetWindow(this),
                $"{result.Message}\n\n包类型：{FormatPackageKind(asset.Kind)}\n文件：{asset.Name}\n大小：{FormatSize(asset.SizeBytes)}\n\n是否现在下载并更新？",
                "发现新版本",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);
            if (answer != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            CheckUpdateButton.IsEnabled = false;
            UpdateStatusText.Text = $"正在下载 {asset.Name}...";
            var applyResult = await _updateService.DownloadAndApplyAsync(result.Release);
            UpdateStatusText.Text = applyResult.Message;
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText.Text = "已取消检查更新。";
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void BrowsePresentMon_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not AppSettings settings)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 PresentMon 控制台程序",
            Filter = "PresentMon|PresentMon*.exe|可执行文件|*.exe|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            settings.PresentMonPath = dialog.FileName;
        }
    }

    private void BrowseScreenshotFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AppSettings settings)
        {
            PickFolder("选择截图保存目录", settings.ScreenshotFolder, folder => settings.ScreenshotFolder = folder);
        }
    }

    private void BrowseRecordingFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AppSettings settings)
        {
            PickFolder("选择视频保存目录", settings.RecordingFolder, folder => settings.RecordingFolder = folder);
        }
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

    private static string FormatPackageKind(UpdatePackageKind kind)
    {
        return kind == UpdatePackageKind.Installer ? "安装版" : "便携版 zip";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "未知";
        }

        var mb = bytes / 1024d / 1024d;
        return $"{mb:F1} MB";
    }

    private void HotkeyTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not WpfTextBox textBox || _hotkeyOriginalTexts.ContainsKey(textBox))
        {
            return;
        }

        _hotkeyOriginalTexts[textBox] = textBox.Text;
        textBox.Text = "正在监测修改中，按下新的快捷键";
        textBox.Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["AccentBrush"];
        textBox.SelectAll();
    }

    private void HotkeyTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not WpfTextBox textBox || !_hotkeyOriginalTexts.Remove(textBox, out var originalText))
        {
            return;
        }

        textBox.Text = originalText;
        textBox.ClearValue(WpfTextBox.ForegroundProperty);
    }

    private void HotkeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not WpfTextBox textBox)
        {
            return;
        }

        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key
        };

        if (!_hotkeyOriginalTexts.ContainsKey(textBox))
        {
            _hotkeyOriginalTexts[textBox] = textBox.Text;
        }

        if (key == Key.Escape)
        {
            RestoreHotkeyTextBox(textBox);
            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            textBox.Text = "正在监测修改中，继续按字母或功能键";
            textBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            textBox.Text = string.Empty;
            textBox.GetBindingExpression(WpfTextBox.TextProperty)?.UpdateSource();
            CommitHotkeyTextBox(textBox);
            e.Handled = true;
            return;
        }

        var keyText = ToHotkeyText(key);
        if (keyText is null)
        {
            e.Handled = true;
            return;
        }

        var parts = new List<string>();
        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(keyText);
        textBox.Text = string.Join("+", parts);
        textBox.GetBindingExpression(WpfTextBox.TextProperty)?.UpdateSource();
        CommitHotkeyTextBox(textBox);
        textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        e.Handled = true;
    }

    private void RestoreHotkeyTextBox(WpfTextBox textBox)
    {
        if (_hotkeyOriginalTexts.Remove(textBox, out var originalText))
        {
            textBox.Text = originalText;
        }

        textBox.ClearValue(WpfTextBox.ForegroundProperty);
    }

    private void CommitHotkeyTextBox(WpfTextBox textBox)
    {
        _hotkeyOriginalTexts.Remove(textBox);
        textBox.ClearValue(WpfTextBox.ForegroundProperty);
    }

    private static string? ToHotkeyText(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return key.ToString();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)(key - Key.D0)).ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return ((int)(key - Key.NumPad0)).ToString();
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            return key.ToString();
        }

        return key switch
        {
            Key.PrintScreen => "PrintScreen",
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Enter or Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Pause => "Pause",
            _ => null
        };
    }
}
