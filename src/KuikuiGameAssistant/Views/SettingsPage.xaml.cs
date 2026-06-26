using System.Diagnostics;
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
    private const string BlogUrl = "https://www.kkano.cc";
    private const string TavernUrl = "https://www.kkano.cc/#/tavern";

    public SettingsPage(AppSettings settings, UpdateService updateService)
    {
        InitializeComponent();
        _updateService = updateService;
        ThemeModeComboBox.ItemsSource = ThemeModeOption.All;
        DataContext = settings;
        UpdateStatusText.Text = $"当前版本 {_updateService.CurrentVersion}。GitHub API 受限时可复制固定直链手动下载。";
    }

    private async void CheckUpdate_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查 GitHub Release...";

        try
        {
            var result = await _updateService.CheckForUpdatesAsync();
            UpdateStatusText.Text = BuildUpdateStatusText(result.Message, result.Release?.Asset.DownloadUrl);
            if (!result.UpdateAvailable || result.Release is null)
            {
                return;
            }

            var asset = result.Release.Asset;
            var answer = System.Windows.MessageBox.Show(
                System.Windows.Window.GetWindow(this),
                $"{result.Message}\n\n包类型：{FormatPackageKind(asset.Kind)}\n文件：{asset.Name}\n大小：{FormatSize(asset.SizeBytes)}\n下载链接：{asset.DownloadUrl}\n\n是否现在下载并更新？",
                "发现新版本",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);
            if (answer != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            CheckUpdateButton.IsEnabled = false;
            UpdateStatusText.Text = $"正在下载 {asset.Name}...";
            ShowUpdateProgress(isIndeterminate: true);
            var progress = new Progress<UpdateDownloadProgress>(value => ApplyUpdateProgress(asset.Name, value));
            var stageResult = await _updateService.DownloadAndStageAsync(result.Release, progress);
            UpdateStatusText.Text = stageResult.Message;
            HideUpdateProgress();
            if (!stageResult.Success)
            {
                return;
            }

            if (ShowUpdateReadyDialog(result.Release))
            {
                var applyResult = _updateService.ApplyPendingUpdateAndShutdown();
                UpdateStatusText.Text = applyResult.Message;
            }
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText.Text = "已取消检查更新。";
            HideUpdateProgress();
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

    private void RestartAsAdmin_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            System.Windows.MessageBox.Show(
                "无法定位当前程序路径。",
                "管理员重启",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
            System.Windows.Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            UpdateStatusText.Text = "已取消管理员重启。";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"管理员重启失败：{ex.Message}",
                "管理员重启",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private void OpenRepository_Click(object sender, System.Windows.RoutedEventArgs e) => OpenUrl(_updateService.RepositoryUrl);

    private void OpenLatestRelease_Click(object sender, System.Windows.RoutedEventArgs e) => OpenUrl(_updateService.LatestReleaseUrl);

    private void CopyDownloadLink_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            var links = BuildManualDownloadText();
            System.Windows.Clipboard.SetText(links);
            UpdateStatusText.Text = $"已复制下载入口：\n{links}";
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"复制下载链接失败：{ex.Message}\n\n{BuildManualDownloadText()}";
        }
    }

    private void OpenBlog_Click(object sender, System.Windows.RoutedEventArgs e) => OpenUrl(BlogUrl);

    private void OpenTavern_Click(object sender, System.Windows.RoutedEventArgs e) => OpenUrl(TavernUrl);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"无法打开链接：{ex.Message}",
                "打开链接失败",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private string BuildUpdateStatusText(string message, string? downloadUrl = null)
    {
        var text = string.IsNullOrWhiteSpace(downloadUrl)
            ? message
            : $"{message}\n下载链接：{downloadUrl}";
        return $"{text}\n\n{BuildManualDownloadText()}";
    }

    private string BuildManualDownloadText()
    {
        return $"最新 Release：{_updateService.LatestReleaseUrl}\n固定安装版直链：{_updateService.InstallerDirectDownloadUrl}\n固定便携版直链：{_updateService.PortableDirectDownloadUrl}\n当前版本安装版：{_updateService.CurrentInstallerDownloadUrl}\n当前版本便携版：{_updateService.CurrentPortableDownloadUrl}";
    }

    private void ShowUpdateProgress(bool isIndeterminate)
    {
        UpdateProgressBar.Visibility = System.Windows.Visibility.Visible;
        UpdateProgressBar.IsIndeterminate = isIndeterminate;
        UpdateProgressBar.Value = 0;
    }

    private void HideUpdateProgress()
    {
        UpdateProgressBar.IsIndeterminate = false;
        UpdateProgressBar.Value = 0;
        UpdateProgressBar.Visibility = System.Windows.Visibility.Collapsed;
    }

    private void ApplyUpdateProgress(string assetName, UpdateDownloadProgress progress)
    {
        UpdateProgressBar.Visibility = System.Windows.Visibility.Visible;
        if (progress.Percent is double percent)
        {
            UpdateProgressBar.IsIndeterminate = false;
            UpdateProgressBar.Value = Math.Clamp(percent, 0d, 100d);
            UpdateStatusText.Text = $"{progress.Phase}\n{assetName}\n{FormatProgress(progress.BytesReceived, progress.TotalBytes)}";
            return;
        }

        UpdateProgressBar.IsIndeterminate = true;
        UpdateStatusText.Text = $"{progress.Phase}\n{assetName}\n已下载 {FormatDownloadedSize(progress.BytesReceived)}";
    }

    private bool ShowUpdateReadyDialog(UpdateRelease release)
    {
        var owner = System.Windows.Window.GetWindow(this);
        var dialog = new UpdateReadyDialog(release)
        {
            Owner = owner
        };
        dialog.ShowDialog();
        return dialog.RestartRequested;
    }

    private static string FormatProgress(long bytesReceived, long? totalBytes)
    {
        if (totalBytes is > 0)
        {
            var percent = Math.Clamp(bytesReceived * 100d / totalBytes.Value, 0d, 100d);
            return $"{FormatDownloadedSize(bytesReceived)} / {FormatDownloadedSize(totalBytes.Value)}（{percent:F0}%）";
        }

        return $"已下载 {FormatDownloadedSize(bytesReceived)}";
    }

    private static string FormatDownloadedSize(long bytes)
    {
        var mb = Math.Max(0, bytes) / 1024d / 1024d;
        return $"{mb:F1} MB";
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

    private sealed record ThemeModeOption(AppThemeMode Mode, string Title)
    {
        public static IReadOnlyList<ThemeModeOption> All { get; } =
        [
            new(AppThemeMode.System, "跟随系统"),
            new(AppThemeMode.Light, "浅色模式"),
            new(AppThemeMode.Dark, "深色模式")
        ];

        public override string ToString() => Title;
    }
}
